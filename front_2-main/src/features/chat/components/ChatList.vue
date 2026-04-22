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
  pushStatus: {
    type: String,
    default: 'subscribe_failed',
  },
  pushBusy: {
    type: Boolean,
    default: false,
  },
  pushRequiresHomeScreen: {
    type: Boolean,
    default: false,
  },
  pushEndpointMasked: {
    type: String,
    default: null,
  },
  pushLastErrorCode: {
    type: String,
    default: null,
  },
  showPushDebug: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits(['select-chat', 'logout', 'create-chat', 'reconnect-push'])
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

function reconnectPush() {
  emit('reconnect-push')
}

function displayPreview(chat) {
  const value = String(chat?.lastMessage || '').trim()
  return value || 'Нет сообщений'
}

function formatChatTime(value) {
  if (!value) return ''

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''

  const now = new Date()
  const sameDay =
    now.getFullYear() === date.getFullYear() &&
    now.getMonth() === date.getMonth() &&
    now.getDate() === date.getDate()

  if (sameDay) {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
  }

  return date.toLocaleDateString([], { day: '2-digit', month: '2-digit' })
}

function pushStatusLabel(value) {
  if (value === 'subscribed') return 'подключены'
  if (value === 'unsupported') return 'не поддерживаются'
  if (value === 'permission_denied') return 'запрещены'
  if (value === 'no_user') return 'нужен вход'
  if (value === 'push_not_configured') return 'сервер не настроен'
  return 'ошибка подключения'
}
</script>

