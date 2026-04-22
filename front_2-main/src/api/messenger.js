import axios from 'axios'
import * as signalR from '@microsoft/signalr'

const API_BASE = (import.meta.env.VITE_API_URL || '').trim()
const BACKEND_BASE = (import.meta.env.VITE_API_URL || import.meta.env.VITE_PROXY_TARGET || '').trim()
const SIGNALR_URL = (import.meta.env.VITE_SIGNALR_URL || '').trim() || `${API_BASE}/hubs/chat`

const CURRENT_USER_KEY = 'ois_current_user'
const TOKEN_KEY = 'token'

const api = axios.create({
  baseURL: API_BASE,
  timeout: 15000,
  headers: {
    'Content-Type': 'application/json',
  },
})

api.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY)
  if (token) {
    config.headers.Authorization = `Bearer ${token}`
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      sessionStorage.removeItem(CURRENT_USER_KEY)
      localStorage.removeItem(TOKEN_KEY)
      if (window.location.pathname !== '/') {
        window.location.href = '/'
      }
    }
    return Promise.reject(error)
  },
)

function normalizeUser(user) {
  return {
    userId: user.userId ?? user.UserId,
    login: user.login ?? user.Login,
    username: user.username ?? user.Username,
  }
}

function resolveAssetUrl(path) {
  if (!path) return null
  if (/^https?:\/\//i.test(path)) return path
  if (BACKEND_BASE) {
    return new URL(path, BACKEND_BASE).toString()
  }
  return path
}

let signalRConnection = null

function getSignalRConnection() {
  if (!signalRConnection) {
    signalRConnection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_URL)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build()
  }
  return signalRConnection
}

export const messengerApi = {
  async getUsers() {
    const response = await api.get('/api/users')
    return (response.data ?? []).map(normalizeUser)
  },

  async searchUsersByLogin(login) {
    const response = await api.get('/api/users/search', {
      params: { login },
    })
    return (response.data ?? []).map(normalizeUser)
  },

  async registerUser({ login, password, username }) {
    const response = await api.post('/api/users/register', {
      login,
      password,
      username,
    })
    const data = response.data ?? {}
    const currentUser = normalizeUser(data)
    currentUser.token = data.token ?? data.Token

    sessionStorage.setItem(CURRENT_USER_KEY, JSON.stringify(currentUser))
    if (currentUser.token) {
      localStorage.setItem(TOKEN_KEY, currentUser.token)
    }

    return currentUser
  },

  async loginUser({ login, password }) {
    const response = await api.post('/api/users/login', {
      login,
      password,
    })
    const data = response.data ?? {}
    const currentUser = normalizeUser(data)
    currentUser.token = data.token ?? data.Token

    sessionStorage.setItem(CURRENT_USER_KEY, JSON.stringify(currentUser))
    if (currentUser.token) {
      localStorage.setItem(TOKEN_KEY, currentUser.token)
    }

    return currentUser
  },

  getCurrentUser() {
    const cached = sessionStorage.getItem(CURRENT_USER_KEY)
    if (!cached) return null
    try {
      return JSON.parse(cached)
    } catch {
      sessionStorage.removeItem(CURRENT_USER_KEY)
      return null
    }
  },

  logout() {
    sessionStorage.removeItem(CURRENT_USER_KEY)
    localStorage.removeItem(TOKEN_KEY)
  },

  async getOrCreateChatWithUserByLogin(currentUserId, peerLogin) {
    const query = peerLogin.trim().toLowerCase()
    const candidates = await this.searchUsersByLogin(query)
    const peer = candidates.find((u) => u.login.toLowerCase() === query)

    if (!peer) {
      throw new Error('User with this login was not found.')
    }

    if (String(peer.userId).toLowerCase() === String(currentUserId).toLowerCase()) {
      throw new Error('Cannot create a chat with yourself.')
    }

    const chats = await this.getChatsByUser(currentUserId)
    const existingChat = chats.find((c) => {
      const isGroup = c.isGroup ?? c.IsGroup
      const participantIds = c.participantUserIds ?? c.ParticipantUserIds ?? []
      return !isGroup && participantIds.some((id) => String(id).toLowerCase() === String(peer.userId).toLowerCase())
    })

    if (existingChat) {
      return existingChat.chatId ?? existingChat.ChatId
    }

    const created = await this.createChat({
      isGroup: false,
      name: null,
      participantUserIds: [currentUserId, peer.userId],
    })

    return created.chatId ?? created.ChatId
  },

  async createChat(payload) {
    const response = await api.post('/api/chats', payload)
    return response.data
  },

  async getChatById(chatId) {
    const response = await api.get(`/api/chats/${chatId}`)
    return response.data
  },

  async getChatsByUser(userId) {
    const response = await api.get(`/api/chats/by-user/${userId}`)
    return response.data
  },

  async getMessages(chatId) {
    const response = await api.get(`/api/chats/${chatId}/messages`)
    return response.data
  },

  async sendMessageRest(chatId, text, senderUserId) {
    const response = await api.post(`/api/chats/${chatId}/messages`, {
      senderUserId,
      text,
    })
    return response.data
  },

  async sendVoiceMessage(chatId, senderUserId, audioBlob, durationSeconds = null, fileName = null) {
    const formData = new FormData()
    formData.append('senderUserId', senderUserId)
    if (durationSeconds != null) {
      formData.append('durationSeconds', String(durationSeconds))
    }
    const resolvedFileName = fileName || `voice-${Date.now()}.webm`
    formData.append('audio', audioBlob, resolvedFileName)

    const response = await api.post(
      `/api/chats/${chatId}/messages/voice`,
      formData,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    )
    return response.data
  },

  async sendMediaMessage(chatId, senderUserId, file) {
    const formData = new FormData()
    formData.append('senderUserId', senderUserId)
    formData.append('file', file, file.name)

    const response = await api.post(
      `/api/chats/${chatId}/messages/media`,
      formData,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    )
    return response.data
  },

  async sendMessageSignalR(chatId, text, senderUserId) {
    const connection = getSignalRConnection()

    if (connection.state !== signalR.HubConnectionState.Connected) {
      await connection.start()
    }

    await connection.invoke('SendMessage', chatId, {
      senderUserId,
      text,
    })
  },

  async joinChat(chatId) {
    const connection = getSignalRConnection()
    if (connection.state !== signalR.HubConnectionState.Connected) {
      await connection.start()
    }
    await connection.invoke('JoinChat', chatId)
  },

  async leaveChat(chatId) {
    const connection = getSignalRConnection()
    if (connection.state === signalR.HubConnectionState.Connected) {
      await connection.invoke('LeaveChat', chatId)
    }
  },

  async markMessagesAsRead(chatId, readerUserId = null) {
    const connection = getSignalRConnection()
    if (connection.state !== signalR.HubConnectionState.Connected) {
      await connection.start()
    }

    const resolvedReaderId = readerUserId || this.getCurrentUser()?.userId
    if (!resolvedReaderId) return

    await connection.invoke('MarkMessagesAsRead', chatId, resolvedReaderId)
  },

  getConnection() {
    return getSignalRConnection()
  },

  resolveAssetUrl,
}

export default messengerApi
