import { literal, odataPage, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc, humanize, isNoise, recordTitle } from '../dom.ts';
import { formDialog } from '../form.ts';
import { icons } from '../icons.ts';
import type { GlobalSearchHit } from '../search.ts';
import { openWebPage } from './web.ts';

type History = Schemas['EntityHistoryDto'];
type Entry = Schemas['EntityHistoryEntryDto'];
type Workflow = Schemas['WorkflowStatusDto'];
type Mode = Schemas['EntityHistoryRestoreMode'];
type Row = Record<string, unknown>;

export const recordAppId = 'coworkee.record';

const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
const quietly = <T>(load: Promise<T>) => load.catch(() => undefined);
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** The record's page in the web client, as the global search links it (its own page or the list searched for it); undefined without a web client or a search of the set. */
export async function recordPage(coworkee: Coworkee, set: string, id: string): Promise<string | undefined> {
  if (!coworkee.user.appUrl || !guid.test(id)) return undefined;
  const { hits } = await coworkee.request<{ hits: GlobalSearchHit[] }>(`/api/v1/search?${new URLSearchParams({ q: id, limit: '5' })}`);
  return hits.find((hit) => hit.entitySet === set && hit.id?.toLowerCase() === id.toLowerCase())?.url ?? undefined;
}

/** The record's fields for people: simple values in the user's format, images as images. */
function fieldsHtml(coworkee: Coworkee, row: Row) {
  const { t, culture } = coworkee;
  const value = (field: string, item: unknown) => typeof item === 'string' && item.startsWith('data:image/')
    ? `<img class="cw-record-image" src="${esc(item)}" alt=""/>`
    : esc(item && typeof item === 'object' ? recordTitle(item as Row) || display(item, culture) : display(item, culture));
  const fields = Object.entries(row).filter(([field, item]) => item !== null && item !== '' && !Array.isArray(item)
    && (!isNoise(field, item) || (typeof item === 'string' && item.startsWith('data:image/'))));
  return `<dl class="cw-fields">${fields.map(([field, item]) => `<dt>${esc(t(humanize(field)))}</dt><dd>${value(field, item)}</dd>`).join('')}</dl>`;
}

/** The changes people read: no tenant key, no field that stayed empty. */
const changes = (entry: Entry) => entry.changes.filter((change) => change.property !== 'TenantId' && (change.oldText || change.oldValue || change.newText || change.newValue));

function historyHtml(coworkee: Coworkee, history: History) {
  const { t, culture } = coworkee;
  const restore = (entry: Entry, mode: Mode, title: string) =>
    `<button data-restore="${esc(entry.id)}" data-mode="${mode}">${esc(t(title))}</button>`;
  const entries = [...history.entries].reverse();
  return `${history.isDeleted ? `<p class="cw-note">${esc(t('This record was deleted. Restoring a state brings it back.'))}</p>` : ''}
    ${entries.map((entry) => `<article class="cw-entry"><header><strong>${esc(t(entry.action))}</strong>
        <span class="cw-muted">${esc(entry.actorName ?? t('system'))} · ${esc(display(entry.occurredAt, culture))}</span><span class="cw-spacer"></span>
        ${history.canRestore ? `${restore(entry, 'After', 'Restore this state')}${entry.action === 'Created' ? '' : restore(entry, 'Before', 'Restore the state before this change')}` : ''}</header>
      ${changes(entry).length ? `<table class="cw-table"><tbody>${changes(entry).map((change) => `<tr><td>${esc(t(humanize(change.property)))}</td>
        <td><s class="cw-muted">${esc(change.oldText ?? change.oldValue ?? '')}</s> &rarr; ${esc(change.newText ?? change.newValue ?? '')}</td>
        <td class="cw-row-actions">${history.canRestore && change.restorable ? `<button data-restore="${esc(entry.id)}" data-mode="Undo" data-property="${esc(change.property)}" title="${esc(t('Undo this change'))}" aria-label="${esc(t('Undo this change'))}">&#x21b6;</button>` : ''}</td></tr>`).join('')}</tbody></table>`
        : `<p class="cw-muted">${esc(t('No field changes recorded.'))}</p>`}</article>`).join('')
    || `<p class="cw-muted">${esc(t('No changes recorded yet.'))}</p>`}`;
}

