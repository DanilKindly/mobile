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
import { usePushStore } from '@/stores/push'
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
const pushStore = usePushStore()
const themeStore = useThemeStore()
const pushDebugEnabled = import.meta.env.VITE_PUSH_DEBUG_UI === '1'

const currentUser = ref(null)
const chatId = ref(null)
const chatName = ref('')
const activePeerId = ref(null)
const showUserSearch = ref(false)
const messagesContainer = ref(null)
const stickToBottom = ref(true)

const onlineUsers = ref({})
const lastSeenByUser = ref({})
const routeChatId = computed(() => String(route.params.chatId || ''))

let openChatRequestId = 0
let lastScrollTop = 0
let realtimeUnsubscribers = []
let olderLoadInFlight = false
let isOpeningChat = false
let userStartedOlderHistoryScroll = false

function handleLogout() {
  const userId = getCurrentUserId()
  if (userId) {
    messengerApi.setPresence(userId, false).catch(() => {})
  }
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

async function handleReconnectPush() {
  await pushStore.reconnectPush()
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

function isNearOlderEdge() {
  const el = messagesContainer.value
  if (!el) return false
  const threshold = 180
  return el.scrollTop <= threshold
}

function pinToLatestNow() {
  const el = messagesContainer.value
  if (!el) return
  el.scrollTop = el.scrollHeight
}

async function pinToLatest() {
  await nextTick()
  pinToLatestNow()
}

function dismissKeyboardIfNeeded() {
  if (window.innerWidth >= 1024) return

  const active = document.activeElement
  if (!active) return

  const tag = active.tagName?.toLowerCase()
  const isEditable = tag === 'input' || tag === 'textarea' || active.isContentEditable
  if (isEditable) {
    active.blur()
  }
}

function handleMessagesScroll() {
  const el = messagesContainer.value
  if (!el) return

  const delta = Math.abs(el.scrollTop - lastScrollTop)
  lastScrollTop = el.scrollTop
  if (delta > 8) {
    userStartedOlderHistoryScroll = true
    dismissKeyboardIfNeeded()
  }

  stickToBottom.value = isNearBottom()
  if (userStartedOlderHistoryScroll && !stickToBottom.value && isNearOlderEdge()) {
    loadOlderMessagesPreservingViewport()
  }
}

async function loadOlderMessagesPreservingViewport() {
  if (olderLoadInFlight || !chatId.value || !currentUser.value) return

  const state = messageStore.getPageState(chatId.value)
  if (!state.hasMoreOlder || state.isLoadingOlder) return

  const el = messagesContainer.value
  if (!el) return

  olderLoadInFlight = true
  const beforeHeight = el.scrollHeight

  try {
    const loaded = await messageStore.loadOlderMessagesByChatId(chatId.value, getCurrentUserId())
    if (!loaded) return
    await nextTick()
    const afterHeight = el.scrollHeight
    const delta = afterHeight - beforeHeight
    if (delta > 0) {
      el.scrollTop += delta
    }
  } finally {
    olderLoadInFlight = false
  }
}

function sanitizeIncomingChatName(nameValue) {
  const normalized = String(nameValue || '').trim()
  if (!normalized) return ''

  const lowered = normalized.toLowerCase()
  if (lowered === 'чат' || lowered === 'chat') {
    return ''
  }

  return normalized
}

function setImmediateChatName(targetChatId) {
  const fromQuery = sanitizeIncomingChatName(route.query.name)
  if (fromQuery) {
    chatName.value = fromQuery
  } else {
    const cached = chatStore.getChatById(targetChatId)
    chatName.value = cached?.name || 'Собеседник'
  }

  const currentUserId = getCurrentUserId()
  const cachedChat = chatStore.getChatById(targetChatId)
  const participantIds = cachedChat?.participantUserIds || []
  activePeerId.value = participantIds.find((id) => String(id) !== String(currentUserId)) || null
}

function resolveChatName(targetChatId) {
  const cached = chatStore.getChatById(targetChatId)
  if (cached?.name) {
    const currentUserId = getCurrentUserId()
    const participantIds = cached.participantUserIds || []
    activePeerId.value = participantIds.find((id) => String(id) !== String(currentUserId)) || null
    return cached.name
  }

  return chatName.value || 'Собеседник'
}

async function syncPresenceSnapshot() {
  const online = await messengerApi.getOnlineUsers()
  onlineUsers.value = (online || []).reduce((acc, userId) => {
    acc[String(userId).toLowerCase()] = true
    return acc
  }, {})
}

async function ensureRealtime() {
  await messengerApi.syncChatSubscriptions(chatStore.chats.map((c) => c.id))

  realtimeUnsubscribers.forEach((unsubscribe) => unsubscribe())
  realtimeUnsubscribers = []

  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('MessageCreated', onMessageCreated))
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('MessageUpdatedStatus', onMessageUpdatedStatus))
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('ChatPreviewChanged', onChatPreviewChanged))
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('PresenceChanged', onPresenceChanged))
}

