<script setup>
import { computed, watch, onMounted, ref, onBeforeUnmount, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import ChatHeader from '../components/ChatHeader.vue'
import ChatInput from '../components/ChatInput.vue'
import Message from '../components/Message.vue'
import ChatList from '../components/ChatList.vue'
import UserSearchDialog from '../components/UserSearchDialog.vue'
import ProfilePanel from '../components/ProfilePanel.vue'
import { useChatStore } from '@/stores/chat'
import { useMessageStore } from '@/stores/message'
import { usePushStore } from '@/stores/push'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'
import { useTextMessageDelivery } from '@/shared/composables/useTextMessageDelivery'
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
const {
  hydrateDeliveryQueue,
  enqueueAndSendText,
  retryTextMessage,
  drainPendingTextQueue,
} = useTextMessageDelivery()
const pushDebugEnabled = import.meta.env.DEV && import.meta.env.VITE_PUSH_DEBUG_UI === '1'

const currentUser = ref(null)
const chatId = ref(null)
const chatName = ref('')
const activePeerId = ref(null)
const showUserSearch = ref(false)
const showProfilePanel = ref(false)
const messagesContainer = ref(null)
const stickToBottom = ref(true)
const openingPhase = ref('idle')

const onlineUsers = ref({})
const lastSeenByUser = ref({})
const routeChatId = computed(() => String(route.params.chatId || ''))

let openChatRequestId = 0
let lastScrollTop = 0
let realtimeUnsubscribers = []
let olderLoadInFlight = false
let userStartedOlderHistoryScroll = false
let isProgrammaticScroll = false
const hydratingRealtimeByChatId = ref({})
const activePeer = computed(() => chatStore.getUserById(activePeerId.value) || null)
const activePeerAvatarUrl = computed(() => activePeer.value?.avatarUrl || chatStore.getChatById(chatId.value)?.avatarUrl || null)

function traceOpenFlow(stage, payload = {}) {
  // Temporary debug trace for open-chat stabilization.
  console.debug('[OpenFlow]', stage, payload)
}

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

function getUnreadMessageKey(message) {
  const directId = message?.messageId ?? message?.MessageId ?? message?.id ?? message?.Id ?? null
  if (directId) {
    return `id:${String(directId)}`
  }

  const rawVersion = Number(message?.version ?? message?.Version ?? 0)
  if (Number.isFinite(rawVersion) && rawVersion > 0) {
    return `v:${rawVersion}`
  }

  const sender = String(message?.senderUserId ?? message?.SenderUserId ?? '')
  const sentAt = String(message?.sentAt ?? message?.SentAt ?? message?.sentAtClient ?? message?.SentAtClient ?? '')
  const type = String(message?.type ?? message?.Type ?? '')
  const text = String(message?.text ?? message?.Text ?? '')
  return `sig:${sender}:${sentAt}:${type}:${text}`
}

function handleCreateChat() {
  showUserSearch.value = true
}

async function handleReconnectPush() {
  await pushStore.reconnectPush()
}

function handleProfileUpdated(profile) {
  chatStore.applyUserProfile(profile)
  currentUser.value = chatStore.currentUser || messengerApi.getCurrentUser()
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
  isProgrammaticScroll = true
  el.scrollTop = el.scrollHeight
  lastScrollTop = el.scrollTop
  requestAnimationFrame(() => {
    isProgrammaticScroll = false
  })
}

async function pinToLatest() {
  await nextTick()
  pinToLatestNow()
}

async function requestAutoPin(source) {
  if (openingPhase.value !== 'anchored') {
    traceOpenFlow('blocked pin', { source, reason: 'not_anchored', phase: openingPhase.value })
    return
  }

  if (!stickToBottom.value) {
    traceOpenFlow('blocked pin', { source, reason: 'not_near_bottom' })
    return
  }

  traceOpenFlow('pin applied', { source })
  await pinToLatest()
}

function queueHydratingRealtime(chatIdValue, event) {
  const key = String(chatIdValue || '').toLowerCase()
  if (!key) return
  const current = hydratingRealtimeByChatId.value[key] || []
  hydratingRealtimeByChatId.value = {
    ...hydratingRealtimeByChatId.value,
    [key]: [...current, event],
  }
}

function drainHydratingRealtime(chatIdValue) {
  const key = String(chatIdValue || '').toLowerCase()
  if (!key) return []
  const queue = hydratingRealtimeByChatId.value[key] || []
  if (!queue.length) return []

  const next = { ...hydratingRealtimeByChatId.value }
  delete next[key]
  hydratingRealtimeByChatId.value = next
  return queue
}

function getSenderUserId(message) {
  return String(message?.senderUserId ?? message?.SenderUserId ?? '')
}

function applyMessageCreatedState(event, currentUserId, options = {}) {
  const message = event?.message || null
  if (!message) return
  const incomingChatId = message.chatId ?? message.ChatId
  if (!incomingChatId) return

  if (event?.chatPreview) {
    chatStore.applyChatPreview(event.chatPreview, currentUserId)
  }

  chatStore.updatePreviewFromMessage(incomingChatId, message, currentUserId)
  messageStore.addBackendMessageToState(message, currentUserId)

  if (String(chatId.value).toLowerCase() === String(incomingChatId).toLowerCase()) {
    const senderUserId = getSenderUserId(message)
    const isMine = senderUserId === String(currentUserId)
    if (!isMine && !options.skipReadAndUnreadOps) {
      chatStore.resetUnreadCount(incomingChatId)
    }
    return
  }

  const senderUserId = getSenderUserId(message)
  const isMine = senderUserId === String(currentUserId)
  if (!isMine) {
    const unreadKey = getUnreadMessageKey(message)
    chatStore.incrementUnreadCount(incomingChatId, unreadKey)
  }
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

  if (isProgrammaticScroll) {
    stickToBottom.value = isNearBottom()
    lastScrollTop = el.scrollTop
    return
  }

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
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('ConnectionReconnected', onConnectionReconnected))
}

