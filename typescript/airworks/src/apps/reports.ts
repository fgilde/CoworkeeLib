import type { Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { crudApp } from '../crud.ts';
import { errorText, esc, humanize } from '../dom.ts';
import { renderEntityTable } from '../table.ts';
import { icon, widgetFilter, type DateRange, type Widget } from './report-model.ts';

type Summary = Schemas['ReportSummaryDto'];
type Result = Schemas['ReportQueryResult'];

const reports = '/api/v1/reports';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
export const reportAppId = 'coworkee.report';
export const reportDesignerAppId = 'coworkee.report-designer';
export const datePresets = ['Today', 'Last7Days', 'Last30Days', 'Last90Days', 'ThisMonth', 'LastMonth', 'ThisQuarter', 'ThisYear', 'LastYear', 'Last12Months'];
export { humanize };

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

const query = (coworkee: Coworkee, widget: Widget, range?: DateRange | null) => {
  const source = widget.query!;
  return coworkee.request<Result>(`${reports}/query`, json('POST', {
    entitySet: source.entitySet, filter: widgetFilter(source, range), groupBy: source.groupBy ?? null, splitBy: source.splitBy ?? null,
    aggregates: source.aggregates, sortBy: source.sortBy, descending: source.descending, top: source.top ?? null,
  }));
};

const label = (row: Schemas['ReportQueryRow']) => `${row.key?.label ?? ''}${row.series ? ` · ${row.series.label}` : ''}`;
const palette = ['var(--cw-primary, #594ae2)', 'var(--cw-secondary, #ff4081)', '#00897b', '#f9a825', '#6a1b9a', '#ef6c00', '#1565c0', '#2e7d32'];

function bars(coworkee: Coworkee, widget: Widget, result: Result): string {
  const values = result.rows.map((row) => Number(row.values[0] ?? 0));
  const max = Math.max(1, ...values.map(Math.abs));
  return `<ul class="cw-bars">${result.rows.map((row, index) => `<li><span class="cw-bar-label" title="${esc(label(row))}">${esc(label(row))}</span>
    <span class="cw-bar" style="--cw-share:${(Math.abs(values[index]) / max * 100).toFixed(1)}%"></span>
    <span class="cw-bar-value">${esc(formatNumber(values[index], widget.options.format, coworkee.culture, widget.options.currency))}</span></li>`).join('')}</ul>`;
}

function line(coworkee: Coworkee, widget: Widget, result: Result, area: boolean): string {
  const values = result.rows.map((row) => Number(row.values[0] ?? 0));
  if (values.length === 0) return '';
  const max = Math.max(1, ...values);
  const points = values.map((value, index) => `${values.length === 1 ? 50 : index / (values.length - 1) * 100},${100 - value / max * 95}`);
  return `<svg class="cw-line" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label="${esc(widget.title ?? '')}">
    ${area ? `<polygon points="0,100 ${points.join(' ')} 100,100" fill="var(--cw-primary, #594ae2)" opacity=".25"/>` : ''}
    <polyline points="${points.join(' ')}" fill="none" stroke="var(--cw-primary, #594ae2)" stroke-width="2" vector-effect="non-scaling-stroke"/></svg>
    <div class="cw-axis">${result.rows.map((row, index) => `<span title="${esc(formatNumber(values[index], widget.options.format, coworkee.culture, widget.options.currency))}">${esc(label(row))}</span>`).join('')}</div>`;
}

function pie(coworkee: Coworkee, widget: Widget, result: Result, donut: boolean): string {
  const values = result.rows.map((row) => Math.max(0, Number(row.values[0] ?? 0)));
  const total = values.reduce((sum, value) => sum + value, 0) || 1;
  let angle = 0;
  const slices = values.map((value, index) => {
    const start = angle;
    angle += value / total * Math.PI * 2;
    const [x1, y1, x2, y2] = [Math.sin(start), -Math.cos(start), Math.sin(angle), -Math.cos(angle)].map((n) => (n * 50 + 50).toFixed(2));
    const path = values.length === 1 ? 'M50,0 A50,50 0 1 1 49.99,0 Z' : `M50,50 L${x1},${y1} A50,50 0 ${angle - start > Math.PI ? 1 : 0} 1 ${x2},${y2} Z`;
    return `<path d="${path}" fill="${palette[index % palette.length]}"><title>${esc(label(result.rows[index]))}: ${esc(formatNumber(value, widget.options.format, coworkee.culture, widget.options.currency))}</title></path>`;
  }).join('');
  return `<div class="cw-pie"><svg viewBox="0 0 100 100" role="img" aria-label="${esc(widget.title ?? '')}">${slices}${donut ? '<circle cx="50" cy="50" r="28" fill="var(--cw-surface, #fff)"/>' : ''}</svg>
    <ul>${result.rows.map((row, index) => `<li><i style="background:${palette[index % palette.length]}"></i>${esc(label(row))}</li>`).join('')}</ul></div>`;
}

function table(coworkee: Coworkee, widget: Widget, result: Result): string {
  const aggregates = widget.query?.aggregates ?? [];
  return `<table class="cw-table"><thead><tr><th></th>${aggregates.map((aggregate) =>
    `<th>${esc(coworkee.t(aggregate.label ?? `${humanize(aggregate.function)}${aggregate.field ? ` ${aggregate.field}` : ''}`))}</th>`).join('')}</tr></thead>
    <tbody>${result.rows.map((row) => `<tr><td>${esc(label(row))}</td>${row.values.map((value) =>
      `<td class="cw-number">${esc(formatNumber(value, widget.options.format, coworkee.culture, widget.options.currency))}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
}

/** Renders one widget into body: KPI, chart (bars, lines, areas, pies), table, pivot, data table, text, image or web page. */
export async function renderWidget(coworkee: Coworkee, widget: Widget, body: HTMLElement, onClose: AppContext['onClose'], range?: DateRange | null) {
  const options = widget.options;
  switch (widget.type) {
    case 'Markdown': body.innerHTML = `<div class="cw-text">${esc(options.text ?? '')}</div>`; return;
    case 'Image': body.innerHTML = options.url ? `<img src="${esc(options.url)}" alt="${esc(widget.title ?? '')}"/>` : ''; return;
    case 'Embed': body.innerHTML = options.url ? `<iframe src="${esc(options.url)}" title="${esc(widget.title ?? '')}"></iframe>` : ''; return;
  }
  if (!widget.query?.entitySet) { body.innerHTML = ''; return; }
  if (widget.type === 'DataTable') {
    const columns = widget.query.columns?.length ? widget.query.columns.map((field) => ({ field: String(field) })) : undefined;
    renderEntityTable(coworkee, body, { entitySet: String(widget.query.entitySet), columns, pageSize: 10, filter: widgetFilter(widget.query, range) ?? undefined, orderBy: widget.query.orderBy ? `${widget.query.orderBy}${widget.query.descending ? ' desc' : ''}` : undefined }, onClose);
    return;
  }
  const result = await query(coworkee, widget, range);
  if (widget.type === 'Kpi') {
    const value = result.rows[0]?.values[0];
    const target = options.target === null || options.target === undefined ? '' : ` <small class="cw-muted">/ ${esc(formatNumber(options.target, options.format, coworkee.culture, options.currency))}</small>`;
    body.innerHTML = `<div class="cw-kpi">${esc(formatNumber(value, options.format, coworkee.culture, options.currency))}${target}</div>`;
  } else if (widget.type === 'Chart') {
    body.innerHTML = options.chart === 'Line' || options.chart === 'Area' ? line(coworkee, widget, result, options.chart === 'Area')
      : options.chart === 'Pie' || options.chart === 'Donut' ? pie(coworkee, widget, result, options.chart === 'Donut')
      : bars(coworkee, widget, result);
  } else {
    body.innerHTML = table(coworkee, widget, result);
  }
}

/** Widgets on the 12 column grid, each loading its data. */
export function renderGrid(coworkee: Coworkee, grid: HTMLElement, widgets: readonly Widget[], onClose: AppContext['onClose'], range?: DateRange | null, selected?: string) {
  grid.innerHTML = widgets.map((widget) => `<article class="cw-widget${widget.id === selected ? ' cw-selected' : ''}" data-widget="${esc(widget.id)}"
      style="grid-column:${Number(widget.x) + 1} / span ${widget.w};grid-row:${Number(widget.y) + 1} / span ${widget.h}">
      <header data-drag>${esc(widget.title ?? '')}${widget.subtitle ? `<small class="cw-muted"> ${esc(widget.subtitle)}</small>` : ''}</header><div class="cw-widget-body"></div><span class="cw-resize" data-resize hidden></span></article>`).join('')
    || `<p class="cw-muted">${esc(coworkee.t('Nothing found.'))}</p>`;
  for (const widget of widgets) {
    const body = grid.querySelector<HTMLElement>(`[data-widget="${CSS.escape(widget.id)}"] .cw-widget-body`)!;
    renderWidget(coworkee, widget, body, onClose, range).catch((error) => { body.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; });
  }
}

export const rangeSelect = (coworkee: Coworkee, range?: DateRange | null) => `<select data-range aria-label="${esc(coworkee.t('Date range'))}">
  <option value="">${esc(coworkee.t('All time'))}</option>${datePresets.map((preset) => `<option value="${preset}" ${range?.preset === preset ? 'selected' : ''}>${esc(coworkee.t(humanize(preset)))}</option>`).join('')}</select>`;

/** One report: its widgets in their places on the grid, the date range to look at, and the designer for those who may change it. */
export const reportApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: reportAppId, title: coworkee.t('Report'), icon: 'fn-analysis.png', accent: '#1565c0', width: 1100, height: 720, hidden: true,
  requiredPermissions: ['Reports.View'],
  render: async ({ host, intent, setTitle, onClose }: AppContext) => {
    const { t } = coworkee;
    host.classList.add('cw-host');
    const report = await coworkee.request<Schemas['ReportDto']>(`${reports}/${intent?.id}`);
    let range = report.definition.dateRange ?? null;
    setTitle(report.name);
    host.innerHTML = `<div class="cw-app"><div class="cw-toolbar">${icon(report.icon)}<strong>${esc(report.name)}</strong><span class="cw-muted">${esc(report.description ?? '')}</span>
      <span class="cw-spacer"></span>${rangeSelect(coworkee, range)}
      ${report.canEdit ? `<button data-action="edit">${esc(t('Edit'))}</button>` : ''}
      <button data-action="refresh" title="${esc(t('Refresh'))}" aria-label="${esc(t('Refresh'))}">&#x21bb;</button></div><div class="cw-report"></div></div>`;
    const grid = host.querySelector<HTMLElement>('.cw-report')!;
    const widgets = report.definition.widgets ?? [];
    const draw = () => renderGrid(coworkee, grid, widgets, onClose, range);
    host.querySelector('[data-action=refresh]')!.addEventListener('click', draw);
    host.querySelector('[data-action=edit]')?.addEventListener('click', () => coworkee.airWorks.openApp(reportDesignerAppId, { id: report.id }));
    host.querySelector<HTMLSelectElement>('[data-range]')!.addEventListener('change', (event) => {
      const preset = (event.target as HTMLSelectElement).value;
      range = preset ? { preset: preset as DateRange['preset'], from: null, to: null } : null;
      draw();
    });
    draw();
    const seconds = Number(report.definition.refreshSeconds ?? 0);
    if (seconds > 0) {
      const timer = setInterval(draw, seconds * 1000);
      onClose(() => clearInterval(timer));
    }
  },
});

/** The reports the user may see: open, design, mark as favourite, duplicate, delete own ones. */
export const reportsApp = crudApp<Summary>({
  id: 'coworkee.reports', title: 'Reports', icon: 'fn-dashboard.png', accent: '#1565c0',
  permissions: ['Reports.View'], probe: reports,
  search: true,
  load: async ({ request }) => ({ items: await request<Summary[]>(reports) }),
  columns: [
    { field: 'isFavorite', title: '', format: (report) => `${report.isFavorite ? '&#x2605;' : ''}${icon(report.icon)}`, html: true },
    { field: 'name', title: 'Name' }, { field: 'description', title: 'Description' }, { field: 'ownerName', title: 'Owner' }, { field: 'modifiedAt', title: 'Date' },
  ],
  open: (report, coworkee) => coworkee.airWorks.openApp(reportAppId, { id: report.id }),
  remove: { permission: 'Reports.Manage', when: (report) => report.isOwner, run: (report, { request }) => request(`${reports}/${report.id}`, json('DELETE')) },
  toolbar: [{ title: 'New report', primary: true, permission: 'Reports.Manage', run: async (coworkee) => { coworkee.airWorks.openApp(reportDesignerAppId, {}); return false; } }],
  actions: [
    { title: 'Open', icon: '&#x25b6;', run: async (report, coworkee) => { coworkee.airWorks.openApp(reportAppId, { id: report.id }); return false; } },
    { title: 'Edit', icon: '&#x270e;', permission: 'Reports.Manage', visible: (report) => report.isOwner, run: async (report, coworkee) => { coworkee.airWorks.openApp(reportDesignerAppId, { id: report.id }); return false; } },
    { title: 'Favorite', icon: '&#x2606;', run: (report, { request }) => request(`${reports}/${report.id}/favorite`, json('PUT', { favorite: !report.isFavorite })) },
    { title: 'Duplicate', icon: '&#x2398;', permission: 'Reports.Manage', run: (report, { request }) => request(`${reports}/${report.id}/duplicate`, json('POST')) },
  ],
  live: () => [],
});