const onMessageCreated = async (event) => {
  const message = event?.message || null
  if (!message) return

  const incomingChatId = message.chatId ?? message.ChatId
  if (!incomingChatId) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  if (event?.chatPreview) {
    chatStore.applyChatPreview(event.chatPreview, currentUserId)
  }

  chatStore.updatePreviewFromMessage(incomingChatId, message, currentUserId)
  messageStore.addBackendMessageToState(message, currentUserId)

  if (String(chatId.value).toLowerCase() === String(incomingChatId).toLowerCase()) {
    const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
    const isMine = senderUserId === String(currentUserId)
    if (!isMine) {
      await messengerApi.markMessagesAsRead(incomingChatId, currentUserId)
    }

    if (stickToBottom.value) {
      await pinToLatest()
    }

    return
  }

  const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
  const isMine = senderUserId === String(currentUserId)

  if (!isMine && document.hidden) {
    const permission = await getNotificationPermissionState()
    if (permission === 'granted') {
      const sourceChat = chatStore.getChatById(incomingChatId)
      const notification = showNewMessageNotification({
        title: sourceChat?.name || 'Новое сообщение',
        body: getMessagePreview(message) || 'Новое сообщение в чате',
        data: { chatId: incomingChatId },
      })

      if (notification) {
        notification.onclick = () => {
          window.focus()
          router.push({ path: `/chat/${incomingChatId}` })
          notification.close()
        }
      }
    }
  }
}

const onMessageUpdatedStatus = (event) => {
  const incomingChatId = event?.chatId
  const messageIds = event?.messageIds
  const readerUserId = event?.readerUserId
  if (!incomingChatId || !Array.isArray(messageIds) || messageIds.length === 0) return

  messageStore.markMessagesAsReadByIds(messageIds, readerUserId, getCurrentUserId(), incomingChatId)
}

const onChatPreviewChanged = (event) => {
  if (!event?.chatPreview) return
  chatStore.applyChatPreview(event.chatPreview, getCurrentUserId())
}

async function handleMessageMediaLoaded() {
  if (!stickToBottom.value) return
  await pinToLatest()
}

const onPresenceChanged = (userId, isOnline, lastSeenAt) => {
  if (typeof userId === 'object' && userId !== null) {
    const payload = userId
    userId = payload.userId
    isOnline = payload.isOnline
    lastSeenAt = payload.lastSeenAt
  }

  const normalized = String(userId || '').toLowerCase()
  if (!normalized) return

  if (isOnline) {
    onlineUsers.value = { ...onlineUsers.value, [normalized]: true }
    return
  }

  const next = { ...onlineUsers.value }
  delete next[normalized]
  onlineUsers.value = next

  if (lastSeenAt) {
    lastSeenByUser.value = { ...lastSeenByUser.value, [normalized]: lastSeenAt }
  }
}

async function openChat(targetChatId) {
  if (!targetChatId || !isValidGuid(targetChatId)) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  const requestId = ++openChatRequestId
  chatId.value = targetChatId
  messageStore.setActiveChat(targetChatId)
  setImmediateChatName(targetChatId)
  stickToBottom.value = true
  isOpeningChat = true
  userStartedOlderHistoryScroll = false

  try {
    const loadPromise = messageStore.loadLatestMessagesByChatId(targetChatId, currentUserId, 40)

    if (requestId !== openChatRequestId) return

    chatName.value = resolveChatName(targetChatId)
    if (requestId !== openChatRequestId) return

    await loadPromise
    await pinToLatest()

    // Do not block initial open on read receipt call.
    messengerApi.markMessagesAsRead(targetChatId, currentUserId)
      .catch((e) => console.error('Failed to mark messages as read:', e))
  } catch (e) {
    console.error('Failed to open chat:', e)
    if (requestId === openChatRequestId) {
      messageStore.clearMessages()
    }
  } finally {
    isOpeningChat = false
  }
}

async function handleSelectChat(chat) {
  router.push({
    path: `/chat/${chat.id}`,
    query: { name: chat.name },
  })
}

const peerStatusText = computed(() => {
  const peer = String(activePeerId.value || '').toLowerCase()
  if (!peer) return 'был(а) в сети --:--'

  if (onlineUsers.value[peer]) {
    return 'в сети'
  }

  const fallbackLastSeen = chatStore.getUserById(peer)?.lastSeenAt || null
  const lastSeen = lastSeenByUser.value[peer] || fallbackLastSeen
  if (!lastSeen) return 'был(а) в сети --:--'

  const dt = new Date(lastSeen)
  if (Number.isNaN(dt.getTime())) return 'был(а) в сети --:--'

  return `был(а) в сети ${dt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`
})