const onMessageCreated = async (event) => {
  const message = event?.message || null
  if (!message) return

  const incomingChatId = message.chatId ?? message.ChatId
  if (!incomingChatId) return

  const currentUserId = getCurrentUserId()
  if (!currentUserId) return

  const isActiveChat = String(chatId.value).toLowerCase() === String(incomingChatId).toLowerCase()
  if (isActiveChat && openingPhase.value === 'hydrating') {
    queueHydratingRealtime(incomingChatId, event)
    traceOpenFlow('blocked pin', { source: 'realtime', reason: 'hydrating_queue' })
    return
  }

  applyMessageCreatedState(event, currentUserId)

  if (isActiveChat) {
    const senderUserId = getSenderUserId(message)
    const isMine = senderUserId === String(currentUserId)
    if (!isMine) {
      await messengerApi.markMessagesAsRead(incomingChatId, currentUserId)
    }

    await requestAutoPin('realtime')

    return
  }

  const senderUserId = getSenderUserId(message)
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

const onConnectionReconnected = async () => {
  await drainTextQueueWithViewport('reconnect')
}

async function handleMessageMediaLoaded() {
  await requestAutoPin('media')
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
  traceOpenFlow('open start', { chatId: targetChatId, requestId })
  chatId.value = targetChatId
  chatStore.resetUnreadCount(targetChatId)
  messageStore.setActiveChat(null)
  setImmediateChatName(targetChatId)
  stickToBottom.value = true
  openingPhase.value = 'hydrating'
  userStartedOlderHistoryScroll = false
  drainHydratingRealtime(targetChatId)

  try {
    await messageStore.loadLatestMessagesByChatId(targetChatId, currentUserId, 40)
    traceOpenFlow('messages loaded', {
      chatId: targetChatId,
      requestId,
      count: messageStore.getMessagesByChatId(targetChatId).length,
    })

    if (requestId !== openChatRequestId) return

    chatName.value = resolveChatName(targetChatId)
    if (requestId !== openChatRequestId) return

    const queued = drainHydratingRealtime(targetChatId)
    if (queued.length > 0) {
      for (const queuedEvent of queued) {
        applyMessageCreatedState(queuedEvent, currentUserId, { skipReadAndUnreadOps: true })
      }
      traceOpenFlow('queued realtime applied', {
        chatId: targetChatId,
        requestId,
        queuedCount: queued.length,
      })
    }

    messageStore.setActiveChat(targetChatId)
    await nextTick()
    pinToLatestNow()
    traceOpenFlow('anchor applied', { chatId: targetChatId, requestId })
    if (requestId !== openChatRequestId) return
    openingPhase.value = 'anchored'
    stickToBottom.value = true

    // Do not block initial open on read receipt call.
    messengerApi.markMessagesAsRead(targetChatId, currentUserId)
      .catch((e) => console.error('Failed to mark messages as read:', e))
  } catch (e) {
    console.error('Failed to open chat:', e)
    if (requestId === openChatRequestId) {
      messageStore.clearMessages()
      openingPhase.value = 'idle'
      traceOpenFlow('open failed', { chatId: targetChatId, requestId })
    }
  }
}

async function handleSelectChat(chat) {
  chatStore.resetUnreadCount(chat.id)
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
  () => {
    if (openingPhase.value === 'hydrating') {
      traceOpenFlow('blocked pin', { source: 'watcher', reason: 'hydrating' })
    }
  },
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
  hydrateDeliveryQueue()

  const shouldOpenFromRoute = routeChatId.value && isValidGuid(routeChatId.value)

  if (shouldOpenFromRoute) {
    traceOpenFlow('direct route open', { chatId: routeChatId.value })
    await openChat(routeChatId.value)
  }

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
  await drainTextQueueWithViewport('mounted')
})

