<script setup>
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useThemeStore } from '@/stores/theme'
import messengerApi from '@/api/messenger'

const router = useRouter()
const themeStore = useThemeStore()

const activeMode = ref('login')
const isLoading = ref(false)
const errorText = ref('')

const loginForm = ref({
  login: '',
  password: '',
})

const registerForm = ref({
  login: '',
  password: '',
  username: '',
})

const title = computed(() => (activeMode.value === 'login' ? 'Вход' : 'Регистрация'))

function validateLoginInput(login) {
  if (!login?.trim()) return 'Введите ваш логин.'
  if (login.trim().length < 3) return 'Логин должен быть не короче 3 символов.'
  if (!/^[a-zA-Z0-9_-]+$/.test(login.trim())) return 'Логин может содержать только буквы, цифры, "_" и "-".'
  return ''
}

function validatePasswordInput(password) {
  if (!password?.trim()) return 'Введите ваш пароль.'
  if (password.trim().length < 6) return 'Пароль должен быть не короче 6 символов.'
  return ''
}

function validateUsernameInput(username) {
  if (!username?.trim()) return 'Введите ваше имя.'
  if (username.trim().length < 2) return 'Имя должно быть не короче 2 символов.'
  return ''
}

async function submitLogin() {
  errorText.value = ''

  const loginError = validateLoginInput(loginForm.value.login)
  if (loginError) {
    errorText.value = loginError
    return
  }

  const passwordError = validatePasswordInput(loginForm.value.password)
  if (passwordError) {
    errorText.value = passwordError
    return
  }

  try {
    isLoading.value = true
    await messengerApi.loginUser({
      login: loginForm.value.login.trim(),
      password: loginForm.value.password,
    })
    router.push('/chats')
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Не удалось войти. Проверьте логин и пароль.'
  } finally {
    isLoading.value = false
  }
}

async function submitRegister() {
  errorText.value = ''

  const loginError = validateLoginInput(registerForm.value.login)
  if (loginError) {
    errorText.value = loginError
    return
  }

  const passwordError = validatePasswordInput(registerForm.value.password)
  if (passwordError) {
    errorText.value = passwordError
    return
  }

  const usernameError = validateUsernameInput(registerForm.value.username)
  if (usernameError) {
    errorText.value = usernameError
    return
  }

  try {
    isLoading.value = true
    await messengerApi.registerUser({
      login: registerForm.value.login.trim(),
      password: registerForm.value.password,
      username: registerForm.value.username.trim(),
    })
    router.push('/chats')
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Не удалось зарегистрироваться.'
  } finally {
    isLoading.value = false
  }
}
</script>

<template>
  <div class="h-screen flex items-center justify-center" :class="themeStore.darkTheme ? 'bg-[#0a0a0a]' : 'bg-gray-100'">
    <div class="w-full max-w-md p-8 rounded-2xl shadow-2xl" :class="themeStore.darkTheme ? 'bg-[#1a1a1a]' : 'bg-white'">
      <div class="flex justify-center mb-4">
        <img src="/logo-mark.png" alt="Kindly messenger" class="w-20 h-20 rounded-2xl object-contain">
      </div>
      <h1 class="text-3xl font-bold text-center mb-2" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">
        Kindly messenger
      </h1>
      <p class="text-center mb-6" :class="themeStore.darkTheme ? 'text-gray-400' : 'text-gray-500'">
        {{ title }}
      </p>

      <div class="flex gap-2 mb-6">
        <button
          class="flex-1 py-2 rounded-xl transition-colors"
          :class="activeMode === 'login'
            ? 'bg-blue-500 text-white'
            : (themeStore.darkTheme ? 'bg-[#2a2a2a] text-gray-300' : 'bg-gray-200 text-gray-600')"
          @click="activeMode = 'login'; errorText = ''"
        >
          Войти
        </button>
        <button
          class="flex-1 py-2 rounded-xl transition-colors"
          :class="activeMode === 'register'
            ? 'bg-blue-500 text-white'
            : (themeStore.darkTheme ? 'bg-[#2a2a2a] text-gray-300' : 'bg-gray-200 text-gray-600')"
          @click="activeMode = 'register'; errorText = ''"
        >
          Регистрация
        </button>
      </div>

      <form v-if="activeMode === 'login'" class="space-y-3" @submit.prevent="submitLogin">
        <input
          v-model="loginForm.login"
          type="text"
          placeholder="Ваш логин"
          class="w-full px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
        >
        <input
          v-model="loginForm.password"
          type="password"
          placeholder="Ваш пароль"
          class="w-full px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
        >
        <button
          class="w-full py-3 rounded-xl bg-blue-500 text-white font-medium hover:bg-blue-600 disabled:opacity-60"
          :disabled="isLoading"
          type="submit"
        >
          {{ isLoading ? 'Входим...' : 'Войти' }}
        </button>
      </form>

      <form v-else class="space-y-3" @submit.prevent="submitRegister">
        <input
          v-model="registerForm.login"
          type="text"
          placeholder="Ваш логин"
          class="w-full px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
        >
        <input
          v-model="registerForm.username"
          type="text"
          placeholder="Ваше имя"
          class="w-full px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
        >
        <input
          v-model="registerForm.password"
          type="password"
          placeholder="Ваш пароль"
          class="w-full px-4 py-3 rounded-xl outline-none"
          :class="themeStore.darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
        >
        <button
          class="w-full py-3 rounded-xl bg-blue-500 text-white font-medium hover:bg-blue-600 disabled:opacity-60"
          :disabled="isLoading"
          type="submit"
        >
          {{ isLoading ? 'Создаем аккаунт...' : 'Зарегистрироваться' }}
        </button>
      </form>

      <p v-if="errorText" class="mt-4 text-sm text-red-400">
        {{ errorText }}
      </p>
    </div>
  </div>
</template>
