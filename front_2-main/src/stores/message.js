import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'

function toTimeString(sentAtValue) {
  const date = sentAtValue ? new Date(sentAtValue) : new Date()
  return `${date.getHours().toString().padStart(2, '0')}:${date
    .getMinutes()
    .toString()
    .padStart(2, '0')}`
}

function mapBackendMessage(m, currentUserId) {
  const messageId = m.messageId ?? m.MessageId
  const senderUserId = m.senderUserId ?? m.SenderUserId
  const sentAt = m.sentAt ?? m.SentAt
  const type = Number(m.type ?? m.Type ?? 0)
  const isFromMe = String(senderUserId) === String(currentUserId)
  const isRead = Boolean(m.isRead ?? m.IsRead ?? false)

  return {
    id: messageId,
    senderUserId,
    sentAt,
    type,
    text: m.text ?? m.Text ?? '',
    audioUrl: messengerApi.resolveAssetUrl(m.audioUrl ?? m.AudioUrl),
    audioContentType: m.audioContentType ?? m.AudioContentType ?? null,
    audioDurationSeconds: m.audioDurationSeconds ?? m.AudioDurationSeconds ?? null,
    audioSizeBytes: m.audioSizeBytes ?? m.AudioSizeBytes ?? null,
    mediaUrl: messengerApi.resolveAssetUrl(m.mediaUrl ?? m.MediaUrl),
    mediaContentType: m.mediaContentType ?? m.MediaContentType ?? null,
    mediaFileName: m.mediaFileName ?? m.MediaFileName ?? null,
    mediaSizeBytes: m.mediaSizeBytes ?? m.MediaSizeBytes ?? null,
    readAt: m.readAt ?? m.ReadAt ?? null,
    time: toTimeString(sentAt),
    isBot: !isFromMe,
    isRead: isFromMe ? isRead : true,
  }
}

export const useMessageStore = defineStore('message', () => {
  const messages = ref([])

  async function loadMessagesByChatId(chatId, currentUserId) {
    try {
      const backendMessages = await messengerApi.getMessages(chatId)
      messages.value = backendMessages.map((m) => mapBackendMessage(m, currentUserId))
    } catch (e) {
      messages.value = []
      console.error('Failed to load messages from backend:', e)
    }
  }

  function addBackendMessageToState(messageDto, currentUserId) {
    const mapped = mapBackendMessage(messageDto, currentUserId)
    const exists = messages.value.some((m) => m.id && m.id === mapped.id)
    if (exists) return
    messages.value.push(mapped)
  }

  function markMessagesAsReadByIds(messageIds, readerUserId, currentUserId) {
    if (!Array.isArray(messageIds) || messageIds.length === 0) return

    const idSet = new Set(messageIds.map((id) => String(id).toLowerCase()))
    const readerNormalized = String(readerUserId || '').toLowerCase()
    const currentNormalized = String(currentUserId || '').toLowerCase()

    if (!readerNormalized || readerNormalized === currentNormalized) {
      return
    }

    let changed = false
    for (const message of messages.value) {
      const messageId = String(message.id || '').toLowerCase()
      const senderId = String(message.senderUserId || '').toLowerCase()

      if (!idSet.has(messageId)) continue
      if (senderId !== currentNormalized) continue
      if (message.isRead) continue

      message.isRead = true
      changed = true
    }

    if (changed) {
      messages.value = [...messages.value]
    }
  }

  function clearMessages() {
    messages.value = []
  }

  return {
    messages,
    loadMessagesByChatId,
    addBackendMessageToState,
    markMessagesAsReadByIds,
    clearMessages,
  }
})
