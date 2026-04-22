import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

export const useChatStore = defineStore('chat', () => {
  const chats = ref([])
  const currentUser = ref(null)
  const usersById = ref({})

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

      let displayName = name || 'Без названия'

      if (!isGroup) {
        const participantIds = c.participantUserIds ?? c.ParticipantUserIds ?? []
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
        name: displayName,
        lastMessage: preview,
        lastMessageSentAt: c.lastMessageSentAt ?? c.LastMessageSentAt ?? null,
        lastMessageSenderUserId: c.lastMessageSenderUserId ?? c.LastMessageSenderUserId ?? null,
        lastMessageType: c.lastMessageType ?? c.LastMessageType ?? null,
        isGroup,
      }
    })
  }

  async function loadChats() {
    try {
      currentUser.value = messengerApi.getCurrentUser()
      if (!currentUser.value) {
        chats.value = []
        usersById.value = {}
        return
      }

      const currentUserId = currentUser.value.userId
      const [backendChats, allUsers] = await Promise.all([
        messengerApi.getChatsByUser(currentUserId),
        messengerApi.getUsers(),
      ])

      usersById.value = allUsers.reduce((acc, user) => {
        acc[normalizeId(user.userId)] = user
        return acc
      }, {})

      chats.value = mapChats(backendChats, allUsers, currentUserId)
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

    chats.value = [...chats.value].sort((a, b) => {
      const aTs = a.lastMessageSentAt ? new Date(a.lastMessageSentAt).getTime() : 0
      const bTs = b.lastMessageSentAt ? new Date(b.lastMessageSentAt).getTime() : 0
      return bTs - aTs
    })
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

  return {
    chats,
    currentUser,
    loadChats,
    updateLastMessage,
    updatePreviewFromMessage,
    getChatById,
    getUserById,
  }
})
