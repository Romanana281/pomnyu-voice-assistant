const TYPE_NAMES = { task: 'Задача', reminder: 'Напоминание', meeting: 'Встреча' };
const SHORT_DAYS = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];
const MONTHS_SHORT = ['янв', 'фев', 'мар', 'апр', 'мая', 'июн', 'июл', 'авг', 'сен', 'окт', 'ноя', 'дек'];

const dayMonth = new Intl.DateTimeFormat('ru-RU', { day: 'numeric', month: 'long' });
const weekdayDayMonth = new Intl.DateTimeFormat('ru-RU', { weekday: 'long', day: 'numeric', month: 'long' });
const weekdayOnly = new Intl.DateTimeFormat('ru-RU', { weekday: 'long' });

export const capitalize = (text) => text.charAt(0).toUpperCase() + text.slice(1);
const pad = (value) => String(value).padStart(2, '0');

export function parseDate(iso) {
  const [year, month, day] = iso.split('-').map(Number);
  return new Date(year, month - 1, day);
}

export function isoDate(date) {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export const todayIso = () => isoDate(new Date());

export function daysBetween(fromIso, toIso) {
  return Math.round((parseDate(toIso) - parseDate(fromIso)) / 86_400_000);
}

export const clock = (time) => (time ? time.slice(0, 5) : '');

export function minutesOf(time) {
  const [hours, minutes] = time.split(':').map(Number);
  return hours * 60 + minutes;
}

export function clockFromMinutes(total) {
  const normalized = ((total % 1440) + 1440) % 1440;
  return `${pad(Math.floor(normalized / 60))}:${pad(normalized % 60)}`;
}

export function endTime(item) {
  if (!item.time || !item.durationMinutes) return null;
  return clockFromMinutes(minutesOf(item.time) + item.durationMinutes);
}

export function shortDate(iso) {
  const date = parseDate(iso);
  return `${SHORT_DAYS[date.getDay()]}, ${dayMonth.format(date)}`;
}

export function compactDate(iso) {
  const date = parseDate(iso);
  return `${date.getDate()} ${MONTHS_SHORT[date.getMonth()]}`;
}

export const dayOfWeekShort = (iso) => SHORT_DAYS[parseDate(iso).getDay()];

export function relativeDay(iso, today = todayIso()) {
  const diff = daysBetween(today, iso);
  if (diff === 0) return 'сегодня';
  if (diff === 1) return 'завтра';
  if (diff === -1) return 'вчера';
  return shortDate(iso);
}

export function dividerLabel(iso, today = todayIso()) {
  const diff = daysBetween(today, iso);
  const full = weekdayDayMonth.format(parseDate(iso));
  if (diff === 0) return `Сегодня, ${full.replace(',', '')}`;
  if (diff === -1) return `Вчера, ${full.replace(',', '')}`;
  return capitalize(full);
}

export const longDate = (iso) => capitalize(weekdayDayMonth.format(parseDate(iso)));

export function sectionDayLabel(iso, today) {
  const diff = daysBetween(today, iso);
  const weekday = capitalize(weekdayOnly.format(parseDate(iso)));
  if (diff === 0) return `${weekday}, сегодня`;
  if (diff === 1) return `${weekday}, завтра`;
  return `${weekday}, ${dayMonth.format(parseDate(iso))}`;
}

export function rangeLabel(fromIso, toIso) {
  if (fromIso === toIso) return longDate(fromIso);
  const from = parseDate(fromIso);
  const to = parseDate(toIso);
  return from.getMonth() === to.getMonth()
    ? `${from.getDate()} — ${dayMonth.format(to)}`
    : `${dayMonth.format(from)} — ${dayMonth.format(to)}`;
}

export const typeName = (type) => TYPE_NAMES[type] ?? type;

export function plural(count, one, few, many) {
  const lastTwo = Math.abs(count) % 100;
  const last = lastTwo % 10;
  const form = lastTwo >= 11 && lastTwo <= 14 ? many : last === 1 ? one : last >= 2 && last <= 4 ? few : many;
  return `${count} ${form}`;
}

export function timeOfDay(date = new Date()) {
  return `${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function durationLabel(seconds) {
  const total = Math.max(1, Math.round(seconds));
  return `${Math.floor(total / 60)}:${pad(total % 60)}`;
}

export function inMinutesLabel(minutes) {
  if (minutes < 1) return 'сейчас';
  if (minutes < 60) return `через ${plural(minutes, 'минуту', 'минуты', 'минут')}`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return rest === 0 ? `через ${plural(hours, 'час', 'часа', 'часов')}` : `через ${hours} ч ${rest} мин`;
}
