import { ref } from "vue";

const BOT_PHRASES = [
  "ничего себе",
  "ага",
  "вот это да",
  "фуууу",
  "чебупеля",
  "ну ты бордюр",
  "слышал?",
  "сегодня не завтра",
  "ну ты понял",
  "конечно",
  "может быть",
  "интересно...",
  "ну ты и ландыш",
  "Привет, шоколадный",
  "не сегодня",
  "серьёзно?",
  "окей",
];

export function useBot() {
  const isBotTyping = ref(false);
  const isBotOnline = ref(false);
  let typingTimeout = null;
  let responseTimeout = null;

  function getRandomPhrase() {
    return BOT_PHRASES[Math.floor(Math.random() * BOT_PHRASES.length)];
  }

  function clearTimeouts() {
    if (typingTimeout) clearTimeout(typingTimeout);
    if (responseTimeout) clearTimeout(responseTimeout);
  }

  function simulateResponse(onBotMessage) {
    clearTimeouts();

    isBotOnline.value = true;

    // Показываем "печатает..." через небольшую задержку
    typingTimeout = setTimeout(() => {
      isBotTyping.value = true;
    }, 500);

    const delay = Math.floor(Math.random() * 2000) + 1000; // 1-3 секунды
    responseTimeout = setTimeout(() => {
      const phrase = getRandomPhrase();
      onBotMessage(phrase);
      isBotTyping.value = false;
      isBotOnline.value = false;
    }, delay);
  }

  return {
    isBotTyping,
    isBotOnline,
    simulateResponse,
    clearTimeouts,
  };
}
