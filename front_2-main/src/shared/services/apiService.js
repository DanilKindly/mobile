const API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5017'

export const apiService = {
  /**
   * Получить список чатов
   * @returns {Promise<Array>}
   */
  async getChats() {
    const response = await fetch(`${API_BASE_URL}/api/chats`)
    if (!response.ok) throw new Error('Failed to fetch chats')
    return response.json()
  },

  /**
   * Получить сообщения чата
   * @param {string} chatName - имя чата
   * @returns {Promise<Array>}
   */
  async getMessages(chatName) {
    const response = await fetch(`${API_BASE_URL}/chats/${chatName}/messages`)
    if (!response.ok) throw new Error('Failed to fetch messages')
    return response.json()
  },

  /**
   * Отправить сообщение
   * @param {string} chatName - имя чата
   * @param {string} text - текст сообщения
   * @returns {Promise<Object>}
   */
  async sendMessage(chatName, text) {
    const response = await fetch(`${API_BASE_URL}/chats/${chatName}/messages`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text })
    })
    if (!response.ok) throw new Error('Failed to send message')
    return response.json()
  },

  /**
   * Отметить сообщения как прочитанные
   * @param {string} chatName - имя чата
   * @param {Array<number>} messageIds - ID сообщений
   * @returns {Promise<Object>}
   */
  async markAsRead(chatName, messageIds) {
    const response = await fetch(`${API_BASE_URL}/chats/${chatName}/read`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messageIds })
    })
    if (!response.ok) throw new Error('Failed to mark as read')
    return response.json()
  },

  /**
   * Получить профиль пользователя
   * @returns {Promise<Object>}
   */
  async getProfile() {
    const response = await fetch(`${API_BASE_URL}/profile`)
    if (!response.ok) throw new Error('Failed to fetch profile')
    return response.json()
  },

  /**
   * Авторизация
   * @param {string} username
   * @param {string} password
   * @returns {Promise<Object>}
   */
  async login(username, password) {
    const response = await fetch(`${API_BASE_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    })
    if (!response.ok) throw new Error('Failed to login')
    return response.json()
  },

  /**
   * Регистрация
   * @param {string} username
   * @param {string} password
   * @returns {Promise<Object>}
   */
  async register(username, password) {
    const response = await fetch(`${API_BASE_URL}/auth/register`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    })
    if (!response.ok) throw new Error('Failed to register')
    return response.json()
  },
}
