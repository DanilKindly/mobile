<script setup>
import { computed, watch, onMounted, ref, onBeforeUnmount } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChatHeader from '../components/ChatHeader.vue'
import ChatInput from '../components/ChatInput.vue'
import Message from '../components/Message.vue'
import ChatList from '../components/ChatList.vue'
import { useChatStore } from '@/stores/chat'
import { useMessageStore } from '@/stores/message'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'

const route = useRoute()
const router = useRouter()
const chatStore = useChatStore()
const messageStore = useMessageStore()
const themeStore = useThemeStore()
const currentUser = ref(null)
const chatId = ref(null)
const chatName = ref(null)
const messagesContainer = ref(null)
let signalRConnection = null

function handleLogout() {
  sessionStorage.removeItem('ois_current_user')
  router.push('/')
}

function getCurrentUserId() {
  return currentUser.value?.userId ?? currentUser.value?.UserId ?? null
}

function getMessagePreview(message) {
  const type = Number(message.type ?? message.Type ?? 0)
  if (type === 1) return 'Voice message'
  if (type === 2) return 'Media message'
  return message.text ?? message.Text ?? ''
}

async function handleCreateChat() {
  try {
    const allUsers = await messengerApi.getUsers()
    const currentUserId = getCurrentUserId()
    const otherUsers = allUsers
      .filter((u) => {
        const userId = u.userId || u.UserId
        return userId !== currentUserId
      })
      .map((u) => ({
        id: u.userId || u.UserId,
        nickname: u.nickname || u.Nickname,
        name: u.name || u.Name,
      }))

    if (otherUsers.length === 0) {
      alert('Нет других пользователей для создания чата')
      return
    }

    if (otherUsers.length === 1) {
      const createdChatId = await messengerApi.getOrCreateChatWithUser(currentUserId, otherUsers[0].nickname)
      router.push(`/chat/${createdChatId}`)
      return
    }

    const selected = prompt(`Выберите собеседника:\n${otherUsers.map((u) => `- ${u.nickname}`).join('\n')}`)
    if (selected) {
      const createdChatId = await messengerApi.getOrCreateChatWithUser(currentUserId, selected)
      router.push(`/chat/${createdChatId}`)
    }
  } catch (e) {
    console.error('Failed to create chat:', e)
  }
}

watch(
  () => messageStore.messages.length,
  () => {
    if (messagesContainer.value) {
      messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
    }
  },
  { flush: 'post' },
)

const routeChatId = computed(() => route.params.chatId)

onMounted(async () => {
  try {
    const cached = sessionStorage.getItem('ois_current_user')
    if (cached) {
      currentUser.value = JSON.parse(cached)
    }
    if (!currentUser.value) {
      currentUser.value = await messengerApi.getOrCreateCurrentUser()
    }
    await chatStore.loadChats()
  } catch (e) {
    console.error('Failed to init current user for backend:', e)
  }
})

onBeforeUnmount(async () => {
  if (chatId.value) {
    try {
      await messengerApi.leaveChat(chatId.value)
    } catch (e) {
      console.error('Failed to leave chat hub:', e)
    }
  }

  if (signalRConnection) {
    try {
      await signalRConnection.stop()
    } catch {
      // ignore
    }
  }
})

async function handleSendText(text) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  try {
    await messengerApi.sendMessageSignalR(chatId.value, text, senderUserId)
    setTimeout(() => {
      messengerApi.markMessagesAsRead(chatId.value)
    }, 100)
  } catch (e) {
    console.error('Failed to send text message:', e)
    throw e
  }
}

async function handleSendVoice({ blob, durationSeconds, fileName }) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  try {
    await messengerApi.sendVoiceMessage(chatId.value, senderUserId, blob, durationSeconds, fileName)
  } catch (e) {
    console.error('Failed to send voice message:', e)
    throw e
  }
}

async function handleSendMedia(file) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  try {
    await messengerApi.sendMediaMessage(chatId.value, senderUserId, file)
  } catch (e) {
    console.error('Failed to send media message:', e)
    throw e
  }
}