function workflowHtml(coworkee: Coworkee, workflow: Workflow) {
  const { t, culture } = coworkee;
  const approval = workflow.approval;
  return `<p><span class="cw-chip">${esc(t(workflow.state))}</span></p>
    ${workflow.transitions.length ? `<div class="cw-actions cw-start">${workflow.transitions.map((transition) =>
      `<button class="cw-primary" data-transition="${esc(transition.name)}" data-comment="${transition.commentRequired}">${esc(t(transition.name))}</button>`).join('')}</div>` : ''}
    ${approval ? `<section class="cw-note"><strong>${esc(approval.title)}</strong>
      <p class="cw-muted">${esc(t(approval.requiresAll ? 'Waiting for the approval of everyone listed.' : 'Waiting for the approval of one of the people listed.'))}
        ${approval.dueAt ? ` · ${esc(t('Due {0}', display(approval.dueAt, culture)))}` : ''}</p>
      ${approval.canDecide ? `<div class="cw-actions cw-start"><button class="cw-primary" data-decide="${esc(approval.id)}" data-approve="true">${esc(t('Approve'))}</button>
        <button data-decide="${esc(approval.id)}" data-approve="false">${esc(t('Reject'))}</button></div>` : ''}</section>` : ''}
    <ol class="cw-timeline">${[...workflow.history].reverse().map((step) => `<li><strong>${esc(step.kind === 'Transition' ? t('{0} → {1}', t(step.from ?? ''), t(step.to ?? '')) : t(step.kind))}</strong>
      <span class="cw-muted">${esc(display(step.at, culture))}</span>${step.comment ? `<p class="cw-text">${esc(step.comment)}</p>` : ''}</li>`).join('')
      || `<li class="cw-muted">${esc(t('Nothing happened yet.'))}</li>`}</ol>`;
}

/** Asks for the comment of a decision or step: required when the step says so. */
const comment = async (coworkee: Coworkee, title: string, required: boolean) => {
  const values = await formDialog(coworkee, { title, submit: title, fields: [{ name: 'comment', label: 'Comment', type: 'textarea', rows: 3, required, hint: required ? 'A comment is needed for this step.' : 'Optional' }] });
  return values ? { comment: (values.comment as string | null) ?? null } : undefined;
};

/** Approves or rejects an approval with an optional comment; false when the user cancelled. */
export async function decide(coworkee: Coworkee, id: string, approve: boolean): Promise<boolean> {
  const answer = await comment(coworkee, coworkee.t(approve ? 'Approve' : 'Reject'), false);
  if (!answer) return false;
  await coworkee.request(`/api/v1/workflows/approvals/${id}`, json('POST', { approve, comment: answer.comment }));
  void coworkee.refreshBadges();
  return true;
}

/** Shows what a restore changes and runs it once the user agrees; false when cancelled. */
async function restore(coworkee: Coworkee, set: string, id: string, request: Schemas['EntityHistoryRestoreRequest']) {
  const { t } = coworkee;
  const base = `/api/v1/history/${encodeURIComponent(set)}/${id}`;
  const preview = await coworkee.request<Schemas['EntityHistoryPreviewDto']>(`${base}/preview`, json('POST', request));
  const changed = preview.fields.some((field) => field.changedLater);
  const ok = await new Promise<boolean>((resolve) => {
    const dialog = document.createElement('dialog');
    dialog.className = 'cw-app cw-dialog cw-wide';
    dialog.innerHTML = `<form method="dialog"><h2>${esc(t('Restore'))}</h2><div class="cw-dialog-body">
      ${preview.undeletes ? `<p class="cw-note">${esc(t('The deleted record comes back.'))}</p>` : ''}
      ${changed ? `<p class="cw-note">${esc(t('Some of these fields were changed after this entry. The restore replaces the newer values.'))}</p>` : ''}
      <table class="cw-table"><thead><tr><th>${esc(t('Field'))}</th><th>${esc(t('Now'))}</th><th>${esc(t('After the restore'))}</th></tr></thead><tbody>
      ${preview.fields.map((field) => `<tr><td>${esc(t(humanize(field.property)))}${field.changedLater ? ` <small class="cw-muted">(${esc(t('Changed later'))})</small>` : ''}</td>
        <td>${esc(field.currentText ?? '')}</td><td><strong>${esc(field.restoredText ?? '')}</strong></td></tr>`).join('')}</tbody></table></div>
      <div class="cw-actions"><button value="cancel">${esc(t('Cancel'))}</button><button value="ok" class="cw-primary">${esc(t('Restore'))}</button></div></form>`;
    document.body.append(dialog);
    dialog.addEventListener('close', () => { dialog.remove(); resolve(dialog.returnValue === 'ok'); });
    dialog.showModal();
  });
  if (ok) await coworkee.request(`${base}/restore`, json('POST', request));
  return ok;
}

