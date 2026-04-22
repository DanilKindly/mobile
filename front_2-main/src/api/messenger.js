import axios from 'axios'
import * as signalR from '@microsoft/signalr'

const API_BASE = (import.meta.env.VITE_API_URL || '').trim()
const BACKEND_BASE = (import.meta.env.VITE_API_URL || import.meta.env.VITE_PROXY_TARGET || '').trim()
const SIGNALR_URL = (import.meta.env.VITE_SIGNALR_URL || '').trim() || `${API_BASE}/hubs/chat`

const CURRENT_USER_KEY = 'ois_current_user'
const TOKEN_KEY = 'token'
const REALTIME_CURSOR_KEY = 'ois_realtime_cursor'

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
      localStorage.removeItem(CURRENT_USER_KEY)
      localStorage.removeItem(TOKEN_KEY)
      localStorage.removeItem(REALTIME_CURSOR_KEY)
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

function parseDeliveryStatus(value) {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value
  }

  const normalized = String(value || '').toLowerCase()
  if (normalized === 'sending') return 0
  if (normalized === 'sent') return 1
  if (normalized === 'failed') return 2
  if (normalized === 'read') return 3
  return 1
}

function normalizeMessage(dto) {
  return {
    messageId: dto.messageId ?? dto.MessageId,
    chatId: dto.chatId ?? dto.ChatId,
    senderUserId: dto.senderUserId ?? dto.SenderUserId,
    clientMessageId: dto.clientMessageId ?? dto.ClientMessageId ?? null,
    sentAtClient: dto.sentAtClient ?? dto.SentAtClient ?? null,
    version: Number(dto.version ?? dto.Version ?? 0),
    deliveryStatus: parseDeliveryStatus(dto.deliveryStatus ?? dto.DeliveryStatus ?? 1),
    type: Number(dto.type ?? dto.Type ?? 0),
    text: dto.text ?? dto.Text ?? '',
    audioUrl: dto.audioUrl ?? dto.AudioUrl ?? null,
    audioContentType: dto.audioContentType ?? dto.AudioContentType ?? null,
    audioDurationSeconds: dto.audioDurationSeconds ?? dto.AudioDurationSeconds ?? null,
    audioSizeBytes: dto.audioSizeBytes ?? dto.AudioSizeBytes ?? null,
    mediaUrl: dto.mediaUrl ?? dto.MediaUrl ?? null,
    mediaContentType: dto.mediaContentType ?? dto.MediaContentType ?? null,
    mediaFileName: dto.mediaFileName ?? dto.MediaFileName ?? null,
    mediaSizeBytes: dto.mediaSizeBytes ?? dto.MediaSizeBytes ?? null,
    isRead: Boolean(dto.isRead ?? dto.IsRead ?? false),
    readAt: dto.readAt ?? dto.ReadAt ?? null,
    sentAt: dto.sentAt ?? dto.SentAt ?? new Date().toISOString(),
  }
}

