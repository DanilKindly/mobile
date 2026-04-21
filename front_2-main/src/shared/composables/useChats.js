import { ref, onMounted } from "vue";
import messengerApi from "@/api/messenger";

export function useChats() {
  const chats = ref([]);
  const loading = ref(false);
  const error = ref(null);

  async function loadChats() {
    loading.value = true;
    error.value = null;
    try {
      // Получаем текущего пользователя
      const currentUser = await messengerApi.getOrCreateCurrentUser();
      const currentUserId = currentUser.userId ?? currentUser.UserId;
      
      // Загружаем чаты с бэкенда
      const backendChats = await messengerApi.getChatsByUser(currentUserId);
      
      chats.value = backendChats.map(chat => ({
        id: chat.chatId ?? chat.ChatId,
        name: chat.name || `Чат ${chat.chatId?.slice(0, 8) || 'Unknown'}`,
        lastMessage: "",
        isGroup: chat.isGroup ?? chat.IsGroup
      }));
    } catch (err) {
      error.value = err.message;
      console.error('Ошибка загрузки чатов:', err);
    } finally {
      loading.value = false;
    }
  }

  function updateLastMessage(chatId, message) {
    const chat = chats.value.find((c) => c.id === chatId);
    if (chat) {
      chat.lastMessage = message;
    }
  }

  onMounted(() => {
    loadChats();
  });

  return {
    chats,
    loading,
    error,
    loadChats,
    updateLastMessage,
  };
}
