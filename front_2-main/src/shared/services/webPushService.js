import messengerApi from '@/api/messenger'

let serviceWorkerRegistrationPromise = null
let syncInFlight = null

export const PUSH_STATUS = {
  UNSUPPORTED: 'unsupported',
  PERMISSION_DENIED: 'permission_denied',
  NO_USER: 'no_user',
  PUSH_NOT_CONFIGURED: 'push_not_configured',
  SUBSCRIBED: 'subscribed',
  SUBSCRIBE_FAILED: 'subscribe_failed',
}

function canUseWebPush() {
  return typeof window !== 'undefined' &&
    window.isSecureContext &&
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
}

function urlBase64ToUint8Array(base64String) {
  const padding = '='.repeat((4 - (base64String.length % 4)) % 4)
  const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/')
  const rawData = window.atob(base64)
  const output = new Uint8Array(rawData.length)
  for (let i = 0; i < rawData.length; i += 1) {
    output[i] = rawData.charCodeAt(i)
  }
  return output
}

function toBase64UrlFromArrayBuffer(buffer) {
  const bytes = new Uint8Array(buffer)
  let binary = ''
  for (let i = 0; i < bytes.byteLength; i += 1) {
    binary += String.fromCharCode(bytes[i])
  }

  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

function extractKey(subscription, keyName) {
  const json = subscription.toJSON?.() || {}
  if (json.keys?.[keyName]) {
    return json.keys[keyName]
  }

  const rawKey = subscription.getKey?.(keyName)
  if (!rawKey) return null
  return toBase64UrlFromArrayBuffer(rawKey)
}

async function ensureServiceWorkerRegistration() {
  if (!canUseWebPush()) return null

  if (!serviceWorkerRegistrationPromise) {
    serviceWorkerRegistrationPromise = navigator.serviceWorker.register('/sw.js')
      .catch((error) => {
        serviceWorkerRegistrationPromise = null
        throw error
      })
  }

  return serviceWorkerRegistrationPromise
}

function isStandaloneMode() {
  const mediaStandalone = typeof window.matchMedia === 'function' && window.matchMedia('(display-mode: standalone)').matches
  const navigatorStandalone = typeof navigator !== 'undefined' && navigator.standalone === true
  return mediaStandalone || navigatorStandalone
}

function isLikelyIphone() {
  const ua = navigator?.userAgent || ''
  return /iPhone/i.test(ua)
}

export async function hasActiveWebPushSubscription() {
  if (!canUseWebPush()) return false

  try {
    const registration = await ensureServiceWorkerRegistration()
    if (!registration) return false

    const existing = await registration.pushManager.getSubscription()
    return Boolean(existing)
  } catch {
    return false
  }
}

async function reportSubscribeFailure(error) {
  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) return

  const errorName = error?.name || 'PushSubscribeError'
  const errorMessage = error?.message || String(error || 'Unknown error')
  try {
    await messengerApi.reportPushSubscribeFailure({
      errorName,
      errorMessage,
      userAgent: navigator.userAgent,
      isStandalone: isStandaloneMode(),
    })
  } catch (reportError) {
    console.warn('Failed to report push subscribe failure:', reportError)
  }
}

async function subscribeAndUpsert(registration) {
  let subscription = await registration.pushManager.getSubscription()

  if (!subscription) {
    const keyResponse = await messengerApi.getPushVapidPublicKey()
    const vapidPublicKey = keyResponse?.publicKey || keyResponse?.PublicKey
    if (!vapidPublicKey) {
      return { status: PUSH_STATUS.PUSH_NOT_CONFIGURED }
    }

    subscription = await registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
    })
  }

  const endpoint = subscription.endpoint
  const p256dh = extractKey(subscription, 'p256dh')
  const auth = extractKey(subscription, 'auth')
  if (!endpoint || !p256dh || !auth) {
    return { status: PUSH_STATUS.SUBSCRIBE_FAILED, error: new Error('Invalid push subscription keys') }
  }

  await messengerApi.upsertPushSubscription({
    endpoint,
    p256dh,
    auth,
    userAgent: navigator.userAgent,
  })

  return { status: PUSH_STATUS.SUBSCRIBED, endpoint }
}