onBeforeUnmount(() => {
  realtimeUnsubscribers.forEach((unsubscribe) => unsubscribe())
  realtimeUnsubscribers = []
})

async function handleSendText(text) {
  if (!chatId.value || !currentUser.value) return

  const result = await enqueueAndSendText(chatId.value, text, {
    onOptimistic: async () => {
      if (openingPhase.value === 'anchored' && stickToBottom.value) {
        await pinToLatest()
      }
    },
    onSent: async () => {
      if (openingPhase.value === 'anchored' && stickToBottom.value) {
        await pinToLatest()
      }
    },
  })

  if (!result.ok) {
    console.error('Failed to send text message:', result.error)
  }
}

async function handleRetryMessage(message) {
  const result = await retryTextMessage(message, {
    onSent: async () => {
      if (openingPhase.value === 'anchored' && stickToBottom.value) {
        await pinToLatest()
      }
    },
  })

  if (!result.ok && !result.skipped) {
    console.error('Failed to retry text message:', result.error)
  }
}

async function drainTextQueueWithViewport(reason) {
  await drainPendingTextQueue({
    reason,
    onSent: async () => {
      if (openingPhase.value === 'anchored' && stickToBottom.value) {
        await pinToLatest()
      }
    },
  })
}

async function handleSendVoice({ blob, durationSeconds, fileName }) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  try {
    const message = await messengerApi.sendVoiceMessage(chatId.value, senderUserId, blob, durationSeconds, fileName)
    messageStore.addBackendMessageToState(message, senderUserId)
    chatStore.updatePreviewFromMessage(chatId.value, message, senderUserId)
    await requestAutoPin('voice-send')
  } catch (e) {
    console.error('Failed to send voice message:', e)
    throw e
  }
}

async function handleSendMedia(file) {
  if (!chatId.value || !currentUser.value) return

  const senderUserId = getCurrentUserId()
  const clientMessageId = crypto.randomUUID()
  const sentAtClient = new Date().toISOString()
  const localUrl = URL.createObjectURL(file)

  messageStore.addOptimisticMediaMessage(
    chatId.value,
    {
      clientMessageId,
      senderUserId,
      sentAtClient,
      localUrl,
      contentType: file.type || 'application/octet-stream',
      fileName: file.name,
      sizeBytes: file.size,
    },
    senderUserId,
  )
  chatStore.updatePreviewFromMessage(
    chatId.value,
    { senderUserId, type: 2, sentAt: sentAtClient },
    senderUserId,
  )
  await requestAutoPin('media-send-optimistic')

  try {
    const message = await messengerApi.sendMediaMessage(chatId.value, senderUserId, file, {
      onUploadProgress: (event) => {
        const total = Number(event.total || file.size || 0)
        if (!total) return
        messageStore.updatePendingUploadProgress(
          chatId.value,
          clientMessageId,
          (Number(event.loaded || 0) / total) * 100,
        )
      },
    })
    messageStore.markPendingMessageAsSent(chatId.value, clientMessageId, message, senderUserId)
    chatStore.updatePreviewFromMessage(chatId.value, message, senderUserId)
    await requestAutoPin('media-send-complete')
  } catch (e) {
    messageStore.markPendingMessageAsFailed(chatId.value, clientMessageId)
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
      @open-profile="showProfilePanel = true"
    />

    <div
      v-if="routeChatId"
      class="flex-1 h-screen flex flex-col overflow-hidden"
      :class="routeChatId ? 'flex' : 'hidden lg:flex'"
    >
      <ChatHeader
        :user-name="chatName || 'Собеседник'"
        :user-state="peerStatusText"
        :avatar-url="activePeerAvatarUrl"
        :dark-theme="themeStore.darkTheme"
        :show-back="true"
      />

      <main
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
            @retry-message="handleRetryMessage"
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

    <ProfilePanel
      v-if="showProfilePanel && currentUser"
      :current-user="currentUser"
      :dark-theme="themeStore.darkTheme"
      @close="showProfilePanel = false"
      @profile-updated="handleProfileUpdated"
    />
  </div>
</template>
