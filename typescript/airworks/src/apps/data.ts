import { entitySets, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { display, errorText, esc, label, recordTitle } from '../dom.ts';
import { recordAppId } from './record.ts';
import { icons } from '../icons.ts';
import { renderEntityTable, type EntityTableOptions } from '../table.ts';

type HistorySet = Schemas['EntityHistorySetDto'];
type Deleted = Schemas['DeletedRecordDto'];

/** Opens a row of an entity set in the record window. */
export const openRecord = (coworkee: Coworkee, entitySet: string, row: Record<string, unknown>) => {
  const id = row.Id ?? row.id;
  if (id !== undefined && id !== null) coworkee.airWorks.openApp(recordAppId, { set: entitySet, id: String(id), title: recordTitle(row) });
};

function renderDeleted(coworkee: Coworkee, panel: HTMLElement, set: string) {
  const { t, culture } = coworkee;
  panel.innerHTML = `<p class="cw-muted">${esc(t('MudDataGrid_Loading'))}</p>`;
  coworkee.request<Deleted[]>(`/api/v1/history/${encodeURIComponent(set)}/deleted`).then((items) => {
    panel.innerHTML = `<table class="cw-table"><thead><tr><th>${esc(t('Name'))}</th><th>${esc(t('Deleted'))}</th><th>${esc(t('Who'))}</th></tr></thead><tbody>${items.map((item) =>
      `<tr class="cw-clickable" tabindex="0" data-deleted="${esc(item.entityId)}" data-title="${esc(item.title ?? '')}"><td>${esc(item.title ?? item.entityId)}</td><td>${esc(display(item.deletedAt, culture))}</td><td>${esc(item.deletedByName ?? t('system'))}</td></tr>`).join('')
      || `<tr><td colspan="3" class="cw-empty">${esc(t('Nothing was deleted.'))}</td></tr>`}</tbody></table>`;
  }).catch((error) => { panel.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; });
}

/** Every OData entity set the user may read: a searchable table per set, a click opens the record with its history and workflow, deleted records come back from the bin. */
export const dataApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.data',
  title: coworkee.t('Records'),
  icon: 'fn-reporttable.png',
  accent: '#167d79',
  width: 1040,
  height: 640,
  probe: '/odata',
  render: async ({ host, intent, setState, onClose }: AppContext) => {
    const { t } = coworkee;
    host.classList.add('cw-host');
    const [sets, histories] = await Promise.all([entitySets(), coworkee.request<HistorySet[]>('/api/v1/history').catch(() => [] as HistorySet[])]);
    sets.sort((a, b) => label(t, a.name).localeCompare(label(t, b.name)));
    host.innerHTML = `<div class="cw-app cw-data"><nav class="cw-list" aria-label="${esc(t('Records'))}">${sets.map((set) =>
      `<button data-set="${esc(set.name)}">${esc(label(t, set.name))}</button>`).join('') || `<p class="cw-muted">${esc(t('Nothing found.'))}</p>`}</nav>
      <section class="cw-stack"><div class="cw-views" hidden><button data-view="all" aria-pressed="true">${esc(t('All'))}</button><button data-view="deleted" aria-pressed="false">${esc(t('Deleted'))}</button></div>
      <div class="cw-panel cw-grow"></div></section></div>`;
    const panel = host.querySelector<HTMLElement>('.cw-panel')!;
    const views = host.querySelector<HTMLElement>('.cw-views')!;
    let current = '';
    let stop: (() => void) | undefined;
    onClose(() => stop?.());
    const open = (name: string, view = 'all') => {
      current = name;
      host.querySelectorAll<HTMLElement>('[data-set]').forEach((button) => button.setAttribute('aria-current', String(button.dataset.set === name)));
      host.querySelectorAll<HTMLElement>('[data-view]').forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.view === view)));
      views.hidden = !histories.some((history) => history.entitySet === name && history.softDelete);
      setState({ set: name }, label(t, name));
      stop?.();
      stop = undefined;
      // a fresh element per set: the old table takes its listeners along
      const content = document.createElement('div');
      content.className = 'cw-fill';
      panel.replaceChildren(content);
      if (view === 'deleted') return renderDeleted(coworkee, content, name);
      const entityType = histories.find((history) => history.entitySet === name)?.entityType;
      renderEntityTable(coworkee, content, { entitySet: name, liveType: entityType, onOpen: (row) => openRecord(coworkee, name, row) }, (callback) => { stop = callback; });
    };
    host.addEventListener('click', (event) => {
      const target = event.target as Element;
      const set = target.closest<HTMLElement>('[data-set]')?.dataset.set;
      const view = target.closest<HTMLElement>('[data-view]')?.dataset.view;
      const deleted = target.closest<HTMLElement>('[data-deleted]');
      if (set) open(set);
      else if (view) open(current, view);
      else if (deleted) coworkee.airWorks.openApp(recordAppId, { set: current, id: deleted.dataset.deleted!, title: deleted.dataset.title ?? '' });
    });
    const first = intent?.set && sets.some((set) => set.name === intent.set) ? intent.set : sets[0]?.name;
    if (first) open(first);
  },
});

export type EntityAppOptions = EntityTableOptions & {
  title?: string;
  icon?: string;
  accent?: string;
  requiredPermissions?: string[];
};

/** One entity set as its own desktop app, e.g. entityApp({ entitySet: 'Products', requiredPermissions: ['Catalog.Products.View'] }). */
export const entityApp = (options: EntityAppOptions) => (coworkee: Coworkee): CoworkeeApp => ({
  id: `coworkee.entity.${options.entitySet}`,
  title: coworkee.t(options.title ?? options.entitySet),
  icon: options.icon ?? icons.table,
  accent: options.accent ?? '#35496b',
  width: 900,
  height: 580,
  requiredPermissions: options.requiredPermissions,
  render: ({ host, onClose }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = '<div class="cw-app"></div>';
    renderEntityTable(coworkee, host.firstElementChild as HTMLElement, { onOpen: (row) => openRecord(coworkee, options.entitySet, row), ...options }, onClose);
  },
});
