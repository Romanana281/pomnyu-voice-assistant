import { api } from './api.js';
import { Chat } from './chat.js';
import { ChatStore } from './chats.js';
import { h, icon, smoothly, crossfade, slidingIndicator, keyed, patchChildren } from './dom.js';
import { Editor } from './editor.js';
import { clock, inMinutesLabel, relativeDay, isoDate, timeOfDay } from './format.js';
import { Plans } from './plans.js';
import { Recorder } from './recorder.js';
import { SpeechInput, Speaker } from './speech.js';
import { storage } from './storage.js';

const $ = (id) => document.getElementById(id);
const app = $('app');
const input = $('input');
const composer = $('composer');
const sendButton = $('send');
const micButton = $('mic');
const chipsBar = $('chips');
const hint = $('hint');
const dock = $('dock');
const MOBILE = window.matchMedia('(max-width: 900px)');

const SPEAK_KEY = 'pomnyu.speak';
const THEME_KEY = 'pomnyu.theme';
const DEFAULT_CHIPS = [
  { label: 'Что у меня завтра?', send: 'Что у меня завтра?' },
  { label: 'Что на этой неделе?', send: 'Что запланировано на эту неделю?' },
  { label: 'Добавить встречу', prefill: 'Добавь встречу ' },
  { label: 'Найти задачу', prefill: 'Найди задачу про ' },
];
const DEFAULT_HINT = 'Говорите как обычно: «в четверг в 11 встреча с дизайнером» или «через два часа напомни выпить воды»';
const EMPTY_HINT = 'При первой записи браузер попросит доступ к микрофону. Можно и печатать.';
const NO_SPEECH_HINT = 'Этот браузер не распознаёт речь — пишите текстом или откройте в Chrome или Safari.';
const ANSWER_HINT = 'Ассистент помнит, о чём речь: можно отвечать коротко — «в четыре», «давай позже»';

let toastTimer = 0;
function toast(message, action) {
  const element = $('toast');
  const children = [h('span', {}, message)];
  if (action) children.push(h('button', {
    type: 'button',
    onClick: async () => {
      element.hidden = true;
      try {
        await action.run();
      } catch (error) {
        toast(error.message);
      }
    },
  }, action.label));
  element.replaceChildren(...children);
  element.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { element.hidden = true; }, action ? 6000 : 3200);
}

function applyTheme(choice) {
  if (choice === 'light' || choice === 'dark') document.documentElement.dataset.theme = choice;
  else delete document.documentElement.dataset.theme;
  const dark = effectiveTheme() === 'dark';
  $('theme-button').setAttribute('aria-label', dark ? 'Включить светлую тему' : 'Включить тёмную тему');
  document.querySelector('meta[name="theme-color"]').setAttribute('content', dark ? '#171A21' : '#FFFFFF');
  document.querySelectorAll('[data-theme-option]').forEach((button) =>
    button.setAttribute('aria-checked', String(button.dataset.themeOption === (choice ?? 'system'))));
}

function effectiveTheme() {
  const forced = document.documentElement.dataset.theme;
  if (forced) return forced;
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function setTheme(choice) {
  storage.set(THEME_KEY, choice);
  const root = document.documentElement;
  root.classList.add('theme-switch');
  crossfade(() => applyTheme(choice)).finally(() => root.classList.remove('theme-switch'));
}

applyTheme(storage.get(THEME_KEY, 'system'));
$('theme-button').addEventListener('click', () => setTheme(effectiveTheme() === 'dark' ? 'light' : 'dark'));
document.querySelectorAll('[data-theme-option]').forEach((button) =>
  button.addEventListener('click', () => setTheme(button.dataset.themeOption)));
window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => applyTheme(storage.get(THEME_KEY, 'system')));

const speaker = new Speaker();
let speakEnabled = storage.get(SPEAK_KEY, true) !== false;
function setSpeak(enabled) {
  speakEnabled = enabled;
  storage.set(SPEAK_KEY, enabled);
  $('speak-toggle').setAttribute('aria-pressed', String(enabled));
  $('speak-setting').checked = enabled;
  if (!enabled) speaker.stop();
}
setSpeak(speakEnabled);
$('speak-toggle').addEventListener('click', () => setSpeak(!speakEnabled));
$('speak-setting').addEventListener('change', (event) => setSpeak(event.target.checked));
if (!speaker.isSupported) {
  $('speak-toggle').hidden = true;
  $('speak-setting').closest('.setting').hidden = true;
}

