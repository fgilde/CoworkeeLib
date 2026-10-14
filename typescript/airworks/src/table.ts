import { odataPage, searchFilter } from '@coworkee/client';
import type { Coworkee } from './coworkee.ts';
import { display, errorText, esc } from './dom.ts';

export type Column = { field: string; title?: string };

export type EntityTableOptions = {
  entitySet: string;
  /** Columns in order; without them every simple field of the first rows. */
  columns?: Column[];
  /** Fields the search box looks in; without them the text columns. */
  searchFields?: string[];
  pageSize?: number;
};

type Row = Record<string, unknown>;

const simple = (value: unknown) => value === null || ['string', 'number', 'boolean'].includes(typeof value);

/** The columns of rows nobody described: their simple fields, the id last. */
export function columnsOf(rows: readonly Row[]): Column[] {
  const fields = [...new Set(rows.flatMap((row) => Object.keys(row).filter((key) => !key.includes('@') && simple(row[key]))))];
  return [...fields.filter((field) => field.toLowerCase() !== 'id'), ...fields.filter((field) => field.toLowerCase() === 'id')].map((field) => ({ field }));
}

/** Fields that hold text in the rows, for the search. */
export const textFields = (rows: readonly Row[], columns: readonly Column[]) =>
  columns.map((column) => column.field).filter((field) => rows.some((row) => typeof row[field] === 'string'));

/** Sorting by a column: ascending, then descending, then the server's order. */
export function nextOrder(current: string | undefined, field: string): string | undefined {
  if (current === field) return `${field} desc`;
  if (current === `${field} desc`) return undefined;
  return field;
}

/** A paged, sortable and searchable table of an OData entity set. */
export function renderEntityTable(coworkee: Coworkee, host: HTMLElement, options: EntityTableOptions) {
  const { t, culture } = coworkee;
  const pageSize = options.pageSize ?? 25;
  let page = 0;
  let orderBy: string | undefined;
  let search = '';
  let columns = options.columns;
  let searchFields = options.searchFields;
  host.innerHTML = `<div class="cw-entity">
    <div class="cw-toolbar"><input type="search" placeholder="${esc(t('Search'))}" aria-label="${esc(t('Search'))}"/><span class="cw-spacer"></span>
      <button data-action="refresh" title="${esc(t('Refresh'))}">⟳</button></div>
    <div class="cw-scroll"><table class="cw-table"><thead></thead><tbody></tbody></table></div>
    <footer class="cw-pager"><button data-action="previous" aria-label="${esc(t('MudTablePager_PreviousPage'))}">‹</button><span></span><button data-action="next" aria-label="${esc(t('MudTablePager_NextPage'))}">›</button></footer>
  </div>`;
  const head = host.querySelector('thead')!;
  const body = host.querySelector('tbody')!;
  const pager = host.querySelector('.cw-pager span')!;
  let total = 0;

  const load = async () => {
    try {
      const filter = search && searchFields ? searchFilter(searchFields, search) : undefined;
      const result = await odataPage<Row>(options.entitySet, { top: pageSize, skip: page * pageSize, orderBy, filter, count: true });
      columns ??= columnsOf(result.items);
      searchFields ??= textFields(result.items, columns);
      total = result.count ?? result.items.length;
      head.innerHTML = `<tr>${columns.map((column) => {
        const sort = orderBy === column.field ? ' ▲' : orderBy === `${column.field} desc` ? ' ▼' : '';
        return `<th><button data-sort="${esc(column.field)}">${esc(t(column.title ?? column.field))}${sort}</button></th>`;
      }).join('')}</tr>`;
      body.innerHTML = result.items.map((row) => `<tr>${columns!.map((column) => `<td>${esc(display(row[column.field], culture))}</td>`).join('')}</tr>`).join('')
        || `<tr><td colspan="${columns.length || 1}" class="cw-muted">${esc(t('Nothing found.'))}</td></tr>`;
      const pages = Math.max(1, Math.ceil(total / pageSize));
      pager.textContent = t('MudDataGridPager_InfoFormat', total === 0 ? 0 : page * pageSize + 1, Math.min(total, (page + 1) * pageSize), total);
      host.querySelector<HTMLButtonElement>('[data-action="previous"]')!.disabled = page === 0;
      host.querySelector<HTMLButtonElement>('[data-action="next"]')!.disabled = page + 1 >= pages;
    } catch (error) {
      body.innerHTML = `<tr><td class="cw-error">${esc(errorText(error))}</td></tr>`;
    }
  };

  host.addEventListener('click', (event) => {
    const target = event.target as Element;
    const action = target.closest<HTMLElement>('[data-action]')?.dataset.action;
    const sort = target.closest<HTMLElement>('[data-sort]')?.dataset.sort;
    if (sort) { orderBy = nextOrder(orderBy, sort); page = 0; }
    else if (action === 'previous') page = Math.max(0, page - 1);
    else if (action === 'next') page++;
    else if (action !== 'refresh') return;
    void load();
  });
  let timer: ReturnType<typeof setTimeout> | undefined;
  host.querySelector('input')!.addEventListener('input', (event) => {
    clearTimeout(timer);
    timer = setTimeout(() => { search = (event.target as HTMLInputElement).value; page = 0; void load(); }, 300);
  });
  void load();
}
