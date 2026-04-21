import { createRouter, createWebHistory } from 'vue-router'
import ChatView from '../features/chat/views/ChatView.vue'
import ChatListView from '../features/chat/views/ChatListView.vue'
import LoginView from '../features/chat/views/LoginView.vue'

const routes = [
  {
    path: '/',
    name: 'login',
    component: LoginView
  },
  {
    path: '/chats',
    name: 'chats',
    component: ChatListView
  },
  {
    path: '/chat/:chatId',
    name: 'chat',
    component: ChatView
  },
  {
    path: '/chat',
    redirect: '/chats'
  }
]

export const router = createRouter({
  history: createWebHistory(),
  routes
})