const editor = new Editor({
  sheet: $('editor-sheet'),
  form: $('editor-form'),
  typeGroup: $('editor-type'),
  error: $('editor-error'),
  save: $('editor-save'),
  toggle: $('editor-toggle'),
  remove: $('editor-delete'),
  durationField: $('duration-field'),
  toast,
  changed: (ids) => refreshPlans(ids),
});

const plans = new Plans({
  elements: {
    search: $('plans-search'),
    title: $('plans-title'),
    date: $('plans-date'),
    counts: $('plans-counts'),
    list: $('plans-list'),
    footer: $('plans-footer'),
    tabs: [...document.querySelectorAll('.tabs__tab')],
  },
  openEditor: (item) => editor.openItem(item),
  toast,
  onAgenda: () => updateNextUp(),
});

function refreshPlans(ids = []) {
  plans.markFresh(ids);
  plans.load();
}

$('add-button').addEventListener('click', () => editor.openNew());

const cardContext = {
  send: (text) => submit(text),
  openEditor: (item, onSaved) => editor.openItem(item, onSaved),
  focusInput: (placeholder) => {
    input.placeholder = placeholder;
    input.focus();
  },
  listen: () => startListening(),
  showPlans: (date) => {
    plans.showDate(date);
    openPlans();
  },
  toast,
  changed: (ids) => refreshPlans(ids),
};

const store = new ChatStore();
store.init();
const chat = new Chat({
  feed: $('feed'),
  store,
  speaker,
  cardContext,
  onBusyChange: (busy) => {
    sendButton.disabled = busy;
    syncMic();
  },
  onHistoryChange: () => {
    updateDock();
    renderChatList();
  },
  onReply: (message) => {
    if (!message) return;
    if (speakEnabled) speaker.speak(message.text, message.id ?? message.at);
    const ids = (message.cards ?? (message.card ? [message.card] : [])).flatMap(cardItems).map((item) => item.id);
    refreshPlans(ids);
  },
});

function cardItems(card) {
  if (card.item) return [card.item];
  if (card.days) return card.days.flatMap((day) => day.items);
  return [];
}

function updateDock() {
  const empty = chat.isEmpty;
  app.classList.toggle('is-empty', empty);
  $('empty').hidden = !empty;
  $('feed').hidden = empty;

  const last = chat.lastReply;
  const awaiting = last && (last.kind === 'question' || last.kind === 'confirmation');
  const quick = awaiting && last.quickReplies?.length > 0 ? last.quickReplies : null;

  chipsBar.replaceChildren(...(empty ? [] : quick
    ? quick.map((label, index) => h('button', {
        class: `chip${index === 0 && last.kind === 'confirmation' ? ' chip--accent' : ''}`,
        type: 'button',
        onClick: () => submit(label),
      }, label))
    : DEFAULT_CHIPS.map((chip) => h('button', {
        class: 'chip',
        type: 'button',
        onClick: () => (chip.send ? submit(chip.send) : prefill(chip.prefill)),
      }, chip.label))));

  input.placeholder = MOBILE.matches
    ? (awaiting ? 'Ответить…' : 'Написать…')
    : empty
      ? 'Или напишите, например: «в пятницу отправить отчёт»'
      : awaiting ? 'Ответьте голосом или напишите…' : 'Скажите или напишите, что запланировать…';
  hint.replaceChildren(...(empty
    ? [icon('info'), SpeechInput.isSupported ? EMPTY_HINT : NO_SPEECH_HINT]
    : [awaiting ? ANSWER_HINT : DEFAULT_HINT]));
  hint.classList.toggle('dock__hint--note', empty);
}

function prefill(text) {
  input.value = text;
  syncComposer();
  input.focus();
  input.setSelectionRange(text.length, text.length);
}

function syncComposer() {
  const hasText = input.value.trim().length > 0;
  composer.classList.toggle('has-text', hasText);
  sendButton.classList.toggle('is-ready', hasText);
}

async function submit(text, via = 'text') {
  const clean = text?.trim();
  if (!clean || chat.isBusy) return null;
  input.value = '';
  syncComposer();
  const reply = await chat.send(clean, { via });
  updateDock();
  return reply;
}

