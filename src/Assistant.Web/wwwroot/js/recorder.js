import { h } from './dom.js';

const BAR_HEIGHTS = [10, 18, 26, 14, 32, 22, 40, 28, 16, 36, 24, 44, 30, 18, 38, 26, 12, 34, 28, 20, 42, 30, 16, 24, 36, 14, 28, 40, 22, 18, 32, 26, 12, 30, 20, 16, 24, 12, 18, 10, 14, 8, 12, 8];

export class Recorder {
  #timer = 0;
  #startedAt = 0;

  constructor({ root, transcript, timer, steps, bars, orb, done, label }) {
    Object.assign(this, { root, transcript, timer, steps, bars, orb, done, label });
    this.bars.replaceChildren(...BAR_HEIGHTS.map((height, index) => h('span', {
      style: `height: ${height}px; --bar-duration: ${(0.85 + (index % 5) * 0.13).toFixed(2)}s; --bar-delay: ${(-(index * 0.07)).toFixed(2)}s`,
    })));
  }

  open() {
    this.root.hidden = false;
    this.root.classList.remove('is-processing');
    this.done.disabled = false;
    this.label.textContent = 'Слушаю';
    this.setTranscript('', '');
    this.step('listen');
    this.#startedAt = Date.now();
    this.timer.textContent = '0:00';
    clearInterval(this.#timer);
    this.#timer = setInterval(() => {
      const seconds = Math.floor((Date.now() - this.#startedAt) / 1000);
      this.timer.textContent = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
    }, 250);
  }

  close() {
    clearInterval(this.#timer);
    this.root.hidden = true;
    this.level(0);
  }

  processing() {
    clearInterval(this.#timer);
    this.root.classList.add('is-processing');
    this.label.textContent = 'Понимаю';
    this.done.disabled = true;
    this.step('understand');
    this.level(0);
  }

  executing() {
    this.label.textContent = 'Выполняю';
    this.step('execute');
  }

  step(name) {
    this.steps.querySelectorAll('[data-step]').forEach((item) => item.classList.toggle('is-active', item.dataset.step === name));
  }

  setTranscript(final, interim) {
    const nodes = [];
    if (!final && !interim) nodes.push(h('span', { class: 'placeholder' }, 'Говорите…'));
    if (final) nodes.push(final, ' ');
    if (interim) nodes.push(h('span', { class: 'tail' }, interim));
    nodes.push(h('span', { class: 'caret' }));
    this.transcript.replaceChildren(...nodes);
  }

  level(value) {
    this.bars.style.setProperty('--level-scale', String(0.55 + Math.min(1, value) * 0.7));
    this.orb.style.transform = `scale(${1 + value * 0.18})`;
  }
}
