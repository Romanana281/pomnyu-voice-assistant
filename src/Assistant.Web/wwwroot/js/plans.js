import { api } from './api.js';
import { h, icon, typeTag, highlight, keyed, patchChildren } from './dom.js';
import { clock, endTime, minutesOf, plural, rangeLabel, relativeDay, sectionDayLabel, typeName } from './format.js';

const COUNT_FORMS = {
  meeting: ['встреча', 'встречи', 'встреч'],
  task: ['задача', 'задачи', 'задач'],
  reminder: ['напоминание', 'напоминания', 'напоминаний'],
};

export class Plans {
  #range = 'today';
  #request = 0;
  #fresh = new Set();
  #searchTimer = 0;
  #query = '';
  #today = null;
  #lastAgenda = null;
  #signature = '';
  #cache = new Map();

  constructor({ elements, openEditor, toast, onAgenda }) {
    Object.assign(this, elements, { openEditor, toast, onAgenda });

    this.tabs.forEach((tab) => tab.addEventListener('click', () => this.selectRange(tab.dataset.range)));
    this.search.addEventListener('input', () => {
      clearTimeout(this.#searchTimer);
      this.#searchTimer = setTimeout(() => this.#runSearch(this.search.value.trim()), 300);
    });
    this.search.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && this.search.value) {
        event.stopPropagation();
        this.search.value = '';
        this.#runSearch('');
      }
    });
  }

  get today() {
    return this.#today;
  }

  selectRange(range) {
    if (range !== this.#range) {
      this.list.classList.remove('fade-in');
      void this.list.offsetWidth;
      this.list.classList.add('fade-in');
    }
    this.#range = range;
    this.tabs.forEach((tab) => tab.setAttribute('aria-selected', String(tab.dataset.range === range)));
    if (this.search.value) {
      this.search.value = '';
      this.#query = '';
    }
    this.load();
  }

  showDate(date) {
    if (!this.#today) return this.selectRange('week');
    const diff = (new Date(date) - new Date(this.#today)) / 86_400_000;
    this.selectRange(diff === 0 ? 'today' : diff === 1 ? 'tomorrow' : 'week');
  }

  markFresh(ids) {
    ids.forEach((id) => this.#fresh.add(id));
  }

  setSaving(state) {
    const label = this.footer.querySelector('span');
    this.footer.classList.toggle('is-saving', state === 'saving');
    this.footer.classList.toggle('is-error', state === 'error');
    label.textContent = state === 'saving' ? 'Сохраняю…' : state === 'error' ? 'Не удалось сохранить' : 'Все изменения сохранены';
  }

  async load() {
    if (this.#query) return this.#runSearch(this.#query);
    const ticket = ++this.#request;
    const range = this.#range;
    const cached = this.#cache.get(range);
    if (cached && this.#lastAgenda?.range !== range) {
      this.#lastAgenda = cached;
      this.#signature = '';
      this.#render(cached);
    } else if (!this.list.hasChildNodes()) {
      this.list.append(h('div', { class: 'skeleton' }), h('div', { class: 'skeleton' }), h('div', { class: 'skeleton' }));
    }
    try {
      const agenda = await api.agenda(range);
      this.#cache.set(range, agenda);
      if (ticket !== this.#request) return;
      this.#today = agenda.today;
      const signature = JSON.stringify({ ...agenda, now: agenda.now.slice(0, 5) }) + [...this.#fresh].join();
      const unchanged = signature === this.#signature && this.list.hasChildNodes() && !this.list.querySelector('.skeleton');
      this.#lastAgenda = agenda;
      this.#signature = signature;
      if (!unchanged) this.#render(agenda);
      else this.#fresh.clear();
      this.onAgenda?.(agenda);
    } catch (error) {
      if (ticket === this.#request) this.toast(error.message);
    }
  }

  async toggle(item) {
    this.setSaving('saving');
    try {
      if (item.isDone) await api.reopen(item.id, item.date);
      else await api.complete(item.id, item.date);
      this.setSaving('saved');
      await this.load();
    } catch (error) {
      this.setSaving('error');
      this.toast(error.message);
    }
  }

  async #runSearch(query) {
    this.#query = query;
    this.#signature = '';
    if (!query) {
      this.title.textContent = 'Планы';
      return this.load();
    }
    const ticket = ++this.#request;
    try {
      const hits = await api.search(query);
      if (ticket !== this.#request) return;
      this.title.textContent = 'Поиск';
      this.date.textContent = hits.length === 0 ? 'Ничего не нашлось' : `Нашлось: ${plural(hits.length, 'запись', 'записи', 'записей')}`;
      this.counts.replaceChildren();
      patchChildren(this.list, (hits.length === 0
        ? [this.#emptyState('Ничего не нашлось', 'Попробуйте другие слова — поиск понимает и близкие по смыслу.')]
        : hits.map((hit) => this.#row(hit.item, { showDate: true, highlights: hit.highlights }))));
    } catch (error) {
      if (ticket === this.#request) this.toast(error.message);
    }
  }

  #render(agenda) {
    this.title.textContent = 'Планы';
    this.date.textContent = rangeLabel(agenda.from, agenda.to);

    const all = [...agenda.overdue, ...agenda.days.flatMap((day) => day.items), ...agenda.undated];
    patchChildren(this.counts, Object.entries(COUNT_FORMS)
      .map(([type, forms]) => [type, all.filter((item) => item.type === type).length, forms])
      .filter(([, count]) => count > 0)
      .map(([type, count, forms]) => keyed(h('span', { class: `count tone-${type}` }, plural(count, ...forms)), type, String(count))));

    const nodes = [];
    if (agenda.overdue.length > 0) {
      nodes.push(this.#label('Просрочено', true));
      agenda.overdue.forEach((item) => nodes.push(this.#row(item, { overdue: true })));
    }

    if (agenda.range === 'week') {
      for (const day of agenda.days) {
        nodes.push(this.#label(sectionDayLabel(day.date, agenda.today)));
        day.items.forEach((item) => nodes.push(this.#row(item)));
      }
    } else {
      const items = agenda.days.flatMap((day) => day.items);
      const timed = items.filter((item) => item.time);
      const untimed = items.filter((item) => !item.time);
      if (timed.length > 0) {
        nodes.push(this.#label('По времени'));
        let nowShown = agenda.range !== 'today';
        const now = minutesOf(agenda.now);
        for (const item of timed) {
          if (!nowShown && minutesOf(item.time) > now) {
            nodes.push(this.#nowLine(agenda.now));
            nowShown = true;
          }
          nodes.push(this.#row(item));
        }
        if (!nowShown) nodes.push(this.#nowLine(agenda.now));
      }
      if (untimed.length > 0) {
        nodes.push(this.#label('Без времени'));
        untimed.forEach((item) => nodes.push(this.#row(item)));
      }
    }

    if (agenda.undated.length > 0) {
      nodes.push(this.#label('Без срока'));
      agenda.undated.forEach((item) => nodes.push(this.#row(item)));
    }

    patchChildren(this.list, (nodes.length > 0
      ? nodes
      : [this.#emptyState('Пока пусто', 'Всё, что вы скажете ассистенту, появится здесь — с датой, временем и статусом.')]));
    this.#fresh.clear();
  }

  #label(text, danger = false) {
    return keyed(h('div', { class: `section-label${danger ? ' section-label--danger' : ''}` }, text), `label:${text}`, text);
  }

  #nowLine(now) {
    return keyed(h('div', { class: 'now-line', 'aria-label': `Сейчас ${clock(now)}` },
      h('span', { class: 'now-line__time' }, clock(now)), h('span', { class: 'now-line__dot' }), h('span', { class: 'now-line__bar' })), 'now', clock(now));
  }

  #row(item, { overdue = false, showDate = false, highlights = null } = {}) {
    const fresh = this.#fresh.has(item.id);
    const [t1, t2] = item.type === 'meeting'
      ? [clock(item.time), endTime(item)]
      : item.time ? [clock(item.time), item.type === 'task' ? 'срок' : ''] : ['', ''];
    const note = overdue
      ? h('span', { class: 'plan-row__note plan-row__note--danger' }, `просрочено · ${relativeDay(item.date, this.#today)}`)
      : showDate
        ? h('span', { class: 'plan-row__note' }, item.date ? relativeDay(item.date, this.#today ?? undefined) : 'без срока')
        : item.recurrence && h('span', { class: 'plan-row__note' }, item.recurrence);
    const completable = item.type !== 'meeting';

    const row = h('div', { class: `plan-row${item.isDone ? ' plan-row--done' : ''}${fresh ? ' plan-row--fresh' : ''}`, dataset: { id: item.id } },
      h('div', { class: 'plan-row__time' }, h('div', { class: 'row__t1' }, t1), t2 && h('div', { class: 'row__t2' }, t2)),
      h('button', { class: 'plan-row__open', type: 'button', onClick: () => this.openEditor(item), 'aria-label': `${typeName(item.type)}: ${item.title}. Открыть` },
        h('div', { class: 'plan-row__title' }, highlights ? highlight(item.title, highlights) : item.title),
        h('div', { class: 'plan-row__tags' }, typeTag(item.type), fresh && h('span', { class: 'badge badge--new' }, 'Новое'), note)),
      completable && h('button', {
        class: 'check',
        type: 'button',
        role: 'checkbox',
        'aria-checked': String(item.isDone),
        'aria-label': `${item.isDone ? 'Вернуть в работу' : 'Отметить выполненной'}: ${item.title}`,
        onClick: () => this.toggle(item),
      }, h('span', { class: 'check__box' }, icon('check'))));
    return keyed(row, `item:${item.id}:${item.date ?? ''}`, [item, fresh, overdue, showDate, highlights, this.#today]);
  }

  #emptyState(title, text) {
    return keyed(h('div', { class: 'plans__empty' },
      h('div', { class: 'ghost-rows', 'aria-hidden': 'true' },
        h('div', {}, h('span'), h('span', { style: 'flex: 1 1 auto' })),
        h('div', {}, h('span'), h('span', { style: 'width: 60%' })),
        h('div', {}, h('span'), h('span', { style: 'width: 75%' }))),
      h('div', {}, h('div', { class: 'plans__empty-title' }, title), h('div', { class: 'plans__empty-text' }, text))), `empty:${title}`, text);
  }

  nextUp(now = new Date()) {
    const agenda = this.#lastAgenda;
    if (!agenda || agenda.range !== 'today') return null;
    const minutesNow = now.getHours() * 60 + now.getMinutes();
    const items = agenda.days.flatMap((day) => day.items)
      .filter((item) => item.time && !item.isDone && minutesOf(item.time) >= minutesNow);
    return items[0] ? { item: items[0], inMinutes: minutesOf(items[0].time) - minutesNow } : null;
  }
}
