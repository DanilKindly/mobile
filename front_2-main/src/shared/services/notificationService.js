import { hasActiveWebPushSubscription } from './webPushService'

function canUseNotifications() {
  return typeof window !== 'undefined' && 'Notification' in window
}

let requestInFlight = null
let requestedInCurrentRuntime = false
let bootstrapListenerBound = false

export async function getNotificationPermissionState() {
  if (!canUseNotifications()) return 'unsupported'

  try {
    if (navigator?.permissions?.query) {
      const status = await navigator.permissions.query({ name: 'notifications' })
      return status.state || Notification.permission
    }
  } catch {
    // Permissions API is optional; fallback below.
  }

  return Notification.permission
}

export async function ensureNotificationPermissionForIncoming() {
  if (!canUseNotifications()) return 'unsupported'

  const currentState = await getNotificationPermissionState()
  if (currentState === 'granted' || currentState === 'denied') {
    return currentState
  }

  if (requestedInCurrentRuntime) {
    return Notification.permission
  }

  if (!requestInFlight) {
    requestedInCurrentRuntime = true
    requestInFlight = Notification.requestPermission().finally(() => {
      requestInFlight = null
    })
  }

  return requestInFlight
}

export async function requestNotificationPermissionOnUserGesture() {
  if (!canUseNotifications()) return 'unsupported'
  const status = await getNotificationPermissionState()
  if (status === 'granted' || status === 'denied') {
    if (status === 'granted') {
      window.dispatchEvent(new Event('kindly-push-bootstrap-request'))
    }
    return status
  }
  const result = await ensureNotificationPermissionForIncoming()
  if (result === 'granted') {
    window.dispatchEvent(new Event('kindly-push-bootstrap-request'))
  }
  return result
}

export async function setupNotificationPermissionBootstrap() {
  if (!canUseNotifications()) return
  if (bootstrapListenerBound) return

  const status = await getNotificationPermissionState()
  if (status === 'granted' || status === 'denied') {
    return
  }

  const onFirstInteraction = async () => {
    document.removeEventListener('pointerdown', onFirstInteraction, true)
    document.removeEventListener('keydown', onFirstInteraction, true)
    bootstrapListenerBound = false
    await requestNotificationPermissionOnUserGesture()
  }

  bootstrapListenerBound = true
  document.addEventListener('pointerdown', onFirstInteraction, true)
  document.addEventListener('keydown', onFirstInteraction, true)
}

export function showNewMessageNotification({ title, body, icon = '/icon-192.png', data = {} }) {
  if (!canUseNotifications()) return null
  if (Notification.permission !== 'granted') return null

  return new Notification(title, {
    body,
    icon,
    badge: '/icon-192.png',
    data,
    tag: `chat-${data?.chatId || 'general'}`,
  })
}

export async function shouldUseLocalNotificationFallback() {
  const hasPushSubscription = await hasActiveWebPushSubscription()
  return !hasPushSubscription
}

