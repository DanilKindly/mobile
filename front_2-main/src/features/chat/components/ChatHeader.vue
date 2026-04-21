<script setup>
import { useRouter } from 'vue-router'

defineProps({
  userName: {
    type: String,
    required: true
  },
  userState: {
    type: String,
    default: 'был в сети'
  },
  darkTheme: {
    type: Boolean,
    default: false
  },
  showBack: {
    type: Boolean,
    default: false
  }
})

const router = useRouter()

function goBack() {
  router.push('/')
}
</script>

<template>
  <header :class="[
    'flex items-center justify-between px-[30px] py-[15px] h-[10vh] w-full',
    darkTheme ? 'bg-[#17212B]' : 'bg-white'
  ]">
    <div class="flex items-center gap-[10px]">
      <button @click="goBack" v-if="showBack" class="lg:hidden">
        <img src="/src/assets/icons/back.svg" class="w-[30px]" :class="darkTheme ? 'invert' : ''" alt="Назад">
      </button>
      <div class="user">
        <div :class="['text-[20px]', darkTheme ? 'text-white' : 'text-black']">{{ userName }}</div>
        <div :class="['text-[13px] ml-[2px]', darkTheme ? 'text-[#6D7F8F]' : 'text-[#868686]']">{{ userState }}</div>
      </div>
    </div>
    <div class="flex items-center gap-[15px]">
      <button>
        <img src="/src/assets/icons/search.svg" class="w-[30px]" :class="darkTheme ? 'invert' : ''" alt="Поиск">
      </button>
    </div>
  </header>
</template>
