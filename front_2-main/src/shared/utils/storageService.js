const STORAGE_PREFIX = "ois_chat_";

export const storageService = {
  getMessages(chatName, isBot) {
    const key = `${STORAGE_PREFIX}messages_${chatName}_${isBot ? "bot" : "user"}`;
    return JSON.parse(localStorage.getItem(key)) || [];
  },

  saveMessage(chatName, isBot, text, time, isRead = false) {
    const key = `${STORAGE_PREFIX}messages_${chatName}_${isBot ? "bot" : "user"}`;
    const messages = this.getMessages(chatName, isBot);
    messages.push([text, time, isRead]);
    localStorage.setItem(key, JSON.stringify(messages));
  },

  updateMessageReadStatus(chatName, isBot, index, isRead) {
    const key = `${STORAGE_PREFIX}messages_${chatName}_${isBot ? "bot" : "user"}`;
    const messages = this.getMessages(chatName, isBot);
    if (messages[index]) {
      messages[index][2] = isRead;
      localStorage.setItem(key, JSON.stringify(messages));
    }
  },

  getLastBotMessage(chatName) {
    const botMessages = this.getMessages(chatName, true);
    return botMessages.length > 0 ? botMessages[botMessages.length - 1][0] : "";
  },

  clearMessages(chatName) {
    localStorage.removeItem(`${STORAGE_PREFIX}messages_${chatName}_user`);
    localStorage.removeItem(`${STORAGE_PREFIX}messages_${chatName}_bot`);
  },
};
