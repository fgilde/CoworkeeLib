import { realtimeEvents, realtimeTopics, unwrap, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc, label } from '../dom.ts';
import { icons } from '../icons.ts';
import { decide, recordAppId } from './record.ts';
import { openWebPage } from './web.ts';

type Approval = Schemas['ApprovalDto'];

export const inboxAppId = 'coworkee.inbox';

function approvalsHtml(coworkee: Coworkee, approvals: Approval[]) {
  const { t, culture } = coworkee;
  return approvals.map((approval) => `<li data-approval="${esc(approval.id)}" data-set="${esc(approval.entityType)}" data-entity="${esc(approval.entityId)}">
      <div class="cw-grow"><button class="cw-link" data-open>${esc(approval.title)}</button><br/>
        <small class="cw-muted">${esc(label(t, approval.entityType))} · ${esc(t(approval.state))} · ${esc(t('Requested {0}', display(approval.createdAt, culture)))}${approval.requiresAll
          ? ` · ${esc(t('{0} of {1} approved', approval.approvers.filter((a) => a.approved === true).length, approval.approvers.filter((a) => !a.escalation).length))}` : ''}</small>
        ${approval.dueAt ? `<br/><small class="${new Date(approval.dueAt) < new Date() ? 'cw-error' : 'cw-muted'}">${esc(t('Due {0}', display(approval.dueAt, culture)))}</small>` : ''}</div>
      ${approval.canDecide ? `<span class="cw-decide"><button class="cw-primary" data-approve="true">${esc(t('Approve'))}</button><button data-approve="false">${esc(t('Reject'))}</button></span>` : ''}</li>`).join('')
    || `<li class="cw-empty">${esc(t('Nothing waits for your decision.'))}</li>`;
}

function notificationsHtml(coworkee: Coworkee, items: Schemas['NotificationDto'][]) {
  const { t, culture } = coworkee;
  const linked = (item: Schemas['NotificationDto']) => !!item.link && !!coworkee.user.appUrl;
  return items.map((item) => {
    const title = esc(t(item.title, ...(item.arguments ?? [])));
    return `<li class="${item.readAt ? '' : 'unread'}" data-id="${esc(item.id)}" ${linked(item) ? `data-link="${esc(item.link)}"` : ''}>
      <div class="cw-grow">${linked(item) ? `<button class="cw-link" data-follow>${title}</button>` : `<strong>${title}</strong>`}
        ${item.body ? `<p>${esc(t(item.body, ...(item.arguments ?? [])))}</p>` : '<br/>'}<small class="cw-muted">${esc(display(item.createdAt, culture))}</small></div>
      <span class="cw-row-actions">${item.readAt ? '' : `<button data-read title="${esc(t('Mark as read'))}" aria-label="${esc(t('Mark as read'))}">&#x2713;</button>`}
        <button data-delete title="${esc(t('Delete'))}" aria-label="${esc(t('Delete'))}">&#x2715;</button></span></li>`;
  }).join('') || `<li class="cw-empty">${esc(t('No notifications yet.'))}</li>`;
}

/** What waits for the user: approvals to decide and notifications, live; the count shows on the tile. */
export const inboxApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: inboxAppId,
  title: coworkee.t('Inbox'),
  icon: icons.inbox,
  accent: '#a2273b',
  width: 640,
  height: 600,
  singleInstance: true,
  probe: '/api/v1/notifications/unread-count',
  render: ({ host, intent, setState, onClose }: AppContext) => {
    const { t, api } = coworkee;
    const withApprovals = coworkee.can('Workflows.View');
    let tab = intent?.tab === 'notifications' || !withApprovals ? 'notifications' : 'approvals';
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-inbox"><div class="cw-toolbar"><nav class="cw-tabs" role="tablist"></nav><span class="cw-spacer"></span>
      <button data-action="all" hidden>${esc(t('Mark all as read'))}</button><button data-action="refresh" title="${esc(t('Refresh'))}" aria-label="${esc(t('Refresh'))}">&#x21bb;</button></div>
      <ul class="cw-feed"></ul></div>`;
    const list = host.querySelector('ul')!;
    const tabs = host.querySelector('nav')!;

    const load = async () => {
      try {
        const [approvals, notifications] = await Promise.all([
          withApprovals ? coworkee.request<Approval[]>('/api/v1/workflows/approvals').catch(() => [] as Approval[]) : Promise.resolve([] as Approval[]),
          api.GET('/api/v1/notifications', { params: { query: { Page: 1, PageSize: 50 } } }).then(unwrap),
        ]);
        const open = approvals.filter((approval) => approval.canDecide).length;
        const unread = notifications.items.filter((item) => !item.readAt).length;
        const sections: Array<[string, string, number]> = [...(withApprovals ? [['approvals', t('Approvals'), open] as [string, string, number]] : []), ['notifications', t('Notifications'), unread]];
        tabs.innerHTML = sections.map(([id, title, count]) =>
          `<button role="tab" data-tab="${id}" aria-selected="${id === tab}">${esc(title)}${count ? ` <span class="cw-badge">${count}</span>` : ''}</button>`).join('');
        host.querySelector<HTMLElement>('[data-action="all"]')!.hidden = tab !== 'notifications' || unread === 0;
        list.innerHTML = tab === 'approvals' ? approvalsHtml(coworkee, approvals) : notificationsHtml(coworkee, notifications.items);
      } catch (error) {
        list.innerHTML = `<li class="cw-error">${esc(errorText(error))}</li>`;
      }
    };
    const run = async (work: () => Promise<unknown>) => {
      try { await work(); } catch (error) { coworkee.notify(errorText(error), 'error'); }
      await load();
      void coworkee.refreshBadges();
    };

    host.addEventListener('click', (event) => {
      const target = event.target as Element;
      const item = target.closest<HTMLElement>('li');
      const id = item?.dataset.id;
      const action = target.closest<HTMLElement>('[data-action]')?.dataset.action;
      const nextTab = target.closest<HTMLElement>('[data-tab]')?.dataset.tab;
      const approve = target.closest<HTMLElement>('[data-approve]')?.dataset.approve;
      if (nextTab) { tab = nextTab; setState({ tab }); void load(); }
      else if (action === 'refresh') void load();
      else if (action === 'all') void run(() => api.POST('/api/v1/notifications/read-all'));
      else if (item?.dataset.approval && approve) void run(() => decide(coworkee, item.dataset.approval!, approve === 'true'));
      else if (item?.dataset.approval && target.closest('[data-open]')) coworkee.airWorks.openApp(recordAppId, { set: item.dataset.set!, id: item.dataset.entity! });
      else if (id && target.closest('[data-read]')) void run(() => api.POST('/api/v1/notifications/{id}/read', { params: { path: { id } } }));
      else if (id && target.closest('[data-delete]')) void run(() => api.DELETE('/api/v1/notifications/{id}', { params: { path: { id } } }));
      else if (id && target.closest('[data-follow]')) {
        openWebPage(coworkee, item!.dataset.link!, target.textContent ?? undefined);
        if (item!.classList.contains('unread')) void run(() => api.POST('/api/v1/notifications/{id}/read', { params: { path: { id } } }));
      }
    });
    // new notifications and approval requests arrive on the user's topic
    const stop = coworkee.user.userId ? coworkee.realtime.subscribe(realtimeTopics.user(coworkee.user.userId), ({ type }) => {
      if (type === realtimeEvents.entityChanged) void load();
    }) : undefined;
    onClose(() => stop?.());
    void load();
  },
});