/** Renders one record of an OData entity set: its fields, its history with undo and restore, its workflow with steps and approvals. */
export function renderRecord(coworkee: Coworkee, host: HTMLElement, set: string, id: string, onTitle?: (title: string) => void): () => void {
  const { t } = coworkee;
  host.innerHTML = `<div class="cw-record"><div class="cw-toolbar" hidden><nav class="cw-tabs" role="tablist"></nav><span class="cw-spacer"></span>
    <button data-web hidden>${esc(t('Open in the web app'))} &#x2197;</button></div><section class="cw-panel"><p class="cw-muted">${esc(t('MudDataGrid_Loading'))}</p></section></div>`;
  const toolbar = host.querySelector<HTMLElement>('.cw-toolbar')!;
  const tabs = host.querySelector('nav')!;
  const web = host.querySelector<HTMLElement>('[data-web]')!;
  const panel = host.querySelector<HTMLElement>('.cw-panel')!;
  let current = 'Details';
  let row: Row | undefined;
  let history: History | undefined;
  let workflow: Workflow | undefined;
  let page: string | undefined;

  const show = (tab = current) => {
    current = tab;
    const available = [['Details', row], ['History', history], ['Workflow', workflow]].filter(([, data]) => data).map(([name]) => name as string);
    tabs.innerHTML = available.map((name) => `<button role="tab" data-tab="${name}" aria-selected="${name === current}">${esc(t(name))}</button>`).join('');
    tabs.hidden = available.length < 2;
    web.hidden = !page;
    toolbar.hidden = tabs.hidden && web.hidden;
    panel.innerHTML = current === 'History' && history ? historyHtml(coworkee, history)
      : current === 'Workflow' && workflow ? workflowHtml(coworkee, workflow)
      : row ? fieldsHtml(coworkee, row)
      : `<p class="cw-muted">${esc(t(history?.isDeleted ? 'This record was deleted. Restoring a state brings it back.' : 'Nothing found.'))}</p>`;
  };

  const load = async () => {
    try {
      const filter = guid.test(id) ? `Id eq ${id}` : `Id eq ${literal(id)}`;
      [row, history, workflow, page] = await Promise.all([
        quietly(odataPage<Row>(set, { filter, top: 1 }).then((page) => page.items[0])),
        quietly(coworkee.request<History>(`/api/v1/history/${encodeURIComponent(set)}/${id}`)),
        quietly(coworkee.request<Workflow>(`/api/v1/workflows/${encodeURIComponent(set)}/${id}`)),
        page ? Promise.resolve(page) : quietly(recordPage(coworkee, set, id)),
      ]);
      if (row) onTitle?.(recordTitle(row));
      if (!row && history?.isDeleted && current === 'Details') current = 'History';
      show();
    } catch (error) {
      panel.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`;
    }
  };

  const run = async (work: () => Promise<unknown>) => {
    try {
      if (await work() !== false) await load();
    } catch (error) {
      coworkee.notify(errorText(error), 'error');
    }
  };

  host.addEventListener('click', (event) => {
    const target = (event.target as Element).closest<HTMLElement>('button');
    if (!target) return;
    const { tab, restore: entryId, mode, property, transition, decide: approval, approve } = target.dataset;
    if (tab) show(tab);
    else if (target === web && page) openWebPage(coworkee, page, row ? recordTitle(row) : undefined);
    else if (entryId) void run(() => restore(coworkee, set, id, { entryId, mode: mode as Mode, property: property ?? null }));
    else if (approval) void run(() => decide(coworkee, approval, approve === 'true'));
    else if (transition) {
      void run(async () => {
        const answer = await comment(coworkee, t(transition), target.dataset.comment === 'true');
        if (!answer) return false;
        await coworkee.request(`/api/v1/workflows/${encodeURIComponent(set)}/${id}/${encodeURIComponent(transition)}`, json('POST', answer));
        void coworkee.refreshBadges();
        return true;
      });
    }
  });
  void load();
  // steps and decisions of others arrive live
  return coworkee.realtime.subscribe(`workflow:${set}:${id}`, () => void load());
}

/** One record in a window, opened from the records, the search, the inbox and the audit log with the intent { set, id }; "Open in the web app" leads to its page there. */
export const recordApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: recordAppId, title: coworkee.t('Details'), icon: icons.table, accent: '#167d79', width: 720, height: 620, hidden: true, probe: '/odata',
  render: ({ host, intent, onIntent, setTitle, setState, onClose }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = '<div class="cw-app"></div>';
    let stop: (() => void) | undefined;
    onClose(() => stop?.());
    const open = (target?: Record<string, string>) => {
      if (!target?.set || !target.id) return;
      stop?.();
      setState({ set: target.set, id: target.id }, target.title);
      if (target.title) setTitle(target.title);
      // a fresh element per record: the old one takes its listeners along
      const view = document.createElement('div');
      host.firstElementChild!.replaceChildren(view);
      stop = renderRecord(coworkee, view, target.set, target.id, (title) => { setTitle(title); setState({ set: target.set, id: target.id }, title); });
    };
    onIntent(open);
    open(intent);
  },
});
