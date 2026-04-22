<script setup>
import { computed, watch, onMounted, ref, onBeforeUnmount, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChatHeader from '../components/ChatHeader.vue'
import ChatInput from '../components/ChatInput.vue'
import Message from '../components/Message.vue'
import ChatList from '../components/ChatList.vue'
import UserSearchDialog from '../components/UserSearchDialog.vue'
import { useChatStore } from '@/stores/chat'
import { useMessageStore } from '@/stores/message'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'
import { requestNotificationsPermissionOnce, showNewMessageNotification } from '@/shared/services/notificationService'

const route = useRoute()
const router = useRouter()
const chatStore = useChatStore()
const messageStore = useMessageStore()
const themeStore = useThemeStore()

const currentUser = ref(null)
const chatId = ref(null)
const chatName = ref('')
const isChatLoading = ref(false)
const showUserSearch = ref(false)
const messagesContainer = ref(null)
let activeChatGroupId = null

const connection = messengerApi.getConnection()

function handleLogout() {
  messengerApi.logout()
  router.push('/')
}

function getCurrentUserId() {
  return currentUser.value?.userId ?? null
}

function getMessagePreview(message) {
  const type = Number(message.type ?? message.Type ?? 0)
  if (type === 1) return 'Голосовое сообщение'
  if (type === 2) return 'Медиафайл'
  return (message.text ?? message.Text ?? '').trim()
}

function handleCreateChat() {
  showUserSearch.value = true
}

async function selectLoginAndCreateChat(login) {
  try {
    const currentUserId = getCurrentUserId()
    const createdChatId = await messengerApi.getOrCreateChatWithUserByLogin(currentUserId, login)
    showUserSearch.value = false
    router.push(`/chat/${createdChatId}`)
  } catch (e) {
    alert(e?.response?.data?.error || e?.message || 'Не удалось создать чат.')
  }
}

watch(
  () => messageStore.messages.length,
  async () => {
    await nextTick()
    if (messagesContainer.value) {
      messagesContainer.value.scrollTop = messagesContainer.value.scrollHeight
    }
  },
  { flush: 'post' },
)

const routeChatId = computed(() => route.params.chatId)

const onMessageReceived = async (message) => {
  const incomingChatId = message.chatId ?? message.ChatId
  if (!incomingChatId) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  chatStore.updatePreviewFromMessage(incomingChatId, message, currentUserId)

  const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
  const isMine = senderUserId === String(currentUserId)

  if (String(chatId.value) === String(incomingChatId)) {
    messageStore.addBackendMessageToState(message, currentUserId)

    if (!isMine) {
      await messengerApi.markMessagesAsRead(incomingChatId, currentUserId)
    }

    return
  }

  if (!isMine && document.hidden) {
    const sourceChat = chatStore.getChatById(incomingChatId)
    showNewMessageNotification({
      title: sourceChat?.name || 'Новое сообщение',
      body: getMessagePreview(message) || 'Новое сообщение в чате',
      data: { chatId: incomingChatId },
    })
  }
}

const onMessagesRead = (incomingChatId, messageIds, readerUserId) => {
  if (String(incomingChatId) !== String(chatId.value)) return
  messageStore.markMessagesAsReadByIds(messageIds, readerUserId, getCurrentUserId())
}

async function ensureRealtime() {
  if (connection.state !== 'Connected') {
    await connection.start()
  }

  connection.off('MessageReceived', onMessageReceived)
  connection.off('MessagesRead', onMessagesRead)

  connection.on('MessageReceived', onMessageReceived)
  connection.on('MessagesRead', onMessagesRead)
}

async function resolveChatName(targetChatId) {
  const cached = chatStore.getChatById(targetChatId)
  if (cached?.name) return cached.name

  const chat = await messengerApi.getChatById(targetChatId)
  const currentUserId = getCurrentUserId()

  const participantIds = chat.participantUserIds ?? chat.ParticipantUserIds ?? []
  const otherUserId = participantIds.find((id) => String(id) !== String(currentUserId))
  if (otherUserId) {
    const users = await messengerApi.getUsers()
    const otherUser = users.find((u) => String(u.userId) === String(otherUserId))
    return otherUser ? otherUser.username || otherUser.login : 'Собеседник'
  }

  return chat.name ?? chat.Name ?? 'Чат'
}

function isValidGuid(str) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(str)
}

async function openChat(targetChatId) {
  if (!targetChatId || !isValidGuid(targetChatId)) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  isChatLoading.value = true
  chatId.value = targetChatId

  const cachedChat = chatStore.getChatById(targetChatId)
  chatName.value = cachedChat?.name || 'Чат'

  try {
    if (activeChatGroupId && String(activeChatGroupId) !== String(targetChatId)) {
      await messengerApi.leaveChat(activeChatGroupId)
    }

    await Promise.all([
      messengerApi.joinChat(targetChatId),
      messageStore.loadMessagesByChatId(targetChatId, currentUserId),
    ])

    activeChatGroupId = targetChatId

    if (!cachedChat?.name) {
      chatName.value = await resolveChatName(targetChatId)
    }

    await messengerApi.markMessagesAsRead(targetChatId, currentUserId)
  } catch (e) {
    console.error('Failed to open chat:', e)
    messageStore.clearMessages()
  } finally {
    isChatLoading.value = false
  }
}

onMounted(async () => {
  currentUser.value = messengerApi.getCurrentUser()
  if (!currentUser.value) {
    router.push('/')
    return
  }

  await Promise.all([
    chatStore.loadChats(),
    ensureRealtime(),
    requestNotificationsPermissionOnce(),
  ])

  if (routeChatId.value && isValidGuid(routeChatId.value)) {
    await openChat(routeChatId.value)
  }
})

onBeforeUnmount(async () => {
  if (activeChatGroupId) {
    try {
      await messengerApi.leaveChat(activeChatGroupId)
    } catch {
      // ignore
    }
  }

  connection.off('MessageReceived', onMessageReceived)
  connection.off('MessagesRead', onMessagesRead)
})

watch(
  routeChatId,
  (newChatId, oldChatId) => {
    if (!newChatId || newChatId === oldChatId || !isValidGuid(newChatId)) return
    openChat(newChatId)
  },
  { immediate: true },
)

async function handleSendText(text) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  try {
    await messengerApi.sendMessageSignalR(chatId.value, text, senderUserId)
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
      v-if="routeChatId"
      class="flex-1 h-screen flex flex-col overflow-hidden"
      :class="routeChatId ? 'flex' : 'hidden lg:flex'"
    >
      <ChatHeader :user-name="chatName || 'Чат'" user-state="в сети" :dark-theme="themeStore.darkTheme" :show-back="true" />

      <main
        ref="messagesContainer"
        class="messages-container flex-1 pb-[100px] px-[30px] py-[15px] overflow-y-auto"
        :class="themeStore.darkTheme ? 'bg-[#0E1621] dark-theme' : 'bg-white'"
        style="z-index: 50;"
      >
        <div class="flex flex-col justify-end min-h-full">
          <div v-if="isChatLoading && !messageStore.messages.length" class="mx-auto text-sm" :class="themeStore.darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]'">
            Загружаем сообщения...
          </div>

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

    <UserSearchDialog
      v-if="showUserSearch"
      :dark-theme="themeStore.darkTheme"
      @close="showUserSearch = false"
      @select-login="selectLoginAndCreateChat"
    />
  </div>
</template>
