/* eslint-disable no-restricted-globals */
const SW_VERSION = '2026-04-23-push-recovery-v1'

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim())
})

self.addEventListener('message', (event) => {
  if (event?.data?.type === 'SKIP_WAITING') {
    self.skipWaiting()
  }
})

self.addEventListener('push', (event) => {
  let payload = {}
  try {
    payload = event.data ? event.data.json() : {}
  } catch {
    payload = { body: event.data?.text() || 'New message' }
  }

  event.waitUntil(handlePush(payload))
})

async function handlePush(payload) {
  const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
  const hasVisibleClient = windows.some((client) => client.visibilityState === 'visible')
  if (hasVisibleClient) {
    return
  }

  const title = payload.title || 'Kindly Messenger'
  const body = payload.body || 'New message'
  const data = payload.data || {}

  await self.registration.showNotification(title, {
    body,
    icon: payload.icon || '/icon-192.png',
    badge: payload.badge || '/icon-192.png',
    tag: payload.tag || `chat-${data.chatId || 'general'}-${SW_VERSION}`,
    data,
    renotify: true,
  })
}

self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(openTargetWindow(event.notification.data || {}))
})

async function openTargetWindow(data) {
  const targetPath = data.url || (data.chatId ? `/chat/${data.chatId}` : '/chats')
  const targetUrl = new URL(targetPath, self.location.origin).toString()

  const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true })
  for (const client of windows) {
    if ('focus' in client) {
      await client.focus()
      if ('navigate' in client) {
        await client.navigate(targetUrl)
      }
      return
    }
  }

  await self.clients.openWindow(targetUrl)
}

