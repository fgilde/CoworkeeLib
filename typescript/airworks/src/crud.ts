import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from './coworkee.ts';
import { display, errorText, esc } from './dom.ts';
import { confirmDialog, formDialog, type Field, type Values } from './form.ts';

export type Row = Record<string, any>;

export type CrudColumn<T extends Row> = { field: string; title: string; format?: (row: T, coworkee: Coworkee) => string; html?: boolean };

export type CrudQuery = { page: number; pageSize: number; search: string };

export type CrudAction<T extends Row> = {
  title: string;
  icon: string;
  permission?: string;
  visible?: (row: T, coworkee: Coworkee) => boolean;
  /** Return false to keep the list as it is; anything else reloads it. */
  run: (row: T, coworkee: Coworkee) => Promise<unknown>;
};

export type CrudToolbarAction = { title: string; permission?: string; primary?: boolean; run: (coworkee: Coworkee) => Promise<unknown> };

export type FieldSource = Field[] | ((coworkee: Coworkee) => Promise<Field[]>);

export type CrudOptions<T extends Row> = {
  id: string;
  title: string;
  icon: string;
  accent?: string;
  width?: number;
  height?: number;
  /** Every one is needed to see the app. */
  permissions?: string[];
  /** Only for users of the system organisation, like the host-only pages of the Blazor app. */
  hostOnly?: boolean;
  /** A GET that must succeed (the module is part of the app). */
  probe?: string;
  /** Section in the administration app; the app then gets no desktop shortcut. */
  group?: string;
  /** Rows of a page; total makes the list paged on the server, search is passed when the list has a search box. */
  load: (coworkee: Coworkee, query: CrudQuery) => Promise<{ items: T[]; total?: number }>;
  columns: CrudColumn<T>[];
  /** Search on the client over the shown columns, unless load uses the query's search itself (serverSearch). */
  search?: boolean;
  serverSearch?: boolean;
  pageSize?: number;
  create?: { permission?: string; fields: FieldSource; values?: Values; save: (values: Values, coworkee: Coworkee) => Promise<unknown> };
  edit?: {
    permission?: string;
    fields: FieldSource;
    values?: (row: T, coworkee: Coworkee) => Values | Promise<Values>;
    save: (row: T, values: Values, coworkee: Coworkee) => Promise<unknown>;
    when?: (row: T) => boolean;
  };
  remove?: { permission?: string; run: (row: T, coworkee: Coworkee) => Promise<unknown>; when?: (row: T) => boolean; name?: (row: T) => string };
  actions?: CrudAction<T>[];
  /** What a click on a row does when it has no edit form, e.g. open the item in its own window. */
  open?: (row: T, coworkee: Coworkee) => void;
  toolbar?: CrudToolbarAction[];
  /** Topics whose events reload the list. */
  live?: (coworkee: Coworkee) => string[];
};

const fieldsOf = (source: FieldSource, coworkee: Coworkee) => Array.isArray(source) ? Promise.resolve(source) : source(coworkee);

