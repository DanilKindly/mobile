<script setup>
import { onBeforeUnmount, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import messengerApi from '@/api/messenger'

const route = useRoute()

let blurTimer = null
let lastPresenceKey = null

function clearBlurTimer() {
  if (!blurTimer) return
  clearTimeout(blurTimer)
  blurTimer = null
}

function isMessengerRoute(pathname) {
  return pathname.startsWith('/chat') || pathname.startsWith('/chats')
}

function resolvePresenceTarget() {
  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) {
    return { userId: null, isOnline: false }
  }

  const visible = typeof document === 'undefined' ? true : document.visibilityState === 'visible'
  const focused = typeof document?.hasFocus === 'function' ? document.hasFocus() : true
  const inMessenger = isMessengerRoute(route.path)

  return {
    userId: currentUser.userId,
    isOnline: inMessenger && visible && focused,
  }
}

async function syncGlobalPresence() {
  const target = resolvePresenceTarget()
  if (!target.userId) {
    lastPresenceKey = null
    return
  }

  const presenceKey = `${target.userId}:${target.isOnline ? '1' : '0'}`
  if (presenceKey === lastPresenceKey) return

  try {
    await messengerApi.setPresence(target.userId, target.isOnline)
    lastPresenceKey = presenceKey
  } catch (error) {
    console.error('Failed to sync global presence:', error)
  }
}

function handleVisibilityChange() {
  clearBlurTimer()
  syncGlobalPresence()
}

function handleWindowFocus() {
  clearBlurTimer()
  syncGlobalPresence()
}

function handleWindowBlur() {
  clearBlurTimer()
  blurTimer = setTimeout(() => {
    syncGlobalPresence()
  }, 900)
}

function handlePageHide() {
  const currentUser = messengerApi.getCurrentUser()
  if (!currentUser?.userId) return
  messengerApi.setPresence(currentUser.userId, false).catch(() => {})
}

watch(
  () => route.path,
  () => {
    syncGlobalPresence()
  },
)

onMounted(() => {
  document.addEventListener('visibilitychange', handleVisibilityChange)
  window.addEventListener('focus', handleWindowFocus)
  window.addEventListener('blur', handleWindowBlur)
  window.addEventListener('pagehide', handlePageHide)
  syncGlobalPresence()
})

onBeforeUnmount(() => {
  clearBlurTimer()
  document.removeEventListener('visibilitychange', handleVisibilityChange)
  window.removeEventListener('focus', handleWindowFocus)
  window.removeEventListener('blur', handleWindowBlur)
  window.removeEventListener('pagehide', handlePageHide)
})
</script>

<template>
  <router-view />
</template>
