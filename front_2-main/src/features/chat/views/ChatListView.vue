<script setup>
import { onBeforeUnmount, onMounted, computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import ChatList from '../components/ChatList.vue'
import UserSearchDialog from '../components/UserSearchDialog.vue'
import { useChatStore } from '@/stores/chat'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'
import {
  getNotificationPermissionState,
  setupNotificationPermissionBootstrap,
  showNewMessageNotification,
} from '@/shared/services/notificationService'

const router = useRouter()
const chatStore = useChatStore()
const themeStore = useThemeStore()

const currentUser = computed(() => chatStore.currentUser)
const showUserSearch = ref(false)
const connection = messengerApi.getConnection()

function getMessagePreview(message) {
  const type = Number(message.type ?? message.Type ?? 0)
  if (type === 1) return 'Голосовое сообщение'
  if (type === 2) return 'Медиафайл'
  return (message.text ?? message.Text ?? '').trim()
}

const onMessageReceived = async (message) => {
  const chatId = message.chatId ?? message.ChatId
  const currentUserId = currentUser.value?.userId
  if (!chatId || !currentUserId) return

  chatStore.updatePreviewFromMessage(chatId, message, currentUserId)

  const senderUserId = String(message.senderUserId ?? message.SenderUserId ?? '')
  if (senderUserId === String(currentUserId)) return

  if (document.hidden) {
    const permission = await getNotificationPermissionState()
    if (permission === 'granted') {
      const sourceChat = chatStore.getChatById(chatId)
      showNewMessageNotification({
        title: sourceChat?.name || 'Новое сообщение',
        body: getMessagePreview(message) || 'Новое сообщение в чате',
        data: { chatId },
      })
    }
  }
}

async function ensureRealtime() {
  const ids = chatStore.chats.map((c) => c.id)
  await messengerApi.syncChatSubscriptions(ids)

  connection.off('MessageReceived', onMessageReceived)
  connection.on('MessageReceived', onMessageReceived)
}

async function handleSelectChat(chat) {
  router.push({
    path: `/chat/${chat.id}`,
    query: { name: chat.name },
  })
}

function handleLogout() {
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

onMounted(async () => {
  await chatStore.loadChats()
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
  connection.off('MessageReceived', onMessageReceived)
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
      @select-chat="handleSelectChat"
      @logout="handleLogout"
      @create-chat="handleCreateChat"
    />

    <UserSearchDialog
      v-if="showUserSearch"
      :dark-theme="themeStore.darkTheme"
      @close="showUserSearch = false"
      @select-login="selectLoginAndCreateChat"
    />
  </div>
</template>
