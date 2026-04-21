<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'

const emit = defineEmits(['send-text', 'send-voice', 'send-media'])

const messageInput = ref('')
const mediaInput = ref(null)

const isRecording = ref(false)
const isSendingVoice = ref(false)
const recordingDurationSeconds = ref(0)
const recordError = ref('')
const sendingError = ref('')
const swipeOffset = ref(0)
const isCancelArmed = ref(false)

let mediaRecorder = null
let currentStream = null
let chunks = []
let timerHandle = null
let pointerStartX = null
let activePointerId = null
let finalizeMode = 'cancel'

const SWIPE_CANCEL_THRESHOLD = 90

const props = defineProps({
  darkTheme: {
    type: Boolean,
    default: false,
  },
  selectedChat: {
    type: String,
    default: '',
  },
})

const micButtonTitle = computed(() => (isRecording.value ? 'Отпустите для отправки' : 'Зажмите для записи'))

function formatDuration(totalSeconds) {
  const seconds = Number(totalSeconds || 0)
  const min = String(Math.floor(seconds / 60)).padStart(2, '0')
  const sec = String(seconds % 60).padStart(2, '0')
  return `${min}:${sec}`
}

function clearTimer() {
  if (timerHandle) {
    clearInterval(timerHandle)
    timerHandle = null
  }
}

function cleanupRecordingStream() {
  if (currentStream) {
    currentStream.getTracks().forEach((track) => track.stop())
    currentStream = null
  }
}

function getPreferredMimeType() {
  if (typeof MediaRecorder === 'undefined' || !MediaRecorder.isTypeSupported) {
    return ''
  }
  if (MediaRecorder.isTypeSupported('audio/webm;codecs=opus')) return 'audio/webm;codecs=opus'
  if (MediaRecorder.isTypeSupported('audio/webm')) return 'audio/webm'
  if (MediaRecorder.isTypeSupported('audio/ogg;codecs=opus')) return 'audio/ogg;codecs=opus'
  return ''
}

function detachPointerListeners() {
  window.removeEventListener('pointermove', onPointerMove)
  window.removeEventListener('pointerup', onPointerUp)
  window.removeEventListener('pointercancel', onPointerCancel)
}

function resetRecordingVisualState() {
  swipeOffset.value = 0
  isCancelArmed.value = false
  pointerStartX = null
  activePointerId = null
}

function hardResetRecordingState() {
  isRecording.value = false
  recordingDurationSeconds.value = 0
  finalizeMode = 'cancel'
  clearTimer()
  cleanupRecordingStream()
  resetRecordingVisualState()
  chunks = []
  mediaRecorder = null
  detachPointerListeners()
}

async function startRecording(pointerX) {
  if (isRecording.value || isSendingVoice.value) return
  if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
    recordError.value = 'Запись голоса не поддерживается в этом браузере.'
    return
  }

  recordError.value = ''
  sendingError.value = ''
  chunks = []
  finalizeMode = 'cancel'
  pointerStartX = pointerX
  swipeOffset.value = 0
  isCancelArmed.value = false

  try {
    currentStream = await navigator.mediaDevices.getUserMedia({ audio: true })
    const mimeType = getPreferredMimeType()
    mediaRecorder = mimeType ? new MediaRecorder(currentStream, { mimeType }) : new MediaRecorder(currentStream)

    mediaRecorder.ondataavailable = (event) => {
      if (event.data && event.data.size > 0) {
        chunks.push(event.data)
      }
    }

    mediaRecorder.onstop = async () => {
      const shouldSend = finalizeMode === 'send'
      const duration = Math.max(recordingDurationSeconds.value, 1)
      const localChunks = chunks
      const localMime = mediaRecorder?.mimeType || 'audio/webm'

      hardResetRecordingState()

      if (!shouldSend || !localChunks.length) {
        return
      }

      isSendingVoice.value = true
      try {
        const blob = new Blob(localChunks, { type: localMime })
        const extension = localMime.includes('ogg') ? 'ogg' : 'webm'
        await emit('send-voice', {
          blob,
          durationSeconds: duration,
          fileName: `voice-${Date.now()}.${extension}`,
        })
      } catch (error) {
        sendingError.value = 'Не удалось отправить голосовое сообщение.'
      } finally {
        isSendingVoice.value = false
      }
    }

    mediaRecorder.start()
    isRecording.value = true
    recordingDurationSeconds.value = 0
    timerHandle = setInterval(() => {
      recordingDurationSeconds.value += 1
    }, 1000)
  } catch (error) {
    recordError.value = 'Не удалось получить доступ к микрофону.'
    hardResetRecordingState()
  }
}

function stopRecording(mode) {
  if (!mediaRecorder || mediaRecorder.state !== 'recording') {
    hardResetRecordingState()
    return
  }
  finalizeMode = mode
  mediaRecorder.stop()
}

function cancelRecordingByButton() {
  stopRecording('cancel')
}

