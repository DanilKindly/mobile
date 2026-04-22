const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5017'

async function handleJson(response, defaultError) {
  if (!response.ok) {
    throw new Error(defaultError)
  }

  return response.json()
}

export const apiService = {
  async getChats() {
    const response = await fetch(`${API_BASE_URL}/api/chats`)
    return handleJson(response, 'Failed to fetch chats')
  },

  async getMessages(chatId) {
    const response = await fetch(`${API_BASE_URL}/api/chats/${chatId}/messages`)
    return handleJson(response, 'Failed to fetch messages')
  },

  async sendMessage(chatId, text, senderUserId) {
    const response = await fetch(`${API_BASE_URL}/api/chats/${chatId}/messages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text, senderUserId }),
    })
    return handleJson(response, 'Failed to send message')
  },

  async markAsRead(chatId, readerUserId) {
    const response = await fetch(`${API_BASE_URL}/api/chats/${chatId}/read`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ readerUserId }),
    })
    return handleJson(response, 'Failed to mark messages as read')
  },

  async login(login, password) {
    const response = await fetch(`${API_BASE_URL}/api/users/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login, password }),
    })
    return handleJson(response, 'Failed to login')
  },

  async register(login, password, username) {
    const response = await fetch(`${API_BASE_URL}/api/users/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ login, password, username }),
    })
    return handleJson(response, 'Failed to register')
  },
}
