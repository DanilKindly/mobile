<script setup>
import { useRouter } from 'vue-router'

defineProps({
  userName: {
    type: String,
    required: true,
  },
  userState: {
    type: String,
    default: 'был в сети',
  },
  darkTheme: {
    type: Boolean,
    default: false,
  },
  showBack: {
    type: Boolean,
    default: false,
  },
})

const router = useRouter()

function goBack() {
  router.push('/chats')
}
</script>

<template>
  <header
    :class="[
      'flex items-center justify-between px-4 sm:px-6 lg:px-8 py-3 w-full',
      darkTheme ? 'bg-[#17212B]' : 'bg-white',
    ]"
  >
    <div class="flex items-center gap-[10px] min-w-0">
      <button v-if="showBack" @click="goBack" class="lg:hidden flex-shrink-0">
        <img src="/src/assets/icons/back.svg" class="w-[30px]" :class="darkTheme ? 'invert' : ''" alt="Назад">
      </button>
      <div class="user min-w-0">
        <div class="text-[20px] truncate" :class="darkTheme ? 'text-white' : 'text-black'">{{ userName }}</div>
        <div class="text-[13px] ml-[2px]" :class="darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]'">{{ userState }}</div>
      </div>
    </div>
    <div class="flex items-center gap-[15px] flex-shrink-0">
      <button type="button">
        <img src="/src/assets/icons/search.svg" class="w-[30px]" :class="darkTheme ? 'invert' : ''" alt="Поиск">
      </button>
    </div>
  </header>
</template>
