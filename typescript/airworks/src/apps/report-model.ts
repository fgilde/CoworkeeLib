import type { Schemas } from '@coworkee/client';

export type Widget = Schemas['ReportWidget'];
export type Source = Schemas['ReportSourceDto'];
export type Field = Schemas['ReportFieldDto'];
export type DateRange = Schemas['ReportDateRange'];
export type Preset = { name: string; type: Widget['type']; chart: Schemas['ChartKind']; w: number; h: number };

/** The widget palette of the report designer, each with a size; the same as in the web app. */
export const presets: Preset[] = [
  { name: 'KPI', type: 'Kpi', chart: 'Bar', w: 3, h: 3 },
  { name: 'Bar chart', type: 'Chart', chart: 'Bar', w: 6, h: 5 },
  { name: 'Horizontal bars', type: 'Chart', chart: 'HorizontalBar', w: 6, h: 5 },
  { name: 'Stacked bars', type: 'Chart', chart: 'StackedBar', w: 6, h: 5 },
  { name: 'Line chart', type: 'Chart', chart: 'Line', w: 6, h: 5 },
  { name: 'Area chart', type: 'Chart', chart: 'Area', w: 6, h: 5 },
  { name: 'Pie chart', type: 'Chart', chart: 'Pie', w: 4, h: 5 },
  { name: 'Donut chart', type: 'Chart', chart: 'Donut', w: 4, h: 5 },
  { name: 'Scatter plot', type: 'Chart', chart: 'Scatter', w: 6, h: 5 },
  { name: 'Radar chart', type: 'Chart', chart: 'Radar', w: 4, h: 5 },
  { name: 'Treemap', type: 'Chart', chart: 'Treemap', w: 6, h: 5 },
  { name: 'Heatmap', type: 'Chart', chart: 'Heatmap', w: 6, h: 5 },
  { name: 'Table', type: 'Table', chart: 'Bar', w: 12, h: 6 },
  { name: 'Data table', type: 'DataTable', chart: 'Bar', w: 12, h: 7 },
  { name: 'Pivot', type: 'Pivot', chart: 'Bar', w: 6, h: 5 },
  { name: 'Text', type: 'Markdown', chart: 'Bar', w: 4, h: 3 },
  { name: 'Image', type: 'Image', chart: 'Bar', w: 4, h: 4 },
  { name: 'Web page', type: 'Embed', chart: 'Bar', w: 6, h: 5 },
];

