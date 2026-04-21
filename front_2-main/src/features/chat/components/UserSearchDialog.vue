<script setup>
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

const props = defineProps({
  darkTheme: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits(['close', 'select-login'])

const query = ref('')
const searched = ref(false)
const isSearching = ref(false)
const errorText = ref('')
const results = ref([])

async function runSearch() {
  const login = query.value.trim()
  if (!login) {
    errorText.value = 'Введите login для поиска.'
    return
  }

  searched.value = true
  isSearching.value = true
  errorText.value = ''

  try {
    results.value = await messengerApi.searchUsersByLogin(login)
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Ошибка поиска.'
  } finally {
    isSearching.value = false
  }
}

function selectUser(user) {
  emit('select-login', user.login)
}
</script>

<template>
  <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
    <div class="w-full max-w-md p-6 rounded-2xl shadow-2xl" :class="darkTheme ? 'bg-[#1a1a1a]' : 'bg-white'">
      <h3 class="text-xl font-bold mb-4" :class="darkTheme ? 'text-white' : 'text-gray-800'">
        Найти по login
      </h3>

      <div class="flex gap-2 mb-3">
        <input
          v-model="query"
          type="text"
          class="flex-1 px-4 py-3 rounded-xl outline-none"
          :class="darkTheme ? 'bg-[#2a2a2a] text-white placeholder-gray-500' : 'bg-gray-100 text-gray-800 placeholder-gray-400'"
          placeholder="login"
          @keyup.enter="runSearch"
        >
        <button
          class="px-4 py-3 rounded-xl bg-blue-500 text-white hover:bg-blue-600 disabled:opacity-60"
          :disabled="isSearching"
          @click="runSearch"
        >
          {{ isSearching ? '...' : 'Найти' }}
        </button>
      </div>

      <div v-if="errorText" class="mb-3 text-sm text-red-400">
        {{ errorText }}
      </div>

      <div class="space-y-2 mb-4 max-h-[320px] overflow-y-auto">
        <button
          v-for="user in results"
          :key="user.userId"
          class="w-full p-3 rounded-xl flex items-center gap-3 transition-colors text-left"
          :class="darkTheme ? 'bg-[#2a2a2a] hover:bg-[#333]' : 'bg-gray-100 hover:bg-gray-200'"
          @click="selectUser(user)"
        >
          <div class="w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white text-sm font-bold">
            {{ (user.username || user.login || '?')[0].toUpperCase() }}
          </div>
          <div class="min-w-0">
            <div class="font-medium truncate" :class="darkTheme ? 'text-white' : 'text-gray-800'">{{ user.username }}</div>
            <div class="text-sm opacity-60" :class="darkTheme ? 'text-gray-400' : 'text-gray-500'">@{{ user.login }}</div>
          </div>
        </button>
      </div>

      <p v-if="searched && !isSearching && !results.length" class="mb-4 text-sm" :class="darkTheme ? 'text-gray-400' : 'text-gray-500'">
        Пользователь не найден.
      </p>

      <button
        class="w-full py-2 rounded-xl"
        :class="darkTheme ? 'bg-[#333] text-gray-400 hover:bg-[#444]' : 'bg-gray-200 text-gray-600 hover:bg-gray-300'"
        @click="emit('close')"
      >
        Закрыть
      </button>
    </div>
  </div>
</template>
