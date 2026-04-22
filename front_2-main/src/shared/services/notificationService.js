function canUseNotifications() {
  return typeof window !== 'undefined' && 'Notification' in window
}

let requestInFlight = null
let requestedInCurrentRuntime = false

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