watch(
  () => messageStore.messages.length,
  async () => {
    if (isOpeningChat) return
    if (stickToBottom.value && !olderLoadInFlight) {
      await pinToLatest()
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

watch(
  () => chatStore.chats,
  () => {
    if (!chatId.value) return
    const currentChat = chatStore.getChatById(chatId.value)
    if (!currentChat) return

    const displayName = currentChat.name || 'Собеседник'
    if (!chatName.value || chatName.value === 'Собеседник' || chatName.value === 'чат') {
      chatName.value = displayName
    }

    const currentUserId = getCurrentUserId()
    const participantIds = currentChat.participantUserIds || []
    activePeerId.value = participantIds.find((id) => String(id) !== String(currentUserId)) || null
  },
  { deep: true },
)

onMounted(async () => {
  currentUser.value = messengerApi.getCurrentUser()
  if (!currentUser.value) {
    router.push('/')
    return
  }

  const shouldOpenFromRoute = routeChatId.value && isValidGuid(routeChatId.value)
  const openingChatPromise = shouldOpenFromRoute ? openChat(routeChatId.value) : Promise.resolve()

  if (chatStore.chats.length === 0) {
    chatStore.loadChats().catch((e) => console.error('Failed to load chats:', e))
  } else {
    // Refresh chats in background without blocking chat open.
    chatStore.loadChats().catch((e) => console.error('Failed to refresh chats:', e))
  }

  chatStore.ensureUsers().catch((e) => console.error('Failed to load users for status fallback:', e))

  await ensureRealtime()
  await Promise.all([
    messengerApi.bootstrapRealtimeSync(),
    syncPresenceSnapshot(),
    setupNotificationPermissionBootstrap(),
  ])

  await openingChatPromise
})

onBeforeUnmount(() => {
  realtimeUnsubscribers.forEach((unsubscribe) => unsubscribe())
  realtimeUnsubscribers = []
})

async function handleSendText(text) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  const clientMessageId = crypto.randomUUID()
  const sentAtClient = new Date().toISOString()

  messageStore.addOptimisticTextMessage(
    chatId.value,
    { clientMessageId, senderUserId, text, sentAtClient },
    senderUserId,
  )
  chatStore.updatePreviewFromMessage(
    chatId.value,
    { senderUserId, text, type: 0, sentAt: sentAtClient },
    senderUserId,
  )

  try {
    const result = await messengerApi.sendMessageReliable(chatId.value, text, senderUserId, {
      clientMessageId,
      sentAtClient,
      ackTimeoutMs: 2800,
      maxRetries: 2,
    })

    messageStore.markPendingMessageAsSent(chatId.value, clientMessageId, result.message, senderUserId)
    chatStore.updatePreviewFromMessage(chatId.value, result.message, senderUserId)
  } catch (e) {
    messageStore.markPendingMessageAsFailed(chatId.value, clientMessageId)
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
      :push-status="pushStore.status"
      :push-busy="pushStore.isBusy"
      :push-requires-home-screen="pushStore.requiresHomeScreen"
      :push-endpoint-masked="pushStore.endpointMasked"
      :push-last-error-code="pushStore.lastErrorCode"
      :show-push-debug="pushDebugEnabled"
      @select-chat="handleSelectChat"
      @logout="handleLogout"
      @create-chat="handleCreateChat"
      @reconnect-push="handleReconnectPush"
    />

    <div
      v-if="routeChatId"
      class="flex-1 h-screen flex flex-col overflow-hidden"
      :class="routeChatId ? 'flex' : 'hidden lg:flex'"
    >
      <ChatHeader :user-name="chatName || 'Собеседник'" :user-state="peerStatusText" :dark-theme="themeStore.darkTheme" :show-back="true" />

      <main
        :key="chatId || routeChatId"
        ref="messagesContainer"
        class="messages-container flex-1 px-4 sm:px-6 lg:px-8 py-3 overflow-y-auto"
        :class="themeStore.darkTheme ? 'bg-[#0E1621] dark-theme' : 'bg-white'"
        style="z-index: 50;"
        @scroll="handleMessagesScroll"
      >
        <div class="flex flex-col justify-end min-h-full">
          <Message
            v-for="msg in messageStore.messages"
            :key="msg.id || `${msg.time}-${msg.text}`"
            :message="msg"
            :dark-theme="themeStore.darkTheme"
            @media-loaded="handleMessageMediaLoaded"
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
