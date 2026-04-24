import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import messengerApi from '@/api/messenger'

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

const TEXT_QUEUE_STORAGE_PREFIX = 'ois_text_delivery_queue_'
const MEDIA_PLACEHOLDER_STORAGE_PREFIX = 'ois_media_delivery_placeholders_'

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
  const queuedTextByClientId = ref({})
  const localMediaPlaceholdersByClientId = ref({})
  const deliveryQueueUserId = ref(null)

  const messages = computed(() => {
    const chatKey = normalizeId(activeChatId.value)
    return messagesByChatId.value[chatKey] || []
  })

  function setActiveChat(chatId) {
    activeChatId.value = chatId || null
  }

  function getTextQueueStorageKey(userId = deliveryQueueUserId.value) {
    const normalized = normalizeId(userId)
    return normalized ? `${TEXT_QUEUE_STORAGE_PREFIX}${normalized}` : null
  }

  function getMediaPlaceholderStorageKey(userId = deliveryQueueUserId.value) {
    const normalized = normalizeId(userId)
    return normalized ? `${MEDIA_PLACEHOLDER_STORAGE_PREFIX}${normalized}` : null
  }

  function readStoredMap(storageKey) {
    if (!storageKey) return {}

    try {
      const parsed = JSON.parse(localStorage.getItem(storageKey) || '{}')
      return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? parsed : {}
    } catch {
      return {}
    }
  }

  function persistMap(storageKey, value) {
    if (!storageKey) return

    try {
      const entries = Object.fromEntries(
        Object.entries(value || {}).filter(([, item]) => item && typeof item === 'object'),
      )
      localStorage.setItem(storageKey, JSON.stringify(entries))
    } catch {
      // Storage must never break message rendering/sending.
    }
  }

  function persistTextQueue() {
    persistMap(getTextQueueStorageKey(), queuedTextByClientId.value)
  }

  function persistMediaPlaceholders() {
    persistMap(getMediaPlaceholderStorageKey(), localMediaPlaceholdersByClientId.value)
  }

  function upsertQueuedTextMessage(item, status = 'pending') {
    if (!item?.clientMessageId || !item.chatId || !item.senderUserId || !item.text) return null

    const key = normalizeId(item.clientMessageId)
    const previous = queuedTextByClientId.value[key] || {}
    const queued = {
      ...previous,
      chatId: item.chatId,
      senderUserId: item.senderUserId,
      clientMessageId: item.clientMessageId,
      sentAtClient: item.sentAtClient || previous.sentAtClient || new Date().toISOString(),
      text: item.text,
      status,
      attempts: Number(previous.attempts || 0),
      updatedAt: new Date().toISOString(),
    }

    queuedTextByClientId.value = {
      ...queuedTextByClientId.value,
      [key]: queued,
    }
    persistTextQueue()
    return queued
  }

  function upsertLocalMediaPlaceholder(item, status = 'pending') {
    if (!item?.clientMessageId || !item.chatId || !item.senderUserId) return null

    const key = normalizeId(item.clientMessageId)
    const previous = localMediaPlaceholdersByClientId.value[key] || {}
    const queued = {
      ...previous,
      chatId: item.chatId,
      senderUserId: item.senderUserId,
      clientMessageId: item.clientMessageId,
      sentAtClient: item.sentAtClient || previous.sentAtClient || new Date().toISOString(),
      mediaContentType: item.mediaContentType || previous.mediaContentType || null,
      mediaFileName: item.mediaFileName || previous.mediaFileName || null,
      mediaSizeBytes: item.mediaSizeBytes ?? previous.mediaSizeBytes ?? null,
      status,
      updatedAt: new Date().toISOString(),
    }

    localMediaPlaceholdersByClientId.value = {
      ...localMediaPlaceholdersByClientId.value,
      [key]: queued,
    }
    persistMediaPlaceholders()
    return queued
  }

  function removeQueuedDelivery(clientMessageId) {
    if (!clientMessageId) return
    const key = normalizeId(clientMessageId)

    if (queuedTextByClientId.value[key]) {
      const next = { ...queuedTextByClientId.value }
      delete next[key]
      queuedTextByClientId.value = next
      persistTextQueue()
    }

    if (localMediaPlaceholdersByClientId.value[key]) {
      const next = { ...localMediaPlaceholdersByClientId.value }
      delete next[key]
      localMediaPlaceholdersByClientId.value = next
      persistMediaPlaceholders()
    }
  }

  function hydrateDeliveryQueue(userId, currentUserId = userId) {
    const normalizedUserId = normalizeId(userId)
    if (!normalizedUserId) return

    deliveryQueueUserId.value = normalizedUserId
    queuedTextByClientId.value = readStoredMap(getTextQueueStorageKey(normalizedUserId))

    const mediaPlaceholders = readStoredMap(getMediaPlaceholderStorageKey(normalizedUserId))
    localMediaPlaceholdersByClientId.value = Object.fromEntries(
      Object.entries(mediaPlaceholders).map(([key, item]) => [
        key,
        {
          ...item,
          // Browser File/Blob objects do not survive reload; never fake media retry.
          status: 'failed',
        },
      ]),
    )
    persistMediaPlaceholders()

    for (const item of Object.values(queuedTextByClientId.value)) {
      if (!item?.clientMessageId) continue
      pendingByClientId.value = {
        ...pendingByClientId.value,
        [normalizeId(item.clientMessageId)]: item,
      }
    }

    if (activeChatId.value) {
      restoreQueuedMessagesForChat(activeChatId.value, currentUserId)
    }
  }

  function removeQueueEntriesForServerMessages(messages) {
    let changed = false
    for (const message of messages || []) {
      const clientMessageId = message.clientMessageId ?? message.ClientMessageId ?? null
      if (!clientMessageId) continue
      const key = normalizeId(clientMessageId)
      if (queuedTextByClientId.value[key] || localMediaPlaceholdersByClientId.value[key]) {
        removeQueuedDelivery(clientMessageId)
        changed = true
      }
    }
    return changed
  }

  function restoreQueuedMessagesForChat(chatId, currentUserId) {
    const chatKey = normalizeId(chatId)
    if (!chatKey) return

    const textItems = Object.values(queuedTextByClientId.value)
      .filter((item) => normalizeId(item.chatId) === chatKey)

    for (const item of textItems) {
      const mapped = buildStoreMessage({
        chatId: item.chatId,
        senderUserId: item.senderUserId,
        clientMessageId: item.clientMessageId,
        sentAtClient: item.sentAtClient,
        sentAt: item.sentAtClient,
        text: item.text,
        type: 0,
        deliveryStatus: item.status === 'failed' ? 2 : 0,
        isRead: false,
        version: Date.parse(item.sentAtClient || '') || Date.now(),
      }, currentUserId)
      upsertMessage(chatId, mapped)
    }

    const mediaItems = Object.values(localMediaPlaceholdersByClientId.value)
      .filter((item) => normalizeId(item.chatId) === chatKey)

    for (const item of mediaItems) {
      const mapped = buildStoreMessage({
        chatId: item.chatId,
        senderUserId: item.senderUserId,
        clientMessageId: item.clientMessageId,
        sentAtClient: item.sentAtClient,
        sentAt: item.sentAtClient,
        type: 2,
        mediaContentType: item.mediaContentType,
        mediaFileName: item.mediaFileName,
        mediaSizeBytes: item.mediaSizeBytes,
        deliveryStatus: 2,
        isRead: false,
        version: Date.parse(item.sentAtClient || '') || Date.now(),
      }, currentUserId)
      mapped.uploadState = 'failed'
      mapped.localOnly = true
      upsertMessage(chatId, mapped)
    }
  }

  function getQueuedTextMessages(statuses = ['pending', 'failed']) {
    const allowed = new Set(statuses)
    return Object.values(queuedTextByClientId.value)
      .filter((item) => item?.clientMessageId && allowed.has(item.status || 'pending'))
      .sort((a, b) => new Date(a.sentAtClient || 0).getTime() - new Date(b.sentAtClient || 0).getTime())
  }

  function ensureQueuedTextMessageFromMessage(message, currentUserId) {
    if (!message?.clientMessageId || Number(message.type ?? 0) !== 0) return null

    const key = normalizeId(message.clientMessageId)
    const existing = queuedTextByClientId.value[key]
    if (existing) return existing

    return upsertQueuedTextMessage({
      chatId: message.chatId,
      senderUserId: message.senderUserId || currentUserId,
      clientMessageId: message.clientMessageId,
      sentAtClient: message.sentAtClient || message.sentAt,
      text: message.text,
    }, 'failed')
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
      removeQueueEntriesForServerMessages(incoming)

      messagesByChatId.value = {
        ...messagesByChatId.value,
        [chatKey]: ensureSorted(incoming),
      }
      restoreQueuedMessagesForChat(chatId, currentUserId)

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
    if (mapped.clientMessageId) {
      removeQueuedDelivery(mapped.clientMessageId)
    }
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
    upsertQueuedTextMessage({
      chatId,
      senderUserId: payload.senderUserId,
      clientMessageId: payload.clientMessageId,
      sentAtClient: payload.sentAtClient,
      text: payload.text,
    }, 'pending')

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
    upsertLocalMediaPlaceholder({
      chatId,
      senderUserId: payload.senderUserId,
      clientMessageId: payload.clientMessageId,
      sentAtClient: payload.sentAtClient,
      mediaContentType: payload.contentType,
      mediaFileName: payload.fileName,
      mediaSizeBytes: payload.sizeBytes,
    }, 'pending')

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
    removeQueuedDelivery(clientMessageId)
  }

  function markPendingMessageAsSending(chatId, clientMessageId) {
    if (!clientMessageId) return
    const chatKey = normalizeId(chatId)
    const pendingKey = normalizeId(clientMessageId)
    const current = messagesByChatId.value[chatKey] || []
    const index = current.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === pendingKey)

    if (index >= 0) {
      current[index] = {
        ...current[index],
        deliveryStatus: 0,
        uploadState: current[index].type === 2 ? 'uploading' : undefined,
      }

      messagesByChatId.value = {
        ...messagesByChatId.value,
        [chatKey]: [...current],
      }
    }

    const queued = queuedTextByClientId.value[pendingKey]
    if (queued) {
      queuedTextByClientId.value = {
        ...queuedTextByClientId.value,
        [pendingKey]: {
          ...queued,
          status: 'pending',
          attempts: Number(queued.attempts || 0) + 1,
          updatedAt: new Date().toISOString(),
        },
      }
      persistTextQueue()
    }
  }

  function markPendingMessageAsFailed(chatId, clientMessageId) {
    if (!clientMessageId) return
    const chatKey = normalizeId(chatId)
    const pendingKey = normalizeId(clientMessageId)
    const current = messagesByChatId.value[chatKey] || []
    const index = current.findIndex((m) => m.clientMessageId && normalizeId(m.clientMessageId) === pendingKey)
    if (index < 0) {
      const queued = queuedTextByClientId.value[pendingKey]
      if (queued) {
        queuedTextByClientId.value = {
          ...queuedTextByClientId.value,
          [pendingKey]: {
            ...queued,
            status: 'failed',
            updatedAt: new Date().toISOString(),
          },
        }
        persistTextQueue()
      }
      return
    }

    current[index] = {
      ...current[index],
      deliveryStatus: 2,
      uploadState: 'failed',
    }

    messagesByChatId.value = {
      ...messagesByChatId.value,
      [chatKey]: [...current],
    }

    if (Number(current[index].type ?? 0) === 0) {
      const queued = queuedTextByClientId.value[pendingKey]
      if (queued) {
        queuedTextByClientId.value = {
          ...queuedTextByClientId.value,
          [pendingKey]: {
            ...queued,
            status: 'failed',
            updatedAt: new Date().toISOString(),
          },
        }
        persistTextQueue()
      }
    }

    if (Number(current[index].type ?? 0) === 2) {
      const queued = localMediaPlaceholdersByClientId.value[pendingKey]
      if (queued) {
        localMediaPlaceholdersByClientId.value = {
          ...localMediaPlaceholdersByClientId.value,
          [pendingKey]: {
            ...queued,
            status: 'failed',
            updatedAt: new Date().toISOString(),
          },
        }
        persistMediaPlaceholders()
      }
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
    queuedTextByClientId,
    localMediaPlaceholdersByClientId,
    activeChatId,
    setActiveChat,
    getMessagesByChatId,
    getPageState,
    loadLatestMessagesByChatId,
    loadOlderMessagesByChatId,
    addBackendMessageToState,
    addOptimisticTextMessage,
    addOptimisticMediaMessage,
    hydrateDeliveryQueue,
    restoreQueuedMessagesForChat,
    getQueuedTextMessages,
    ensureQueuedTextMessageFromMessage,
    updatePendingUploadProgress,
    markPendingMessageAsSent,
    markPendingMessageAsSending,
    markPendingMessageAsFailed,
    markMessagesAsReadByIds,
    clearMessages,
  }
})