<template>
  <aside
    :class="[
      'h-screen flex flex-col',
      'w-full max-w-full lg:w-[360px]',
      darkTheme ? 'bg-[#17212B]' : 'bg-white',
      'fixed inset-y-0 left-0 lg:relative z-20 transition-transform duration-300',
      selectedChat ? '-translate-x-full lg:translate-x-0' : 'translate-x-0',
    ]"
  >
    <div
      :class="[
        'px-4 py-3 border-b flex-shrink-0',
        darkTheme ? 'bg-[#17212B] border-[#182533]' : 'bg-white border-gray-200',
      ]"
    >
      <div class="flex items-center justify-between mb-3">
        <h2 :class="['text-[20px] font-semibold tracking-tight', darkTheme ? 'text-white' : 'text-black']">Чаты</h2>
        <button
          class="text-xs px-3 py-1.5 rounded-full bg-blue-500 text-white hover:bg-blue-600 transition-colors"
          @click="createChat"
        >
          + Чат
        </button>
      </div>
      <div v-if="currentUser" class="flex items-center gap-2.5">
        <div
          class="w-9 h-9 rounded-full bg-gradient-to-br from-blue-500 to-cyan-500 flex items-center justify-center text-white text-sm font-semibold"
        >
          {{ (currentUser.username || currentUser.login || '?')[0].toUpperCase() }}
        </div>
        <div class="flex-1 min-w-0">
          <div class="text-[13px] font-semibold truncate" :class="darkTheme ? 'text-white' : 'text-gray-900'">
            {{ currentUser.username || 'Пользователь' }}
          </div>
          <div class="text-[12px] truncate" :class="darkTheme ? 'text-gray-400' : 'text-gray-500'">
            @{{ currentUser.login || '-' }}
          </div>
        </div>
        <button
          class="text-xs px-2 py-1 rounded-full hover:opacity-80"
          :class="darkTheme ? 'text-gray-400 hover:bg-[#182533]' : 'text-gray-500 hover:bg-gray-100'"
          title="Выйти"
          @click="logout"
        >
          ×
        </button>
      </div>

      <div
        v-if="showPushDebug"
        class="mt-3 rounded-lg px-3 py-2 border"
        :class="darkTheme ? 'border-[#263748] bg-[#141f2b]' : 'border-gray-200 bg-gray-50'"
      >
        <div class="flex items-center justify-between gap-2">
          <span class="text-xs font-medium" :class="darkTheme ? 'text-gray-200' : 'text-gray-700'">Уведомления</span>
          <span class="text-[11px]" :class="pushStatus === 'subscribed' ? 'text-green-500' : (darkTheme ? 'text-amber-300' : 'text-amber-600')">
            {{ pushStatusLabel(pushStatus) }}
          </span>
        </div>
        <div
          v-if="pushRequiresHomeScreen"
          class="mt-1 text-[11px]"
          :class="darkTheme ? 'text-amber-300' : 'text-amber-700'"
        >
          Для iPhone открой через «На экран Домой».
        </div>
        <div
          v-if="pushEndpointMasked"
          class="mt-1 text-[11px] truncate"
          :class="darkTheme ? 'text-gray-400' : 'text-gray-500'"
          :title="pushEndpointMasked"
        >
          {{ pushEndpointMasked }}
        </div>
        <div
          v-if="pushLastErrorCode"
          class="mt-1 text-[11px] truncate"
          :class="darkTheme ? 'text-red-300' : 'text-red-600'"
        >
          {{ pushLastErrorCode }}
        </div>
        <button
          class="mt-2 text-[11px] px-2 py-1 rounded bg-blue-500 text-white hover:bg-blue-600 disabled:opacity-60"
          :disabled="pushBusy"
          @click="reconnectPush"
        >
          {{ pushBusy ? 'Подключаем...' : 'Переподключить уведомления' }}
        </button>
      </div>
    </div>

    <div
      class="flex-1 overflow-y-auto px-2 py-2"
      :class="darkTheme ? 'bg-[#111B25]' : 'bg-[#f7f8fa]'"
    >
      <div
        v-for="chat in chats"
        :key="chat.id"
        class="cursor-pointer rounded-xl px-3 py-2.5 mb-1.5 border transition-all duration-150"
        :class="[
          darkTheme ? 'border-transparent hover:bg-[#1b2835]' : 'border-transparent hover:bg-[#eef2f7]',
          isChatSelected(chat.id)
            ? (darkTheme ? 'bg-[#223244] border-[#2d4560]' : 'bg-[#e6f0ff] border-[#cfe1ff]')
            : '',
        ]"
        @click="selectChat(chat)"
      >
        <div class="flex items-center gap-3 min-w-0">
          <div
            class="w-[48px] h-[48px] rounded-full flex-shrink-0 bg-gradient-to-br from-blue-500 to-cyan-400 flex items-center justify-center text-white text-[17px] font-semibold"
          >
            {{ chat.name[0]?.toUpperCase() || '?' }}
          </div>
          <div class="min-w-0 flex-1">
            <div class="flex items-center justify-between gap-2">
              <div :class="['text-[15px] font-semibold truncate', darkTheme ? 'text-white' : 'text-gray-900']">
                {{ chat.name }}
              </div>
              <div class="flex flex-col items-end gap-1 flex-shrink-0 min-w-[44px]">
                <div
                  v-if="formatChatTime(chat.lastMessageSentAt)"
                  class="text-[11px]"
                  :class="darkTheme ? 'text-[#8ea2b6]' : 'text-[#7f8a9a]'"
                >
                  {{ formatChatTime(chat.lastMessageSentAt) }}
                </div>
                <div
                  v-if="Number(chat.unreadCount || 0) > 0"
                  class="min-w-[20px] h-[20px] px-[6px] rounded-full text-[11px] font-semibold flex items-center justify-center"
                  :class="darkTheme ? 'bg-blue-500 text-white' : 'bg-blue-500 text-white'"
                >
                  {{ Number(chat.unreadCount) > 99 ? '99+' : chat.unreadCount }}
                </div>
              </div>
            </div>
            <div :class="['text-[13px] truncate mt-0.5', darkTheme ? 'text-[#93a7bb]' : 'text-[#6f7d8f]']">
              {{ displayPreview(chat) }}
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
