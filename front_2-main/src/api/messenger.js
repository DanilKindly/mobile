// src/api/messenger.js
import axios from 'axios';
import * as signalR from '@microsoft/signalr';

// Базовый URL — берём из .env (VITE_API_URL=http://localhost:5017)
const API_BASE = (import.meta.env.VITE_API_URL || '').trim();
const BACKEND_BASE = (import.meta.env.VITE_API_URL || import.meta.env.VITE_PROXY_TARGET || '').trim();

// Один экземпляр axios для всех запросов
const api = axios.create({
  baseURL: API_BASE,
  timeout: 10000,
  headers: {
    'Content-Type': 'application/json',
  },
});

// Логирование запросов
api.interceptors.request.use(config => {
  console.log(`📡 [API] → ${config.method?.toUpperCase()} ${config.url}`, config.data || '');
  const token = localStorage.getItem('token');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Логирование ответов
api.interceptors.response.use(
  response => {
    console.log(`📡 [API] ← ${response.config.method?.toUpperCase()} ${response.config.url} (${response.status})`, response.data);
    return response;
  },
  error => {
    console.error(`❌ [API] ← ${error.config?.method?.toUpperCase()} ${error.config?.url} (${error.response?.status || 'ERROR'})`, error.response?.data || error.message);
    if (error.response?.status === 401) {
      localStorage.removeItem('token');
      window.location.href = '/login'; // или используйте router.push
    }
    return Promise.reject(error);
  }
);

// SignalR — один экземпляр на приложение
let signalRConnection = null;

function getSignalRConnection() {
  if (!signalRConnection) {
    signalRConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/chat`)
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information) // Логирование SignalR
      .build();

    // Логирование событий SignalR
    signalRConnection.onclose((error) => {
      console.log('🔴 [SignalR] Соединение закрыто', error ? error : '');
    });

    signalRConnection.onreconnecting((error) => {
      console.log('🔄 [SignalR] Переподключение...', error ? error : '');
    });

    signalRConnection.onreconnected((connectionId) => {
      console.log('✅ [SignalR] Переподключено! ID:', connectionId);
    });
  }
  return signalRConnection;
}

const CURRENT_USER_KEY = 'ois_current_user';

function resolveAssetUrl(path) {
  if (!path) return null;
  if (/^https?:\/\//i.test(path)) return path;
  if (BACKEND_BASE) {
    return new URL(path, BACKEND_BASE).toString();
  }
  return path;
}

function getStoredCurrentUserRaw() {
  return sessionStorage.getItem(CURRENT_USER_KEY);
}

function setStoredCurrentUser(user) {
  sessionStorage.setItem(CURRENT_USER_KEY, JSON.stringify(user));
}

function clearStoredCurrentUser() {
  sessionStorage.removeItem(CURRENT_USER_KEY);
}

// API методы
export const messengerApi = {
  // ---------- Пользователи ----------
  async getUsers() {
    const response = await api.get('/api/users');
    return response.data;
  },

  async createUser({ nickname, name, phoneNumber = null }) {
    const response = await api.post('/api/users', {
      nickname,
      name,
      phoneNumber,
    });
    return response.data;
  },

  /**
   * Получить или создать локального "текущего" пользователя.
   * Результат кэшируется в sessionStorage.
   */
  async getOrCreateCurrentUser() {
    const cached = getStoredCurrentUserRaw();
    if (cached) {
      try {
        const parsed = JSON.parse(cached);
        // Проверяем, существует ли пользователь на бэкенде
        const users = await this.getUsers();
        const exists = users.some(u =>
          (u.userId || u.UserId) === (parsed.userId || parsed.UserId) ||
          (u.nickname || u.Nickname) === (parsed.nickname || parsed.Nickname)
        );
        if (exists) {
          return parsed;
        }
        // Пользователя нет в бэкенде — удаляем кэш
        clearStoredCurrentUser();
      } catch {
        clearStoredCurrentUser();
      }
    }

    const desiredNickname = 'glorbo';
    const desiredName = 'glorbo';

    const users = await this.getUsers();
    let user =
      users.find(u => (u.nickname || u.Nickname) === desiredNickname) ?? null;

    if (!user) {
      user = await this.createUser({
        nickname: desiredNickname,
        name: desiredName,
        phoneNumber: null,
      });
    }

    setStoredCurrentUser(user);
    return user;
  },

  /**
   * Получить или создать личный чат между текущим пользователем и собеседником.
   * chatName здесь используем как ник/имя собеседника.
   */
  async getOrCreateChatWithUser(currentUserId, chatName) {
    const cacheKey = `ois_chat_backend_id_${currentUserId}_${chatName}`;
    const cached = localStorage.getItem(cacheKey);
    if (cached) {
      try {
        const cachedChat = await this.getChatById(cached);
        const participantIds = cachedChat.participantUserIds ?? cachedChat.ParticipantUserIds ?? [];
        const hasCurrentUser = participantIds.some(
          (id) => String(id).toLowerCase() === String(currentUserId).toLowerCase()
        );
        if (hasCurrentUser) {
          return cached;
        }
      } catch {
        // ignore stale cache and resolve chat again
      }
    }

    // Получаем всех пользователей, ищем собеседника по нику
    const users = await this.getUsers();
    let peer =
      users.find(u => (u.nickname || u.Nickname) === chatName) ?? null;

    if (!peer) {
      peer = await this.createUser({
        nickname: chatName,
        name: chatName,
        phoneNumber: null,
      });
    }

    const peerUserId = peer.userId ?? peer.UserId;

    // Ищем уже существующий чат между currentUser и peer
    const chatsResponse = await api.get(
      `/api/chats/by-user/${currentUserId}`
    );
    const chats = chatsResponse.data ?? [];

    let chat =
      chats.find(
        c =>
          (c.isGroup ?? c.IsGroup) === false &&
          (c.participantUserIds ?? c.ParticipantUserIds)?.includes(peerUserId)
      ) ?? null;

    if (!chat) {
      const createResponse = await api.post('/api/chats', {
        isGroup: false,
        name: null,
        participantUserIds: [currentUserId, peerUserId],
      });
      chat = createResponse.data;
    }

    const chatId = chat.chatId ?? chat.ChatId;
    localStorage.setItem(cacheKey, chatId);
    return chatId;
  },

  // ---------- Чаты ----------
  async createChat(payload) {
    // payload = { isGroup: true/false, name: 'Группа', participantUserIds: ['guid1', 'guid2'] }
    const response = await api.post('/api/chats', payload);
    return response.data;
  },

  async getChatById(chatId) {
    const response = await api.get(`/api/chats/${chatId}`);
    return response.data;
  },

  async getChatsByUser(userId) {
    const response = await api.get(`/api/chats/by-user/${userId}`);
    return response.data;
  },

  // ---------- Сообщения ----------
  // История сообщений через REST
  async getMessages(chatId) {
    const response = await api.get(`/api/chats/${chatId}/messages`);
    return response.data; // массив объектов { messageId, chatId, senderUserId, text, sentAt }
  },

  // Отправка сообщения через HTTP (MessagesController)
  async sendMessageRest(chatId, text, senderUserId) {
    const response = await api.post(`/api/chats/${chatId}/messages`, {
      senderUserId,
      text,
    });
    return response.data;
  },

  async sendVoiceMessage(chatId, senderUserId, audioBlob, durationSeconds = null, fileName = null) {
    const formData = new FormData();
    formData.append('senderUserId', senderUserId);
    if (durationSeconds != null) {
      formData.append('durationSeconds', String(durationSeconds));
    }
    const resolvedFileName = fileName || `voice-${Date.now()}.webm`;
    formData.append('audio', audioBlob, resolvedFileName);

    const response = await api.post(
      `/api/chats/${chatId}/messages/voice`,
      formData,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    return response.data;
  },

  async sendMediaMessage(chatId, senderUserId, file) {
    const formData = new FormData();
    formData.append('senderUserId', senderUserId);
    formData.append('file', file, file.name);

    const response = await api.post(
      `/api/chats/${chatId}/messages/media`,
      formData,
      { headers: { 'Content-Type': 'multipart/form-data' } },
    );
    return response.data;
  },

  // Отправка сообщения через SignalR (для реального тайминга, при необходимости)
  async sendMessageSignalR(chatId, text, senderUserId) {
    const connection = getSignalRConnection();

    if (connection.state !== signalR.HubConnectionState.Connected) {
      console.log('🔌 [SignalR] Подключение...');
      await connection.start();
      console.log('✅ [SignalR] Подключено!');
    }

    const dto = {
      senderUserId,
      text,
    };

    console.log(`📤 [SignalR] Отправка сообщения в чат ${chatId}:`, text);
    await connection.invoke('SendMessage', chatId, dto);
    console.log('✅ [SignalR] Сообщение отправлено');
  },

  // Подключение / отключение от чата для реал-тайма
  async joinChat(chatId) {
    const connection = getSignalRConnection();
    if (connection.state !== signalR.HubConnectionState.Connected) {
      console.log('🔌 [SignalR] Подключение...');
      await connection.start();
      console.log('✅ [SignalR] Подключено!');
    }
    console.log(`📥 [SignalR] Присоединение к чату ${chatId}`);
    await connection.invoke('JoinChat', chatId);
  },

  async leaveChat(chatId) {
    const connection = getSignalRConnection();
    console.log(`📤 [SignalR] Выход из чата ${chatId}`);
    await connection.invoke('LeaveChat', chatId);
  },

  async markMessagesAsRead(chatId) {
    const connection = getSignalRConnection();
    if (connection.state !== signalR.HubConnectionState.Connected) {
      await connection.start();
    }
    console.log(`📤 [SignalR] Помечаем сообщения как прочитанные в чате ${chatId}`);
    await connection.invoke('MarkMessagesAsRead', chatId);
  },

  // Получить экземпляр connection, чтобы подписаться на события в компоненте
  getConnection() {
    return getSignalRConnection();
  },

  resolveAssetUrl,
};

// Экспорт по умолчанию — удобно импортировать как import api from '@/api/messenger'
export default messengerApi;
