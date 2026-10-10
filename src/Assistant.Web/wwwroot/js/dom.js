export function h(tag, props = {}, ...children) {
  const element = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined || value === null || value === false) continue;
    if (key === 'class') element.className = value;
    else if (key === 'dataset') Object.assign(element.dataset, value);
    else if (key === 'style') element.style.cssText = value;
    else if (key.startsWith('on')) element.addEventListener(key.slice(2).toLowerCase(), value);
    else element.setAttribute(key, value === true ? '' : value);
  }
  element.append(...children.flat(Infinity).filter((child) => child !== null && child !== undefined && child !== false));
  return element;
}

const SVG_NS = 'http://www.w3.org/2000/svg';

const ICON_SHAPES = {
  meeting: [['path', 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2'], ['circle', 9, 7, 4], ['path', 'M22 21v-2a4 4 0 0 0-3-3.87'], ['path', 'M16 3.13a4 4 0 0 1 0 7.75']],
  task: [['rect', 3, 3, 18, 18, 4], ['path', 'm8 12 3 3 5-6']],
  reminder: [['path', 'M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9'], ['path', 'M10.3 21a1.94 1.94 0 0 0 3.4 0']],
  calendar: [['rect', 3, 4, 18, 17, 3], ['path', 'M16 2v4M8 2v4M3 10h18']],
  clock: [['circle', 12, 12, 9], ['path', 'M12 7v5l3 2']],
  check: [['path', 'M20 6 9 17l-5-5']],
  edit: [['path', 'M12 20h9'], ['path', 'M16.5 3.5a2.12 2.12 0 0 1 3 3L7 19l-4 1 1-4z']],
  undo: [['path', 'M9 14 4 9l5-5'], ['path', 'M4 9h11a5 5 0 0 1 0 10h-3']],
  arrow: [['path', 'M5 12h14'], ['path', 'm13 5 7 7-7 7']],
  chevron: [['path', 'm9 6 6 6-6 6']],
  warning: [['path', 'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z'], ['path', 'M12 9v4M12 17h.01']],
  play: [['path', 'M7 4.5v15a1 1 0 0 0 1.5.86l12.5-7.5a1 1 0 0 0 0-1.72L8.5 3.64A1 1 0 0 0 7 4.5z']],
  stop: [['rect', 6, 6, 12, 12, 2]],
  mic: [['rect', 9, 2, 6, 12, 3], ['path', 'M19 10v1a7 7 0 0 1-14 0v-1'], ['path', 'M12 18v4']],
  micOff: [['path', 'M2 2l20 20'], ['path', 'M9 9v2a3 3 0 0 0 5.1 2.1M15 9.3V5a3 3 0 0 0-5.7-1.3'], ['path', 'M19 10v1a7 7 0 0 1-1.2 3.9M5 10v1a7 7 0 0 0 11.4 5.4M12 18v4']],
  wifiOff: [['path', 'M2 2l20 20'], ['path', 'M8.5 16.4a5 5 0 0 1 7 0M5 12.9a10 10 0 0 1 5.2-2.8M19 12.9a10 10 0 0 0-2.2-1.6M1.4 9a15 15 0 0 1 4.3-2.9M22.6 9A15 15 0 0 0 11 5.1M12 20h.01']],
  keyboard: [['rect', 2, 5, 20, 14, 2], ['path', 'M6 9h.01M10 9h.01M14 9h.01M18 9h.01M8 15h8']],
  chat: [['path', 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z']],
  info: [['circle', 12, 12, 9], ['path', 'M12 16v-4'], ['path', 'M12 8h.01']],
  trash: [['path', 'M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6']],
};

export function icon(name) {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('aria-hidden', 'true');
  for (const [tag, ...args] of ICON_SHAPES[name]) {
    const shape = document.createElementNS(SVG_NS, tag);
    if (tag === 'path') shape.setAttribute('d', args[0]);
    if (tag === 'circle') ['cx', 'cy', 'r'].forEach((attr, index) => shape.setAttribute(attr, args[index]));
    if (tag === 'rect') ['x', 'y', 'width', 'height', 'rx'].forEach((attr, index) => shape.setAttribute(attr, args[index]));
    svg.append(shape);
  }
  if (name === 'play' || name === 'stop') svg.style.cssText = 'fill: currentColor; stroke: none;';
  return svg;
}

export function logo(size = 30) {
  const svg = document.createElementNS(SVG_NS, 'svg');
  svg.setAttribute('viewBox', '0 0 32 32');
  svg.setAttribute('aria-hidden', 'true');
  svg.setAttribute('class', 'logo');
  svg.style.width = `${size}px`;
  svg.style.height = `${size}px`;
  const parts = [
    ['logo__bg', { width: 32, height: 32, rx: 10 }],
    ['logo__bar', { x: 8.5, y: 13, width: 3, height: 6, rx: 1.5 }],
    ['logo__bar', { x: 14.5, y: 8.5, width: 3, height: 15, rx: 1.5 }],
    ['logo__accent', { x: 20.5, y: 11, width: 3, height: 10, rx: 1.5 }],
  ];
  for (const [className, attrs] of parts) {
    const rect = document.createElementNS(SVG_NS, 'rect');
    rect.setAttribute('class', className);
    for (const [attr, value] of Object.entries(attrs)) rect.setAttribute(attr, value);
    svg.append(rect);
  }
  return svg;
}

export function typeTag(type) {
  return h('span', { class: `tag tone-${type}` }, icon(type), { task: 'Задача', reminder: 'Напоминание', meeting: 'Встреча' }[type]);
}

export function highlight(text, words) {
  if (!words?.length) return [text];
  const wanted = new Set(words.map((word) => word.toLowerCase()));
  return text.split(/([\p{L}\d]+)/u).filter(Boolean).map((part) =>
    wanted.has(part.toLowerCase()) ? h('mark', {}, part) : part);
}

const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

export function keyed(node, key, signature) {
  node.dataset.key = key;
  node.dataset.sig = typeof signature === 'string' ? signature : JSON.stringify(signature);
  return node;
}

export function patchChildren(parent, next) {
  const previous = new Map();
  for (const child of parent.children) {
    if (child.dataset.key) previous.set(child.dataset.key, child);
  }
  const resolved = next.map((node) => {
    const old = node.dataset?.key ? previous.get(node.dataset.key) : null;
    if (old && old.dataset.sig === node.dataset.sig) {
      previous.delete(node.dataset.key);
      return old;
    }
    return node;
  });

  let cursor = parent.firstChild;
  for (const node of resolved) {
    if (node === cursor) {
      cursor = cursor.nextSibling;
      continue;
    }
    parent.insertBefore(node, cursor);
  }
  while (cursor) {
    const following = cursor.nextSibling;
    cursor.remove();
    cursor = following;
  }
}

export function smoothly(update, target) {
  update();
  if (target && !reducedMotion.matches) {
    target.classList.remove('fade-in');
    void target.offsetWidth;
    target.classList.add('fade-in');
  }
  return Promise.resolve();
}

export function crossfade(update) {
  if (!document.startViewTransition || reducedMotion.matches || document.visibilityState !== 'visible') {
    update();
    return Promise.resolve();
  }
  const transition = document.startViewTransition(update);
  const guard = setTimeout(() => transition.skipTransition?.(), 700);
  return transition.finished.catch(() => {}).finally(() => clearTimeout(guard));
}

export function slidingIndicator(container, attribute) {
  const indicator = h('span', { class: 'slider', 'aria-hidden': 'true' });
  container.prepend(indicator);
  container.classList.add('has-slider');
  const place = () => {
    const active = container.querySelector(`[${attribute}="true"]`);
    if (!active || active.offsetParent === null) {
      indicator.style.opacity = '0';
      return;
    }
    indicator.style.opacity = '1';
    indicator.style.width = `${active.offsetWidth}px`;
    indicator.style.height = `${active.offsetHeight}px`;
    indicator.style.transform = `translate(${active.offsetLeft}px, ${active.offsetTop}px)`;
  };
  new MutationObserver(place).observe(container, { attributes: true, subtree: true, attributeFilter: [attribute] });
  new ResizeObserver(place).observe(container);
  requestAnimationFrame(() => {
    place();
    requestAnimationFrame(() => indicator.classList.add('is-ready'));
  });
  return place;
}
