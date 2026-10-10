import { api } from './api.js';
import { h, icon, typeTag, highlight } from './dom.js';
import {
  clock, compactDate, dayOfWeekShort, daysBetween, endTime, minutesOf, clockFromMinutes,
  plural, relativeDay, shortDate, todayIso, typeName,
} from './format.js';

const SAVED_BADGES = {
  created: ['badge--success', 'check', 'Сохранено'],
  completed: ['badge--success', 'check', 'Выполнено'],
  deleted: ['badge--error', 'trash', 'Удалено'],
  cancelled: ['badge--neutral', 'undo', 'Отменено'],
  reopened: ['badge--neutral', 'undo', 'Вернул в работу'],
};

export function renderCard(card, ctx) {
  switch (card?.type) {
    case 'saved': return savedCard(card, ctx);
    case 'changed': return changedCard(card, ctx);
    case 'draft': return draftCard(card, ctx);
    case 'conflict': return conflictCard(card, ctx);
    case 'agenda': return agendaCard(card, ctx);
    case 'search': return card.layout === 'timeline' ? timelineCard(card, ctx) : searchCard(card, ctx);
    case 'choice': return choiceCard(card, ctx);
    case 'confirmDelete': return confirmDeleteCard(card, ctx);
    default: return null;
  }
}

function itemMeta(item) {
  return [
    item.date && h('span', {}, icon('calendar'), shortDate(item.date)),
    item.time && h('span', {}, icon('clock'), endTime(item) ? `${clock(item.time)}–${endTime(item)}` : clock(item.time)),
    !item.date && h('span', {}, 'Без срока'),
    h('span', {}, typeName(item.type)),
    item.recurrence && h('span', {}, item.recurrence),
  ];
}

function savedCard(card, ctx) {
  const root = h('div', { class: 'card card--padded' });
  const render = (state) => {
    const [tone, badgeIcon, label] = SAVED_BADGES[state];
    const item = card.item;
    const done = state === 'completed' || item.isDone;
    const actions = {
      created: [
        h('button', { class: 'outline-button', type: 'button', onClick: () => ctx.openEditor(item, (updated) => { card.item = updated; render('created'); }) }, icon('edit'), 'Изменить'),
        h('button', { class: 'ghost-button', type: 'button', onClick: () => act(() => api.remove(item.id), 'cancelled', 'Запись отменена') }, icon('undo'), 'Отменить'),
      ],
      completed: [h('button', { class: 'ghost-button', type: 'button', onClick: () => act(() => api.reopen(item.id, item.date), 'reopened', 'Вернул в работу') }, icon('undo'), 'Вернуть')],
      deleted: [h('button', { class: 'ghost-button', type: 'button', onClick: () => act(() => api.restore(item.id), 'created', 'Запись восстановлена') }, icon('undo'), 'Вернуть')],
      cancelled: [h('button', { class: 'ghost-button', type: 'button', onClick: () => act(() => api.restore(item.id), 'created', 'Запись восстановлена') }, icon('undo'), 'Вернуть')],
      reopened: [],
    }[state];

    root.replaceChildren(
      h('div', { class: 'saved' },
        h('span', { class: `tile tone-${item.type}` }, icon(item.type)),
        h('div', { class: 'saved__body' },
          h('div', { class: `saved__title${done || state === 'deleted' || state === 'cancelled' ? ' saved__title--done' : ''}` }, item.title),
          h('div', { class: 'saved__meta' }, itemMeta(item))),
        h('span', { class: `badge ${tone}` }, icon(badgeIcon), label)),
      actions.length > 0 && h('div', { class: 'actions' }, actions));
  };

  const act = async (operation, nextState, message) => {
    try {
      await operation();
      if (nextState === 'reopened') card.item = { ...card.item, isDone: false };
      card.action = nextState;
      ctx.persist?.();
      render(nextState);
      ctx.toast(message);
      ctx.changed([card.item.id]);
    } catch (error) {
      ctx.toast(error.message);
    }
  };

  render(card.action);
  return root;
}