/** The icons a report can have (MudBlazor's outlined ones, as the web app stores them). */
export const reportIcons: Record<string, string> = {
  SpaceDashboard: '<rect fill="none" height="24" width="24"/><path d="M19,3H5C3.9,3,3,3.9,3,5v14c0,1.1,0.9,2,2,2h14c1.1,0,2-0.9,2-2V5C21,3.9,20.1,3,19,3z M5,19V5h6v14H5z M19,19h-6v-7h6V19z M19,10h-6V5h6V10z"/>',
  Insights: '<g><rect fill="none" height="24" width="24"/></g><g><g><path d="M21,8c-1.45,0-2.26,1.44-1.93,2.51l-3.55,3.56c-0.3-0.09-0.74-0.09-1.04,0l-2.55-2.55C12.27,10.45,11.46,9,10,9 c-1.45,0-2.27,1.44-1.93,2.52l-4.56,4.55C2.44,15.74,1,16.55,1,18c0,1.1,0.9,2,2,2c1.45,0,2.26-1.44,1.93-2.51l4.55-4.56 c0.3,0.09,0.74,0.09,1.04,0l2.55,2.55C12.73,16.55,13.54,18,15,18c1.45,0,2.27-1.44,1.93-2.52l3.56-3.55 C21.56,12.26,23,11.45,23,10C23,8.9,22.1,8,21,8z"/><polygon points="15,9 15.94,6.93 18,6 15.94,5.07 15,3 14.08,5.07 12,6 14.08,6.93"/><polygon points="3.5,11 4,9 6,8.5 4,8 3.5,6 3,8 1,8.5 3,9"/></g></g>',
  QueryStats: '<rect fill="none" height="24" width="24"/><g><path d="M19.88,18.47c0.44-0.7,0.7-1.51,0.7-2.39c0-2.49-2.01-4.5-4.5-4.5s-4.5,2.01-4.5,4.5s2.01,4.5,4.49,4.5 c0.88,0,1.7-0.26,2.39-0.7L21.58,23L23,21.58L19.88,18.47z M16.08,18.58c-1.38,0-2.5-1.12-2.5-2.5c0-1.38,1.12-2.5,2.5-2.5 s2.5,1.12,2.5,2.5C18.58,17.46,17.46,18.58,16.08,18.58z M15.72,10.08c-0.74,0.02-1.45,0.18-2.1,0.45l-0.55-0.83l-3.8,6.18 l-3.01-3.52l-3.63,5.81L1,17l5-8l3,3.5L13,6C13,6,15.72,10.08,15.72,10.08z M18.31,10.58c-0.64-0.28-1.33-0.45-2.05-0.49 c0,0,5.12-8.09,5.12-8.09L23,3.18L18.31,10.58z"/></g>',
  ShoppingCart: '<path d="M0 0h24v24H0V0z" fill="none"/><path d="M15.55 13c.75 0 1.41-.41 1.75-1.03l3.58-6.49c.37-.66-.11-1.48-.87-1.48H5.21l-.94-2H1v2h2l3.6 7.59-1.35 2.44C4.52 15.37 5.48 17 7 17h12v-2H7l1.1-2h7.45zM6.16 6h12.15l-2.76 5H8.53L6.16 6zM7 18c-1.1 0-1.99.9-1.99 2S5.9 22 7 22s2-.9 2-2-.9-2-2-2zm10 0c-1.1 0-1.99.9-1.99 2s.89 2 1.99 2 2-.9 2-2-.9-2-2-2z"/>',
  Inventory2: '<g><rect fill="none" height="24" width="24"/></g><g><g><path d="M20,2H4C3,2,2,2.9,2,4v3.01C2,7.73,2.43,8.35,3,8.7V20c0,1.1,1.1,2,2,2h14c0.9,0,2-0.9,2-2V8.7c0.57-0.35,1-0.97,1-1.69V4 C22,2.9,21,2,20,2z M19,20H5V9h14V20z M20,7H4V4h16V7z"/><rect height="2" width="6" x="9" y="12"/></g></g>',
  Groups: '<rect fill="none" height="24" width="24"/><g><path d="M4,13c1.1,0,2-0.9,2-2c0-1.1-0.9-2-2-2s-2,0.9-2,2C2,12.1,2.9,13,4,13z M5.13,14.1C4.76,14.04,4.39,14,4,14 c-0.99,0-1.93,0.21-2.78,0.58C0.48,14.9,0,15.62,0,16.43V18l4.5,0v-1.61C4.5,15.56,4.73,14.78,5.13,14.1z M20,13c1.1,0,2-0.9,2-2 c0-1.1-0.9-2-2-2s-2,0.9-2,2C18,12.1,18.9,13,20,13z M24,16.43c0-0.81-0.48-1.53-1.22-1.85C21.93,14.21,20.99,14,20,14 c-0.39,0-0.76,0.04-1.13,0.1c0.4,0.68,0.63,1.46,0.63,2.29V18l4.5,0V16.43z M16.24,13.65c-1.17-0.52-2.61-0.9-4.24-0.9 c-1.63,0-3.07,0.39-4.24,0.9C6.68,14.13,6,15.21,6,16.39V18h12v-1.61C18,15.21,17.32,14.13,16.24,13.65z M8.07,16 c0.09-0.23,0.13-0.39,0.91-0.69c0.97-0.38,1.99-0.56,3.02-0.56s2.05,0.18,3.02,0.56c0.77,0.3,0.81,0.46,0.91,0.69H8.07z M12,8 c0.55,0,1,0.45,1,1s-0.45,1-1,1s-1-0.45-1-1S11.45,8,12,8 M12,6c-1.66,0-3,1.34-3,3c0,1.66,1.34,3,3,3s3-1.34,3-3 C15,7.34,13.66,6,12,6L12,6z"/></g>',
  Euro: '<g><rect fill="none" height="24" width="24"/></g><g><g/><path d="M15,18.5c-2.51,0-4.68-1.42-5.76-3.5H15l1-2H8.58c-0.05-0.33-0.08-0.66-0.08-1s0.03-0.67,0.08-1H15l1-2H9.24 C10.32,6.92,12.5,5.5,15,5.5c1.61,0,3.09,0.59,4.23,1.57L21,5.3C19.41,3.87,17.3,3,15,3c-3.92,0-7.24,2.51-8.48,6H3l-1,2h4.06 C6.02,11.33,6,11.66,6,12s0.02,0.67,0.06,1H3l-1,2h4.52c1.24,3.49,4.56,6,8.48,6c2.31,0,4.41-0.87,6-2.3l-1.78-1.77 C18.09,17.91,16.62,18.5,15,18.5z"/></g>',
  LocalShipping: '<path d="M0 0h24v24H0V0z" fill="none"/><path d="M20 8h-3V4H3c-1.1 0-2 .9-2 2v11h2c0 1.66 1.34 3 3 3s3-1.34 3-3h6c0 1.66 1.34 3 3 3s3-1.34 3-3h2v-5l-3-4zm-.5 1.5l1.96 2.5H17V9.5h2.5zM6 18c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1zm2.22-3c-.55-.61-1.33-1-2.22-1s-1.67.39-2.22 1H3V6h12v9H8.22zM18 18c-.55 0-1-.45-1-1s.45-1 1-1 1 .45 1 1-.45 1-1 1z"/>',
  Speed: '<path d="M0 0h24v24H0z" fill="none"/><path d="M20.38 8.57l-1.23 1.85a8 8 0 0 1-.22 7.58H5.07A8 8 0 0 1 15.58 6.85l1.85-1.23A10 10 0 0 0 3.35 19a2 2 0 0 0 1.72 1h13.85a2 2 0 0 0 1.74-1 10 10 0 0 0-.27-10.44z"/><path d="M10.59 15.41a2 2 0 0 0 2.83 0l5.66-8.49-8.49 5.66a2 2 0 0 0 0 2.83z"/>',
};

