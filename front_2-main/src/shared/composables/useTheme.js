import { ref } from "vue";

const THEME_KEY = "ois_dark_theme";

export function useTheme() {
  const darkTheme = ref(false);

  function loadTheme() {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved !== null) {
      darkTheme.value = saved === "true";
    }
  }

  function toggleTheme() {
    darkTheme.value = !darkTheme.value;
    localStorage.setItem(THEME_KEY, darkTheme.value.toString());
  }

  function setTheme(isDark) {
    darkTheme.value = isDark;
    localStorage.setItem(THEME_KEY, isDark.toString());
  }

  // Загружаем тему при создании
  loadTheme();

  return {
    darkTheme,
    toggleTheme,
    setTheme,
    loadTheme,
  };
}