function normalizeRealtimeEvent(rawEvent) {
  const eventType = rawEvent.eventType ?? rawEvent.EventType
  const cursor = Number(rawEvent.cursor ?? rawEvent.Cursor ?? 0)
  const version = Number(rawEvent.version ?? rawEvent.Version ?? cursor)
  const chatId = rawEvent.chatId ?? rawEvent.ChatId ?? null
  const readerUserId = rawEvent.readerUserId ?? rawEvent.ReaderUserId ?? null
  const messageIds = rawEvent.messageIds ?? rawEvent.MessageIds ?? null
  const chatPreview = rawEvent.chatPreview ?? rawEvent.ChatPreview ?? null
  const message = rawEvent.message ?? rawEvent.Message ?? null

  return {
    eventType,
    cursor,
    version,
    chatId,
    readerUserId,
    messageIds,
    chatPreview,
    message: message ? normalizeMessage(message) : null,
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

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function withTimeout(promise, timeoutMs = 3000) {
  let timeoutId = null
  const timeoutPromise = new Promise((_, reject) => {
    timeoutId = setTimeout(() => reject(new Error('ACK_TIMEOUT')), timeoutMs)
  })

  return Promise.race([promise, timeoutPromise]).finally(() => {
    if (timeoutId) {
      clearTimeout(timeoutId)
    }
  })
}

function getRealtimeCursor() {
  const value = Number(localStorage.getItem(REALTIME_CURSOR_KEY) || '0')
  return Number.isFinite(value) && value > 0 ? value : 0
}

function setRealtimeCursor(cursor) {
  const normalized = Number(cursor || 0)
  if (!Number.isFinite(normalized) || normalized <= 0) return

  const current = getRealtimeCursor()
  if (normalized <= current) return
  localStorage.setItem(REALTIME_CURSOR_KEY, String(normalized))
}

let signalRConnection = null
let connectionStartPromise = null
const joinedChatGroups = new Set()
let currentPresenceUserId = null
let rawHandlersAttached = false
const realtimeSubscribers = new Map()

function emitRealtime(eventType, payload) {
  const set = realtimeSubscribers.get(eventType)
  if (!set || set.size === 0) return

  for (const handler of [...set]) {
    try {
      handler(payload)
    } catch (error) {
      console.error(`Realtime subscriber error for ${eventType}:`, error)
    }
  }
}

async function fetchMissedChanges() {
  const currentUser = messengerApi.getCurrentUser()
  const userId = currentUser?.userId
  if (!userId) return

  const cursor = getRealtimeCursor()
  const changes = await messengerApi.getChangesByUser(userId, cursor, 400)
  const nextCursor = Number(changes.cursor ?? changes.Cursor ?? cursor)

  const chats = changes.chats ?? changes.Chats ?? []
  for (const chat of chats) {
    emitRealtime('ChatPreviewChanged', {
      eventType: 'ChatPreviewChanged',
      cursor: nextCursor,
      version: nextCursor,
      chatPreview: chat,
      chatId: chat.chatId ?? chat.ChatId,
    })
  }

  const messages = changes.messages ?? changes.Messages ?? []
  for (const message of messages) {
    const normalizedMessage = normalizeMessage(message)
    emitRealtime('MessageCreated', {
      eventType: 'MessageCreated',
      cursor: Math.max(nextCursor, normalizedMessage.version || 0),
      version: normalizedMessage.version || nextCursor,
      chatId: normalizedMessage.chatId,
      message: normalizedMessage,
    })
  }

  setRealtimeCursor(nextCursor)
}

function ensureRawHandlersAttached(connection) {
  if (rawHandlersAttached) return
  rawHandlersAttached = true

  connection.on('RealtimeEvent', (rawEvent) => {
    const event = normalizeRealtimeEvent(rawEvent)
    setRealtimeCursor(event.cursor)
    emitRealtime('RealtimeEvent', event)
    if (event.eventType) {
      emitRealtime(event.eventType, event)
    }
  })

  // Backward compatibility with old hub payloads.
  connection.on('MessageReceived', (message) => {
    const normalizedMessage = normalizeMessage(message)
    const fallbackEvent = {
      eventType: 'MessageCreated',
      cursor: normalizedMessage.version || Date.now(),
      version: normalizedMessage.version || Date.now(),
      chatId: normalizedMessage.chatId,
      message: normalizedMessage,
    }
    setRealtimeCursor(fallbackEvent.cursor)
    emitRealtime('MessageCreated', fallbackEvent)
  })

  connection.on('MessagesRead', (chatId, messageIds, readerUserId) => {
    const fallbackEvent = {
      eventType: 'MessageUpdatedStatus',
      cursor: Date.now(),
      version: Date.now(),
      chatId,
      messageIds,
      readerUserId,
    }
    emitRealtime('MessageUpdatedStatus', fallbackEvent)
  })

  connection.on('PresenceChanged', (userId, isOnline, lastSeenAt) => {
    emitRealtime('PresenceChanged', { userId, isOnline, lastSeenAt })
  })
}

function getSignalRConnection() {
  if (!signalRConnection) {
    signalRConnection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_URL)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build()

    ensureRawHandlersAttached(signalRConnection)

    signalRConnection.onreconnected(async () => {
      if (currentPresenceUserId) {
        try {
          await signalRConnection.invoke('SetPresence', currentPresenceUserId, true)
        } catch (e) {
          console.error('Failed to restore presence after reconnect:', e)
        }
      }

      const chatIds = [...joinedChatGroups]
      joinedChatGroups.clear()
      for (const chatId of chatIds) {
        try {
          await signalRConnection.invoke('JoinChat', chatId)
          joinedChatGroups.add(chatId)
        } catch (e) {
          console.error('Failed to rejoin chat group after reconnect:', e)
        }
      }

      try {
        await fetchMissedChanges()
      } catch (error) {
        console.error('Failed to fetch missed changes after reconnect:', error)
      }
    })
  }
  return signalRConnection
}

async function ensureConnectionStarted() {
  const connection = getSignalRConnection()
  if (connection.state === signalR.HubConnectionState.Connected) {
    return connection
  }

  if (!connectionStartPromise) {
    connectionStartPromise = connection.start().finally(() => {
      connectionStartPromise = null
    })
  }

  await connectionStartPromise
  return connection
}

function persistCurrentUser(currentUser) {
  const serialized = JSON.stringify(currentUser)
  sessionStorage.setItem(CURRENT_USER_KEY, serialized)
  localStorage.setItem(CURRENT_USER_KEY, serialized)
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

    persistCurrentUser(currentUser)
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

    persistCurrentUser(currentUser)
    if (currentUser.token) {
      localStorage.setItem(TOKEN_KEY, currentUser.token)
    }

    return currentUser
  },

  getCurrentUser() {
    const cached = localStorage.getItem(CURRENT_USER_KEY) || sessionStorage.getItem(CURRENT_USER_KEY)
    if (!cached) return null
    try {
      const parsed = JSON.parse(cached)
      persistCurrentUser(parsed)
      return parsed
    } catch {
      sessionStorage.removeItem(CURRENT_USER_KEY)
      localStorage.removeItem(CURRENT_USER_KEY)
      return null
    }
  },

  logout() {
    sessionStorage.removeItem(CURRENT_USER_KEY)
    localStorage.removeItem(CURRENT_USER_KEY)
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(REALTIME_CURSOR_KEY)
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

  async getChangesByUser(userId, cursor = 0, limit = 250) {
    const response = await api.get(`/api/chats/changes/by-user/${userId}`, {
      params: { cursor, limit },
    })
    return response.data
  },

  async sendMessageRest(chatId, text, senderUserId, options = {}) {
    const response = await api.post(`/api/chats/${chatId}/messages`, {
      senderUserId,
      text,
      clientMessageId: options.clientMessageId ?? null,
      sentAtClient: options.sentAtClient ?? null,
    })
    return normalizeMessage(response.data ?? {})
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
    return normalizeMessage(response.data ?? {})
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
    return normalizeMessage(response.data ?? {})
  },

  async sendMessageSignalRAck(chatId, payload, ackTimeoutMs = 3000) {
    const connection = await ensureConnectionStarted()
    const response = await withTimeout(connection.invoke('SendMessage', chatId, payload), ackTimeoutMs)
    const normalizedEvent = normalizeRealtimeEvent(response)
    if (normalizedEvent.cursor) {
      setRealtimeCursor(normalizedEvent.cursor)
    }
    return normalizedEvent
  },

  async sendMessageReliable(chatId, text, senderUserId, options = {}) {
    const clientMessageId = options.clientMessageId || crypto.randomUUID()
    const sentAtClient = options.sentAtClient || new Date().toISOString()
    const ackTimeoutMs = options.ackTimeoutMs ?? 3000
    const maxRetries = options.maxRetries ?? 2

    const payload = {
      senderUserId,
      text,
      clientMessageId,
      sentAtClient,
    }

    for (let attempt = 0; attempt <= maxRetries; attempt += 1) {
      try {
        const ackEvent = await this.sendMessageSignalRAck(chatId, payload, ackTimeoutMs)
        if (ackEvent?.message) {
          return { message: ackEvent.message, source: 'signalr_ack' }
        }
      } catch (signalRError) {
        if (attempt === maxRetries) {
          try {
            const restMessage = await this.sendMessageRest(chatId, text, senderUserId, {
              clientMessageId,
              sentAtClient,
            })
            return { message: restMessage, source: 'rest_fallback' }
          } catch (restError) {
            throw restError
          }
        }
      }

      await sleep(250 * (attempt + 1))
    }

    throw new Error('Failed to send message reliably.')
  },

  async joinChat(chatId) {
    const normalizedChatId = String(chatId || '').toLowerCase()
    if (!normalizedChatId || joinedChatGroups.has(normalizedChatId)) {
      return
    }

    const connection = await ensureConnectionStarted()
    await connection.invoke('JoinChat', chatId)
    joinedChatGroups.add(normalizedChatId)
  },

  async leaveChat(chatId) {
    const normalizedChatId = String(chatId || '').toLowerCase()
    if (!normalizedChatId || !joinedChatGroups.has(normalizedChatId)) {
      return
    }

    const connection = getSignalRConnection()
    if (connection.state === signalR.HubConnectionState.Connected) {
      await connection.invoke('LeaveChat', chatId)
    }
    joinedChatGroups.delete(normalizedChatId)
  },

  async syncChatSubscriptions(chatIds) {
    const nextMap = new Map(
      (chatIds || [])
        .map((id) => String(id || '').trim())
        .filter(Boolean)
        .map((id) => [id.toLowerCase(), id]),
    )
    const nextSet = new Set(nextMap.keys())

    await ensureConnectionStarted()

    await Promise.all(
      [...joinedChatGroups]
        .filter((existing) => !nextSet.has(existing))
        .map((existing) => this.leaveChat(existing)),
    )

    await Promise.all(
      [...nextSet].map((normalizedId) => this.joinChat(nextMap.get(normalizedId) || normalizedId)),
    )
  },

  async markMessagesAsRead(chatId, readerUserId = null) {
    const connection = await ensureConnectionStarted()

    const resolvedReaderId = readerUserId || this.getCurrentUser()?.userId
    if (!resolvedReaderId) return

    await connection.invoke('MarkMessagesAsRead', chatId, resolvedReaderId)
  },

  async setPresence(userId, isOnline) {
    if (!userId) return
    const connection = await ensureConnectionStarted()

    await connection.invoke('SetPresence', userId, Boolean(isOnline))
    if (isOnline) {
      currentPresenceUserId = userId
    } else if (String(currentPresenceUserId) === String(userId)) {
      currentPresenceUserId = null
    }
  },

  async getOnlineUsers() {
    const connection = await ensureConnectionStarted()
    return connection.invoke('GetOnlineUsers')
  },

  subscribeRealtime(eventType, handler) {
    if (!realtimeSubscribers.has(eventType)) {
      realtimeSubscribers.set(eventType, new Set())
    }
    const set = realtimeSubscribers.get(eventType)
    set.add(handler)

    return () => {
      set.delete(handler)
    }
  },

  async bootstrapRealtimeSync() {
    await ensureConnectionStarted()
    await fetchMissedChanges()
  },

  getConnection() {
    return getSignalRConnection()
  },

  normalizeMessage,
  resolveAssetUrl,
}

export default messengerApi