input.addEventListener('input', syncComposer);
composer.addEventListener('submit', (event) => {
  event.preventDefault();
  submit(input.value);
});
document.querySelectorAll('[data-example]').forEach((button) =>
  button.addEventListener('click', () => submit(button.dataset.example)));

const recorder = new Recorder({
  root: $('recorder'),
  transcript: $('transcript'),
  timer: $('recorder-timer'),
  steps: $('steps'),
  bars: $('bars'),
  orb: $('orb-inner'),
  done: $('recorder-done'),
  label: $('recorder-label'),
});

function closeRecorder() {
  smoothly(() => {
    recorder.close();
    app.classList.remove('is-recording');
    dock.hidden = false;
  }, dock);
  syncMic();
}

const speech = new SpeechInput({
  onInterim: (final, interim) => recorder.setTranscript(final, interim),
  onLevel: (value) => recorder.level(value),
  onFinal: async (text) => {
    recorder.setTranscript(text, '');
    recorder.processing();
    const pending = submit(text, 'voice');
    const reply = await pending;
    if (reply) {
      recorder.executing();
      setTimeout(closeRecorder, 350);
    } else {
      closeRecorder();
    }
  },
  onError: (kind) => {
    closeRecorder();
    chat.showError(kind);
  },
  onEnd: ({ cancelled }) => {
    if (cancelled) closeRecorder();
  },
});

function startListening() {
  if (!SpeechInput.isSupported) {
    toast('Этот браузер не распознаёт речь. Попробуйте Chrome или Safari — или пишите текстом.');
    input.focus();
    return;
  }
  if (chat.isBusy || speech.isListening) return;
  speaker.stop();
  closePlans();
  document.querySelectorAll('dialog[open]').forEach((dialog) => dialog.close());
  smoothly(() => {
    recorder.open();
    app.classList.add('is-recording');
    dock.hidden = true;
  }, $('recorder'));
  if (!speech.start()) closeRecorder();
  syncMic();
}

function syncMic() {
  const state = chat.isBusy ? 'busy' : speaker.isSpeaking ? 'speaking' : 'idle';
  micButton.dataset.state = state;
  micButton.disabled = state === 'busy';
  micButton.setAttribute('aria-label', {
    idle: 'Начать запись голоса',
    busy: 'Понимаю запрос',
    speaking: 'Прервать ответ и говорить',
  }[state]);
}

speaker.onChange(() => syncMic());
document.addEventListener('pointerdown', () => speaker.unlock(), { once: true, capture: true });
micButton.addEventListener('click', () => {
  if (speaker.isSpeaking) speaker.stop();
  startListening();
});
$('hero-mic').addEventListener('click', startListening);
$('recorder-done').addEventListener('click', () => speech.stop());
$('recorder-cancel').addEventListener('click', () => speech.cancel());
$('recorder-cancel-mobile').addEventListener('click', () => speech.cancel());

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape') {
    if (speech.isListening) {
      event.preventDefault();
      speech.cancel();
    } else if (app.classList.contains('is-plans-open') && !document.querySelector('dialog[open]')) {
      closePlans();
    } else if (app.classList.contains('is-sidebar-open')) {
      setSidebar(false);
    }
    return;
  }
  const target = event.target;
  const typing = target instanceof HTMLElement && (target.matches('input, textarea, select, [contenteditable]') || target.closest('dialog'));
  if (event.code === 'Space' && !typing && !event.repeat && !document.querySelector('dialog[open]')) {
    event.preventDefault();
    if (speech.isListening) speech.stop();
    else if (speaker.isSpeaking) { speaker.stop(); startListening(); }
    else startListening();
  }
});

function openPlans() {
  if (!MOBILE.matches) return;
  app.classList.add('is-plans-open');
  plans.load();
}

function closePlans() {
  app.classList.remove('is-plans-open');
}

$('plans-button').addEventListener('click', openPlans);
$('plans-close').addEventListener('click', closePlans);

function updateNextUp() {
  const element = $('next-up');
  const next = plans.nextUp();
  if (!next || chat.isEmpty) {
    element.hidden = true;
    return;
  }
  element.replaceChildren(h('button', { type: 'button', onClick: openPlans },
    h('span', { class: `tile tone-${next.item.type}` }, icon(next.item.type)),
    h('span', { class: 'next-up__text' },
      h('span', { class: 'next-up__label' }, `Далее · ${inMinutesLabel(next.inMinutes)}`),
      h('span', { class: 'next-up__title' }, `${next.item.title}, ${clock(next.item.time)}`)),
    icon('chevron')));
  element.hidden = false;
}
setInterval(updateNextUp, 60_000);

