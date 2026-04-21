<script setup>
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import { useThemeStore } from '@/stores/theme'

const props = defineProps({
  chats: {
    type: Array,
    required: true
  },
  selectedChat: {
    type: String,
    default: null
  },
  darkTheme: {
    type: Boolean,
    default: false
  },
  currentUser: {
    type: Object,
    default: null
  }
})

const emit = defineEmits(['select-chat', 'logout', 'create-chat'])

const router = useRouter()
const themeStore = useThemeStore()

function selectChat(chat) {
  emit('select-chat', chat)
}

function toggleTheme() {
  themeStore.toggleTheme()
}

function clearStorageAndReload() {
  localStorage.clear()
  window.location.reload()
}

function logout() {
  emit('logout')
}

function createChat() {
  emit('create-chat')
}

// Вычисляем имя выбранного чата для подсветки
const selectedChatName = computed(() => {
  if (!props.selectedChat) return null
  const chat = props.chats.find(c => c.id === props.selectedChat)
  return chat ? chat.name : null
})
</script>

<template>
  <aside :class="[
    'h-screen flex flex-col',
    'w-[280px] lg:w-[320px]',
    darkTheme ? 'bg-[#17212B]' : 'bg-white',
    'fixed lg:relative z-20 transition-transform duration-300',
    selectedChat ? '-translate-x-full lg:translate-x-0' : 'translate-x-0'
  ]">
    <!-- Шапка с текущим пользователем -->
    <div :class="[
      'p-[15px] border-b flex-shrink-0',
      darkTheme ? 'bg-[#17212B] border-[#182533]' : 'bg-white border-gray-200'
    ]">
      <div class="flex items-center justify-between mb-2">
        <h2 :class="['text-[18px] font-medium', darkTheme ? 'text-white' : 'text-black']">Чаты</h2>
        <button
          @click="createChat"
          class="text-xs px-3 py-1 rounded-lg bg-blue-500 text-white hover:bg-blue-600 transition-colors"
        >
          + Чат
        </button>
      </div>
      <div v-if="currentUser" class="flex items-center gap-2">
        <div class="w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white text-sm font-bold">
          {{ (currentUser.nickname || '?')[0].toUpperCase() }}
        </div>
        <div class="flex-1 min-w-0">
          <div class="text-sm font-medium truncate" :class="darkTheme ? 'text-white' : 'text-gray-800'">
            {{ currentUser.nickname || 'Пользователь' }}
          </div>
        </div>
        <button
          @click="logout"
          class="text-xs px-2 py-1 rounded hover:opacity-80"
          :class="darkTheme ? 'text-gray-400 hover:bg-[#182533]' : 'text-gray-500 hover:bg-gray-100'"
          title="Выйти"
        >
          ✕
        </button>
      </div>
    </div>
    
    <!-- Список чатов (прокручивается) -->
    <div class="flex-1 overflow-y-auto">
      <div
        v-for="chat in chats"
        :key="chat.id"
        @click="selectChat(chat)"
        :class="[
          'p-[15px] cursor-pointer',
          darkTheme ? 'hover:bg-[#182533]' : 'hover:bg-[#f5f5f5]',
          selectedChat === chat.id ? (darkTheme ? 'bg-[#182533]' : 'bg-[#d0d0d0]') : ''
        ]"
      >
        <div class="flex items-center gap-[10px]">
          <div class="w-[40px] h-[40px] rounded-full bg-[#e3f2fd] flex items-center justify-center text-[18px] font-medium">
            {{ chat.name[0].toUpperCase() }}
          </div>
          <div>
            <div :class="['text-[16px] font-medium', darkTheme ? 'text-white' : 'text-black']">{{ chat.name }}</div>
            <div :class="['text-[13px]', darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]']">{{ chat.lastMessage || 'Нет сообщений' }}</div>
          </div>
        </div>
      </div>
    </div>
    
    <!-- Подвал с настройками (не прокручивается) -->
    <div :class="[
      'p-[15px] border-t flex-shrink-0',
      darkTheme ? 'bg-[#17212B] border-[#182533]' : 'bg-[#f5f5f5] border-[#d0d0d0]'
    ]">
      <div class="flex items-center justify-between gap-[10px] mb-[10px]">
        <span :class="['text-[14px]', darkTheme ? 'text-[#6D7F8F]' : 'text-[#666]']">Тёмная тема</span>
        <button
          @click="toggleTheme"
          :class="[
            'relative w-[50px] h-[26px] rounded-[13px] transition-colors',
            darkTheme ? 'bg-[#4CAF50]' : 'bg-[#ccc]'
          ]"
        >
          <div :class="[
            'absolute top-[3px] w-[20px] h-[20px] rounded-full bg-white transition-transform',
            darkTheme ? 'left-[27px]' : 'left-[3px]'
          ]"></div>
        </button>
      </div>
      <button
        @click="clearStorageAndReload"
        :class="[
          'w-full text-[13px] py-[8px] rounded-[8px] transition-colors border',
          darkTheme ? 'bg-[#182533] text-[#6D7F8F] hover:bg-[#1a3a5c] border-[#182533]' : 'bg-[#fff] text-[#666] hover:bg-[#e0e0e0] border-[#ccc]'
        ]"
      >
        Сбросить данные
      </button>
    </div>
  </aside>
</template>
