import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

export const useChatStore = defineStore('chat', () => {
  const chats = ref([])
  const currentUser = ref(null)

  function mapChats(backendChats, allUsers, currentUserId) {
    const normalizeId = (id) => String(id || '').toLowerCase()
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

      return {
        id: chatId,
        name: displayName,
        lastMessage: '',
        isGroup,
      }
    })
  }

  async function loadChats() {
    try {
      currentUser.value = messengerApi.getCurrentUser()
      if (!currentUser.value) {
        chats.value = []
        return
      }

      const currentUserId = currentUser.value.userId
      const [backendChats, allUsers] = await Promise.all([
        messengerApi.getChatsByUser(currentUserId),
        messengerApi.getUsers(),
      ])

      chats.value = mapChats(backendChats, allUsers, currentUserId)
    } catch (e) {
      console.error('Failed to load chats from backend:', e)
    }
  }

  function updateLastMessage(chatId, message) {
    const chat = chats.value.find((c) => c.id === chatId)
    if (chat) {
      chat.lastMessage = message
    }
  }

  return {
    chats,
    currentUser,
    loadChats,
    updateLastMessage,
  }
})
