<script setup>
import { computed, ref } from 'vue'

const props = defineProps({
  message: {
    type: Object,
    required: true,
  },
  darkTheme: {
    type: Boolean,
    default: false,
  },
})
const emit = defineEmits(['media-loaded'])

const voiceAudioRef = ref(null)
const isVoicePlaying = ref(false)

const isBot = computed(() => !!props.message.isBot)
const isRead = computed(() => !!props.message.isRead)
const type = computed(() => Number(props.message.type ?? 0))
const isText = computed(() => type.value === 0)
const isVoice = computed(() => type.value === 1)
const isMedia = computed(() => type.value === 2)

const mediaContentType = computed(() => props.message.mediaContentType || '')
const isImage = computed(() => mediaContentType.value.startsWith('image/'))
const isVideo = computed(() => mediaContentType.value.startsWith('video/'))
const isAudioFile = computed(() => mediaContentType.value.startsWith('audio/'))

const formattedDuration = computed(() => {
  const duration = Number(props.message.audioDurationSeconds || 0)
  if (!duration) return ''
  const min = Math.floor(duration / 60)
  const sec = String(duration % 60).padStart(2, '0')
  return `${min}:${sec}`
})

function toggleVoicePlayback() {
  const audio = voiceAudioRef.value
  if (!audio) return
  if (audio.paused) {
    audio.play()
  } else {
    audio.pause()
  }
}

function onVoicePlay() {
  isVoicePlaying.value = true
}

function onVoicePause() {
  isVoicePlaying.value = false
}

function onMediaLoaded() {
  emit('media-loaded')
}
</script>

<template>
  <div
    :class="[
      'px-[15px] py-[10px] rounded-[10px] mb-[10px] max-w-[78%] break-words',
      isBot ? (darkTheme ? 'bg-[#182533] mr-auto' : 'bg-[#e3f2fd] mr-auto') : (darkTheme ? 'bg-[#2B5278] ml-auto' : 'bg-[#f0f0f0] ml-auto'),
    ]"
  >
    <div class="flex flex-col">
      <span v-if="isText" :class="darkTheme ? 'text-white' : 'text-black'">{{ message.text }}</span>

      <div v-if="isVoice" class="flex flex-col gap-1">
        <div class="flex items-center gap-2">
          <button
            class="text-xs px-3 py-1 rounded-[12px]"
            :class="darkTheme ? 'bg-[#182533] text-white' : 'bg-white text-black'"
            @click="toggleVoicePlayback"
          >
            {{ isVoicePlaying ? 'Пауза' : 'Воспроизвести' }}
          </button>
          <span class="text-xs" :class="darkTheme ? 'text-[#B9C4CE]' : 'text-[#4d4d4d]'">
            {{ formattedDuration || 'Голосовое' }}
          </span>
        </div>
        <audio
          ref="voiceAudioRef"
          :src="message.audioUrl"
          preload="metadata"
          class="hidden"
          @play="onVoicePlay"
          @pause="onVoicePause"
          @ended="onVoicePause"
        />
      </div>

      <div v-if="isMedia" class="flex flex-col gap-2">
        <img
          v-if="isImage"
          :src="message.mediaUrl"
          alt="media"
          class="max-w-[260px] max-h-[260px] rounded-[8px] object-cover"
          @load="onMediaLoaded"
        >

        <video
          v-else-if="isVideo"
          :src="message.mediaUrl"
          controls
          class="max-w-[260px] max-h-[260px] rounded-[8px]"
          @loadedmetadata="onMediaLoaded"
        />

        <audio
          v-else-if="isAudioFile"
          :src="message.mediaUrl"
          controls
          class="max-w-[260px]"
          @loadedmetadata="onMediaLoaded"
        />

        <a
          v-else
          :href="message.mediaUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="px-3 py-2 rounded-[8px] text-sm underline"
          :class="darkTheme ? 'bg-[#182533] text-white' : 'bg-white text-black'"
        >
          {{ message.mediaFileName || 'Открыть файл' }}
        </a>
      </div>

      <div class="flex items-center justify-end gap-[8px] mt-[4px]">
        <span
          v-if="!isBot && message.deliveryStatus === 0"
          class="text-[11px]"
          :class="darkTheme ? 'text-[#8fb3d9]' : 'text-[#5a88bf]'"
        >
          Отправка...
        </span>
        <span
          v-if="!isBot && message.deliveryStatus === 2"
          class="text-[11px] text-red-400"
        >
          Не отправлено
        </span>
        <span :class="['text-[11px]', darkTheme ? 'text-[#6D7F8F]' : 'text-[#666]']">{{ message.time }}</span>
        <div v-if="!isBot && message.deliveryStatus !== 2" class="flex items-center relative w-[18px] h-[12px]">
          <img src="/src/assets/icons/readFlagDark.svg" class="absolute w-[12px] h-[12px]" v-if="darkTheme" alt="read">
          <img src="/src/assets/icons/readFlag.svg" class="absolute w-[12px] h-[12px]" v-else alt="read">
          <img src="/src/assets/icons/readFlagDark.svg" class="absolute w-[12px] h-[12px] left-[5px]" v-if="darkTheme && isRead" alt="read">
          <img src="/src/assets/icons/readFlag.svg" class="absolute w-[12px] h-[12px] left-[5px]" v-else-if="isRead" alt="read">
        </div>
      </div>
    </div>
  </div>
</template>
