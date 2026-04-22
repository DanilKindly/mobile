<script setup>
import { useThemeStore } from '@/stores/theme'

const props = defineProps({
  chats: {
    type: Array,
    required: true,
  },
  selectedChat: {
    type: String,
    default: null,
  },
  darkTheme: {
    type: Boolean,
    default: false,
  },
  currentUser: {
    type: Object,
    default: null,
  },
})

const emit = defineEmits(['select-chat', 'logout', 'create-chat'])
const themeStore = useThemeStore()

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

function isChatSelected(chatId) {
  return normalizeId(chatId) === normalizeId(props.selectedChat)
}

function selectChat(chat) {
  emit('select-chat', chat)
}

function toggleTheme() {
  themeStore.toggleTheme()
}

function logout() {
  emit('logout')
}

function createChat() {
  emit('create-chat')
}
</script>

<template>
  <aside
    :class="[
      'h-screen flex flex-col',
      'w-full max-w-full lg:w-[320px]',
      darkTheme ? 'bg-[#17212B]' : 'bg-white',
      'fixed inset-y-0 left-0 lg:relative z-20 transition-transform duration-300',
      selectedChat ? '-translate-x-full lg:translate-x-0' : 'translate-x-0',
    ]"
  >
    <div
      :class="[
        'p-[15px] border-b flex-shrink-0',
        darkTheme ? 'bg-[#17212B] border-[#182533]' : 'bg-white border-gray-200',
      ]"
    >
      <div class="flex items-center justify-between mb-2">
        <h2 :class="['text-[18px] font-medium', darkTheme ? 'text-white' : 'text-black']">Чаты</h2>
        <button
          class="text-xs px-3 py-1 rounded-lg bg-blue-500 text-white hover:bg-blue-600 transition-colors"
          @click="createChat"
        >
          + Чат
        </button>
      </div>
      <div v-if="currentUser" class="flex items-center gap-2">
        <div
          class="w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white text-sm font-bold"
        >
          {{ (currentUser.username || currentUser.login || '?')[0].toUpperCase() }}
        </div>
        <div class="flex-1 min-w-0">
          <div class="text-sm font-medium truncate" :class="darkTheme ? 'text-white' : 'text-gray-800'">
            {{ currentUser.username || 'Пользователь' }}
          </div>
          <div class="text-xs truncate" :class="darkTheme ? 'text-gray-400' : 'text-gray-500'">
            @{{ currentUser.login || '-' }}
          </div>
        </div>
        <button
          class="text-xs px-2 py-1 rounded hover:opacity-80"
          :class="darkTheme ? 'text-gray-400 hover:bg-[#182533]' : 'text-gray-500 hover:bg-gray-100'"
          title="Выйти"
          @click="logout"
        >
          ×
        </button>
      </div>
    </div>

    <div class="flex-1 overflow-y-auto">
      <div
        v-for="chat in chats"
        :key="chat.id"
        class="p-[15px] cursor-pointer"
        :class="[
          darkTheme ? 'hover:bg-[#182533]' : 'hover:bg-[#f5f5f5]',
          isChatSelected(chat.id) ? (darkTheme ? 'bg-[#182533]' : 'bg-[#d0d0d0]') : '',
        ]"
        @click="selectChat(chat)"
      >
        <div class="flex items-center gap-[10px]">
          <div class="w-[40px] h-[40px] rounded-full bg-[#e3f2fd] flex items-center justify-center text-[18px] font-medium">
            {{ chat.name[0]?.toUpperCase() || '?' }}
          </div>
          <div class="min-w-0">
            <div :class="['text-[16px] font-medium truncate', darkTheme ? 'text-white' : 'text-black']">{{ chat.name }}</div>
            <div :class="['text-[13px] truncate', darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]']">
              {{ chat.lastMessage || 'Нет сообщений' }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <div
      class="chat-list-bottom-controls"
      :class="[
        'p-[15px] border-t flex-shrink-0',
        darkTheme ? 'bg-[#17212B] border-[#182533]' : 'bg-[#f5f5f5] border-[#d0d0d0]',
      ]"
    >
      <div class="flex items-center justify-between gap-[10px]">
        <span :class="['text-[14px]', darkTheme ? 'text-[#6D7F8F]' : 'text-[#666]']">Темная тема</span>
        <button
          class="relative w-[50px] h-[26px] rounded-[13px] transition-colors"
          :class="darkTheme ? 'bg-[#4CAF50]' : 'bg-[#ccc]'"
          @click="toggleTheme"
        >
          <div
            :class="[
              'absolute top-[3px] w-[20px] h-[20px] rounded-full bg-white transition-transform',
              darkTheme ? 'left-[27px]' : 'left-[3px]',
            ]"
          />
        </button>
      </div>
    </div>
  </aside>
</template>

<style scoped>
@media (max-width: 1023px) {
  .chat-list-bottom-controls {
    padding-bottom: calc(18px + env(safe-area-inset-bottom));
  }
}
</style>