function onPointerMove(event) {
  if (!isRecording.value || activePointerId !== event.pointerId || pointerStartX == null) return
  const deltaX = event.clientX - pointerStartX
  const clamped = Math.min(0, Math.max(deltaX, -140))
  swipeOffset.value = clamped
  isCancelArmed.value = Math.abs(clamped) >= SWIPE_CANCEL_THRESHOLD
}

function onPointerUp(event) {
  if (activePointerId !== event.pointerId) return
  stopRecording(isCancelArmed.value ? 'cancel' : 'send')
}

function onPointerCancel(event) {
  if (activePointerId !== event.pointerId) return
  stopRecording('cancel')
}

async function handleMicPointerDown(event) {
  if (isSendingVoice.value) return
  event.preventDefault()

  activePointerId = event.pointerId
  await startRecording(event.clientX)

  window.addEventListener('pointermove', onPointerMove)
  window.addEventListener('pointerup', onPointerUp)
  window.addEventListener('pointercancel', onPointerCancel)
}

async function onMediaPicked(event) {
  const file = event.target.files?.[0]
  if (!file) return

  sendingError.value = ''

  try {
    await emit('send-media', file)
  } catch (error) {
    sendingError.value = 'Не удалось отправить файл.'
  } finally {
    event.target.value = ''
  }
}

function openMediaPicker() {
  mediaInput.value?.click()
}

async function sendTextMessage() {
  const text = messageInput.value.trim()
  if (!text || isRecording.value) return

  try {
    await emit('send-text', text)
    messageInput.value = ''
    sendingError.value = ''
  } catch (error) {
    sendingError.value = 'Не удалось отправить сообщение.'
  }
}

function handleKeydown(event) {
  if (event.key === 'Enter') {
    sendTextMessage()
  }
}

onBeforeUnmount(() => {
  if (isRecording.value) {
    stopRecording('cancel')
  } else {
    hardResetRecordingState()
  }
})
</script>

<template>
  <footer
    :class="[
      'w-full absolute bottom-0 left-0 right-0 px-[20px] py-[10px]',
      darkTheme ? 'bg-[#17212B]' : 'bg-white',
    ]"
    style="z-index: 100;"
  >
    <input
      ref="mediaInput"
      type="file"
      class="hidden"
      accept="image/*,video/*,audio/*,.pdf,.doc,.docx,.xls,.xlsx,.zip,.rar,.txt"
      @change="onMediaPicked"
    >

    <div
      v-if="isRecording"
      class="flex items-center gap-3 rounded-[20px] px-4 py-3 select-none"
      :class="darkTheme ? 'bg-[#182533]' : 'bg-[#f3f5f7]'"
      :style="{ transform: `translateX(${swipeOffset}px)` }"
    >
      <span class="relative flex h-3 w-3">
        <span class="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-500 opacity-75" />
        <span class="relative inline-flex rounded-full h-3 w-3 bg-red-500" />
      </span>
      <span :class="darkTheme ? 'text-white' : 'text-black'">
        {{ formatDuration(recordingDurationSeconds) }}
      </span>
      <span class="text-xs" :class="isCancelArmed ? 'text-red-400' : (darkTheme ? 'text-[#9db0c1]' : 'text-[#6b7280]')">
        {{ isCancelArmed ? 'Отпустите — отмена' : 'Свайп влево для отмены, отпустите для отправки' }}
      </span>
      <button
        class="ml-auto text-xs px-3 py-2 rounded-[14px]"
        :class="darkTheme ? 'bg-[#253647] text-white' : 'bg-white text-black'"
        @click="cancelRecordingByButton"
      >
        Отмена
      </button>
    </div>

    <div v-else class="flex items-center gap-[10px]">
      <button class="flex-shrink-0" @click="openMediaPicker" title="Прикрепить файл">
        <img src="/src/assets/icons/plus.svg" class="w-[28px]" :class="darkTheme ? 'invert' : ''" alt="Добавить">
      </button>

      <input
        v-model="messageInput"
        @keydown="handleKeydown"
        class="flex-1 min-w-0 px-[15px] py-[8px] rounded-[20px]"
        :class="[
          darkTheme ? 'bg-[#182533] text-white placeholder-[#6D7F8F]' : 'bg-[#f0f0f0] text-black placeholder-[#888]',
        ]"
        placeholder="Сообщение..."
      >

      <button
        class="flex-shrink-0 text-xs px-3 py-2 rounded-[14px]"
        :class="darkTheme ? 'bg-[#182533] text-white' : 'bg-[#f0f0f0] text-black'"
        :title="micButtonTitle"
        @pointerdown="handleMicPointerDown"
      >
        🎤
      </button>

      <button @click="sendTextMessage" class="flex-shrink-0" title="Отправить текст">
        <img src="/src/assets/icons/send-message.svg" class="w-[30px]" alt="Отправить">
      </button>
    </div>

    <div v-if="isSendingVoice" class="mt-1 text-xs text-blue-400">
      Отправка голосового...
    </div>
    <div v-if="recordError || sendingError" class="mt-1 text-xs text-red-400">
      {{ recordError || sendingError }}
    </div>
  </footer>
</template>
