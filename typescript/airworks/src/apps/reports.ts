import type { Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { crudApp } from '../crud.ts';
import { errorText, esc } from '../dom.ts';
import { renderEntityTable } from '../table.ts';

type Summary = Schemas['ReportSummaryDto'];
type Widget = Schemas['ReportWidget'];
type Result = Schemas['ReportQueryResult'];

const reports = '/api/v1/reports';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
export const reportAppId = 'coworkee.report';

/** A value in the widget's number format. */
export function formatNumber(value: unknown, format: string | undefined, culture: string, currency?: string): string {
  const number = Number(value ?? 0);
  if (Number.isNaN(number)) return String(value ?? '');
  switch (format) {
    case 'Integer': return Math.round(number).toLocaleString(culture);
    case 'Compact': return number.toLocaleString(culture, { notation: 'compact' });
    case 'Currency': return number.toLocaleString(culture, { style: 'currency', currency: currency || 'EUR' });
    case 'Percent': return number.toLocaleString(culture, { style: 'percent', maximumFractionDigits: 1 });
    default: return number.toLocaleString(culture, { maximumFractionDigits: 2 });
  }
}

const query = (coworkee: Coworkee, widget: Widget) => {
  const source = widget.query!;
  return coworkee.request<Result>(`${reports}/query`, json('POST', {
    entitySet: source.entitySet, filter: source.filter ?? null, groupBy: source.groupBy ?? null, splitBy: source.splitBy ?? null,
    aggregates: source.aggregates, sortBy: source.sortBy, descending: source.descending, top: source.top ?? null,
  }));
};

const label = (row: Schemas['ReportQueryRow']) => row.key?.label ?? '';

function bars(coworkee: Coworkee, widget: Widget, result: Result): string {
  const values = result.rows.map((row) => Number(row.values[0] ?? 0));
  const max = Math.max(1, ...values.map(Math.abs));
  return `<ul class="cw-bars">${result.rows.map((row, index) => `<li><span class="cw-bar-label" title="${esc(label(row))}">${esc(label(row))}${row.series ? ` &middot; ${esc(row.series.label)}` : ''}</span>
    <span class="cw-bar" style="--cw-share:${(Math.abs(values[index]) / max * 100).toFixed(1)}%"></span>
    <span class="cw-bar-value">${esc(formatNumber(values[index], widget.options.format, coworkee.culture, widget.options.currency))}</span></li>`).join('')}</ul>`;
}

function table(coworkee: Coworkee, widget: Widget, result: Result): string {
  const aggregates = widget.query?.aggregates ?? [];
  const split = result.rows.some((row) => row.series);
  return `<table class="cw-table"><thead><tr><th></th>${split ? '<th></th>' : ''}${aggregates.map((aggregate) =>
    `<th>${esc(coworkee.t(aggregate.label ?? `${aggregate.function}${aggregate.field ? ` ${aggregate.field}` : ''}`))}</th>`).join('')}</tr></thead>
    <tbody>${result.rows.map((row) => `<tr><td>${esc(label(row))}</td>${split ? `<td>${esc(row.series?.label ?? '')}</td>` : ''}${row.values.map((value) =>
      `<td class="cw-number">${esc(formatNumber(value, widget.options.format, coworkee.culture, widget.options.currency))}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
}

async function renderWidget(coworkee: Coworkee, widget: Widget, body: HTMLElement, onClose: AppContext['onClose']) {
  const options = widget.options;
  switch (widget.type) {
    case 'Markdown': body.innerHTML = `<div class="cw-text">${esc(options.text ?? '')}</div>`; return;
    case 'Image': body.innerHTML = options.url ? `<img src="${esc(options.url)}" alt="${esc(widget.title ?? '')}"/>` : ''; return;
    case 'Embed': body.innerHTML = options.url ? `<iframe src="${esc(options.url)}" title="${esc(widget.title ?? '')}"></iframe>` : ''; return;
  }
  if (!widget.query) return;
  if (widget.type === 'DataTable') {
    const columns = widget.query.columns?.length ? widget.query.columns.map((field) => ({ field: String(field) })) : undefined;
    renderEntityTable(coworkee, body, { entitySet: String(widget.query.entitySet), columns, pageSize: 10 }, onClose);
    return;
  }
  const result = await query(coworkee, widget);
  if (widget.type === 'Kpi') {
    body.innerHTML = `<div class="cw-kpi">${esc(formatNumber(result.rows[0]?.values[0], options.format, coworkee.culture, options.currency))}</div>`;
  } else if (widget.type === 'Chart') {
    body.innerHTML = bars(coworkee, widget, result);
  } else {
    body.innerHTML = table(coworkee, widget, result);
  }
}

/** One report: its widgets in their places on the grid, KPIs, charts as bars and tables queried on the server. */
export const reportApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: reportAppId, title: coworkee.t('Report'), icon: 'fn-analysis.png', accent: '#1565c0', width: 1100, height: 720, hidden: true,
  requiredPermissions: ['Reports.View'],
  render: async ({ host, intent, setTitle, onClose }: AppContext) => {
    host.classList.add('cw-host');
    const report = await coworkee.request<Schemas['ReportDto']>(`${reports}/${intent?.id}`);
    setTitle(report.name);
    host.innerHTML = `<div class="cw-app"><div class="cw-toolbar"><strong>${esc(report.name)}</strong><span class="cw-muted">${esc(report.description ?? '')}</span>
      <span class="cw-spacer"></span><button data-action="refresh" title="${esc(coworkee.t('Refresh'))}" aria-label="${esc(coworkee.t('Refresh'))}">&#x21bb;</button></div><div class="cw-report"></div></div>`;
    const grid = host.querySelector<HTMLElement>('.cw-report')!;
    const widgets = report.definition.widgets ?? [];
    const draw = () => {
      grid.innerHTML = widgets.map((widget, index) => `<article class="cw-widget" data-widget="${index}"
          style="grid-column:${Number(widget.x) + 1} / span ${widget.w};grid-row:${Number(widget.y) + 1} / span ${widget.h}">
          <header>${esc(widget.title ?? '')}${widget.subtitle ? `<small class="cw-muted"> ${esc(widget.subtitle)}</small>` : ''}</header><div class="cw-widget-body"></div></article>`).join('')
        || `<p class="cw-muted">${esc(coworkee.t('Nothing found.'))}</p>`;
      widgets.forEach((widget, index) => {
        const body = grid.querySelector<HTMLElement>(`[data-widget="${index}"] .cw-widget-body`)!;
        renderWidget(coworkee, widget, body, onClose).catch((error) => { body.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; });
      });
    };
    host.querySelector('[data-action=refresh]')!.addEventListener('click', draw);
    draw();
    const seconds = Number(report.definition.refreshSeconds ?? 0);
    if (seconds > 0) {
      const timer = setInterval(draw, seconds * 1000);
      onClose(() => clearInterval(timer));
    }
  },
});

/** The reports the user may see: open, mark as favourite, duplicate, delete own ones. Reports are designed in the web app. */
export const reportsApp = crudApp<Summary>({
  id: 'coworkee.reports', title: 'Reports', icon: 'fn-dashboard.png', accent: '#1565c0',
  permissions: ['Reports.View'], probe: reports,
  search: true,
  load: async ({ request }) => ({ items: await request<Summary[]>(reports) }),
  columns: [
    { field: 'isFavorite', title: '', format: (report) => report.isFavorite ? '&#x2605;' : '', html: true },
    { field: 'name', title: 'Name' }, { field: 'description', title: 'Description' }, { field: 'ownerName', title: 'Owner' }, { field: 'modifiedAt', title: 'Date' },
  ],
  open: (report, coworkee) => coworkee.airWorks.openApp(reportAppId, { id: report.id }),
  remove: { permission: 'Reports.Manage', when: (report) => report.isOwner, run: (report, { request }) => request(`${reports}/${report.id}`, json('DELETE')) },
  actions: [
    { title: 'Open', icon: '&#x25b6;', run: async (report, coworkee) => { coworkee.airWorks.openApp(reportAppId, { id: report.id }); return false; } },
    { title: 'Favorite', icon: '&#x2606;', run: (report, { request }) => request(`${reports}/${report.id}/favorite`, json('PUT', { favorite: !report.isFavorite })) },
    { title: 'Duplicate', icon: '&#x2398;', permission: 'Reports.Manage', run: (report, { request }) => request(`${reports}/${report.id}/duplicate`, json('POST')) },
  ],
});