/** Renders a list with search, paging, create, edit, delete and row actions into host; used by the administration apps. */
export function renderCrud<T extends Row>(coworkee: Coworkee, host: HTMLElement, options: CrudOptions<T>, onClose?: AppContext['onClose']) {
  const { t, culture } = coworkee;
  const allowed = (permission?: string) => !permission || coworkee.can(permission);
  const pageSize = options.pageSize ?? 25;
  let page = 1;
  let search = '';
  let rows: T[] = [];
  const toolbar = [
    ...(options.create && allowed(options.create.permission) ? [{ title: t('New'), primary: true, run: () => create() } as CrudToolbarAction] : []),
    ...(options.toolbar ?? []).filter((action) => allowed(action.permission)),
  ];
  const actions = (options.actions ?? []).filter((action) => allowed(action.permission));
  const canEdit = !!options.edit && allowed(options.edit.permission);
  const canRemove = !!options.remove && allowed(options.remove.permission);
  host.innerHTML = `<div class="cw-entity">
    <div class="cw-toolbar">${options.search ? `<input type="search" placeholder="${esc(t('Search'))}" aria-label="${esc(t('Search'))}"/>` : ''}<span class="cw-spacer"></span>
      ${toolbar.map((action, index) => `<button data-tool="${index}" class="${action.primary ? 'cw-primary' : ''}">${esc(t(action.title))}</button>`).join('')}
      <button data-action="refresh" title="${esc(t('Refresh'))}" aria-label="${esc(t('Refresh'))}">&#x21bb;</button></div>
    <div class="cw-scroll"><table class="cw-table"><thead><tr>${options.columns.map((column) => `<th>${esc(t(column.title))}</th>`).join('')}<th></th></tr></thead><tbody></tbody></table></div>
    <footer class="cw-pager" hidden><button data-action="previous" aria-label="${esc(t('MudTablePager_PreviousPage'))}">&lsaquo;</button><span></span><button data-action="next" aria-label="${esc(t('MudTablePager_NextPage'))}">&rsaquo;</button></footer>
  </div>`;
  const body = host.querySelector('tbody')!;
  const pager = host.querySelector<HTMLElement>('.cw-pager')!;
  const cell = (row: T, column: CrudColumn<T>) => column.format
    ? (column.html ? column.format(row, coworkee) : esc(column.format(row, coworkee)))
    : esc(display(column.field.split('.').reduce<any>((value, key) => value?.[key], row), culture));
  const matches = (row: T) => !search || options.serverSearch
    || options.columns.some((column) => cell(row, column).toLowerCase().includes(search.toLowerCase()));

  const load = async () => {
    try {
      const result = await options.load(coworkee, { page, pageSize, search });
      rows = result.items;
      const shown = rows.map((row, index) => [row, index] as const).filter(([row]) => matches(row));
      body.innerHTML = shown.map(([row, index]) => `<tr data-row="${index}" class="${options.open || (canEdit && (options.edit!.when?.(row) ?? true)) ? 'cw-clickable' : ''}">
          ${options.columns.map((column) => `<td>${cell(row, column)}</td>`).join('')}
          <td class="cw-row-actions">${actions.filter((action) => action.visible?.(row, coworkee) ?? true).map((action) =>
            `<button data-act="${actions.indexOf(action)}" title="${esc(t(action.title))}" aria-label="${esc(t(action.title))}">${action.icon}</button>`).join('')}
          ${canEdit && (options.edit!.when?.(row) ?? true) ? `<button data-edit title="${esc(t('Edit'))}" aria-label="${esc(t('Edit'))}">&#x270e;</button>` : ''}
          ${canRemove && (options.remove!.when?.(row) ?? true) ? `<button data-remove title="${esc(t('Delete'))}" aria-label="${esc(t('Delete'))}">&#x1f5d1;</button>` : ''}</td></tr>`).join('')
        || `<tr><td colspan="${options.columns.length + 1}" class="cw-muted">${esc(t('Nothing found.'))}</td></tr>`;
      pager.hidden = result.total === undefined;
      if (result.total !== undefined) {
        const total = result.total;
        pager.querySelector('span')!.textContent = t('MudDataGridPager_InfoFormat', total === 0 ? 0 : (page - 1) * pageSize + 1, Math.min(total, page * pageSize), total);
        pager.querySelector<HTMLButtonElement>('[data-action="previous"]')!.disabled = page <= 1;
        pager.querySelector<HTMLButtonElement>('[data-action="next"]')!.disabled = page * pageSize >= total;
      }
    } catch (error) {
      body.innerHTML = `<tr><td colspan="${options.columns.length + 1}" class="cw-error">${esc(errorText(error))}</td></tr>`;
    }
  };

  const run = async (work: () => Promise<unknown>) => {
    try {
      if (await work() !== false) await load();
    } catch (error) {
      coworkee.notify(errorText(error), 'error');
    }
  };

  async function create() {
    const create = options.create!;
    const fields = await fieldsOf(create.fields, coworkee);
    const values = await formDialog(coworkee, { title: `${t(options.title)} - ${t('New')}`, fields, values: create.values, onSubmit: (values) => create.save(values, coworkee) });
    return values ? undefined : false;
  }

  async function edit(row: T) {
    const edit = options.edit!;
    const [fields, values] = await Promise.all([fieldsOf(edit.fields, coworkee), edit.values ? edit.values(row, coworkee) : row]);
    const saved = await formDialog(coworkee, { title: `${t(options.title)} - ${t('Edit')}`, fields, values, onSubmit: (changed) => edit.save(row, changed, coworkee) });
    return saved ? undefined : false;
  }

  host.addEventListener('click', (event) => {
    const target = event.target as Element;
    const action = target.closest<HTMLElement>('[data-action]')?.dataset.action;
    const tool = target.closest<HTMLElement>('[data-tool]')?.dataset.tool;
    const row = rows[Number(target.closest<HTMLElement>('[data-row]')?.dataset.row)];
    if (action === 'refresh') void load();
    else if (action === 'previous') { page = Math.max(1, page - 1); void load(); }
    else if (action === 'next') { page++; void load(); }
    else if (tool) void run(() => toolbar[Number(tool)].run(coworkee));
    else if (!row) return;
    else if (target.closest('[data-act]')) void run(() => actions[Number(target.closest<HTMLElement>('[data-act]')!.dataset.act)].run(row, coworkee));
    else if (target.closest('[data-remove]')) {
      const name = options.remove!.name?.(row) ?? String(row.name ?? row.title ?? row.id ?? '');
      void run(async () => await confirmDialog(coworkee, t('Delete {0}? This cannot be undone.', name), t('Delete')) ? options.remove!.run(row, coworkee) : false);
    } else if (options.open && !canEdit && !target.closest('button, a, input')) options.open(row, coworkee);
    else if (target.closest('[data-edit]') || (canEdit && !target.closest('button, a, input') && (options.edit!.when?.(row) ?? true))) void run(() => edit(row));
  });
  let timer: ReturnType<typeof setTimeout> | undefined;
  host.querySelector('input[type=search]')?.addEventListener('input', (event) => {
    clearTimeout(timer);
    timer = setTimeout(() => { search = (event.target as HTMLInputElement).value; page = 1; void load(); }, 300);
  });
  const stops = (options.live?.(coworkee) ?? []).map((topic) => coworkee.realtime.subscribe(topic, () => void load()));
  onClose?.(() => stops.forEach((stop) => stop()));
  void load();
  return { reload: load };
}

/** A desktop app around renderCrud. */
export const crudApp = <T extends Row>(options: CrudOptions<T>) => (coworkee: Coworkee): CoworkeeApp => ({
  id: options.id,
  title: coworkee.t(options.title),
  icon: options.icon,
  accent: options.accent ?? '#35496b',
  width: options.width ?? 860,
  height: options.height ?? 560,
  requiredPermissions: options.permissions,
  hostOnly: options.hostOnly,
  probe: options.probe,
  group: options.group,
  render: ({ host, onClose }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = '<div class="cw-app"></div>';
    renderCrud(coworkee, host.firstElementChild as HTMLElement, options, onClose);
  },
});

/** Choices from a list: value and label of each item. */
export const choices = <T>(items: readonly T[], value: (item: T) => string, label: (item: T) => string) => items.map((item) => ({ value: value(item), label: label(item) }));

/** Values without the nulls the form gives for empty texts, for APIs that want empty strings. */
export const text = (value: unknown) => value === null || value === undefined ? '' : String(value);
