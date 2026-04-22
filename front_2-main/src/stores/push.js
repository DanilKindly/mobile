import { defineStore } from 'pinia'
import { ref } from 'vue'
import messengerApi from '@/api/messenger'
import { bootstrapWebPush, forceResubscribe, PUSH_STATUS } from '@/shared/services/webPushService'

export const usePushStore = defineStore('push', () => {
  const status = ref(PUSH_STATUS.SUBSCRIBE_FAILED)
  const isBusy = ref(false)
  const requiresHomeScreen = ref(false)
  const isIphone = ref(false)
  const isStandalone = ref(false)
  const endpointMasked = ref(null)
  const lastSuccessAt = ref(null)
  const lastFailureAt = ref(null)
  const failureCount = ref(0)
  const lastErrorCode = ref(null)

  let bootstrapInFlight = null

  function applyPushStatus(pushResult) {
    status.value = pushResult?.status || PUSH_STATUS.SUBSCRIBE_FAILED
    requiresHomeScreen.value = Boolean(pushResult?.requiresHomeScreen)
    isIphone.value = Boolean(pushResult?.isIphone)
    isStandalone.value = Boolean(pushResult?.isStandalone)
  }

  function applyServerStatus(serverStatus) {
    endpointMasked.value = serverStatus?.endpointMasked ?? serverStatus?.EndpointMasked ?? null
    lastSuccessAt.value = serverStatus?.lastSuccessAt ?? serverStatus?.LastSuccessAt ?? null
    lastFailureAt.value = serverStatus?.lastFailureAt ?? serverStatus?.LastFailureAt ?? null
    failureCount.value = Number(serverStatus?.failureCount ?? serverStatus?.FailureCount ?? 0)
    lastErrorCode.value = serverStatus?.lastErrorCode ?? serverStatus?.LastErrorCode ?? null
  }

  async function refreshServerStatus() {
    try {
      const serverStatus = await messengerApi.getPushStatus()
      applyServerStatus(serverStatus)
      if (serverStatus?.hasActiveSubscription === false && status.value === PUSH_STATUS.SUBSCRIBED) {
        status.value = PUSH_STATUS.SUBSCRIBE_FAILED
      }
    } catch (error) {
      console.warn('Failed to load push status:', error)
    }
  }

  async function bootstrapAfterLogin() {
    if (bootstrapInFlight) return bootstrapInFlight

    bootstrapInFlight = (async () => {
      isBusy.value = true
      try {
        const currentUser = messengerApi.getCurrentUser()
        if (!currentUser?.userId) {
          status.value = PUSH_STATUS.NO_USER
          return
        }

        const result = await bootstrapWebPush()
        applyPushStatus(result)
        await refreshServerStatus()
      } finally {
        isBusy.value = false
        bootstrapInFlight = null
      }
    })()

    return bootstrapInFlight
  }

  async function reconnectPush() {
    isBusy.value = true
    try {
      const result = await forceResubscribe()
      applyPushStatus(result)
      await refreshServerStatus()
      return result
    } finally {
      isBusy.value = false
    }
  }

  return {
    status,
    isBusy,
    requiresHomeScreen,
    isIphone,
    isStandalone,
    endpointMasked,
    lastSuccessAt,
    lastFailureAt,
    failureCount,
    lastErrorCode,
    bootstrapAfterLogin,
    reconnectPush,
    refreshServerStatus,
  }
})