function changedCard(card, ctx) {
  const item = card.item;
  const timeChanged = card.previousTime !== item.time && item.time;
  const dateChanged = card.previousDate !== item.date;
  const sub = item.date ? shortDate(item.date) : 'Без срока';

  const diff = card.previousTitle
    ? [h('span', { class: 'changed__old' }, card.previousTitle), icon('arrow'), h('span', { class: 'changed__new' }, item.title)]
    : dateChanged
      ? [h('span', { class: 'changed__old' }, card.previousDate ? compactDate(card.previousDate) + (card.previousTime ? ` ${clock(card.previousTime)}` : '') : 'без срока'), icon('arrow'), h('span', { class: 'changed__new' }, item.date ? compactDate(item.date) + (item.time ? ` ${clock(item.time)}` : '') : 'без срока')]
      : timeChanged
        ? [h('span', { class: 'changed__old' }, card.previousTime ? clock(card.previousTime) : 'без времени'), icon('arrow'), h('span', { class: 'changed__new' }, clock(item.time))]
        : [];

  const root = h('div', { class: 'card changed' },
    h('span', { class: `tile tile--md tone-${item.type}` }, icon(item.type)),
    h('div', { class: 'changed__body' }, h('div', { class: 'changed__title' }, item.title), h('div', { class: 'changed__sub' }, sub)),
    diff.length > 0 && h('div', { class: 'changed__diff' }, diff));

  const revert = h('button', {
    class: 'ghost-button',
    type: 'button',
    onClick: async () => {
      try {
        await api.update(item.id, {
          title: card.previousTitle ?? item.title,
          date: card.previousDate ?? null,
          time: card.previousTime ?? null,
          durationMinutes: item.durationMinutes ?? null,
        });
        card.reverted = true;
        ctx.persist?.();
        revert.replaceWith(h('span', { class: 'badge badge--neutral' }, icon('undo'), 'Вернул как было'));
        ctx.toast('Вернул как было');
        ctx.changed([item.id]);
      } catch (error) {
        ctx.toast(error.message);
      }
    },
  }, icon('undo'), 'Вернуть');
  root.append(card.reverted ? h('span', { class: 'badge badge--neutral' }, icon('undo'), 'Вернул как было') : revert);
  return root;
}

function draftCard(card, ctx) {
  const missingTime = card.missing === 'time';
  const kindLabel = card.itemType === 'meeting' ? 'Черновик встречи' : 'Черновик напоминания';
  const whenValue = card.date ? shortDate(card.date) : null;

  const field = (label, value, missing) => missing
    ? h('div', { class: 'slot slot--missing' }, h('span', { class: 'slot__label' }, label), h('span', { class: 'slot__value' }, icon('clock'), 'Нужно уточнить'))
    : h('div', { class: 'slot' }, h('span', { class: 'slot__label' }, label), h('span', { class: 'slot__value' }, value));

  const slotsLabel = card.date
    ? `Свободно ${relativeDay(card.date) === 'сегодня' || relativeDay(card.date) === 'завтра' ? relativeDay(card.date) : shortDate(card.date)}:`
    : null;

  return h('div', { class: 'draft' },
    h('div', { class: 'draft__head' }, h('span', { class: 'draft__label' }, kindLabel), h('span', { class: 'draft__note' }, 'ещё не сохранено')),
    h('div', { class: 'draft__fields' },
      field('Что', card.title, false),
      field('Когда', whenValue, !card.date),
      field('Время', card.time ? clock(card.time) : null, missingTime || !card.date)),
    missingTime && h('div', { class: 'draft__slots' },
      card.freeSlots.length > 0
        ? [h('span', { class: 'draft__slots-label' }, slotsLabel), card.freeSlots.map((slot) =>
            h('button', { class: 'time-button', type: 'button', onClick: () => ctx.send(`В ${clock(slot)}`) }, clock(slot)))]
        : h('span', { class: 'draft__slots-label' }, 'Свободных окон в этот день почти нет.'),
      h('button', { class: 'text-button', type: 'button', onClick: () => ctx.focusInput('Например: в 16:30') }, 'Другое время')));
}