export const icon = (svg?: string | null) => svg ? `<svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" class="cw-report-icon">${svg}</svg>` : '';

export const hasData = (type: Widget['type']) => type === 'Kpi' || type === 'Chart' || type === 'Table' || type === 'DataTable' || type === 'Pivot';
export const isNumber = (field: Field) => field.kind === 'Integer' || field.kind === 'Number';
/** The data table pages through OData, which computed sources (the app's own, `computed`) do not have. */
export const fits = (type: Widget['type'], source: Source) => type !== 'DataTable' || !source.computed;

let counter = 0;
const id = () => (globalThis.crypto?.randomUUID?.() ?? `w${Date.now()}${counter++}`).replace(/-/g, '').slice(0, 12);

/** A new widget below the others with a first query that already shows something, like the web app's palette. */
export function createWidget(preset: Preset, sources: readonly Source[], existing: readonly Widget[], entitySet?: string): Widget {
  const widget = {
    id: id(), type: preset.type, title: preset.name, subtitle: null, x: 0, y: existing.length === 0 ? 0 : Math.max(...existing.map((w) => Number(w.y) + Number(w.h))),
    w: preset.w, h: preset.h, query: null,
    options: { chart: preset.chart, format: 'Number', currency: 'EUR', showLegend: true, showDataLabels: false, palette: [], compareToPrevious: false, higherIsBetter: true, sparkline: 'None', target: null, view: 'Table', crossFilter: false, text: preset.type === 'Markdown' ? '## Notes\nWrite **markdown** here.' : null, url: null },
  } as unknown as Widget;
  const source = sources.find((s) => s.entitySet === entitySet && fits(preset.type, s)) ?? sources.find((s) => fits(preset.type, s));
  if (!hasData(preset.type) || !source) return widget;
  const date = source.fields.find((f) => f.kind === 'Date');
  const category = source.fields.find((f) => f.kind === 'Enum' || f.kind === 'Boolean') ?? source.fields.find((f) => f.kind === 'Text');
  const number = source.fields.find(isNumber);
  const timeline = (preset.chart === 'Line' || preset.chart === 'Area') && date;
  const other = source.fields.find((f) => (f.kind === 'Enum' || f.kind === 'Text') && f.path !== category?.path);
  const split = preset.chart === 'StackedBar' || preset.chart === 'Heatmap' || preset.type === 'Pivot'
    ? (other ? { field: other.path, bucket: 'None' } : date ? { field: date.path, bucket: 'Year' } : null) : null;
  widget.query = {
    entitySet: source.entitySet, filter: null, rules: null, facets: [], dateField: date?.path ?? null,
    groupBy: preset.type === 'Kpi' || preset.type === 'Table' || preset.type === 'DataTable' ? null
      : timeline ? { field: date!.path, bucket: 'Month' } : category ? { field: category.path, bucket: 'None' } : null,
    splitBy: split,
    aggregates: preset.chart === 'Scatter' && number ? [{ function: 'Count', field: null, label: null }, { function: 'Sum', field: number.path, label: null }] : [{ function: 'Count', field: null, label: null }],
    sortBy: 'Key', descending: false, top: null, columns: [], orderBy: null,
  } as unknown as Widget['query'];
  return widget;
}

