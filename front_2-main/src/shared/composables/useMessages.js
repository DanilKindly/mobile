import { ref } from "vue";
import { storageService } from "@/shared/utils/storageService";

export function useMessages() {
  const messages = ref([]);

  function loadMessages(chatName) {
    const userMessages = storageService.getMessages(chatName, false);
    const botMessages = storageService.getMessages(chatName, true);

    // Объединяем и сортируем по времени
    const allMessages = [
      ...userMessages.map(([text, time]) => ({ text, time, isBot: false })),
      ...botMessages.map(([text, time]) => ({ text, time, isBot: true })),
    ];

    // Сортируем по времени (новые последние)
    allMessages.sort((a, b) => {
      const [aHours, aMins] = a.time.split(":").map(Number);
      const [bHours, bMins] = b.time.split(":").map(Number);
      return aHours * 60 + aMins - (bHours * 60 + bMins);
    });

    // Переворачиваем для flex-col-reverse
    messages.value = allMessages.reverse();
  }

  function addMessage(chatName, text, isBot = false) {
    const now = new Date();
    const timeString = `${now.getHours().toString().padStart(2, "0")}:${now.getMinutes().toString().padStart(2, "0")}`;

    storageService.saveMessage(chatName, isBot, text, timeString);

    const newMessage = { text, time: timeString, isBot };
    messages.value.unshift(newMessage);

    return newMessage;
  }

  function clearMessages() {
    messages.value = [];
  }

  return {
    messages,
    loadMessages,
    addMessage,
    clearMessages,
  };
}
