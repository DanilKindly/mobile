const PERMISSION_REQUESTED_KEY = 'km_notifications_requested'

function canUseNotifications() {
  return typeof window !== 'undefined' && 'Notification' in window
}

export async function requestNotificationsPermissionOnce() {
  if (!canUseNotifications()) return 'unsupported'

  if (Notification.permission === 'granted' || Notification.permission === 'denied') {
    return Notification.permission
  }

  if (localStorage.getItem(PERMISSION_REQUESTED_KEY) === '1') {
    return Notification.permission
  }

  localStorage.setItem(PERMISSION_REQUESTED_KEY, '1')
  return Notification.requestPermission()
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