const SYNC_INTERVAL_MS = 15_000;
let syncing = false;

async function syncFromServer() {
  if (syncing || document.visibilityState !== 'visible' || chat.isBusy || speech.isListening) return;
  syncing = true;
  try {
    plans.load();
    const { activeChanged } = await store.refresh();
    if (activeChanged && !chat.isBusy) {
      chat.show({ reconcile: true });
      updateNextUp();
    } else {
      renderChatList();
    }
  } finally {
    syncing = false;
  }
}

store.onActiveUpdated = () => {
  if (chat.isBusy || speech.isListening) return;
  chat.show({ reconcile: true });
  renderChatList();
};

store.sync().then(({ activeChanged }) => {
  if (activeChanged && !chat.isBusy) chat.show({ reconcile: true });
  renderChatList();
});

setInterval(syncFromServer, SYNC_INTERVAL_MS);
document.addEventListener('visibilitychange', syncFromServer);
window.addEventListener('focus', syncFromServer);

for (const dialog of document.querySelectorAll('dialog')) {
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog || event.target.closest('[data-close]')) dialog.close();
  });
}

$('settings-button').addEventListener('click', () => $('settings-sheet').showModal());
$('profile-button').addEventListener('click', () => {
  $('link-panel').hidden = true;
  $('calendar-panel').hidden = true;
  $('import-panel').hidden = true;
  $('profile-sheet').showModal();
});

$('clear-chat').addEventListener('click', async () => {
  try {
    await chat.clear();
  } catch (error) {
    toast(error.message);
    return;
  }
  updateNextUp();
  $('settings-sheet').close();
  toast('История этого чата очищена');
});

function switchChat(chatToShow) {
  speaker.stop();
  if (speech.isListening) speech.cancel();
  smoothly(() => {
    renderChatList();
    chat.show();
    updateDock();
  }, $('feed'));
  updateNextUp();
  input.value = '';
  syncComposer();
  return chatToShow;
}

function goHome() {
  document.querySelectorAll('dialog[open]').forEach((dialog) => dialog.close());
  closePlans();
  switchChat(store.create());
  $('main').scrollTop = 0;
}

$('brand').addEventListener('click', (event) => {
  if (event.metaKey || event.ctrlKey || event.shiftKey || event.button !== 0) return;
  event.preventDefault();
  goHome();
});

function renderChatList() {
  const list = $('chat-list');
  const today = isoDate(new Date());
  const yesterday = isoDate(new Date(Date.now() - 86_400_000));
  const groups = new Map();
  for (const item of store.all) {
    const day = isoDate(new Date(item.updatedAt));
    const label = day === today ? 'Сегодня' : day === yesterday ? 'Вчера' : 'Ранее';
    if (!groups.has(label)) groups.set(label, []);
    groups.get(label).push(item);
  }

  patchChildren(list, [...groups].flatMap(([label, chats]) => [
    keyed(h('li', { class: 'chat-group' }, label), `group:${label}`, label),
    ...chats.map((item) => {
      const updated = new Date(item.updatedAt);
      const day = isoDate(updated);
      const userMessages = item.messageCount ?? 0;
      const last = item.lastText;
      const active = item.id === store.active.id;
      return keyed(h('li', { class: `chat-row${active ? ' chat-row--active' : ''}` },
        h('button', {
          class: 'chat-row__open',
          type: 'button',
          'aria-current': active ? 'true' : undefined,
          onClick: () => {
            closeSidebarOnMobile();
            switchChat(store.select(item.id));
          },
        },
          h('span', { class: 'chat-row__icon' }, icon('chat')),
          h('span', { class: 'chat-row__text' },
            h('span', { class: 'chat-row__title' }, userMessages === 0 ? 'Новый чат' : item.title),
            h('span', { class: 'chat-row__meta' }, userMessages === 0 ? 'Пока без сообщений' : last ?? `Сообщений: ${userMessages}`)),
          h('span', { class: 'chat-row__time' }, day === today ? timeOfDay(updated) : relativeDay(day, today))),
        h('button', {
          class: 'chat-row__delete',
          type: 'button',
          'aria-label': `Удалить чат «${item.title}»`,
          onClick: async () => {
            try {
              await store.remove(item.id);
            } catch (error) {
              toast(error.message);
              return;
            }
            if (active) switchChat(store.active);
            renderChatList();
            toast('Чат удалён');
          },
        }, icon('trash'))), `chat:${item.id}`, [item.title, item.lastText, item.messageCount, active, day === today ? timeOfDay(updated) : day]);
    }),
  ]));
}