/** A widget moved or resized on the 12 column grid stays inside it and at least one cell big. */
export function place(widget: Pick<Widget, 'x' | 'y' | 'w' | 'h'>, change: { x?: number; y?: number; w?: number; h?: number }) {
  const w = Math.min(12, Math.max(1, Math.round(change.w ?? Number(widget.w))));
  const h = Math.max(1, Math.round(change.h ?? Number(widget.h)));
  const x = Math.min(12 - w, Math.max(0, Math.round(change.x ?? Number(widget.x))));
  const y = Math.max(0, Math.round(change.y ?? Number(widget.y)));
  return { x, y, w, h };
}

const day = (date: Date) => new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
const add = (date: Date, days = 0, months = 0) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + months, date.getUTCDate() + days));
const iso = (date: Date) => date.toISOString().slice(0, 10);

/** The days of a date range preset: from inclusive, to exclusive (as the web app resolves them). */
export function resolveRange(range: DateRange, today = new Date()): { from: string; to: string } {
  const now = day(today);
  const month = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1));
  const quarter = new Date(Date.UTC(now.getUTCFullYear(), Math.floor(now.getUTCMonth() / 3) * 3, 1));
  const year = (offset: number) => new Date(Date.UTC(now.getUTCFullYear() + offset, 0, 1));
  const [from, to]: [Date, Date] = (() => {
    switch (range.preset) {
      case 'Today': return [now, add(now, 1)];
      case 'Last7Days': return [add(now, -6), add(now, 1)];
      case 'Last30Days': return [add(now, -29), add(now, 1)];
      case 'Last90Days': return [add(now, -89), add(now, 1)];
      case 'ThisMonth': return [month, add(month, 0, 1)];
      case 'LastMonth': return [add(month, 0, -1), month];
      case 'ThisQuarter': return [quarter, add(quarter, 0, 3)];
      case 'ThisYear': return [year(0), year(1)];
      case 'LastYear': return [year(-1), year(0)];
      case 'Last12Months': return [add(month, 0, -11), add(month, 0, 1)];
      default: return [new Date(`${range.from ?? '0001-01-01'}T00:00:00Z`), add(new Date(`${range.to ?? '9999-12-30'}T00:00:00Z`), 1)];
    }
  })();
  return { from: iso(from), to: iso(to) };
}

/** The widget filter with the report's date range on the widget's date field (plain days for date-only fields). */
export function widgetFilter(query: NonNullable<Widget['query']>, range?: DateRange | null, today?: Date, dateOnly = false): string | null {
  const parts = [query.filter, ...(query.facets ?? []).map((facet) => facet.oData)].filter((part): part is string => !!part);
  if (range && query.dateField) {
    const { from, to } = resolveRange(range, today);
    const time = dateOnly ? '' : 'T00:00:00Z';
    parts.push(`${query.dateField} ge ${from}${time} and ${query.dateField} lt ${to}${time}`);
  }
  return parts.length === 0 ? null : parts.map((part) => `(${part})`).join(' and ');
}

/** The filter of a widget on its source: the date range as plain days when the source's date field is date-only. */
export const sourceFilter = (query: NonNullable<Widget['query']>, source: Source | undefined, range?: DateRange | null, today?: Date) =>
  widgetFilter(query, range, today, !!source?.fields.find((field) => field.path === query.dateField)?.dateOnly);

/** The columns of a table: the chosen ones, otherwise the first six plain fields of the source. */
export const tableColumns = (query: NonNullable<Widget['query']>, source?: Source): string[] => query.columns?.length
  ? query.columns.map(String)
  : (source?.fields ?? []).filter((field) => field.kind !== 'Guid' && !field.path.includes('/')).slice(0, 6).map((field) => field.path);

/** The first page of a table on a computed source, for POST /api/v1/reports/rows. */
export const rowsRequest = (query: NonNullable<Widget['query']>, source: Source, range?: DateRange | null, today?: Date, top = 50): Schemas['ReportRowsRequest'] => ({
  entitySet: String(query.entitySet), columns: tableColumns(query, source), filter: sourceFilter(query, source, range, today),
  orderBy: query.orderBy ?? null, descending: !!query.descending, skip: 0, top,
});
