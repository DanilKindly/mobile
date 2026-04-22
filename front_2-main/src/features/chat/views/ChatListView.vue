<script setup>
import { onBeforeUnmount, onMounted, computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import ChatList from '../components/ChatList.vue'
import UserSearchDialog from '../components/UserSearchDialog.vue'
import { useChatStore } from '@/stores/chat'
import { usePushStore } from '@/stores/push'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'
import {
  getNotificationPermissionState,
  setupNotificationPermissionBootstrap,
  showNewMessageNotification,
} from '@/shared/services/notificationService'

const router = useRouter()
const chatStore = useChatStore()
const pushStore = usePushStore()
const themeStore = useThemeStore()
const pushDebugEnabled = import.meta.env.DEV && import.meta.env.VITE_PUSH_DEBUG_UI === '1'

const currentUser = computed(() => chatStore.currentUser)
const showUserSearch = ref(false)
let realtimeUnsubscribers = []

function getMessagePreview(message) {
  const type = Number(message.type ?? message.Type ?? 0)
  if (type === 1) return 'Голосовое сообщение'
  if (type === 2) return 'Медиафайл'
  return (message.text ?? message.Text ?? '').trim()
}

const onMessageCreated = async (event) => {
  const message = event?.message || null
  if (!message) return
  const chatId = message.chatId ?? message.ChatId
  const currentUserId = currentUser.value?.userId
  if (!chatId || !currentUserId) return

  if (event?.chatPreview) {
    chatStore.applyChatPreview(event.chatPreview, currentUserId)
  }

  chatStore.updatePreviewFromMessage(chatId, message, currentUserId)

  const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
  if (senderUserId === String(currentUserId)) return

  chatStore.incrementUnreadCount(chatId)

  if (document.hidden) {
    const permission = await getNotificationPermissionState()
    if (permission === 'granted') {
      const sourceChat = chatStore.getChatById(chatId)
      const notification = showNewMessageNotification({
        title: sourceChat?.name || 'Новое сообщение',
        body: getMessagePreview(message) || 'Новое сообщение в чате',
        data: { chatId },
      })

      if (notification) {
        notification.onclick = () => {
          window.focus()
          router.push({ path: `/chat/${chatId}` })
          notification.close()
        }
      }
    }
  }
}

const onChatPreviewChanged = (event) => {
  if (!event?.chatPreview) return
  chatStore.applyChatPreview(event.chatPreview, currentUser.value?.userId)
}

async function ensureRealtime() {
  const ids = chatStore.chats.map((c) => c.id)
  await messengerApi.syncChatSubscriptions(ids)
  await messengerApi.bootstrapRealtimeSync()

  realtimeUnsubscribers.forEach((unsubscribe) => unsubscribe())
  realtimeUnsubscribers = []
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('MessageCreated', onMessageCreated))
  realtimeUnsubscribers.push(messengerApi.subscribeRealtime('ChatPreviewChanged', onChatPreviewChanged))
}

async function handleSelectChat(chat) {
  chatStore.resetUnreadCount(chat.id)
  router.push({
    path: `/chat/${chat.id}`,
    query: { name: chat.name },
  })
}

function handleLogout() {
  const userId = currentUser.value?.userId
  if (userId) {
    messengerApi.setPresence(userId, false).catch(() => {})
  }
  messengerApi.logout()
  router.push('/')
}

function handleCreateChat() {
  showUserSearch.value = true
}

async function selectLoginAndCreateChat(login) {
  try {
    const currentUserId = currentUser.value.userId
    const chatId = await messengerApi.getOrCreateChatWithUserByLogin(currentUserId, login)
    await chatStore.loadChats()
    const created = chatStore.getChatById(chatId)
    showUserSearch.value = false

    router.push({
      path: `/chat/${chatId}`,
      query: { name: created?.name || '' },
    })
  } catch (e) {
    alert(e?.response?.data?.error || e?.message || 'Не удалось создать чат.')
  }
}

async function handleReconnectPush() {
  await pushStore.reconnectPush()
}

onMounted(async () => {
  const rememberedUser = messengerApi.getCurrentUser()
  if (!rememberedUser) {
    router.push('/')
    return
  }

  if (!chatStore.currentUser) {
    chatStore.currentUser = rememberedUser
  }

  if (chatStore.chats.length === 0) {
    await chatStore.loadChats()
  } else {
    chatStore.loadChats().catch((e) => console.error('Failed to refresh chats:', e))
  }

  await ensureRealtime()
  await setupNotificationPermissionBootstrap()
})

watch(
  () => chatStore.chats.map((c) => c.id).join(','),
  async () => {
    await messengerApi.syncChatSubscriptions(chatStore.chats.map((c) => c.id))
  },
)

onBeforeUnmount(() => {
  realtimeUnsubscribers.forEach((unsubscribe) => unsubscribe())
  realtimeUnsubscribers = []
})
</script>

<template>
  <div class="h-screen flex" :class="themeStore.darkTheme ? 'bg-[#0a0a0a]' : 'bg-white'">
    <div v-if="!currentUser" class="flex items-center justify-center w-full">
      <p :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">Загрузка...</p>
    </div>
    <ChatList
      v-else
      :chats="chatStore.chats"
      :selected-chat="null"
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

    <UserSearchDialog
      v-if="showUserSearch"
      :dark-theme="themeStore.darkTheme"
      @close="showUserSearch = false"
      @select-login="selectLoginAndCreateChat"
    />
  </div>
</template>
