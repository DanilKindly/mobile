import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import messengerApi from '@/api/messenger'

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

function toTimeString(sentAtValue) {
  const date = sentAtValue ? new Date(sentAtValue) : new Date()
  return `${date.getHours().toString().padStart(2, '0')}:${date
    .getMinutes()
    .toString()
    .padStart(2, '0')}`
}

function buildStoreMessage(raw, currentUserId, fallback = {}) {
  const messageId = raw.messageId ?? raw.MessageId ?? fallback.id ?? null
  const chatId = raw.chatId ?? raw.ChatId ?? fallback.chatId ?? null
  const senderUserId = raw.senderUserId ?? raw.SenderUserId ?? fallback.senderUserId ?? null
  const sentAt = raw.sentAt ?? raw.SentAt ?? fallback.sentAt ?? new Date().toISOString()
  const sentAtClient = raw.sentAtClient ?? raw.SentAtClient ?? fallback.sentAtClient ?? null
  const clientMessageId = raw.clientMessageId ?? raw.ClientMessageId ?? fallback.clientMessageId ?? null
  const type = Number(raw.type ?? raw.Type ?? fallback.type ?? 0)
  const version = Number(raw.version ?? raw.Version ?? fallback.version ?? 0)
  const isRead = Boolean(raw.isRead ?? raw.IsRead ?? fallback.isRead ?? false)
  const status = Number(raw.deliveryStatus ?? raw.DeliveryStatus ?? fallback.deliveryStatus ?? 1)
  const isFromMe = normalizeId(senderUserId) === normalizeId(currentUserId)

  return {
    id: messageId || `tmp:${clientMessageId || crypto.randomUUID()}`,
    messageId: messageId || null,
    clientMessageId,
    chatId,
    senderUserId,
    sentAt,
    sentAtClient,
    version,
    type,
    text: raw.text ?? raw.Text ?? fallback.text ?? '',
    audioUrl: messengerApi.resolveAssetUrl(raw.audioUrl ?? raw.AudioUrl ?? fallback.audioUrl ?? null),
    audioContentType: raw.audioContentType ?? raw.AudioContentType ?? fallback.audioContentType ?? null,
    audioDurationSeconds: raw.audioDurationSeconds ?? raw.AudioDurationSeconds ?? fallback.audioDurationSeconds ?? null,
    audioSizeBytes: raw.audioSizeBytes ?? raw.AudioSizeBytes ?? fallback.audioSizeBytes ?? null,
    mediaUrl: messengerApi.resolveAssetUrl(raw.mediaUrl ?? raw.MediaUrl ?? fallback.mediaUrl ?? null),
    mediaContentType: raw.mediaContentType ?? raw.MediaContentType ?? fallback.mediaContentType ?? null,
    mediaFileName: raw.mediaFileName ?? raw.MediaFileName ?? fallback.mediaFileName ?? null,
    mediaSizeBytes: raw.mediaSizeBytes ?? raw.MediaSizeBytes ?? fallback.mediaSizeBytes ?? null,
    readAt: raw.readAt ?? raw.ReadAt ?? fallback.readAt ?? null,
    time: toTimeString(sentAt),
    isBot: !isFromMe,
    isRead: isFromMe ? isRead : true,
    deliveryStatus: isFromMe ? status : 3,
  }
}

function ensureSorted(messages) {
  return [...messages].sort((a, b) => {
    const aTs = new Date(a.sentAt || a.sentAtClient || 0).getTime()
    const bTs = new Date(b.sentAt || b.sentAtClient || 0).getTime()
    if (aTs !== bTs) return aTs - bTs
    return String(a.id).localeCompare(String(b.id))
  })
}

