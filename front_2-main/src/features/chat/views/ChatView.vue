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
import {
  getNotificationPermissionState,
  setupNotificationPermissionBootstrap,
  showNewMessageNotification,
} from '@/shared/services/notificationService'

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
const stickToBottom = ref(true)

const connection = messengerApi.getConnection()
const routeChatId = computed(() => String(route.params.chatId || ''))

let openChatRequestId = 0
let scrollTimers = []

function clearScrollTimers() {
  scrollTimers.forEach((id) => clearTimeout(id))
  scrollTimers = []
}

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
    await chatStore.loadChats()
    const created = chatStore.getChatById(createdChatId)
    showUserSearch.value = false

    router.push({
      path: `/chat/${createdChatId}`,
      query: { name: created?.name || '' },
    })
  } catch (e) {
    alert(e?.response?.data?.error || e?.message || 'Не удалось создать чат.')
  }
}

function isValidGuid(str) {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(str)
}

function isNearBottom() {
  const el = messagesContainer.value
  if (!el) return true
  const threshold = 120
  return el.scrollHeight - el.scrollTop - el.clientHeight <= threshold
}

function scrollToBottomNow() {
  const el = messagesContainer.value
  if (!el) return
  el.scrollTop = el.scrollHeight
}

async function scrollToBottomStable() {
  clearScrollTimers()

  await nextTick()
  scrollToBottomNow()

  scrollTimers.push(setTimeout(scrollToBottomNow, 60))
  scrollTimers.push(setTimeout(scrollToBottomNow, 180))
}

function handleMessagesScroll() {
  stickToBottom.value = isNearBottom()
}

function setImmediateChatName(targetChatId) {
  const fromQuery = typeof route.query.name === 'string' ? route.query.name.trim() : ''
  if (fromQuery) {
    chatName.value = fromQuery
    return
  }

  const cached = chatStore.getChatById(targetChatId)
  chatName.value = cached?.name || 'Собеседник'
}

async function resolveChatName(targetChatId) {
  const cached = chatStore.getChatById(targetChatId)
  if (cached?.name) return cached.name

  await chatStore.loadChats()
  const afterReload = chatStore.getChatById(targetChatId)
  return afterReload?.name || 'Собеседник'
}

async function ensureRealtime() {
  await messengerApi.syncChatSubscriptions(chatStore.chats.map((c) => c.id))

  connection.off('MessageReceived', onMessageReceived)
  connection.off('MessagesRead', onMessagesRead)

  connection.on('MessageReceived', onMessageReceived)
  connection.on('MessagesRead', onMessagesRead)
}

const onMessageReceived = async (message) => {
  const incomingChatId = message.chatId ?? message.ChatId
  if (!incomingChatId) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  chatStore.updatePreviewFromMessage(incomingChatId, message, currentUserId)

  const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
  const isMine = senderUserId === String(currentUserId)

  if (String(chatId.value).toLowerCase() === String(incomingChatId).toLowerCase()) {
    messageStore.addBackendMessageToState(message, currentUserId)

    if (!isMine) {
      await messengerApi.markMessagesAsRead(incomingChatId, currentUserId)
    }

    if (stickToBottom.value) {
      await scrollToBottomStable()
    }

    return
  }

  if (!isMine && document.hidden) {
    const permission = await getNotificationPermissionState()
    if (permission === 'granted') {
      const sourceChat = chatStore.getChatById(incomingChatId)
      showNewMessageNotification({
        title: sourceChat?.name || 'Новое сообщение',
        body: getMessagePreview(message) || 'Новое сообщение в чате',
        data: { chatId: incomingChatId },
      })
    }
  }
}

const onMessagesRead = (incomingChatId, messageIds, readerUserId) => {
  if (String(incomingChatId).toLowerCase() !== String(chatId.value).toLowerCase()) return
  messageStore.markMessagesAsReadByIds(messageIds, readerUserId, getCurrentUserId())
}

async function openChat(targetChatId) {
  if (!targetChatId || !isValidGuid(targetChatId)) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  const requestId = ++openChatRequestId
  isChatLoading.value = true
  chatId.value = targetChatId
  setImmediateChatName(targetChatId)

  try {
    await messengerApi.syncChatSubscriptions(chatStore.chats.map((c) => c.id))
    await messageStore.loadMessagesByChatId(targetChatId, currentUserId)

    if (requestId !== openChatRequestId) return

    chatName.value = await resolveChatName(targetChatId)
    if (requestId !== openChatRequestId) return

    await messengerApi.markMessagesAsRead(targetChatId, currentUserId)
    stickToBottom.value = true
    await scrollToBottomStable()
  } catch (e) {
    console.error('Failed to open chat:', e)
    if (requestId === openChatRequestId) {
      messageStore.clearMessages()
    }
  } finally {
    if (requestId === openChatRequestId) {
      isChatLoading.value = false
    }
  }
}

async function handleSelectChat(chat) {
  router.push({
    path: `/chat/${chat.id}`,
    query: { name: chat.name },
  })
}

watch(
  () => messageStore.messages.length,
  async () => {
    if (stickToBottom.value) {
      await scrollToBottomStable()
    }
  },
  { flush: 'post' },
)

watch(
  () => chatStore.chats.map((c) => c.id).join(','),
  async () => {
    await messengerApi.syncChatSubscriptions(chatStore.chats.map((c) => c.id))
  },
)

watch(
  routeChatId,
  (newChatId, oldChatId) => {
    if (!newChatId || newChatId === oldChatId || !isValidGuid(newChatId)) return
    openChat(newChatId)
  },
)

onMounted(async () => {
  currentUser.value = messengerApi.getCurrentUser()
  if (!currentUser.value) {
    router.push('/')
    return
  }

  await Promise.all([
    chatStore.loadChats(),
    ensureRealtime(),
    setupNotificationPermissionBootstrap(),
  ])

  if (routeChatId.value && isValidGuid(routeChatId.value)) {
    await openChat(routeChatId.value)
  }
})

onBeforeUnmount(() => {
  clearScrollTimers()
  connection.off('MessageReceived', onMessageReceived)
  connection.off('MessagesRead', onMessagesRead)
})

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
      @select-chat="handleSelectChat"
      @logout="handleLogout"
      @create-chat="handleCreateChat"
    />

    <div
      v-if="routeChatId"
      class="flex-1 h-screen flex flex-col overflow-hidden"
      :class="routeChatId ? 'flex' : 'hidden lg:flex'"
    >
      <ChatHeader :user-name="chatName || 'Собеседник'" user-state="в сети" :dark-theme="themeStore.darkTheme" :show-back="true" />

      <main
        ref="messagesContainer"
        class="messages-container flex-1 px-4 sm:px-6 lg:px-8 py-3 overflow-y-auto"
        :class="themeStore.darkTheme ? 'bg-[#0E1621] dark-theme' : 'bg-white'"
        style="z-index: 50;"
        @scroll="handleMessagesScroll"
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
