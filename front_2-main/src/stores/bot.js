import { defineStore } from 'pinia'
import { ref } from 'vue'

export const useBotStore = defineStore('bot', () => {
  const isBotTyping = ref(false)
  const isBotOnline = ref(false)

  return {
    isBotTyping,
    isBotOnline,
  }
})