function conflictCard(card, ctx) {
  const start = minutesOf(card.start);
  const end = start + card.durationMinutes;
  const blocks = card.conflicts.map((item) => ({
    item,
    start: minutesOf(item.time),
    end: minutesOf(item.time) + (item.durationMinutes ?? 60),
  }));
  const suggested = card.suggested ? minutesOf(card.suggested) : null;

  const points = [start, end, ...blocks.flatMap((block) => [block.start, block.end]), ...(suggested !== null ? [suggested + card.durationMinutes] : [])];
  let from = Math.floor(Math.min(...points) / 60) * 60;
  let to = Math.ceil(Math.max(...points) / 60) * 60;
  if (to - from < 180) to = from + 180;
  if (to > 1440) { from = Math.max(0, 1440 - (to - from)); to = 1440; }
  const span = to - from;
  const pos = (minutes) => `${((Math.min(Math.max(minutes, from), to) - from) / span) * 100}%`;
  const width = (a, b) => `${((Math.min(b, to) - Math.max(a, from)) / span) * 100}%`;
  const hours = [];
  for (let minute = from; minute <= to; minute += 60) hours.push(minute);

  const grid = () => hours.slice(1, -1).map((minute) => h('span', { class: 'timeline__grid', style: `left: ${pos(minute)}` }));
  const replies = ctx.quickReplies ?? [];

  return h('div', { class: 'card card--padded' },
    h('div', { class: 'conflict__head' }, icon('warning'), `Пересечение по времени · ${shortDate(card.date)}`),
    h('div', { class: 'timeline', role: 'img', 'aria-label': `${card.title} в ${clock(card.start)} пересекается с ${card.conflicts.map((item) => item.title).join(', ')}` },
      h('div', { class: 'timeline__axis', 'aria-hidden': 'true' }, hours.map((minute) => h('span', { style: `left: ${pos(minute)}` }, clockFromMinutes(minute)))),
      blocks.map((block) => h('div', { class: 'timeline__row', 'aria-hidden': 'true' },
        h('span', { class: 'timeline__label' }, block.item.title),
        h('div', { class: 'timeline__track' }, grid(),
          h('div', { class: 'timeline__block timeline__block--busy', style: `left: ${pos(block.start)}; width: ${width(block.start, block.end)}` },
            `${clockFromMinutes(block.start)}–${clockFromMinutes(block.end)}`)))),
      h('div', { class: 'timeline__row', 'aria-hidden': 'true' },
        h('span', { class: 'timeline__label' }, card.title),
        h('div', { class: 'timeline__track' }, grid(),
          h('div', { class: 'timeline__block timeline__block--clash', style: `left: ${pos(start)}; width: ${width(start, end)}` }, `${clock(card.start)} · пересечение`),
          suggested !== null && h('div', { class: 'timeline__block timeline__block--free', style: `left: ${pos(suggested)}; width: ${width(suggested, suggested + card.durationMinutes)}` }, `${clock(card.suggested)} · свободно`)))),
    h('div', { class: 'actions' },
      card.suggested
        ? [
            h('button', { class: 'primary-button', type: 'button', onClick: () => ctx.send(replies[0]) }, icon('check'), `Поставить на ${clock(card.suggested)}`),
            h('button', { class: 'outline-button', type: 'button', onClick: () => ctx.send(replies[1]) }, `Оставить на ${clock(card.start)}`),
          ]
        : h('button', { class: 'primary-button', type: 'button', onClick: () => ctx.send(replies[0]) }, icon('check'), `Поставить на ${clock(card.start)}`),
      h('button', { class: 'ghost-button', type: 'button', onClick: () => ctx.focusInput('Например: давай в 17:30') }, 'Другое время')));
}

function agendaCard(card, ctx) {
  return card.days.length === 1 && card.from === card.to ? dayTable(card.days[0].items, ctx) : weekList(card.days, ctx);
}

function timeCell(item) {
  if (!item.time) return h('div', { class: 'row__time' });
  if (item.type === 'meeting') {
    return h('div', { class: 'row__time' }, h('div', { class: 'row__t1' }, clock(item.time)), endTime(item) && h('div', { class: 'row__t2' }, endTime(item)));
  }
  if (item.type === 'task') {
    return h('div', { class: 'row__time' }, h('div', { class: 'row__t2' }, 'до'), h('div', { class: 'row__t1' }, clock(item.time)));
  }
  return h('div', { class: 'row__time' }, h('div', { class: 'row__t1' }, clock(item.time)));
}

