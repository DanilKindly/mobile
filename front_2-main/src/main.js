import { createApp } from "vue"
import { createPinia } from 'pinia'
import App from "./App.vue"
import { router } from "./router"
import "./assets/main.css"
import { bootstrapWebPush } from '@/shared/services/webPushService'

const pinia = createPinia()

bootstrapWebPush().catch(() => {})

createApp(App).use(pinia).use(router).mount("#app")
