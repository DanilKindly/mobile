import messengerApi from '@/api/messenger'
import { useChatStore } from '@/stores/chat'
import { useMessageStore } from '@/stores/message'

const inFlightClientIds = new Set()

function normalizeId(id) {
  return String(id || '').toLowerCase()
}

export function useTextMessageDelivery() {
  const chatStore = useChatStore()
  const messageStore = useMessageStore()

  function getCurrentUserId() {
    return chatStore.currentUser?.userId || messengerApi.getCurrentUser()?.userId || null
  }

  function hydrateDeliveryQueue() {
    const currentUserId = getCurrentUserId()
    if (!currentUserId) return
    messageStore.hydrateDeliveryQueue(currentUserId, currentUserId)
  }

  async function sendQueuedTextMessage(item, options = {}) {
    const currentUserId = getCurrentUserId()
    if (!currentUserId || !item?.clientMessageId || !item.chatId || !item.text) {
      return { ok: false, error: new Error('Invalid queued message.') }
    }

    const key = normalizeId(item.clientMessageId)
    if (inFlightClientIds.has(key)) {
      return { ok: false, skipped: true }
    }

    inFlightClientIds.add(key)
    messageStore.markPendingMessageAsSending(item.chatId, item.clientMessageId)

    try {
      const result = await messengerApi.sendMessageReliable(item.chatId, item.text, item.senderUserId || currentUserId, {
        clientMessageId: item.clientMessageId,
        sentAtClient: item.sentAtClient,
        ackTimeoutMs: options.ackTimeoutMs ?? 2800,
        maxRetries: options.maxRetries ?? 2,
      })

      messageStore.markPendingMessageAsSent(item.chatId, item.clientMessageId, result.message, currentUserId)
      chatStore.updatePreviewFromMessage(item.chatId, result.message, currentUserId)
      await options.onSent?.(result.message)
      return { ok: true, message: result.message, source: result.source }
    } catch (error) {
      messageStore.markPendingMessageAsFailed(item.chatId, item.clientMessageId)
      await options.onFailed?.(error)
      return { ok: false, error }
    } finally {
      inFlightClientIds.delete(key)
    }
  }

  async function enqueueAndSendText(chatId, text, options = {}) {
    const currentUserId = getCurrentUserId()
    if (!currentUserId || !chatId || !text) {
      return { ok: false, error: new Error('Cannot send without current user/chat/text.') }
    }

    hydrateDeliveryQueue()

    const clientMessageId = crypto.randomUUID()
    const sentAtClient = new Date().toISOString()
    const item = {
      chatId,
      senderUserId: currentUserId,
      clientMessageId,
      sentAtClient,
      text,
    }

    messageStore.addOptimisticTextMessage(chatId, item, currentUserId)
    chatStore.updatePreviewFromMessage(
      chatId,
      { senderUserId: currentUserId, text, type: 0, sentAt: sentAtClient },
      currentUserId,
    )
    await options.onOptimistic?.()

    return sendQueuedTextMessage(item, options)
  }

  async function retryTextMessage(message, options = {}) {
    const currentUserId = getCurrentUserId()
    if (!currentUserId || !message) {
      return { ok: false, error: new Error('Cannot retry without current user/message.') }
    }

    hydrateDeliveryQueue()
    const queued = messageStore.ensureQueuedTextMessageFromMessage(message, currentUserId)
    if (!queued) {
      return { ok: false, error: new Error('Message is not retryable.') }
    }

    return sendQueuedTextMessage(queued, options)
  }

  async function drainPendingTextQueue(options = {}) {
    hydrateDeliveryQueue()
    const pending = messageStore.getQueuedTextMessages(['pending'])
    for (const item of pending) {
      await sendQueuedTextMessage(item, {
        ...options,
        onFailed: async (error) => {
          console.warn('Failed to drain pending message:', error)
          await options.onFailed?.(error)
        },
      })
    }
  }

  return {
    hydrateDeliveryQueue,
    enqueueAndSendText,
    retryTextMessage,
    drainPendingTextQueue,
  }
}
