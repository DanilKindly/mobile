import { createRouter, createWebHistory } from 'vue-router'
import ChatView from '../features/chat/views/ChatView.vue'
import ChatListView from '../features/chat/views/ChatListView.vue'
import LoginView from '../features/chat/views/LoginView.vue'
import messengerApi from '@/api/messenger'

const routes = [
  {
    path: '/',
    name: 'login',
    component: LoginView,
    meta: { guestOnly: true },
  },
  {
    path: '/chats',
    name: 'chats',
    component: ChatListView,
    meta: { requiresAuth: true },
  },
  {
    path: '/chat/:chatId',
    name: 'chat',
    component: ChatView,
    meta: { requiresAuth: true },
  },
  {
    path: '/chat',
    redirect: '/chats',
  },
]

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach((to) => {
  const currentUser = messengerApi.getCurrentUser()

  if (to.meta?.requiresAuth && !currentUser) {
    return { path: '/' }
  }

  if (to.meta?.guestOnly && currentUser) {
    return { path: '/chats' }
  }

  return true
})
