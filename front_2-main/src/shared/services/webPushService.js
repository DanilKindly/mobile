import messengerApi from '@/api/messenger'

let serviceWorkerRegistrationPromise = null
let syncInFlight = null

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

export async function ensureWebPushSubscription() {
  if (!canUseWebPush()) return { status: 'unsupported' }
  if (Notification.permission !== 'granted') return { status: Notification.permission }

  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) {
    return { status: 'no-user' }
  }

  if (syncInFlight) return syncInFlight

  syncInFlight = (async () => {
    const registration = await ensureServiceWorkerRegistration()
    if (!registration) return { status: 'sw-unavailable' }

    let subscription = await registration.pushManager.getSubscription()

    if (!subscription) {
      const keyResponse = await messengerApi.getPushVapidPublicKey()
      const vapidPublicKey = keyResponse?.publicKey || keyResponse?.PublicKey
      if (!vapidPublicKey) {
        return { status: 'push-not-configured' }
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
      return { status: 'invalid-subscription' }
    }

    await messengerApi.upsertPushSubscription({
      endpoint,
      p256dh,
      auth,
      userAgent: navigator.userAgent,
    })

    return { status: 'subscribed' }
  })().finally(() => {
    syncInFlight = null
  })

  return syncInFlight
}

export async function bootstrapWebPush() {
  if (!canUseWebPush()) return

  try {
    await ensureServiceWorkerRegistration()
    if (Notification.permission === 'granted') {
      await ensureWebPushSubscription()
    }
  } catch (error) {
    console.error('Web push bootstrap failed:', error)
  }
}

