import { api } from './api.js';
import { h, icon, logo, keyed, patchChildren } from './dom.js';
import { renderCard, renderErrorCard } from './cards.js';
import { dividerLabel, durationLabel, isoDate, timeOfDay } from './format.js';

export class Chat {
  #busy = false;
  #lastDay = null;

  constructor({ feed, store, speaker, cardContext, onReply, onBusyChange, onHistoryChange }) {
    Object.assign(this, { feed, store, speaker, cardContext, onReply, onBusyChange, onHistoryChange });
    this.speaker.onChange((key) => this.#syncListenButtons(key));
    this.show({ notify: false });
  }

  get #history() {
    return this.store.active.messages;
  }

  show({ notify = true, reconcile = false } = {}) {
    const scroller = this.feed.closest('.main');
    const nearBottom = scroller ? scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight < 160 : true;
    const before = this.feed.children.length;

    const nodes = [];
    let lastDay = null;
    for (const message of this.#history) {
      const day = isoDate(new Date(message.at ?? Date.now()));
      if (day !== lastDay) {
        lastDay = day;
        nodes.push(keyed(h('div', { class: 'divider' }, dividerLabel(day)), `day:${day}`, dividerLabel(day)));
      }
      nodes.push(this.#element(message, { animate: reconcile }));
    }
    if (this.store.active.loading) nodes.push(keyed(this.#placeholder(), 'loading', 'loading'));
    this.#lastDay = lastDay;
    patchChildren(this.feed, nodes);

    if (scroller && (!reconcile || (nearBottom && this.feed.children.length > before))) {
      if (reconcile) scroller.scrollTo({ top: scroller.scrollHeight, behavior: 'smooth' });
      else scroller.scrollTop = scroller.scrollHeight;
    }
    if (notify) this.onHistoryChange?.();
  }


  get isBusy() {
    return this.#busy;
  }

  get isEmpty() {
    return this.#history.length === 0 && !this.store.active.loading;
  }

  #placeholder() {
    return h('div', { class: 'bot-msg', 'aria-label': 'Загружаю чат' },
      logo(30), h('div', { class: 'bot-msg__body' }, h('div', { class: 'thinking' }, h('span'), h('span'), h('span'))));
  }

  get lastReply() {
    return [...this.#history].reverse().find((message) => message.role === 'assistant') ?? null;
  }

  async send(text, { via = 'text', isRetry = false } = {}) {
    const clean = text?.trim();
    if (this.#busy || !clean) return null;
    this.#setBusy(true);
    if (!isRetry) this.#push({ role: 'user', text: clean, via, at: new Date().toISOString() });

    const thinking = h('div', { class: 'bot-msg', 'aria-label': 'Ассистент думает' },
      logo(30), h('div', { class: 'bot-msg__body' }, h('div', { class: 'thinking' }, h('span'), h('span'), h('span'))));
    this.feed.append(thinking);
    this.#scrollToEnd();

    try {
      const chatId = this.store.active.id;
      const reply = await api.chat(clean, chatId, via);
      if (this.store.active.id !== chatId) return null;
      thinking.remove();
      this.store.assignUserMessageId(clean, reply.userMessageId);
      const parts = [reply, ...(reply.followUps ?? [])];
      const messages = parts.map((part, index) => ({
        id: part.messageId,
        role: 'assistant',
        text: part.text,
        kind: part.kind,
        card: part.card ?? null,
        quickReplies: part.quickReplies ?? [],
        at: new Date(Date.now() + index).toISOString(),
      }));
      messages.forEach((message) => this.#push(message));
      const last = messages[messages.length - 1];
      const combined = { ...last, text: messages.map((message) => message.text).join(' '), cards: messages.map((message) => message.card).filter(Boolean) };
      this.onReply?.(combined, { via });
      return last;
    } catch (error) {
      thinking.remove();
      const failure = { role: 'error', kind: 'failed', details: error.message, retry: clean, at: new Date().toISOString() };
      this.#render(failure);
      this.onReply?.(null, { via, error });
      return null;
    } finally {
      this.#setBusy(false);
    }
  }

  showError(kind) {
    this.#render({ role: 'error', kind, at: new Date().toISOString() });
  }

  async clear() {
    await this.store.clearActive();
    this.show();
  }

  #push(message) {
    this.store.append(message);
    this.#render(message);
    this.onHistoryChange?.();
  }

  #render(message) {
    const day = isoDate(new Date(message.at ?? Date.now()));
    if (day !== this.#lastDay) {
      this.#lastDay = day;
      this.feed.append(keyed(h('div', { class: 'divider' }, dividerLabel(day)), `day:${day}`, dividerLabel(day)));
    }
    this.feed.append(this.#element(message, { animate: true }));
    this.#scrollToEnd();
  }

  #element(message, { animate }) {
    const element = message.role === 'user'
      ? this.#userMessage(message)
      : message.role === 'error'
        ? this.#errorMessage(message)
        : this.#assistantMessage(message);
    if (!animate) element.style.animation = 'none';
    const key = message.role === 'error' ? `error:${message.at}` : `${message.role}:${message.id ?? message.at}`;
    return keyed(element, key, [message.text, message.card, message.quickReplies, message.via, timeOfDay(new Date(message.at))]);
  }

  #userMessage(message) {
    const voice = message.via === 'voice';
    return h('div', { class: 'user-msg' },
      h('p', { class: 'user-msg__text' }, message.text),
      h('span', { class: 'meta' }, icon(voice ? 'mic' : 'keyboard'), `${voice ? 'Голосом' : 'Текстом'} · ${timeOfDay(new Date(message.at))}`));
  }

  #assistantMessage(message) {
    const ctx = {
      ...this.cardContext,
      quickReplies: message.quickReplies,
      send: (text) => this.cardContext.send(text),
      persist: () => this.store.persistCard(message),
    };
    const key = message.id ?? message.at;
    const listen = this.speaker.isSupported && h('button', {
      class: 'listen-button',
      type: 'button',
      dataset: { speakKey: key },
      'aria-pressed': 'false',
      onClick: () => (this.speaker.currentKey === key ? this.speaker.stop() : this.speaker.speak(message.text, key)),
    }, icon('play'), h('span', { class: 'listen-button__label' }, 'Прослушать ответ'),
      h('span', { class: 'listen-button__time' }, durationLabel(this.speaker.estimateSeconds(message.text))));

    return h('div', { class: 'bot-msg' },
      logo(30),
      h('div', { class: 'bot-msg__body' },
        h('div', { class: 'bot-msg__name' }, logo(22), 'Помню'),
        h('p', { class: 'bot-msg__text' }, ...emphasizeQuestion(message.text)),
        message.card && renderCard(message.card, ctx),
        h('div', { class: 'bot-msg__foot' }, listen, h('span', { class: 'meta' }, timeOfDay(new Date(message.at))))));
  }

  #errorMessage(message) {
    const ctx = {
      ...this.cardContext,
      retry: (element) => {
        element?.remove();
        this.send(message.retry, { isRetry: true });
      },
    };
    return h('div', { class: 'bot-msg' },
      logo(30),
      h('div', { class: 'bot-msg__body' }, renderErrorCard(message.kind, ctx, message.details)));
  }

  #syncListenButtons(key) {
    this.feed.querySelectorAll('.listen-button').forEach((button) => {
      const active = button.dataset.speakKey === key;
      button.setAttribute('aria-pressed', String(active));
      button.querySelector('.listen-button__label').textContent = active ? 'Остановить' : 'Прослушать ответ';
      button.firstElementChild.replaceWith(icon(active ? 'stop' : 'play'));
    });
  }

  #setBusy(busy) {
    this.#busy = busy;
    this.onBusyChange?.(busy);
  }

  #scrollToEnd() {
    requestAnimationFrame(() => {
      const scroller = this.feed.closest('.main');
      scroller?.scrollTo({ top: scroller.scrollHeight, behavior: 'smooth' });
    });
  }
}

function emphasizeQuestion(text) {
  const match = /^(.*?[.!])\s+([^.!?]*\?)$/su.exec(text);
  return match ? [match[1], ' ', h('strong', {}, match[2])] : [text];
}
