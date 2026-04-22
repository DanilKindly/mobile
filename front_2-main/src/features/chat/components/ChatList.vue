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
