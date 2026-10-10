const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export class ApiError extends Error {
  constructor(code, message, status, field) {
    super(message);
    this.code = code;
    this.status = status;
    this.field = field;
  }
}

async function request(method, path, body) {
  let response;
  try {
    response = await fetch(path, {
      method,
      headers: {
        Accept: 'application/json',
        'X-Time-Zone': timeZone,
        ...(body !== undefined && { 'Content-Type': 'application/json' }),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      credentials: 'same-origin',
    });
  } catch {
    throw new ApiError('offline', 'Нет соединения с сервером. Проверьте интернет.', 0);
  }

  if (response.status === 204) return null;
  const data = await response.json().catch(() => null);
  if (!response.ok) {
    const fallback = response.status === 400 ? 'Проверьте введённые данные.' : 'Что-то пошло не так. Попробуйте ещё раз.';
    throw new ApiError(data?.code ?? 'unknown', data?.message ?? fallback, response.status, data?.field);
  }
  return data;
}

export const api = {
  chat: (text, conversationId, via) => request('POST', '/api/chat', { text, conversationId, via }),
  conversations: () => request('GET', '/api/conversations'),
  conversationMessages: (id) => request('GET', `/api/conversations/${encodeURIComponent(id)}/messages`),
  deleteConversation: (id) => request('DELETE', `/api/conversations/${encodeURIComponent(id)}`),
  updateCard: (id, messageId, card) => request('PUT', `/api/conversations/${encodeURIComponent(id)}/messages/${messageId}/card`, card),
  importConversation: (id, messages) => request('POST', `/api/conversations/${encodeURIComponent(id)}/import`, { messages }),
  importFile: async (file) => {
    const form = new FormData();
    form.append('file', file);
    let response;
    try {
      response = await fetch('/api/calendar/import', { method: 'POST', body: form, headers: { 'X-Time-Zone': timeZone }, credentials: 'same-origin' });
    } catch {
      throw new ApiError('offline', 'Нет соединения с сервером. Проверьте интернет.', 0);
    }
    const data = await response.json().catch(() => null);
    if (!response.ok) throw new ApiError(data?.code ?? 'unknown', data?.message ?? 'Не удалось импортировать файл.', response.status, data?.field);
    return data;
  },
  subscriptions: () => request('GET', '/api/calendar/subscriptions'),
  subscribe: (url) => request('POST', '/api/calendar/subscriptions', { url }),
  syncSubscription: (id) => request('POST', `/api/calendar/subscriptions/${id}/sync`),
  unsubscribe: (id) => request('DELETE', `/api/calendar/subscriptions/${id}`),
  agenda: (range) => request('GET', `/api/agenda?range=${encodeURIComponent(range)}`),
  search: (query) => request('GET', `/api/search?q=${encodeURIComponent(query)}`),
  create: (item) => request('POST', '/api/items', item),
  update: (id, item) => request('PUT', `/api/items/${id}`, item),
  complete: (id, date) => request('POST', `/api/items/${id}/complete`, { date }),
  reopen: (id, date) => request('POST', `/api/items/${id}/reopen`, { date }),
  restore: (id) => request('POST', `/api/items/${id}/restore`),
  remove: (id) => request('DELETE', `/api/items/${id}`),
  account: () => request('GET', '/api/account'),
  createLink: () => request('POST', '/api/account/link'),
};