export async function ensureWebPushSubscription() {
  if (!canUseWebPush()) return { status: PUSH_STATUS.UNSUPPORTED }
  if (Notification.permission !== 'granted') {
    return {
      status: Notification.permission === 'denied'
        ? PUSH_STATUS.PERMISSION_DENIED
        : PUSH_STATUS.SUBSCRIBE_FAILED,
    }
  }

  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) {
    return { status: PUSH_STATUS.NO_USER }
  }

  if (syncInFlight) return syncInFlight

  syncInFlight = (async () => {
    try {
      const registration = await ensureServiceWorkerRegistration()
      if (!registration) return { status: PUSH_STATUS.UNSUPPORTED }
      return await subscribeAndUpsert(registration)
    } catch (error) {
      console.error('ensureWebPushSubscription failed:', error)
      await reportSubscribeFailure(error)
      return { status: PUSH_STATUS.SUBSCRIBE_FAILED, error }
    }
  })().finally(() => {
    syncInFlight = null
  })

  return syncInFlight
}

export async function forceResubscribe() {
  if (!canUseWebPush()) return { status: PUSH_STATUS.UNSUPPORTED }
  if (Notification.permission !== 'granted') {
    return {
      status: Notification.permission === 'denied'
        ? PUSH_STATUS.PERMISSION_DENIED
        : PUSH_STATUS.SUBSCRIBE_FAILED,
    }
  }

  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) {
    return { status: PUSH_STATUS.NO_USER }
  }

  try {
    const registration = await ensureServiceWorkerRegistration()
    if (!registration) return { status: PUSH_STATUS.UNSUPPORTED }

    const existing = await registration.pushManager.getSubscription()
    if (existing) {
      try {
        await messengerApi.removePushSubscription(existing.endpoint)
      } catch (error) {
        console.warn('Failed to remove push subscription from server:', error)
      }
      await existing.unsubscribe()
    }

    return await subscribeAndUpsert(registration)
  } catch (error) {
    console.error('forceResubscribe failed:', error)
    await reportSubscribeFailure(error)
    return { status: PUSH_STATUS.SUBSCRIBE_FAILED, error }
  }
}

export async function bootstrapWebPush() {
  if (!canUseWebPush()) {
    return { status: PUSH_STATUS.UNSUPPORTED, isStandalone: false, requiresHomeScreen: false, isIphone: false }
  }

  try {
    const registration = await ensureServiceWorkerRegistration()
    if (registration?.waiting) {
      registration.waiting.postMessage({ type: 'SKIP_WAITING' })
    }

    const standalone = isStandaloneMode()
    const iphone = isLikelyIphone()
    const requiresHomeScreen = iphone && !standalone

    if (Notification.permission === 'granted') {
      const subscribed = await ensureWebPushSubscription()
      return {
        ...subscribed,
        isStandalone: standalone,
        requiresHomeScreen,
        isIphone: iphone,
      }
    }

    return {
      status: Notification.permission === 'denied'
        ? PUSH_STATUS.PERMISSION_DENIED
        : PUSH_STATUS.SUBSCRIBE_FAILED,
      isStandalone: standalone,
      requiresHomeScreen,
      isIphone: iphone,
    }
  } catch (error) {
    console.error('Web push bootstrap failed:', error)
    await reportSubscribeFailure(error)
    return { status: PUSH_STATUS.SUBSCRIBE_FAILED, error, isStandalone: isStandaloneMode(), requiresHomeScreen: isLikelyIphone() && !isStandaloneMode(), isIphone: isLikelyIphone() }
  }
}