function isValidGuid(str) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(str)
}

watch(
  routeChatId,
  (newChatId, oldChatId) => {
    if (!newChatId || !isValidGuid(newChatId) || newChatId === oldChatId) return

    ;(async () => {
      try {
        if (!currentUser.value) {
          currentUser.value = await messengerApi.getOrCreateCurrentUser()
        }
        const currentUserId = getCurrentUserId()

        const chat = await messengerApi.getChatById(newChatId)
        chatId.value = chat.chatId ?? chat.ChatId

        const participantIds = chat.participantUserIds ?? chat.ParticipantUserIds
        const otherUserId = participantIds.find((id) => id !== currentUserId)
        if (otherUserId) {
          const users = await messengerApi.getUsers()
          const otherUser = users.find((u) => (u.userId || u.UserId) === otherUserId)
          chatName.value = otherUser ? otherUser.nickname || otherUser.Nickname : 'Собеседник'
        } else {
          chatName.value = chat.name ?? chat.Name ?? 'Чат'
        }

        await chatStore.loadChats()
        await messageStore.loadMessagesByChatId(chatId.value, currentUserId)

        if (!signalRConnection) {
          signalRConnection = messengerApi.getConnection()

          signalRConnection.on('MessageReceived', (message) => {
            const currentUserIdLocal = getCurrentUserId()
            messageStore.addBackendMessageToState(message, currentUserIdLocal)
            chatStore.updateLastMessage(chatId.value, getMessagePreview(message))
          })

          signalRConnection.on('MessagesRead', () => {
            messageStore.markMessagesAsRead()
          })

          signalRConnection.on('UserJoinedChat', (userId) => {
            const currentUserIdLocal = getCurrentUserId()
            if (userId !== currentUserIdLocal) {
              messageStore.markMessagesAsRead()
            }
          })
        }

        await messengerApi.joinChat(chatId.value)
      } catch (e) {
        console.error('Failed to switch chat or load data from backend:', e)
        messageStore.clearMessages()
      }
    })()
  },
  { immediate: true },
)
</script>

<template>
  <div class="h-screen flex relative" :class="themeStore.darkTheme ? 'bg-[#0E1621]' : 'bg-white'">
    <ChatList
      :class="routeChatId ? 'hidden lg:flex' : 'flex'"
      :chats="chatStore.chats"
      :selected-chat="routeChatId"
      :dark-theme="themeStore.darkTheme"
      :current-user="currentUser"
      @select-chat="(chat) => router.push(`/chat/${chat.id}`)"
      @logout="handleLogout"
      @create-chat="handleCreateChat"
    />

    <div
      v-if="routeChatId && chatName"
      class="flex-1 h-screen flex flex-col overflow-hidden"
      :class="routeChatId ? 'flex' : 'hidden lg:flex'"
    >
      <ChatHeader :user-name="chatName" user-state="чаты" :dark-theme="themeStore.darkTheme" :show-back="true" />

      <main
        ref="messagesContainer"
        class="messages-container flex-1 pb-[100px] px-[30px] py-[15px] overflow-y-auto"
        :class="themeStore.darkTheme ? 'bg-[#0E1621] dark-theme' : 'bg-white'"
        style="z-index: 50;"
      >
        <div class="flex flex-col justify-end min-h-full">
          <Message
            v-for="msg in messageStore.messages"
            :key="msg.id || `${msg.time}-${msg.text}`"
            :message="msg"
            :dark-theme="themeStore.darkTheme"
          />
        </div>
      </main>

      <ChatInput
        :dark-theme="themeStore.darkTheme"
        :selected-chat="chatName"
        @send-text="handleSendText"
        @send-voice="handleSendVoice"
        @send-media="handleSendMedia"
      />
    </div>

    <div
      v-else
      class="flex-1 h-screen flex items-center justify-center"
      :class="themeStore.darkTheme ? 'bg-[#0E1621]' : 'bg-white'"
    >
      <p :class="themeStore.darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]'">Выберите чат для начала общения</p>
    </div>
  </div>
</template>
