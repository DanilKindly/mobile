import { expect, request, test } from '@playwright/test'

const apiUrl = process.env.E2E_API_URL || ''
const password = 'Password123!'
const pixelPng = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=',
  'base64',
)

test.skip(!apiUrl, 'Set E2E_API_URL to run smoke tests against a non-production test backend.')

let api
let alice
let bob
let chat

async function registerUser(role) {
  const suffix = `${Date.now()}_${Math.random().toString(16).slice(2)}`
  const login = `e2e_${role}_${suffix}`.replace(/[^a-zA-Z0-9_-]/g, '_')
  const response = await api.post('/api/users/register', {
    data: {
      login,
      password,
      username: `E2E ${role} ${suffix}`,
    },
  })
  expect(response.ok()).toBeTruthy()
  return normalizeUser(await response.json())
}

function normalizeUser(raw) {
  return {
    userId: raw.userId ?? raw.UserId,
    login: raw.login ?? raw.Login,
    username: raw.username ?? raw.Username,
    token: raw.token ?? raw.Token,
  }
}

async function createDirectChat(currentUser, peerUser) {
  const response = await api.post('/api/chats', {
    headers: {
      Authorization: `Bearer ${currentUser.token}`,
    },
    data: {
      isGroup: false,
      name: null,
      participantUserIds: [currentUser.userId, peerUser.userId],
    },
  })
  expect(response.ok()).toBeTruthy()
  const raw = await response.json()
  return {
    chatId: raw.chatId ?? raw.ChatId,
  }
}

async function sendSeedMedia(currentUser, chatId) {
  const response = await api.post(`/api/chats/${chatId}/messages/media`, {
    headers: {
      Authorization: `Bearer ${currentUser.token}`,
    },
    multipart: {
      senderUserId: currentUser.userId,
      file: {
        name: 'seed.png',
        mimeType: 'image/png',
        buffer: pixelPng,
      },
    },
  })
  expect(response.ok()).toBeTruthy()
}

test.beforeAll(async () => {
  if (!apiUrl) return
  api = await request.newContext({ baseURL: apiUrl })
  alice = await registerUser('alice')
  bob = await registerUser('bob')
  chat = await createDirectChat(alice, bob)
  await sendSeedMedia(bob, chat.chatId)
})

test.afterAll(async () => {
  await api?.dispose()
})

test('login, chat list, open chat, send text, and render protected media', async ({ page }) => {
  await page.goto('/')

  await page.locator('form input[type="text"]').fill(alice.login)
  await page.locator('form input[type="password"]').fill(password)
  await page.locator('form button[type="submit"]').click()

  await expect(page).toHaveURL(/\/chats/)
  await expect(page.getByText(bob.username)).toBeVisible()

  await page.getByText(bob.username).click()
  await expect(page).toHaveURL(new RegExp(`/chat/${chat.chatId}`, 'i'))
  await expect(page.locator('img[alt="media"]')).toBeVisible()

  const messageText = `hello e2e ${Date.now()}`
  await page.locator('footer input').fill(messageText)
  await page.locator('footer input').press('Enter')
  await expect(page.getByText(messageText)).toBeVisible()
})
