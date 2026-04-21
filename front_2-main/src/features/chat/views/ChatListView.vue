<script setup>
import { onMounted, computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import ChatList from '../components/ChatList.vue'
import UserSearchDialog from '../components/UserSearchDialog.vue'
import { useChatStore } from '@/stores/chat'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'

const router = useRouter()
const chatStore = useChatStore()
const themeStore = useThemeStore()

const currentUser = computed(() => chatStore.currentUser)
const showUserSearch = ref(false)

async function handleSelectChat(chat) {
  router.push(`/chat/${chat.id}`)
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
    showUserSearch.value = false
    router.push(`/chat/${chatId}`)
  } catch (e) {
    alert(e?.response?.data?.error || e?.message || 'Не удалось создать чат.')
  }
}

onMounted(() => {
  chatStore.loadChats()
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