function dayTable(items, ctx) {
  const timed = items.filter((item) => item.time);
  const untimed = items.filter((item) => !item.time);
  const row = (item) => h('button', { class: 'row row--button', type: 'button', onClick: () => ctx.openEditor(item) },
    timeCell(item),
    h('div', { class: `row__title${item.isDone ? ' row__title--done' : ''}` }, item.title),
    item.isDone && h('span', { class: 'badge badge--success' }, icon('check'), 'Выполнено'),
    typeTag(item.type));

  const root = h('div', { class: 'card rows' }, (timed.length > 0 ? timed : untimed).map(row));
  if (timed.length > 0 && untimed.length > 0) {
    const foot = h('div', { class: 'rows__foot' },
      h('span', {}, `Ещё ${plural(untimed.length, 'запись', 'записи', 'записей')} без времени`),
      h('button', {
        type: 'button',
        onClick: () => foot.replaceWith(...untimed.map(row)),
      }, 'Показать все', icon('chevron')));
    root.append(foot);
  }
  return root;
}

function weekList(days, ctx) {
  const today = todayIso();
  return h('div', { class: 'card week' }, days.map((day) => {
    const shown = day.items.slice(0, 2);
    const rest = day.items.length - shown.length;
    return h('div', { class: `week__day${day.date === today ? ' week__day--today' : ''}` },
      h('span', { class: 'week__date' }, h('span', { class: 'week__dow' }, dayOfWeekShort(day.date)), h('span', { class: 'week__num' }, String(Number(day.date.slice(8))))),
      h('span', { class: 'week__lines' },
        shown.map((item) => h('span', { class: `week__line${item.isDone ? ' week__line--done' : ''}` },
          item.isDone && icon('check'), item.time && h('b', {}, clock(item.time)), item.time ? ' ' : '', item.title)),
        rest > 0 && h('button', { class: 'text-button', type: 'button', style: 'height: auto; padding: 0; font-size: 12px; font-weight: 500; color: var(--muted); text-align: left;', onClick: () => ctx.showPlans(day.date) },
          `и ещё ${plural(rest, 'запись', 'записи', 'записей')}`)));
  }));
}

function hitMeta(item) {
  const parts = [typeName(item.type)];
  if (item.date) parts.push(shortDate(item.date));
  else parts.push('без срока');
  if (item.time) parts.push(clock(item.time));
  if (item.isDone) parts.push('выполнено');
  else if (item.date && daysBetween(todayIso(), item.date) < 0) parts.push(item.type === 'meeting' ? 'прошла' : 'просрочено');
  return parts.join(' · ');
}

function searchCard(card, ctx) {
  return h('div', { class: 'card hits' },
    card.hits.map((hit, index) => h('button', {
      class: `hit${index === 0 && hit.isExact ? ' hit--best' : ''}`,
      type: 'button',
      'aria-pressed': String(index === 0 && hit.isExact),
      onClick: () => ctx.openEditor(hit.item),
    },
      h('span', { class: `tile tile--md tone-${hit.item.type}` }, icon(hit.item.type)),
      h('span', { class: 'hit__body' },
        h('span', { class: 'hit__title' }, highlight(hit.item.title, hit.highlights)),
        h('span', { class: 'hit__meta' }, hitMeta(hit.item))),
      h('span', { class: 'hit__score' },
        h('span', { class: `score-bars${hit.isExact ? '' : ' score-bars--partial'}`, 'aria-hidden': 'true' }, h('span'), h('span'), h('span')),
        hit.isExact ? 'Точное совпадение' : 'Близко по смыслу'))),
    h('p', { class: 'hits__hint' }, 'Не то? Уточните, примерно когда ставили задачу или с кем она связана.'));
}

