import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

export const useChatStore = defineStore('chat', () => {
  const chats = ref([])
  const currentUser = ref(null)
  const defaultPeers = []

  function mapChats(backendChats, allUsers, currentUserId) {
    const normalizeId = (id) => String(id || '').toLowerCase()
    const normalizedCurrentId = normalizeId(currentUserId)
    
    return backendChats.map((c) => {
      const chatId = c.chatId ?? c.ChatId
      const isGroup = c.isGroup ?? c.IsGroup
      const name = c.name ?? c.Name

      let displayName = name || 'Без названия'

      if (!isGroup) {
        const participantIds = c.participantUserIds ?? c.ParticipantUserIds
        const otherUserId = participantIds?.find((id) => normalizeId(id) !== normalizedCurrentId)

        if (otherUserId) {
          const otherUser = allUsers.find((u) => {
            const userId = u.userId || u.UserId
            return normalizeId(userId) === normalizeId(otherUserId)
          })
          if (otherUser) {
            displayName = otherUser.nickname || otherUser.Nickname || 'Собеседник'
          } else {
            // Если пользователь не найден, используем ID как запасной вариант
            displayName = 'Собеседник'
          }
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
      // Получаем текущего пользователя из sessionStorage
      const cached = sessionStorage.getItem('ois_current_user')
      if (cached) {
        currentUser.value = JSON.parse(cached)
      }
      
      if (!currentUser.value) {
        currentUser.value = await messengerApi.getOrCreateCurrentUser()
      }
      
      const currentUserId = currentUser.value.userId ?? currentUser.value.UserId

      let backendChats = await messengerApi.getChatsByUser(currentUserId)

      // If there are no chats yet, create starter dialogs with other users.
      if (!backendChats.length) {
        const currentNickname = (currentUser.value.nickname ?? currentUser.value.Nickname ?? '').toLowerCase()
        const peersToCreate = defaultPeers.filter((peerName) => peerName.toLowerCase() !== currentNickname)

        for (const peerName of peersToCreate) {
          await messengerApi.getOrCreateChatWithUser(currentUserId, peerName)
        }
        backendChats = await messengerApi.getChatsByUser(currentUserId)
      }

      const allUsers = await messengerApi.getUsers()
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
