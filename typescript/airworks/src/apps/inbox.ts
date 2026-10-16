import { realtimeEvents, realtimeTopics, unwrap } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc } from '../dom.ts';
import { icons } from '../icons.ts';

/** The user's notifications: unread first in bold, mark read, mark all read, delete. */
export const notificationsApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.notifications',
  title: coworkee.t('Notifications'),
  icon: icons.notifications,
  accent: '#a2273b',
  width: 560,
  height: 600,
  singleInstance: true,
  probe: '/api/v1/notifications/unread-count',
  render: ({ host, onClose }: AppContext) => {
    const { t, api, culture } = coworkee;
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-notifications"><div class="cw-toolbar"><span class="cw-spacer"></span>
      <button data-action="all">${esc(t('Mark all as read'))}</button><button data-action="refresh" title="${esc(t('Refresh'))}">⟳</button></div><ul class="cw-feed"></ul></div>`;
    const list = host.querySelector('ul')!;
    const load = async () => {
      try {
        const page = unwrap(await api.GET('/api/v1/notifications', { params: { query: { Page: 1, PageSize: 50 } } }));
        list.innerHTML = page.items.map((item) => `<li class="${item.readAt ? '' : 'unread'}" data-id="${esc(item.id)}">
            <div><strong>${esc(t(item.title, ...(item.arguments ?? [])))}</strong>${item.body ? `<p>${esc(t(item.body, ...(item.arguments ?? [])))}</p>` : ''}
            <small class="cw-muted">${esc(display(item.createdAt, culture))}</small></div>
            <span class="cw-row-actions">${item.readAt ? '' : `<button data-read title="${esc(t('Mark as read'))}">✓</button>`}<button data-delete title="${esc(t('Delete'))}">🗑</button></span></li>`).join('')
          || `<li class="cw-muted">${esc(t('No notifications yet.'))}</li>`;
      } catch (error) {
        list.innerHTML = `<li class="cw-error">${esc(errorText(error))}</li>`;
      }
    };
    const run = async (work: () => Promise<unknown>) => {
      try { await work(); } catch (error) { coworkee.notify(errorText(error), 'error'); }
      await load();
    };
    host.addEventListener('click', (event) => {
      const target = event.target as Element;
      const id = target.closest<HTMLElement>('[data-id]')?.dataset.id;
      const action = target.closest<HTMLElement>('[data-action]')?.dataset.action;
      if (action === 'refresh') void load();
      else if (action === 'all') void run(() => api.POST('/api/v1/notifications/read-all'));
      else if (id && target.closest('[data-read]')) void run(() => api.POST('/api/v1/notifications/{id}/read', { params: { path: { id } } }));
      else if (id && target.closest('[data-delete]')) void run(() => api.DELETE('/api/v1/notifications/{id}', { params: { path: { id } } }));
    });
    const stop = coworkee.user.userId ? coworkee.realtime.subscribe(realtimeTopics.user(coworkee.user.userId), ({ type, payload }) => {
      if (type === realtimeEvents.entityChanged && (payload as { entityType?: string }).entityType === 'Notification') void load();
    }) : undefined;
    onClose(() => stop?.());
    void load();
  },
});

/** The API documentation (Swagger UI) of the app in a window. */
export const apiDocsApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.api-docs',
  title: coworkee.t('API documentation'),
  icon: icons.dataObject,
  accent: '#447d1c',
  width: 1000,
  height: 680,
  requiredPermissions: ['ApiDocs.View'],
  url: '/swagger/index.html',
});