function timelineCard(card, ctx) {
  const today = todayIso();
  return h('div', { class: 'card rows' }, card.hits.map(({ item }) => {
    const diff = item.date ? daysBetween(today, item.date) : null;
    const past = diff !== null && diff < 0;
    return h('button', { class: 'row row--button', type: 'button', onClick: () => ctx.openEditor(item) },
      h('div', { class: 'row__time row__time--wide' },
        h('div', { class: 'row__t1', style: past ? 'color: var(--text-2)' : '' }, item.date ? compactDate(item.date) : '—'),
        item.time && h('div', { class: 'row__t2' }, clock(item.time))),
      h('div', { class: `row__title${past ? ' row__title--past' : ''}` }, item.title),
      past
        ? h('span', { class: 'row__status' }, item.isDone ? 'выполнено' : 'прошла')
        : h('span', { class: `tag tone-${item.type}` }, relativeDay(item.date, today)));
  }));
}

function choiceCard(card, ctx) {
  const replies = ctx.quickReplies ?? [];
  return h('div', { class: 'card rows' }, card.options.map((item, index) =>
    h('button', { class: 'row row--button', type: 'button', onClick: () => ctx.send(replies[index] ?? String(index + 1)) },
      h('span', { class: 'row__number' }, String(index + 1)),
      h('div', { class: 'row__title' }, item.title, h('div', { class: 'row__t2' }, hitMeta(item))),
      typeTag(item.type))));
}

function confirmDeleteCard(card, ctx) {
  const item = card.item;
  const replies = ctx.quickReplies ?? [];
  return h('div', { class: 'card card--padded' },
    h('div', { class: 'saved' },
      h('span', { class: `tile tone-${item.type}` }, icon(item.type)),
      h('div', { class: 'saved__body' }, h('div', { class: 'saved__title' }, item.title), h('div', { class: 'saved__meta' }, itemMeta(item)))),
    h('div', { class: 'actions' },
      h('button', { class: 'primary-button', type: 'button', style: 'background: var(--error-fg)', onClick: () => ctx.send(replies[0]) }, icon('trash'), 'Удалить'),
      h('button', { class: 'outline-button', type: 'button', onClick: () => ctx.send(replies[1]) }, 'Оставить')));
}

const ERRORS = {
  'no-speech': ['mic', 'Не расслышал', 'Было тихо или шумно. Повторите или напишите текстом.', 'Сказать ещё раз', 'listen'],
  denied: ['micOff', 'Нет доступа к микрофону', 'Разрешите его в настройках браузера — или пишите текстом.', 'Как включить', 'help'],
  'no-mic': ['micOff', 'Микрофон не найден', 'Подключите микрофон или пишите текстом.', null, null],
  network: ['wifiOff', 'Нет связи с распознаванием', 'Для распознавания речи нужен интернет. Можно писать текстом.', 'Сказать ещё раз', 'listen'],
  unknown: ['mic', 'Не удалось распознать речь', 'Попробуйте ещё раз или напишите текстом.', 'Сказать ещё раз', 'listen'],
  failed: ['wifiOff', 'Не удалось выполнить', null, 'Повторить', 'retry'],
};

export function renderErrorCard(kind, ctx, details) {
  const [iconName, title, text, actionLabel, action] = ERRORS[kind] ?? ERRORS.unknown;
  const message = details ?? text;
  const help = h('p', { class: 'error-card__text', hidden: true },
    'Нажмите на значок замка или камеры в адресной строке → «Микрофон» → «Разрешить», затем обновите страницу. На iPhone: Настройки → Safari → Микрофон.');
  return h('div', { class: 'error-card' },
    h('span', { class: 'tile tile--md' }, icon(iconName)),
    h('div', { class: 'error-card__body' },
      h('div', { class: 'error-card__title' }, title),
      h('div', { class: 'error-card__text' }, message),
      help,
      actionLabel && h('div', { class: 'actions' }, h('button', {
        class: 'outline-button',
        type: 'button',
        onClick: (event) => {
          if (action === 'help') { help.hidden = false; event.currentTarget.remove(); }
          else if (action === 'listen') ctx.listen();
          else if (action === 'retry') ctx.retry?.(event.currentTarget.closest('.bot-msg'));
        },
      }, actionLabel))));
}
