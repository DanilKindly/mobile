<script setup>
import { computed, onBeforeUnmount, ref } from 'vue'
import messengerApi from '@/api/messenger'
import UserAvatar from './UserAvatar.vue'

const props = defineProps({
  currentUser: {
    type: Object,
    required: true,
  },
  darkTheme: {
    type: Boolean,
    default: false,
  },
})

const emit = defineEmits(['close', 'profile-updated'])

const fileInput = ref(null)
const previewUrl = ref('')
const selectedFile = ref(null)
const isSaving = ref(false)
const errorText = ref('')

const displayName = computed(() => props.currentUser?.username || props.currentUser?.login || 'Пользователь')
const hasAvatar = computed(() => Boolean(props.currentUser?.avatarUrl))

function openFilePicker() {
  errorText.value = ''
  fileInput.value?.click()
}

function cleanupPreview() {
  if (previewUrl.value) {
    URL.revokeObjectURL(previewUrl.value)
  }
  previewUrl.value = ''
  selectedFile.value = null
}

function loadImage(url) {
  return new Promise((resolve, reject) => {
    const img = new Image()
    img.onload = () => resolve(img)
    img.onerror = reject
    img.src = url
  })
}

async function compressAvatar(file) {
  if (!file.type?.startsWith('image/')) {
    throw new Error('Выберите изображение.')
  }

  const localUrl = URL.createObjectURL(file)
  try {
    const img = await loadImage(localUrl)
    const targetSize = 512
    const canvas = document.createElement('canvas')
    canvas.width = targetSize
    canvas.height = targetSize

    const ctx = canvas.getContext('2d')
    const sourceSize = Math.min(img.naturalWidth || img.width, img.naturalHeight || img.height)
    const sx = ((img.naturalWidth || img.width) - sourceSize) / 2
    const sy = ((img.naturalHeight || img.height) - sourceSize) / 2

    ctx.drawImage(img, sx, sy, sourceSize, sourceSize, 0, 0, targetSize, targetSize)

    const blob = await new Promise((resolve) => {
      canvas.toBlob(resolve, 'image/jpeg', 0.84)
    })

    if (!blob) {
      return file
    }

    return new File([blob], 'avatar.jpg', { type: 'image/jpeg' })
  } finally {
    URL.revokeObjectURL(localUrl)
  }
}

async function handleFileChange(event) {
  const file = event.target.files?.[0]
  event.target.value = ''
  if (!file) return

  cleanupPreview()
  errorText.value = ''

  try {
    const compressed = await compressAvatar(file)
    selectedFile.value = compressed
    previewUrl.value = URL.createObjectURL(compressed)
  } catch (error) {
    errorText.value = error?.message || 'Не удалось подготовить фото.'
  }
}

async function saveAvatar() {
  if (!selectedFile.value) return

  isSaving.value = true
  errorText.value = ''
  try {
    const profile = await messengerApi.uploadAvatar(selectedFile.value)
    emit('profile-updated', profile)
    cleanupPreview()
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Не удалось сохранить фото.'
  } finally {
    isSaving.value = false
  }
}

async function removeAvatar() {
  isSaving.value = true
  errorText.value = ''
  try {
    const profile = await messengerApi.deleteAvatar()
    emit('profile-updated', profile)
    cleanupPreview()
  } catch (error) {
    errorText.value = error?.response?.data?.error || 'Не удалось удалить фото.'
  } finally {
    isSaving.value = false
  }
}

onBeforeUnmount(cleanupPreview)
</script>

<template>
  <div class="fixed inset-0 z-[70] bg-black/45 flex items-end sm:items-center justify-center px-3" @click.self="emit('close')">
    <section
      class="w-full max-w-[420px] rounded-t-[26px] sm:rounded-[26px] shadow-2xl overflow-hidden"
      :class="darkTheme ? 'bg-[#17212B] text-white' : 'bg-white text-gray-900'"
    >
      <div class="px-5 pt-5 pb-4 border-b" :class="darkTheme ? 'border-[#263748]' : 'border-gray-100'">
        <div class="flex items-center justify-between gap-3">
          <h3 class="text-[20px] font-semibold">Профиль</h3>
          <button
            type="button"
            class="w-9 h-9 rounded-full transition-colors"
            :class="darkTheme ? 'hover:bg-[#223244] text-gray-300' : 'hover:bg-gray-100 text-gray-500'"
            @click="emit('close')"
          >
            ×
          </button>
        </div>
      </div>

      <div class="px-5 py-6">
        <div class="flex flex-col items-center text-center">
          <div class="relative">
            <img
              v-if="previewUrl"
              :src="previewUrl"
              alt="preview"
              class="w-28 h-28 rounded-full object-cover ring-4"
              :class="darkTheme ? 'ring-[#223244]' : 'ring-blue-50'"
            >
            <UserAvatar
              v-else
              :avatar-url="currentUser.avatarUrl"
              :name="displayName"
              size-class="w-28 h-28 text-[36px]"
              :dark-theme="darkTheme"
            />
          </div>

          <div class="mt-4 text-[18px] font-semibold truncate max-w-full">
            {{ currentUser.username || 'Пользователь' }}
          </div>
          <div class="mt-0.5 text-[13px]" :class="darkTheme ? 'text-[#8ea2b6]' : 'text-gray-500'">
            @{{ currentUser.login || '-' }}
          </div>
        </div>

        <div v-if="errorText" class="mt-4 text-sm text-red-500 text-center">
          {{ errorText }}
        </div>

        <input
          ref="fileInput"
          type="file"
          accept="image/jpeg,image/png,image/webp"
          class="hidden"
          @change="handleFileChange"
        >

        <div v-if="previewUrl" class="mt-6 grid grid-cols-2 gap-3">
          <button
            type="button"
            class="py-3 rounded-2xl font-medium transition-colors"
            :class="darkTheme ? 'bg-[#223244] hover:bg-[#2a3c52]' : 'bg-gray-100 hover:bg-gray-200'"
            :disabled="isSaving"
            @click="cleanupPreview"
          >
            Отмена
          </button>
          <button
            type="button"
            class="py-3 rounded-2xl font-medium bg-blue-500 text-white hover:bg-blue-600 disabled:opacity-60 transition-colors"
            :disabled="isSaving"
            @click="saveAvatar"
          >
            {{ isSaving ? 'Сохраняем...' : 'Сохранить' }}
          </button>
        </div>

        <div v-else class="mt-6 space-y-3">
          <button
            type="button"
            class="w-full py-3 rounded-2xl font-medium bg-blue-500 text-white hover:bg-blue-600 disabled:opacity-60 transition-colors"
            :disabled="isSaving"
            @click="openFilePicker"
          >
            Изменить фото
          </button>
          <button
            v-if="hasAvatar"
            type="button"
            class="w-full py-3 rounded-2xl font-medium transition-colors"
            :class="darkTheme ? 'bg-[#241f24] text-red-300 hover:bg-[#30252b]' : 'bg-red-50 text-red-600 hover:bg-red-100'"
            :disabled="isSaving"
            @click="removeAvatar"
          >
            {{ isSaving ? 'Удаляем...' : 'Удалить фото' }}
          </button>
        </div>
      </div>
    </section>
  </div>
</template>
