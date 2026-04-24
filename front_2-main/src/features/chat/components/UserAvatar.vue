<script setup>
import { computed, ref, watch } from 'vue'
import messengerApi from '@/api/messenger'

const props = defineProps({
  avatarUrl: {
    type: String,
    default: null,
  },
  name: {
    type: String,
    default: '',
  },
  sizeClass: {
    type: String,
    default: 'w-10 h-10 text-sm',
  },
  darkTheme: {
    type: Boolean,
    default: false,
  },
})

const resolvedUrl = ref('')
const failed = ref(false)

const fallbackLetter = computed(() => {
  const source = String(props.name || '?').trim()
  return (source[0] || '?').toUpperCase()
})

watch(
  () => props.avatarUrl,
  async (url) => {
    resolvedUrl.value = ''
    failed.value = false
    if (!url) return

    try {
      resolvedUrl.value = await messengerApi.getProtectedAssetObjectUrl(url)
    } catch {
      failed.value = true
    }
  },
  { immediate: true },
)
</script>

<template>
  <div
    :class="[
      'rounded-full flex-shrink-0 overflow-hidden flex items-center justify-center font-semibold text-white bg-gradient-to-br from-blue-500 to-cyan-400',
      sizeClass,
      darkTheme ? 'ring-1 ring-white/5' : 'ring-1 ring-black/5',
    ]"
  >
    <img
      v-if="resolvedUrl && !failed"
      :src="resolvedUrl"
      :alt="name || 'avatar'"
      class="w-full h-full object-cover"
      @error="failed = true"
    >
    <span v-else>{{ fallbackLetter }}</span>
  </div>
</template>