const SIDEBAR_KEY = 'pomnyu.sidebar';
const WIDE = window.matchMedia('(min-width: 1200px)');

function setSidebar(open, { remember = true } = {}) {
  if (MOBILE.matches) {
    app.classList.remove('is-sidebar-collapsed');
    app.classList.toggle('is-sidebar-open', open);
    $('sidebar-backdrop').hidden = !open;
  } else {
    app.classList.remove('is-sidebar-open');
    $('sidebar-backdrop').hidden = true;
    app.classList.toggle('is-sidebar-collapsed', !open);
    if (remember) storage.set(SIDEBAR_KEY, open);
  }
  $('chats-button').setAttribute('aria-expanded', String(open));
  $('chats-button').setAttribute('aria-label', open ? 'Скрыть список чатов' : 'Показать список чатов');
  if (open) renderChatList();
}

function isSidebarOpen() {
  return MOBILE.matches ? app.classList.contains('is-sidebar-open') : !app.classList.contains('is-sidebar-collapsed');
}

function closeSidebarOnMobile() {
  if (MOBILE.matches) setSidebar(false);
}

$('chats-button').addEventListener('click', () => setSidebar(!isSidebarOpen()));
$('sidebar-close').addEventListener('click', () => setSidebar(false));
$('sidebar-backdrop').addEventListener('click', () => setSidebar(false));
$('new-chat').addEventListener('click', () => {
  closeSidebarOnMobile();
  goHome();
});
MOBILE.addEventListener('change', () => {
  app.classList.remove('is-sidebar-open');
  setSidebar(!MOBILE.matches && storage.get(SIDEBAR_KEY, WIDE.matches), { remember: false });
});
setSidebar(!MOBILE.matches && storage.get(SIDEBAR_KEY, WIDE.matches), { remember: false });

$('open-link').addEventListener('click', async () => {
  try {
    const link = await api.createLink();
    $('link-url').value = link.url;
    const svg = new DOMParser().parseFromString(link.qrSvg, 'image/svg+xml').documentElement;
    $('link-qr').replaceChildren(svg.nodeName === 'svg' ? document.importNode(svg, true) : '');
    $('link-panel').hidden = false;
  } catch (error) {
    toast(error.message);
  }
});

async function renderSubscriptions() {
  const list = $('subscription-list');
  try {
    const subscriptions = await api.subscriptions();
    list.replaceChildren(...subscriptions.map((subscription) => h('li', { class: 'subscription' },
      h('div', { class: 'subscription__body' },
        h('div', { class: 'subscription__name' }, subscription.name),
        h('div', { class: `subscription__meta${subscription.lastError ? ' subscription__meta--error' : ''}` },
          subscription.lastError
            ? `Ошибка: ${subscription.lastError}`
            : subscription.lastSyncedAt ? `Обновлено ${relativeDay(isoDate(new Date(subscription.lastSyncedAt)))} в ${timeOfDay(new Date(subscription.lastSyncedAt))}` : 'Ещё не обновлялся')),
      h('button', {
        class: 'text-button',
        type: 'button',
        onClick: async (event) => {
          event.currentTarget.disabled = true;
          try {
            const result = await api.syncSubscription(subscription.id);
            toast(importMessage(result.summary));
            refreshPlans();
          } catch (error) {
            toast(error.message);
          }
          renderSubscriptions();
        },
      }, 'Обновить'),
      h('button', {
        class: 'text-button text-button--danger',
        type: 'button',
        onClick: async () => {
          try {
            await api.unsubscribe(subscription.id);
            toast('Календарь отключён, его события убраны');
            refreshPlans();
          } catch (error) {
            toast(error.message);
          }
          renderSubscriptions();
        },
      }, 'Отключить'))));
  } catch (error) {
    toast(error.message);
  }
}

