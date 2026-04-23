import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

function buildDirectPairKey(participantUserIds) {
  const normalized = (participantUserIds || [])
    .map((id) => normalizeId(id))
    .filter(Boolean)
    .sort()

  return normalized.join(':')
}

export const useChatStore = defineStore('chat', () => {
  const chats = ref([])
  const currentUser = ref(null)
  const usersById = ref({})
  const usersFetchedAt = ref(0)
  const chatsFetchedAt = ref(0)
  const unreadSeenByChatId = ref({})
  const unreadCountsByChatId = ref({})
  const unreadStorageKey = ref(null)

  function getUnreadStorageKey(userId) {
    const normalized = normalizeId(userId)
    return normalized ? `ois_unread_counts_${normalized}` : null
  }

  function loadUnreadCountsFromStorage(userId) {
    const key = getUnreadStorageKey(userId)
    unreadStorageKey.value = key
    unreadCountsByChatId.value = {}
    if (!key) return

    try {
      const raw = localStorage.getItem(key)
      if (!raw) return

      const parsed = JSON.parse(raw)
      if (!parsed || typeof parsed !== 'object') return

      const normalized = {}
      for (const [chatId, countValue] of Object.entries(parsed)) {
        const count = Number(countValue || 0)
        if (!Number.isFinite(count) || count <= 0) continue
        normalized[normalizeId(chatId)] = count
      }
      unreadCountsByChatId.value = normalized
    } catch {
      unreadCountsByChatId.value = {}
    }
  }

  function persistUnreadCounts() {
    if (!unreadStorageKey.value) return
    try {
      localStorage.setItem(unreadStorageKey.value, JSON.stringify(unreadCountsByChatId.value))
    } catch {
      // ignore quota/storage errors
    }
  }

  function getStoredUnreadCount(chatId) {
    return Number(unreadCountsByChatId.value[normalizeId(chatId)] || 0)
  }

  function resolveUnreadCount(chatId, liveById = null) {
    const normalizedChatId = normalizeId(chatId)
    if (liveById?.has(normalizedChatId)) {
      const liveCount = Number(liveById.get(normalizedChatId)?.unreadCount || 0)
      if (liveCount > 0) return liveCount
    }
    return getStoredUnreadCount(chatId)
  }

  function syncUnreadCountsFromRenderedChats() {
    const next = {}
    for (const chat of chats.value) {
      const count = Number(chat?.unreadCount || 0)
      if (count > 0) {
        next[normalizeId(chat.id)] = count
      }
    }
    unreadCountsByChatId.value = next
    persistUnreadCounts()
  }

  function withUnreadCount(chat, fallbackUnread = 0) {
    return {
      ...chat,
      unreadCount: Number(chat?.unreadCount ?? fallbackUnread ?? 0),
    }
  }

  function messagePreviewByType(type, text) {
    const normalizedType = Number(type ?? 0)
    if (normalizedType === 1) return 'Голосовое сообщение'
    if (normalizedType === 2) return 'Медиафайл'
    return (text || '').trim()
  }

  function buildPreview({ text, type, senderUserId, currentUserId }) {
    const base = messagePreviewByType(type, text)
    if (!base) return ''
    return normalizeId(senderUserId) === normalizeId(currentUserId) ? `Вы: ${base}` : base
  }

  function mapChats(backendChats, allUsers, currentUserId) {
    const normalizedCurrentId = normalizeId(currentUserId)

    return backendChats.map((c) => {
      const chatId = c.chatId ?? c.ChatId
      const isGroup = c.isGroup ?? c.IsGroup
      const name = c.name ?? c.Name
      const participantIds = c.participantUserIds ?? c.ParticipantUserIds ?? []

      let displayName = name || 'Без названия'

      if (!isGroup) {
        const otherUserId = participantIds.find((id) => normalizeId(id) !== normalizedCurrentId)

        if (otherUserId) {
          const otherUser = allUsers.find((u) => normalizeId(u.userId) === normalizeId(otherUserId))
          displayName = otherUser?.username || otherUser?.login || 'Собеседник'
        } else {
          displayName = 'Собеседник'
        }
      }

      const preview = buildPreview({
        text: c.lastMessageText ?? c.LastMessageText ?? '',
        type: c.lastMessageType ?? c.LastMessageType ?? 0,
        senderUserId: c.lastMessageSenderUserId ?? c.LastMessageSenderUserId ?? null,
        currentUserId,
      })

      return {
        id: chatId,
        createdAt: c.createdAt ?? c.CreatedAt ?? null,
        name: displayName,
        lastMessage: preview,
        lastMessageSentAt: c.lastMessageSentAt ?? c.LastMessageSentAt ?? null,
        lastMessageSenderUserId: c.lastMessageSenderUserId ?? c.LastMessageSenderUserId ?? null,
        lastMessageType: c.lastMessageType ?? c.LastMessageType ?? null,
        participantUserIds: participantIds,
        isGroup,
        // Keep unread count nullable here so live fallback is preserved on preview refreshes.
        unreadCount: c.unreadCount ?? c.UnreadCount ?? null,
      }
    })
  }

  function dedupeDirectChats(items) {
    const directByPair = new Map()
    const groups = []

    for (const chat of items) {
      if (chat.isGroup) {
        groups.push(chat)
        continue
      }

      const pairKey = buildDirectPairKey(chat.participantUserIds)
      if (!pairKey) {
        groups.push(chat)
        continue
      }

      const existing = directByPair.get(pairKey)
      if (!existing) {
        directByPair.set(pairKey, chat)
        continue
      }

      const chatLastTs = chat.lastMessageSentAt ? new Date(chat.lastMessageSentAt).getTime() : 0
      const existingLastTs = existing.lastMessageSentAt ? new Date(existing.lastMessageSentAt).getTime() : 0
      const chatCreatedTs = chat.createdAt ? new Date(chat.createdAt).getTime() : 0
      const existingCreatedTs = existing.createdAt ? new Date(existing.createdAt).getTime() : 0

      const preferCurrent =
        chatLastTs > existingLastTs ||
        (chatLastTs === existingLastTs && chatCreatedTs > existingCreatedTs)

      if (preferCurrent) {
        directByPair.set(pairKey, chat)
      }
    }

    return [...groups, ...directByPair.values()]
  }

  function sortChatsByLastMessage(items) {
    return [...items].sort((a, b) => {
      const aTs = a.lastMessageSentAt ? new Date(a.lastMessageSentAt).getTime() : 0
      const bTs = b.lastMessageSentAt ? new Date(b.lastMessageSentAt).getTime() : 0
      return bTs - aTs
    })
  }

  async function ensureUsers(userIds = [], forceRefresh = false) {
    const now = Date.now()
    const requestedIds = [...new Set((userIds || []).map((id) => normalizeId(id)).filter(Boolean))]
    const hasAllRequestedUsers = requestedIds.every((id) => usersById.value[id])
    const isFresh = now - usersFetchedAt.value < 30_000

    if (!forceRefresh && hasAllRequestedUsers && isFresh) {
      return Object.values(usersById.value)
    }

    const users = await messengerApi.getUsers(requestedIds)
    usersById.value = users.reduce((acc, user) => {
      acc[normalizeId(user.userId)] = user
      return acc
    }, { ...usersById.value })
    usersFetchedAt.value = now
    return Object.values(usersById.value)
  }

  async function loadChats(options = {}) {
    const {
      forceUsersRefresh = false,
      forceChatsRefresh = false,
    } = options

    try {
      currentUser.value = messengerApi.getCurrentUser()
      if (!currentUser.value) {
        chats.value = []
        usersById.value = {}
        usersFetchedAt.value = 0
        chatsFetchedAt.value = 0
        unreadSeenByChatId.value = {}
        unreadCountsByChatId.value = {}
        unreadStorageKey.value = null
        return
      }

      const currentUserId = currentUser.value.userId
      const nextUnreadStorageKey = getUnreadStorageKey(currentUserId)
      if (nextUnreadStorageKey !== unreadStorageKey.value) {
        loadUnreadCountsFromStorage(currentUserId)
      }

      const now = Date.now()
      const shouldReuseChats = !forceChatsRefresh && chats.value.length > 0 && now - chatsFetchedAt.value < 3_000

      if (shouldReuseChats) {
        return
      }

      const backendChats = await messengerApi.getChatsByUser(currentUserId)
      chatsFetchedAt.value = now
      const liveById = new Map(chats.value.map((chat) => [normalizeId(chat.id), chat]))
      const participantIds = backendChats
        .flatMap((chat) => chat.participantUserIds ?? chat.ParticipantUserIds ?? [])
        .filter((id) => normalizeId(id) !== normalizeId(currentUserId))

      // Render chat list instantly using cached users (if available).
      const cachedUsers = Object.values(usersById.value)
      const mappedChats = mapChats(backendChats, cachedUsers, currentUserId)
        .map((chat) => withUnreadCount(chat, resolveUnreadCount(chat.id, liveById)))
      chats.value = sortChatsByLastMessage(dedupeDirectChats(mappedChats))
      syncUnreadCountsFromRenderedChats()

      // Hydrate visible participant names in background so UI is not blocked.
      ensureUsers(participantIds, forceUsersRefresh)
        .then((allUsers) => {
          if (!currentUser.value || normalizeId(currentUser.value.userId) !== normalizeId(currentUserId)) {
            return
          }

          const mappedWithUsers = mapChats(backendChats, allUsers, currentUserId)
          const liveById = new Map(chats.value.map((chat) => [normalizeId(chat.id), chat]))

          const mergedWithLive = mappedWithUsers.map((mappedChat) => {
            const liveChat = liveById.get(normalizeId(mappedChat.id))
            if (!liveChat) return mappedChat

            const liveTs = liveChat.lastMessageSentAt ? new Date(liveChat.lastMessageSentAt).getTime() : 0
            const mappedTs = mappedChat.lastMessageSentAt ? new Date(mappedChat.lastMessageSentAt).getTime() : 0
            const shouldKeepLivePreview = liveTs >= mappedTs && (liveChat.lastMessage || '').trim().length > 0

            if (!shouldKeepLivePreview) {
              return mappedChat
            }

            return withUnreadCount({
              ...mappedChat,
              lastMessage: liveChat.lastMessage,
              lastMessageSentAt: liveChat.lastMessageSentAt,
              lastMessageSenderUserId: liveChat.lastMessageSenderUserId,
              lastMessageType: liveChat.lastMessageType,
            }, liveChat.unreadCount || 0)
          })

          chats.value = sortChatsByLastMessage(
            dedupeDirectChats(
              mergedWithLive.map((chat) => withUnreadCount(chat, resolveUnreadCount(chat.id, liveById))),
            ),
          )
          syncUnreadCountsFromRenderedChats()
        })
        .catch((e) => {
          console.error('Failed to hydrate users for chats:', e)
        })
    } catch (e) {
      console.error('Failed to load chats from backend:', e)
    }
  }

  function updateLastMessage(chatId, message, sentAt = null, senderUserId = null, type = null) {
    const normalizedChatId = normalizeId(chatId)
    const chat = chats.value.find((c) => normalizeId(c.id) === normalizedChatId)
    if (!chat) return

    chat.lastMessage = message
    chat.lastMessageSentAt = sentAt || new Date().toISOString()
    chat.lastMessageSenderUserId = senderUserId
    chat.lastMessageType = type

    chats.value = sortChatsByLastMessage(chats.value)
  }

  function incrementUnreadCount(chatId, messageId = null) {
    const normalizedChatId = normalizeId(chatId)
    const chat = chats.value.find((c) => normalizeId(c.id) === normalizedChatId)
    if (!chat) return

    const normalizedMessageId = normalizeId(messageId)
    if (normalizedMessageId) {
      const seenMap = unreadSeenByChatId.value[normalizedChatId] || {}
      if (seenMap[normalizedMessageId]) {
        return
      }

      unreadSeenByChatId.value = {
        ...unreadSeenByChatId.value,
        [normalizedChatId]: {
          ...seenMap,
          [normalizedMessageId]: true,
        },
      }
    }

    chat.unreadCount = Number(chat.unreadCount || 0) + 1
    unreadCountsByChatId.value = {
      ...unreadCountsByChatId.value,
      [normalizedChatId]: chat.unreadCount,
    }
    persistUnreadCounts()
  }

  function resetUnreadCount(chatId) {
    const normalizedChatId = normalizeId(chatId)
    const chat = chats.value.find((c) => normalizeId(c.id) === normalizedChatId)
    if (!chat) return

    chat.unreadCount = 0
    if (unreadCountsByChatId.value[normalizedChatId]) {
      const nextCounts = { ...unreadCountsByChatId.value }
      delete nextCounts[normalizedChatId]
      unreadCountsByChatId.value = nextCounts
      persistUnreadCounts()
    }

    if (unreadSeenByChatId.value[normalizedChatId]) {
      const next = { ...unreadSeenByChatId.value }
      delete next[normalizedChatId]
      unreadSeenByChatId.value = next
    }
  }

  function updatePreviewFromMessage(chatId, messageDto, currentUserId) {
    const senderUserId = messageDto.senderUserId ?? messageDto.SenderUserId ?? null
    const text = messageDto.text ?? messageDto.Text ?? ''
    const type = messageDto.type ?? messageDto.Type ?? 0
    const sentAt = messageDto.sentAt ?? messageDto.SentAt ?? new Date().toISOString()
    const preview = buildPreview({ text, type, senderUserId, currentUserId })

    updateLastMessage(chatId, preview, sentAt, senderUserId, Number(type))
  }

  function getChatById(chatId) {
    const normalizedChatId = normalizeId(chatId)
    return chats.value.find((c) => normalizeId(c.id) === normalizedChatId) || null
  }

  function getUserById(userId) {
    return usersById.value[normalizeId(userId)] || null
  }

  function applyChatPreview(chatDto, currentUserId = null) {
    if (!chatDto) return

    const userId = currentUserId || currentUser.value?.userId || messengerApi.getCurrentUser()?.userId
    const mapped = mapChats([chatDto], Object.values(usersById.value), userId || '')[0]
    if (!mapped) return

    const existingIndex = chats.value.findIndex((c) => normalizeId(c.id) === normalizeId(mapped.id))
    if (existingIndex >= 0) {
      const merged = [...chats.value]
      merged[existingIndex] = {
        ...merged[existingIndex],
        ...withUnreadCount(mapped, merged[existingIndex].unreadCount || 0),
      }
      chats.value = sortChatsByLastMessage(merged)
      return
    }

    if (!mapped.isGroup) {
      const pairKey = buildDirectPairKey(mapped.participantUserIds)
      if (pairKey) {
        const directDuplicateIndex = chats.value.findIndex((c) => !c.isGroup && buildDirectPairKey(c.participantUserIds) === pairKey)
        if (directDuplicateIndex >= 0) {
          const merged = [...chats.value]
          merged[directDuplicateIndex] = {
            ...merged[directDuplicateIndex],
            ...withUnreadCount(mapped, merged[directDuplicateIndex].unreadCount || 0),
          }
          chats.value = sortChatsByLastMessage(dedupeDirectChats(merged))
          return
        }
      }
    }

    chats.value = sortChatsByLastMessage(
      dedupeDirectChats([...chats.value, withUnreadCount(mapped, getStoredUnreadCount(mapped.id))]),
    )
    syncUnreadCountsFromRenderedChats()
  }

  return {
    chats,
    currentUser,
    loadChats,
    updateLastMessage,
    updatePreviewFromMessage,
    applyChatPreview,
    incrementUnreadCount,
    resetUnreadCount,
    getChatById,
    getUserById,
    ensureUsers,
  }
})
