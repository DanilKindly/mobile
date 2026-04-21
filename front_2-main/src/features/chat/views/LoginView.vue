<script setup>
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'

const router = useRouter()
const themeStore = useThemeStore()

const users = ref([])
const isLoading = ref(true)
const showNewUserInput = ref(false)
const newUsername = ref('')

onMounted(async () => {
  try {
    const allUsers = await messengerApi.getUsers()
    users.value = allUsers.map(u => ({
      id: u.userId || u.UserId,
      nickname: u.nickname || u.Nickname,
      name: u.name || u.Name || (u.nickname || u.Nickname)
    }))
  } catch (e) {
    console.error('Failed to load users:', e)
  } finally {
    isLoading.value = false
  }
})

async function selectUser(user) {
  sessionStorage.setItem('ois_current_user', JSON.stringify({
    userId: user.id,
    nickname: user.nickname,
    name: user.name
  }))
  router.push('/chats')
}

async function createAndSelectUser() {
  if (!newUsername.value.trim()) return
  
  try {
    const newUser = await messengerApi.createUser({
      nickname: newUsername.value.trim(),
      name: newUsername.value.trim(),
      phoneNumber: null
    })
    
    sessionStorage.setItem('ois_current_user', JSON.stringify({
      userId: newUser.userId || newUser.UserId,
      nickname: newUser.nickname || newUser.Nickname,
      name: newUser.name || newUser.Name
    }))
    
    newUsername.value = ''
    showNewUserInput.value = false
    await loadUsers()
    router.push('/chats')
  } catch (e) {
    console.error('Failed to create user:', e)
  }
}

async function loadUsers() {
  try {
    const allUsers = await messengerApi.getUsers()
    users.value = allUsers.map(u => ({
      id: u.userId || u.UserId,
      nickname: u.nickname || u.Nickname,
      name: u.name || u.Name || (u.nickname || u.Nickname)
    }))
  } catch (e) {
    console.error('Failed to load users:', e)
  }
}

function handleKeyPress(event) {
  if (event.key === 'Enter') {
    createAndSelectUser()
  }
}
</script>

<template>
  <div class="h-screen flex items-center justify-center" :class="themeStore.darkTheme ? 'bg-[#0a0a0a]' : 'bg-gray-100'">
    <div class="w-full max-w-md p-8 rounded-2xl shadow-2xl" :class="themeStore.darkTheme ? 'bg-[#1a1a1a]' : 'bg-white'">
      <h1 class="text-3xl font-bold text-center mb-2" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">
        Мессенджер
      </h1>
      <p class="text-center mb-8" :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">
        Выберите пользователя для входа
      </p>

      <!-- Существующие пользователи -->
      <div v-if="!isLoading && users.length > 0" class="space-y-3 mb-6">
        <button
          v-for="user in users"
          :key="user.id"
          @click="selectUser(user)"
          class="w-full p-4 rounded-xl flex items-center gap-4 transition-all duration-200 hover:scale-[1.02]"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] hover:bg-[#333]' : 'bg-gray-50 hover:bg-gray-100'"
        >
          <div class="w-10 h-10 rounded-full bg-gradient-to-br from-blue-500 to-purple-500 flex items-center justify-center text-white font-bold">
            {{ user.nickname[0].toUpperCase() }}
          </div>
          <div class="text-left">
            <div class="font-medium" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">{{ user.name }}</div>
            <div class="text-sm" :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">@{{ user.nickname }}</div>
          </div>
        </button>
      </div>

      <p v-if="!isLoading && users.length === 0" class="text-center mb-6" :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">
        Нет пользователей
      </p>

      <!-- Кнопка создания нового -->
      <div v-if="!showNewUserInput">
        <button
          @click="showNewUserInput = true"
          class="w-full py-3 rounded-xl border-2 border-dashed transition-colors"
          :class="themeStore.darkTheme ? 'border-[#333] text-gray-400 hover:border-[#444] hover:text-white' : 'border-gray-300 text-gray-500 hover:border-gray-400 hover:text-gray-700'"
        >
          + Новый пользователь
        </button>
      </div>
      <div v-else class="flex gap-2">
        <input
          v-model="newUsername"
          @keyup.enter="createAndSelectUser"
          type="text"
          placeholder="Введите имя"
          class="flex-1 px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
          autofocus
        />
        <button
          @click="createAndSelectUser"
          class="px-6 py-3 rounded-xl bg-blue-500 text-white font-medium hover:bg-blue-600"
        >
          Войти
        </button>
        <button
          @click="showNewUserInput = false; newUsername = ''"
          class="px-4 py-3 rounded-xl"
          :class="themeStore.darkTheme ? 'bg-[#333] text-gray-400 hover:bg-[#444]' : 'bg-gray-200 text-gray-600 hover:bg-gray-300'"
        >
          ✕
        </button>
      </div>
    </div>
  </div>
</template>
