import { api } from './api.js';
import { storage } from './storage.js';

const ACTIVE_KEY = 'pomnyu.activeChat';
const CACHE_KEY = 'pomnyu.chatCache.v1';
const LEGACY_KEYS = ['pomnyu.chats.v1', 'pomnyu.chat.v1'];
const TITLE_LENGTH = 80;
const CACHED_CHATS = 12;
const CACHED_MESSAGES = 80;
const PREFETCH_CHATS = 6;

const newId = () => (crypto.randomUUID ? crypto.randomUUID() : `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 12)}`);

const persistedCount = (messages) => messages.filter((message) => message.id || message.role === 'user').length;

export class ChatStore {
  #list = [];
  #messages = new Map();
  #active = { id: newId(), title: 'Новый чат', messages: [] };
  #pickOnSync = false;
  #opening = 0;
  onActiveUpdated = null;

  init() {
    const cache = storage.get(CACHE_KEY, null);
    if (cache && Array.isArray(cache.list)) {
      this.#list = cache.list;
      for (const [id, messages] of Object.entries(cache.messages ?? {})) {
        if (Array.isArray(messages)) this.#messages.set(id, messages);
      }
    }
    const remembered = storage.get(ACTIVE_KEY, null);
    const target = this.#list.find((chat) => chat.id === remembered) ?? this.#list[0];
    if (target) this.#show(target.id);
    else {
      this.create();
      this.#pickOnSync = true;
    }
  }

  async sync() {
    await this.#migrateLegacy();
    const result = await this.refresh();
    this.#prefetch();
    return result;
  }

  get active() {
    return this.#active;
  }

