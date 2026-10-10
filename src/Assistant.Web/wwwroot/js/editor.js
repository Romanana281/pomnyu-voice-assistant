import { api } from './api.js';

const TITLES = { create: 'Новая запись', edit: 'Изменить запись' };

export class Editor {
  #item = null;
  #type = 'task';
  #onSaved = null;

  constructor({ sheet, form, typeGroup, error, save, toggle, remove, durationField, toast, changed }) {
    Object.assign(this, { sheet, form, typeGroup, error, save, toggle, remove, durationField, toast, changed });

    typeGroup.querySelectorAll('[data-type]').forEach((button) =>
      button.addEventListener('click', () => this.#setType(button.dataset.type)));
    form.addEventListener('submit', (event) => {
      event.preventDefault();
      this.#submit();
    });
    toggle.addEventListener('click', () => this.#toggleDone());
    remove.addEventListener('click', () => this.#remove());
    sheet.addEventListener('close', () => { this.#onSaved = null; });
  }

  openNew(defaults = {}) {
    this.#item = null;
    this.#prepare('create');
    this.#setType(defaults.type ?? 'task');
    this.form.elements.title.value = '';
    this.form.elements.date.value = defaults.date ?? '';
    this.form.elements.time.value = '';
    this.form.elements.durationMinutes.value = '60';
    this.sheet.showModal();
    this.form.elements.title.focus();
  }

  openItem(item, onSaved) {
    this.#item = item;
    this.#onSaved = onSaved ?? null;
    this.#prepare('edit');
    this.#setType(item.type);
    this.form.elements.title.value = item.title;
    this.form.elements.date.value = item.date ?? '';
    this.form.elements.time.value = item.time ? item.time.slice(0, 5) : '';
    this.form.elements.durationMinutes.value = String(item.durationMinutes ?? 60);
    if (![...this.form.elements.durationMinutes.options].some((option) => option.value === this.form.elements.durationMinutes.value)) {
      this.form.elements.durationMinutes.append(new Option(`${item.durationMinutes} мин`, String(item.durationMinutes)));
      this.form.elements.durationMinutes.value = String(item.durationMinutes);
    }
    this.toggle.hidden = item.type === 'meeting';
    this.toggle.textContent = item.isDone ? 'Вернуть в работу' : 'Отметить выполненной';
    this.remove.hidden = false;
    this.sheet.showModal();
  }

  #prepare(mode) {
    this.sheet.querySelector('#editor-title').textContent = TITLES[mode];
    this.toggle.hidden = true;
    this.remove.hidden = true;
    this.remove.textContent = 'Удалить';
    delete this.remove.dataset.armed;
    this.#clearErrors();
  }

  #setType(type) {
    this.#type = type;
    this.typeGroup.querySelectorAll('[data-type]').forEach((button) => button.setAttribute('aria-checked', String(button.dataset.type === type)));
    this.durationField.hidden = type !== 'meeting';
    if (this.#item) this.toggle.hidden = type === 'meeting';
    this.#clearErrors();
  }

  #payload() {
    const elements = this.form.elements;
    return {
      type: this.#type,
      title: elements.title.value.trim(),
      date: elements.date.value || null,
      time: elements.time.value || null,
      durationMinutes: this.#type === 'meeting' ? Number(elements.durationMinutes.value) : null,
    };
  }

  #validate(payload) {
    if (!payload.title) return ['title', 'Напишите, что нужно сделать.'];
    if (payload.time && !payload.date) return ['date', 'Укажите день — время без даты не сохранить.'];
    if (payload.type === 'meeting' && !payload.date) return ['date', 'У встречи должен быть день.'];
    if (payload.type === 'meeting' && !payload.time) return ['time', 'У встречи должно быть время.'];
    if (payload.type === 'reminder' && !payload.time) return ['time', 'Укажите, во сколько напомнить.'];
    return null;
  }

  async #submit() {
    this.#clearErrors();
    const payload = this.#payload();
    const invalid = this.#validate(payload);
    if (invalid) return this.#showError(...invalid);

    this.save.disabled = true;
    try {
      const response = this.#item ? await api.update(this.#item.id, payload) : await api.create(payload);
      this.sheet.close();
      this.toast(response.warning ? `Сохранено. ${response.warning}` : 'Сохранено');
      this.#onSaved?.(response.item);
      this.changed([response.item.id]);
    } catch (error) {
      this.#showError(error.field, error.message);
    } finally {
      this.save.disabled = false;
    }
  }

  async #toggleDone() {
    const item = this.#item;
    try {
      if (item.isDone) await api.reopen(item.id, item.date);
      else await api.complete(item.id, item.date);
      this.sheet.close();
      this.toast(item.isDone ? 'Вернул в работу' : 'Отмечено выполненным');
      this.changed([item.id]);
    } catch (error) {
      this.#showError(null, error.message);
    }
  }

  async #remove() {
    if (!this.remove.dataset.armed) {
      this.remove.dataset.armed = 'true';
      this.remove.textContent = 'Точно удалить?';
      return;
    }
    const item = this.#item;
    try {
      await api.remove(item.id);
      this.sheet.close();
      this.toast('Удалено', { label: 'Вернуть', run: async () => { await api.restore(item.id); this.changed([item.id]); } });
      this.changed([]);
    } catch (error) {
      this.#showError(null, error.message);
    }
  }

  #showError(field, message) {
    const target = field && this.form.querySelector(`[data-error="${field}"]`);
    if (target) {
      target.textContent = message;
      this.form.elements[field]?.setAttribute('aria-invalid', 'true');
      this.form.elements[field]?.focus();
    } else {
      this.error.textContent = message;
      this.error.hidden = false;
    }
  }

  #clearErrors() {
    this.form.querySelectorAll('[data-error]').forEach((element) => { element.textContent = ''; });
    this.form.querySelectorAll('[aria-invalid]').forEach((element) => element.removeAttribute('aria-invalid'));
    this.error.hidden = true;
  }
}
