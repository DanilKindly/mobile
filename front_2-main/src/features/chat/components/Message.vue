<script setup>
import { computed, ref, watch } from 'vue'
import messengerApi from '@/api/messenger'

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
const voiceProgress = ref(0)
const assetLoading = ref(false)
const assetError = ref('')
const resolvedMediaUrl = ref('')
const resolvedVoiceUrl = ref('')

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
const isUploading = computed(() => props.message.deliveryStatus === 0 || props.message.uploadState === 'uploading')
const uploadProgress = computed(() => {
  const value = Number(props.message.uploadProgress ?? 0)
  if (!Number.isFinite(value)) return 0
  return Math.max(0, Math.min(100, Math.round(value)))
})

const formattedDuration = computed(() => {
  const duration = Number(props.message.audioDurationSeconds || voiceAudioRef.value?.duration || 0)
  if (!Number.isFinite(duration) || duration <= 0) return '0:00'
  const rounded = Math.round(duration)
  const min = Math.floor(rounded / 60)
  const sec = String(rounded % 60).padStart(2, '0')
  return `${min}:${sec}`
})

function isLocalUrl(url) {
  return /^(blob|data):/i.test(String(url || ''))
}

async function resolveProtectedAsset(sourceUrl, targetRef) {
  assetError.value = ''
  if (!sourceUrl) {
    targetRef.value = ''
    return
  }

  if (isLocalUrl(sourceUrl)) {
    targetRef.value = sourceUrl
    return
  }

  assetLoading.value = true
  try {
    targetRef.value = await messengerApi.getProtectedAssetObjectUrl(sourceUrl)
  } catch {
    targetRef.value = ''
    assetError.value = 'Не удалось загрузить файл'
  } finally {
    assetLoading.value = false
  }
}