  get all() {
    const list = [...this.#list];
    if (!list.some((chat) => chat.id === this.#active.id)) {
      list.unshift({ id: this.#active.id, title: this.#active.title, updatedAt: new Date().toISOString(), messageCount: this.#active.messages.length, lastText: null });
    }
    return list;
  }

  select(id) {
    if (id === this.#active.id) return this.#active;
    this.#show(id);
    this.#reload(id);
    return this.#active;
  }

  create() {
    this.#active = { id: newId(), title: 'Новый чат', messages: [] };
    storage.set(ACTIVE_KEY, this.#active.id);
    return this.#active;
  }

  async remove(id) {
    await api.deleteConversation(id);
    this.#list = this.#list.filter((chat) => chat.id !== id);
    this.#messages.delete(id);
    this.#save();
    if (id === this.#active.id) {
      if (this.#list[0]) this.select(this.#list[0].id);
      else this.create();
    }
    return this.#active;
  }

  async clearActive() {
    const id = this.#active.id;
    if (this.#list.some((chat) => chat.id === id)) await api.deleteConversation(id);
    this.#list = this.#list.filter((chat) => chat.id !== id);
    this.#messages.delete(id);
    this.#save();
    return this.create();
  }

  append(message) {
    const chat = this.#active;
    chat.messages.push(message);
    this.#messages.set(chat.id, chat.messages);
    if (message.role === 'user' && chat.messages.filter((item) => item.role === 'user').length === 1) {
      chat.title = message.text.length > TITLE_LENGTH ? `${message.text.slice(0, TITLE_LENGTH - 1)}…` : message.text;
    }
    const previous = this.#list.find((item) => item.id === chat.id);
    const entry = {
      id: chat.id,
      title: chat.title,
      updatedAt: new Date().toISOString(),
      messageCount: persistedCount(chat.messages),
      lastText: message.role === 'assistant' ? message.text : previous?.lastText ?? null,
    };
    this.#list = [entry, ...this.#list.filter((item) => item.id !== chat.id)];
    this.#save();
  }

  assignUserMessageId(text, id) {
    if (!id) return;
    const message = [...this.#active.messages].reverse().find((item) => item.role === 'user' && !item.id && item.text === text);
    if (message) {
      message.id = id;
      this.#save();
    }
  }

  persistCard(message) {
    if (!message.id || !message.card) return;
    this.#save();
    api.updateCard(this.#active.id, message.id, message.card).catch(() => {});
  }

  async refresh() {
    try {
      this.#list = await api.conversations();
    } catch {
      return { activeChanged: false };
    }

    const summary = this.#list.find((chat) => chat.id === this.#active.id);
    if (!summary) {
      if (this.#pickOnSync && this.#active.messages.length === 0 && this.#list[0]) {
        this.#pickOnSync = false;
        this.#show(this.#list[0].id);
        await this.#reload(this.#list[0].id, { notify: false });
        return { activeChanged: true };
      }
      this.#save();
      return { activeChanged: false };
    }

    this.#pickOnSync = false;
    if (summary.messageCount === persistedCount(this.#active.messages)) {
      this.#save();
      return { activeChanged: false };
    }
    return { activeChanged: await this.#reload(this.#active.id, { notify: false }) };
  }

  #show(id) {
    const summary = this.#list.find((chat) => chat.id === id);
    const cached = this.#messages.get(id);
    this.#active = { id, title: summary?.title ?? 'Новый чат', messages: cached ?? [], loading: !cached && (summary?.messageCount ?? 0) > 0 };
    storage.set(ACTIVE_KEY, id);
  }

  async #prefetch() {
    for (const chat of this.#list.slice(0, PREFETCH_CHATS)) {
      if (this.#messages.has(chat.id) || chat.messageCount === 0) continue;
      try {
        this.#messages.set(chat.id, (await api.conversationMessages(chat.id)).map(fromServer));
      } catch {
        return;
      }
    }
    this.#save();
  }

  async #reload(id, { notify = true } = {}) {
    const ticket = ++this.#opening;
    let messages;
    try {
      messages = (await api.conversationMessages(id)).map(fromServer);
    } catch {
      if (this.#active.id === id && this.#active.loading) {
        this.#active = { ...this.#active, loading: false };
        if (notify) this.onActiveUpdated?.();
      }
      return false;
    }
    if (ticket !== this.#opening) return false;

    const known = this.#messages.get(id) ?? [];
    const changed = this.#active.loading || known.length !== messages.length || persistedCount(known) !== messages.length
      || known.some((message, index) => message.id !== messages[index]?.id || JSON.stringify(message.card) !== JSON.stringify(messages[index]?.card));
    this.#messages.set(id, messages);
    if (this.#active.id === id) this.#active = { ...this.#active, messages, loading: false };
    this.#save();
    if (changed && notify && this.#active.id === id) this.onActiveUpdated?.();
    return changed;
  }

  #save() {
    const keep = this.#list.slice(0, CACHED_CHATS).map((chat) => chat.id);
    if (!keep.includes(this.#active.id)) keep.push(this.#active.id);
    const messages = Object.fromEntries(keep
      .filter((id) => this.#messages.has(id))
      .map((id) => [id, this.#messages.get(id).filter((message) => message.role !== 'error').slice(-CACHED_MESSAGES)]));
    storage.set(CACHE_KEY, { list: this.#list, messages });
  }

  async #migrateLegacy() {
    for (const key of LEGACY_KEYS) {
      const saved = storage.get(key, null);
      if (!saved) continue;
      const chats = Array.isArray(saved) ? [{ id: newId(), messages: saved }] : Array.isArray(saved.chats) ? saved.chats : [];
      for (const chat of chats) {
        const messages = (chat.messages ?? [])
          .filter((message) => message && typeof message.text === 'string' && message.text.trim() && (message.role === 'user' || message.role === 'assistant'))
          .slice(-200)
          .map((message) => ({ role: message.role, text: message.text.slice(0, 4000), via: message.via, kind: message.kind, at: message.at, card: message.card ?? null, quickReplies: message.quickReplies ?? [] }));
        const id = typeof chat.id === 'string' && /^[A-Za-z0-9-]{1,64}$/.test(chat.id) ? chat.id : newId();
        if (messages.length > 0) await api.importConversation(id, messages).catch(() => {});
      }
      storage.remove(key);
    }
  }
}

function fromServer(message) {
  return {
    id: message.id,
    role: message.role,
    text: message.text,
    via: message.via,
    kind: message.kind,
    card: message.card ?? null,
    quickReplies: message.quickReplies ?? [],
    at: message.at,
  };
}
