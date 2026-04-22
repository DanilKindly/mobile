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
const showRegisterSuccess = ref(false)

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

    messengerApi.logout()
    showRegisterSuccess.value = true
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Не удалось зарегистрироваться.'
  } finally {
    isLoading.value = false
  }
}

function goToLoginAfterRegister() {
  showRegisterSuccess.value = false
  activeMode.value = 'login'
  errorText.value = ''
  loginForm.value.login = registerForm.value.login
  loginForm.value.password = ''
}
</script>

<template>
  <div class="h-screen flex items-center justify-center" :class="themeStore.darkTheme ? 'bg-[#0a0a0a]' : 'bg-gray-100'">
    <div class="w-full max-w-md p-8 rounded-2xl shadow-2xl" :class="themeStore.darkTheme ? 'bg-[#1a1a1a]' : 'bg-white'">
      <div class="flex justify-center mb-4">
        <div class="km-logo" role="img" aria-label="Kindly Messenger logo">
          <img src="/logo-mark.png" alt="" class="km-logo__layer km-logo__outer">
          <img src="/logo-mark.png" alt="" class="km-logo__layer km-logo__k">
          <img src="/logo-mark.png" alt="" class="km-logo__layer km-logo__m">
          <span class="km-logo__glow" aria-hidden="true"></span>
        </div>
      </div>
      <h1 class="text-3xl font-bold text-center mb-2" :class="themeStore.darkTheme ? 'text-white' : 'text-gray-800'">
        Kindly Messenger
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

    <div
      v-if="showRegisterSuccess"
      class="fixed inset-0 bg-black/40 flex items-center justify-center px-4"
      style="z-index: 200"
    >
      <div
        class="w-full max-w-sm rounded-2xl p-6 shadow-2xl"
        :class="themeStore.darkTheme ? 'bg-[#1a1a1a] text-white' : 'bg-white text-gray-900'"
      >
        <h2 class="text-xl font-semibold mb-2">Регистрация прошла успешно</h2>
        <p :class="themeStore.darkTheme ? 'text-gray-400 mb-5' : 'text-gray-600 mb-5'">
          Аккаунт создан. Теперь выполните вход в мессенджер.
        </p>
        <button
          class="w-full py-3 rounded-xl bg-blue-500 text-white font-medium hover:bg-blue-600"
          @click="goToLoginAfterRegister"
        >
          Войти в мессенджер
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.km-logo {
  position: relative;
  width: 5rem;
  height: 5rem;
  isolation: isolate;
  animation: logo-settle 300ms cubic-bezier(0.2, 0.8, 0.25, 1) 980ms both;
}

.km-logo__layer {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: contain;
  will-change: transform, opacity, filter;
  pointer-events: none;
}

.km-logo__outer {
  clip-path: polygon(5% 18%, 40% 0%, 66% 0%, 95% 18%, 95% 82%, 66% 100%, 36% 100%, 5% 82%);
  animation: logo-outer-reveal 520ms cubic-bezier(0.16, 0.84, 0.2, 1) 40ms both;
}

.km-logo__k {
  clip-path: polygon(8% 8%, 56% 8%, 44% 53%, 8% 70%);
  animation: logo-inner-k 460ms cubic-bezier(0.2, 0.9, 0.25, 1) 360ms both;
}

.km-logo__m {
  clip-path: polygon(30% 42%, 91% 17%, 92% 96%, 60% 96%);
  animation: logo-inner-m 440ms cubic-bezier(0.2, 0.9, 0.25, 1) 620ms both;
}

.km-logo__glow {
  position: absolute;
  inset: -12%;
  border-radius: 1.3rem;
  background: radial-gradient(circle at center, rgba(37, 99, 235, 0.28), rgba(37, 99, 235, 0) 68%);
  opacity: 0;
  filter: blur(6px);
  z-index: -1;
  animation: logo-glow 320ms ease-out 1020ms both;
}

@keyframes logo-outer-reveal {
  0% {
    opacity: 0;
    transform: scale(0.9) translateY(4px);
    filter: blur(2px);
  }
  70% {
    opacity: 1;
    transform: scale(1.02) translateY(0);
    filter: blur(0);
  }
  100% {
    opacity: 1;
    transform: scale(1);
    filter: blur(0);
  }
}

@keyframes logo-inner-k {
  0% {
    opacity: 0;
    transform: translateX(-7px) scale(0.97);
    filter: blur(2px);
  }
  100% {
    opacity: 1;
    transform: translateX(0) scale(1);
    filter: blur(0);
  }
}

@keyframes logo-inner-m {
  0% {
    opacity: 0;
    transform: translateX(8px) scale(0.97);
    filter: blur(2px);
  }
  100% {
    opacity: 1;
    transform: translateX(0) scale(1);
    filter: blur(0);
  }
}

@keyframes logo-settle {
  0% {
    transform: scale(1);
  }
  45% {
    transform: scale(1.03);
  }
  100% {
    transform: scale(1);
  }
}

@keyframes logo-glow {
  0% {
    opacity: 0;
  }
  45% {
    opacity: 0.42;
  }
  100% {
    opacity: 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .km-logo,
  .km-logo__outer,
  .km-logo__k,
  .km-logo__m,
  .km-logo__glow {
    animation: none !important;
    opacity: 1 !important;
    transform: none !important;
    filter: none !important;
  }
}
</style>