function importMessage(summary) {
  if (summary.total === 0) return 'В календаре нет событий для импорта';
  const parts = [`добавлено ${summary.created}`];
  if (summary.updated) parts.push(`обновлено ${summary.updated}`);
  if (summary.removed) parts.push(`убрано ${summary.removed}`);
  return `Готово: ${parts.join(', ')}`;
}

$('open-import').addEventListener('click', () => {
  $('import-panel').hidden = !$('import-panel').hidden;
  if (!$('import-panel').hidden) renderSubscriptions();
});

$('import-url-form').addEventListener('submit', async (event) => {
  event.preventDefault();
  const field = $('import-url');
  const error = $('import-url-form').querySelector('[data-error="url"]');
  const submit = $('import-url-submit');
  error.textContent = '';
  if (!field.value.trim()) {
    error.textContent = 'Вставьте ссылку на календарь.';
    field.focus();
    return;
  }
  submit.disabled = true;
  submit.textContent = 'Подключаю…';
  try {
    const result = await api.subscribe(field.value.trim());
    field.value = '';
    toast(importMessage(result.summary));
    refreshPlans();
    renderSubscriptions();
  } catch (failure) {
    error.textContent = failure.message;
  } finally {
    submit.disabled = false;
    submit.textContent = 'Подключить и синхронизировать';
  }
});

$('import-file').addEventListener('change', async (event) => {
  const file = event.target.files?.[0];
  event.target.value = '';
  if (!file) return;
  if (file.size > 5 * 1024 * 1024) {
    toast('Файл больше 5 МБ');
    return;
  }
  try {
    const result = await api.importFile(file);
    toast(importMessage(result.summary));
    refreshPlans();
  } catch (error) {
    toast(error.message);
  }
});

$('open-calendar').addEventListener('click', async () => {
  try {
    const account = await api.account();
    $('calendar-url').value = account.calendarUrl;
    $('calendar-apple').href = account.webcalUrl;
    $('calendar-google').href = `https://calendar.google.com/calendar/r?cid=${encodeURIComponent(account.webcalUrl)}`;
    $('calendar-panel').hidden = false;
  } catch (error) {
    toast(error.message);
  }
});

document.querySelectorAll('[data-copy]').forEach((button) => {
  button.addEventListener('click', async () => {
    const field = $(button.dataset.copy);
    try {
      await navigator.clipboard.writeText(field.value);
      toast('Скопировано');
    } catch {
      field.select();
    }
  });
});

if (MOBILE.matches) {
  $('profile-button').hidden = true;
}
$('settings-sheet').querySelector('.setting--danger').insertAdjacentElement('beforebegin', h('button', {
  class: 'setting setting--link',
  type: 'button',
  onClick: () => {
    $('settings-sheet').close();
    $('profile-button').click();
  },
}, h('span', { class: 'setting__text' }, h('span', { class: 'setting__name' }, 'Профиль и устройства'), h('span', { class: 'setting__hint' }, 'Другое устройство, подписка на календарь')), icon('chevron')));

window.addEventListener('offline', () => toast('Нет интернета — новые запросы не отправятся'));
window.addEventListener('online', () => {
  toast('Связь восстановлена');
  plans.load();
});

const params = new URLSearchParams(location.search);
if (params.has('link')) {
  toast(params.get('link') === 'ok'
    ? 'Устройство подключено — здесь те же задачи и встречи'
    : 'Ссылка устарела или уже использована. Создайте новую на первом устройстве.');
  history.replaceState(null, '', location.pathname);
}

if (!SpeechInput.isSupported) {
  micButton.hidden = true;
  $('hero-mic').disabled = true;
}

updateDock();
syncComposer();
syncMic();
plans.load();

slidingIndicator(document.querySelector('.tabs'), 'aria-selected');
document.querySelectorAll('.segmented').forEach((group) => {
  const place = slidingIndicator(group, 'aria-checked');
  group.closest('dialog')?.addEventListener('toggle', place);
});

const reveal = () => app.classList.remove('is-loading');
Promise.race([document.fonts?.ready ?? Promise.resolve(), new Promise((resolve) => setTimeout(resolve, 800))])
  .then(() => requestAnimationFrame(() => {
    $('feed').lastElementChild?.scrollIntoView({ block: 'end' });
    reveal();
  }));

if ('serviceWorker' in navigator) {
  navigator.serviceWorker.register('/sw.js').catch(() => {});
}