export const useMessageStore = defineStore('message', () => {
  const activeChatId = ref(null)
  const messagesByChatId = ref({})
  const messagePageByChatId = ref({})
  const pendingByClientId = ref({})

  const messages = computed(() => {
    const chatKey = normalizeId(activeChatId.value)
    return messagesByChatId.value[chatKey] || []
  })

  function setActiveChat(chatId) {
    activeChatId.value = chatId || null
  }

  function getMessagesByChatId(chatId) {
    return messagesByChatId.value[normalizeId(chatId)] || []
  }

  function getPageState(chatId) {
    return messagePageByChatId.value[normalizeId(chatId)] || {
      hasMoreOlder: false,
      nextBeforeSentAt: null,
      nextBeforeMessageId: null,
      limit: 40,
      isLoadingOlder: false,
      isLoadingLatest: false,
    }
  }

  function setPageState(chatId, patch) {
    const chatKey = normalizeId(chatId)
    messagePageByChatId.value = {
      ...messagePageByChatId.value,
      [chatKey]: {
        ...getPageState(chatId),
        ...patch,
      },
    }
  }

  function upsertMessage(chatId, mappedMessage) {
    const chatKey = normalizeId(chatId)
    const current = messagesByChatId.value[chatKey] || []

    const byMessageId = mappedMessage.messageId
      ? current.findIndex((m) => normalizeId(m.messageId) === normalizeId(mappedMessage.messageId))
      : -1
    const byClientId = mappedMessage.clientMessageId
      ? current.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === normalizeId(mappedMessage.clientMessageId))
      : -1

    const targetIndex = byMessageId >= 0 ? byMessageId : byClientId
    if (targetIndex >= 0) {
      current[targetIndex] = { ...current[targetIndex], ...mappedMessage }
    } else {
      current.push(mappedMessage)
    }

    messagesByChatId.value = {
      ...messagesByChatId.value,
      [chatKey]: ensureSorted(current),
    }
  }

  function mergeMessages(base, incoming) {
    const merged = [...base]
    for (const message of incoming) {
      const byMessageId = message.messageId
        ? merged.findIndex((m) => normalizeId(m.messageId) === normalizeId(message.messageId))
        : -1
      const byClientId = message.clientMessageId
        ? merged.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === normalizeId(message.clientMessageId))
        : -1
      const targetIndex = byMessageId >= 0 ? byMessageId : byClientId

      if (targetIndex >= 0) {
        merged[targetIndex] = { ...merged[targetIndex], ...message }
      } else {
        merged.push(message)
      }
    }

    return ensureSorted(merged)
  }

  async function loadLatestMessagesByChatId(chatId, currentUserId, limit = 40) {
    const chatKey = normalizeId(chatId)
    setPageState(chatId, { isLoadingLatest: true, limit })

    try {
      const page = await messengerApi.getMessages(chatId, { limit })
      const incoming = (page.messages || []).map((m) => buildStoreMessage(m, currentUserId))

      messagesByChatId.value = {
        ...messagesByChatId.value,
        [chatKey]: ensureSorted(incoming),
      }

      setPageState(chatId, {
        isLoadingLatest: false,
        hasMoreOlder: page.hasMoreOlder,
        nextBeforeSentAt: page.nextBeforeSentAt,
        nextBeforeMessageId: page.nextBeforeMessageId,
        limit,
      })
    } catch (e) {
      console.error('Failed to load latest messages from backend:', e)
      setPageState(chatId, { isLoadingLatest: false })
      throw e
    }
  }

  async function loadOlderMessagesByChatId(chatId, currentUserId) {
    const state = getPageState(chatId)
    if (state.isLoadingOlder || !state.hasMoreOlder || !state.nextBeforeSentAt) {
      return false
    }

    setPageState(chatId, { isLoadingOlder: true })
    const chatKey = normalizeId(chatId)

    try {
      const page = await messengerApi.getMessages(chatId, {
        limit: state.limit || 40,
        beforeSentAt: state.nextBeforeSentAt,
        beforeMessageId: state.nextBeforeMessageId,
      })

      const incoming = (page.messages || []).map((m) => buildStoreMessage(m, currentUserId))
      const current = messagesByChatId.value[chatKey] || []
      const merged = mergeMessages(current, incoming)

      messagesByChatId.value = {
        ...messagesByChatId.value,
        [chatKey]: merged,
      }

      setPageState(chatId, {
        isLoadingOlder: false,
        hasMoreOlder: page.hasMoreOlder,
        nextBeforeSentAt: page.nextBeforeSentAt,
        nextBeforeMessageId: page.nextBeforeMessageId,
      })
      return incoming.length > 0
    } catch (e) {
      console.error('Failed to load older messages from backend:', e)
      setPageState(chatId, { isLoadingOlder: false })
      return false
    }
  }

  function addBackendMessageToState(messageDto, currentUserId) {
    const mapped = buildStoreMessage(messageDto, currentUserId)
    if (!mapped.chatId) return
    upsertMessage(mapped.chatId, mapped)
  }

  function addOptimisticTextMessage(chatId, payload, currentUserId) {
    const mapped = buildStoreMessage({
      chatId,
      senderUserId: payload.senderUserId,
      clientMessageId: payload.clientMessageId,
      sentAtClient: payload.sentAtClient,
      sentAt: payload.sentAtClient,
      text: payload.text,
      type: 0,
      deliveryStatus: 0,
      isRead: false,
      version: Date.now(),
    }, currentUserId)

    pendingByClientId.value = {
      ...pendingByClientId.value,
      [normalizeId(payload.clientMessageId)]: {
        chatId,
        senderUserId: payload.senderUserId,
        sentAtClient: payload.sentAtClient,
        text: payload.text,
      },
    }

    upsertMessage(chatId, mapped)
    return mapped
  }

  function addOptimisticMediaMessage(chatId, payload, currentUserId) {
    const mapped = buildStoreMessage({
      chatId,
      senderUserId: payload.senderUserId,
      clientMessageId: payload.clientMessageId,
      sentAtClient: payload.sentAtClient,
      sentAt: payload.sentAtClient,
      type: 2,
      mediaUrl: payload.localUrl,
      mediaContentType: payload.contentType,
      mediaFileName: payload.fileName,
      mediaSizeBytes: payload.sizeBytes,
      deliveryStatus: 0,
      isRead: false,
      version: Date.now(),
    }, currentUserId)

    mapped.uploadState = 'uploading'
    mapped.uploadProgress = 0

    pendingByClientId.value = {
      ...pendingByClientId.value,
      [normalizeId(payload.clientMessageId)]: {
        chatId,
        senderUserId: payload.senderUserId,
        sentAtClient: payload.sentAtClient,
        mediaUrl: payload.localUrl,
        mediaContentType: payload.contentType,
        mediaFileName: payload.fileName,
        mediaSizeBytes: payload.sizeBytes,
      },
    }

    upsertMessage(chatId, mapped)
    return mapped
  }

  function updatePendingUploadProgress(chatId, clientMessageId, progress) {
    if (!clientMessageId) return
    const chatKey = normalizeId(chatId)
    const current = messagesByChatId.value[chatKey] || []
    const index = current.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === normalizeId(clientMessageId))
    if (index < 0) return

    current[index] = {
      ...current[index],
      uploadState: 'uploading',
      uploadProgress: Math.max(0, Math.min(100, Math.round(Number(progress || 0)))),
    }

    messagesByChatId.value = {
      ...messagesByChatId.value,
      [chatKey]: [...current],
    }
  }

  function markPendingMessageAsSent(chatId, clientMessageId, messageDto, currentUserId) {
    if (!clientMessageId) return
    const pendingKey = normalizeId(clientMessageId)
    const mapped = buildStoreMessage(messageDto, currentUserId, pendingByClientId.value[pendingKey] || {})
    mapped.clientMessageId = mapped.clientMessageId || clientMessageId
    mapped.deliveryStatus = mapped.isRead ? 3 : 1
    mapped.uploadState = 'sent'
    mapped.uploadProgress = 100
    upsertMessage(chatId, mapped)

    const nextPending = { ...pendingByClientId.value }
    delete nextPending[pendingKey]
    pendingByClientId.value = nextPending
  }

  function markPendingMessageAsFailed(chatId, clientMessageId) {
    if (!clientMessageId) return
    const chatKey = normalizeId(chatId)
    const current = messagesByChatId.value[chatKey] || []
    const index = current.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === normalizeId(clientMessageId))
    if (index < 0) return

    current[index] = {
      ...current[index],
      deliveryStatus: 2,
      uploadState: 'failed',
    }

    messagesByChatId.value = {
      ...messagesByChatId.value,
      [chatKey]: [...current],
    }
  }

  function markMessagesAsReadByIds(messageIds, readerUserId, currentUserId, chatId = null) {
    if (!Array.isArray(messageIds) || messageIds.length === 0) return

    const chatKey = normalizeId(chatId || activeChatId.value)
    if (!chatKey) return

    const current = messagesByChatId.value[chatKey] || []
    const idSet = new Set(messageIds.map((id) => normalizeId(id)))
    const readerNormalized = normalizeId(readerUserId)
    const currentNormalized = normalizeId(currentUserId)

    if (!readerNormalized || readerNormalized === currentNormalized) {
      return
    }

    let changed = false
    for (const message of current) {
      const messageId = normalizeId(message.messageId || message.id)
      const senderId = normalizeId(message.senderUserId)

      if (!idSet.has(messageId)) continue
      if (senderId !== currentNormalized) continue
      if (message.isRead && message.deliveryStatus === 3) continue

      message.isRead = true
      message.deliveryStatus = 3
      changed = true
    }

    if (changed) {
      messagesByChatId.value = {
        ...messagesByChatId.value,
        [chatKey]: [...current],
      }
    }
  }

  function clearMessages(chatId = null) {
    const targetChatId = chatId || activeChatId.value
    const chatKey = normalizeId(targetChatId)
    if (!chatKey) return

    messagesByChatId.value = {
      ...messagesByChatId.value,
      [chatKey]: [],
    }

    messagePageByChatId.value = {
      ...messagePageByChatId.value,
      [chatKey]: {
        hasMoreOlder: false,
        nextBeforeSentAt: null,
        nextBeforeMessageId: null,
        limit: 40,
        isLoadingOlder: false,
        isLoadingLatest: false,
      },
    }
  }

  return {
    messages,
    messagesByChatId,
    messagePageByChatId,
    pendingByClientId,
    activeChatId,
    setActiveChat,
    getMessagesByChatId,
    getPageState,
    loadLatestMessagesByChatId,
    loadOlderMessagesByChatId,
    addBackendMessageToState,
    addOptimisticTextMessage,
    addOptimisticMediaMessage,
    updatePendingUploadProgress,
    markPendingMessageAsSent,
    markPendingMessageAsFailed,
    markMessagesAsReadByIds,
    clearMessages,
  }
})