function toggleVoicePlayback() {
  const audio = voiceAudioRef.value
  if (!audio) return

  if (audio.paused) {
    audio.play().catch(() => {
      assetError.value = 'Не удалось воспроизвести голосовое'
    })
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

function onVoiceTimeUpdate() {
  const audio = voiceAudioRef.value
  if (!audio || !Number.isFinite(audio.duration) || audio.duration <= 0) {
    voiceProgress.value = 0
    return
  }

  voiceProgress.value = Math.max(0, Math.min(100, (audio.currentTime / audio.duration) * 100))
}

function onMediaLoaded() {
  emit('media-loaded')
}

watch(
  () => props.message.mediaUrl,
  (url) => {
    if (isMedia.value) {
      resolveProtectedAsset(url, resolvedMediaUrl)
    }
  },
  { immediate: true },
)

watch(
  () => props.message.audioUrl,
  (url) => {
    if (isVoice.value) {
      resolveProtectedAsset(url, resolvedVoiceUrl)
    }
  },
  { immediate: true },
)
</script>

<template>
  <div
    :class="[
      'px-[15px] py-[10px] rounded-[16px] mb-[10px] max-w-[78%] break-words shadow-sm',
      isBot ? (darkTheme ? 'bg-[#182533] mr-auto' : 'bg-[#e3f2fd] mr-auto') : (darkTheme ? 'bg-[#2B5278] ml-auto' : 'bg-[#f0f0f0] ml-auto'),
    ]"
  >
    <div class="flex flex-col">
      <span v-if="isText" :class="darkTheme ? 'text-white' : 'text-black'">{{ message.text }}</span>

      <div v-if="isVoice" class="voice-bubble" :class="darkTheme ? 'voice-bubble-dark' : 'voice-bubble-light'">
        <button
          type="button"
          class="voice-play-button"
          :class="isVoicePlaying ? 'voice-play-button-active' : ''"
          :disabled="assetLoading || !resolvedVoiceUrl"
          @click="toggleVoicePlayback"
          :aria-label="isVoicePlaying ? 'Пауза' : 'Воспроизвести'"
        >
          <span v-if="isVoicePlaying">Ⅱ</span>
          <span v-else>▶</span>
        </button>

        <div class="min-w-0 flex-1">
          <div class="flex items-center justify-between gap-3">
            <span class="text-[13px] font-medium" :class="darkTheme ? 'text-white' : 'text-[#111827]'">
              Голосовое сообщение
            </span>
            <span class="text-[12px]" :class="darkTheme ? 'text-[#B9C4CE]' : 'text-[#6b7280]'">
              {{ formattedDuration }}
            </span>
          </div>

          <div class="voice-progress-track" :class="darkTheme ? 'bg-[#31465b]' : 'bg-[#dbe5f0]'">
            <div class="voice-progress-fill" :style="{ width: `${voiceProgress}%` }" />
          </div>
        </div>

        <audio
          ref="voiceAudioRef"
          :src="resolvedVoiceUrl"
          preload="metadata"
          class="hidden"
          @loadedmetadata="onMediaLoaded"
          @play="onVoicePlay"
          @pause="onVoicePause"
          @ended="onVoicePause"
          @timeupdate="onVoiceTimeUpdate"
        />
      </div>

      <div v-if="isMedia" class="flex flex-col gap-2">
        <div v-if="isImage" class="relative overflow-hidden rounded-[12px]">
          <img
            v-if="resolvedMediaUrl"
            :src="resolvedMediaUrl"
            alt="media"
            class="max-w-[260px] max-h-[260px] rounded-[12px] object-cover"
            @load="onMediaLoaded"
          >
          <div
            v-if="assetLoading || isUploading"
            class="absolute inset-0 flex items-center justify-center bg-black/25 backdrop-blur-[1px]"
          >
            <div class="upload-spinner">
              <span>{{ uploadProgress > 0 ? `${uploadProgress}%` : '' }}</span>
            </div>
          </div>
        </div>

        <video
          v-else-if="isVideo && resolvedMediaUrl"
          :src="resolvedMediaUrl"
          controls
          class="max-w-[260px] max-h-[260px] rounded-[12px]"
          @loadedmetadata="onMediaLoaded"
        />

        <audio
          v-else-if="isAudioFile && resolvedMediaUrl"
          :src="resolvedMediaUrl"
          controls
          class="max-w-[260px]"
          @loadedmetadata="onMediaLoaded"
        />

        <a
          v-else-if="resolvedMediaUrl"
          :href="resolvedMediaUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="px-3 py-2 rounded-[8px] text-sm underline"
          :class="darkTheme ? 'bg-[#182533] text-white' : 'bg-white text-black'"
        >
          {{ message.mediaFileName || 'Открыть файл' }}
        </a>

        <div v-if="assetError" class="text-xs text-red-400">
          {{ assetError }}
        </div>
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

<style scoped>
.voice-bubble {
  display: flex;
  align-items: center;
  gap: 12px;
  min-width: 230px;
  max-width: 280px;
  border-radius: 18px;
  padding: 10px 12px;
}

.voice-bubble-light {
  background: rgba(255, 255, 255, 0.68);
}

.voice-bubble-dark {
  background: rgba(24, 37, 51, 0.72);
}

.voice-play-button {
  display: grid;
  place-items: center;
  width: 38px;
  height: 38px;
  flex: 0 0 38px;
  border-radius: 999px;
  background: linear-gradient(135deg, #28a8ff 0%, #0967ff 100%);
  color: white;
  font-size: 14px;
  font-weight: 700;
  box-shadow: 0 8px 18px rgba(9, 103, 255, 0.25);
  transition: transform 160ms ease, box-shadow 160ms ease, opacity 160ms ease;
}

.voice-play-button:disabled {
  opacity: 0.55;
}

.voice-play-button:not(:disabled):active {
  transform: scale(0.94);
}

.voice-play-button-active {
  box-shadow: 0 8px 22px rgba(9, 103, 255, 0.35);
}

.voice-progress-track {
  height: 4px;
  margin-top: 8px;
  overflow: hidden;
  border-radius: 999px;
}

.voice-progress-fill {
  height: 100%;
  border-radius: inherit;
  background: linear-gradient(90deg, #38bdf8 0%, #0967ff 100%);
  transition: width 120ms linear;
}

.upload-spinner {
  display: grid;
  place-items: center;
  width: 46px;
  height: 46px;
  border-radius: 999px;
  color: white;
  font-size: 11px;
  font-weight: 700;
  border: 3px solid rgba(255, 255, 255, 0.35);
  border-top-color: white;
  animation: spin 900ms linear infinite;
}

.upload-spinner span {
  animation: spin-reverse 900ms linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

@keyframes spin-reverse {
  to {
    transform: rotate(-360deg);
  }
}
</style>
