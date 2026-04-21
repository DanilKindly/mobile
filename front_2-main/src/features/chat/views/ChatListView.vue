<script setup>
import { onMounted, computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import ChatList from '../components/ChatList.vue'
import { useChatStore } from '@/stores/chat'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'

const router = useRouter()
const chatStore = useChatStore()
const themeStore = useThemeStore()

const currentUser = computed(() => chatStore.currentUser)
const users = ref([])
const showUserSelect = ref(false)

async function handleSelectChat(chat) {
  router.push(`/chat/${chat.id}`)
}

function handleLogout() {
  sessionStorage.removeItem('ois_current_user')
  router.push('/')
}

async function handleCreateChat() {
  try {
    const allUsers = await messengerApi.getUsers()
    users.value = allUsers.filter(u => {
      const userId = u.userId || u.UserId
      const currentUserId = currentUser.value?.userId || currentUser.value?.UserId
      return userId !== currentUserId
    }).map(u => ({
      id: u.userId || u.UserId,
      nickname: u.nickname || u.Nickname,
      name: u.name || u.Name
    }))
    showUserSelect.value = true
  } catch (e) {
    console.error('Failed to load users:', e)
  }
}

async function selectUserAndCreateChat(user) {
  try {
    const currentUserId = currentUser.value.userId || currentUser.value.UserId
    const chatId = await messengerApi.getOrCreateChatWithUser(currentUserId, user.nickname)
    await chatStore.loadChats()
    showUserSelect.value = false
    router.push(`/chat/${chatId}`)
  } catch (e) {
    console.error('Failed to create chat:', e)
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
    
    <!-- Модальное окно выбора пользователя -->
    <div v-if="showUserSelect" class="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
      <div class="w-full max-w-md p-6 rounded-2xl shadow-2xl" :class="themeStore.darkTheme ? 'bg-[#1a1a1a]' : 'bg-white'">
        <h3 class="text-xl font-bold mb-4" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">
          Выберите собеседника
        </h3>
        <div class="space-y-2 mb-4">
          <button
            v-for="user in users"
            :key="user.id"
            @click="selectUserAndCreateChat(user)"
            class="w-full p-3 rounded-xl flex items-center gap-3 transition-colors"
            :class="themeStore.darkTheme ? 'bg-[#2a2a2a] hover:bg-[#333]' : 'bg-gray-100 hover:bg-gray-200'"
          >
            <div class="w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white text-sm font-bold">
              {{ user.nickname[0].toUpperCase() }}
            </div>
            <div class="text-left flex-1">
              <div class="font-medium" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">{{ user.name }}</div>
              <div class="text-sm opacity-60" :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">@{{ user.nickname }}</div>
            </div>
          </button>
        </div>
        <button
          @click="showUserSelect = false"
          class="w-full py-2 rounded-xl"
          :class="themeStore.darkTheme ? 'bg-[#333] text-gray-400 hover:bg-[#444]' : 'bg-gray-200 text-gray-600 hover:bg-gray-300'"
        >
          Отмена
        </button>
      </div>
    </div>
  </div>
</template>
