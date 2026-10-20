import type { Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { errorText, esc } from '../dom.ts';
import { formDialog } from '../form.ts';
import { icons } from '../icons.ts';
import { createWidget, fits, hasData, isNumber, place, presets, reportIcons, type Source, type Widget } from './report-model.ts';
import { humanize, rangeSelect, renderGrid, renderWidget, reportAppId, reportDesignerAppId } from './reports.ts';

type Report = { id?: string; name: string; description: string | null; icon: string | null; visibility: Schemas['ReportVisibility']; roleIds: string[]; definition: Schemas['ReportDefinition'] };
type Query = NonNullable<Widget['query']>;

const reports = '/api/v1/reports';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
const functions = ['Count', 'Sum', 'Average', 'Min', 'Max', 'DistinctCount'];
const buckets = ['Day', 'Week', 'Month', 'Quarter', 'Year'];
const refreshes: Array<[number, string]> = [[0, 'Never'], [30, 'Every 30 seconds'], [60, 'Every minute'], [300, 'Every 5 minutes'], [900, 'Every 15 minutes'], [3600, 'Every hour']];
const types: Array<Widget['type']> = ['Kpi', 'Chart', 'Table', 'DataTable', 'Pivot', 'Markdown', 'Image', 'Embed'];
const typeLabel: Record<string, string> = { Kpi: 'KPI', Chart: 'Bar chart', Table: 'Table', DataTable: 'Data table', Pivot: 'Pivot', Markdown: 'Text', Image: 'Image', Embed: 'Web page' };

/** The settings form of the side panel for one widget; every control names the path it changes in data-path. */
function panelHtml(coworkee: Coworkee, widget: Widget, sources: readonly Source[]): string {
  const { t } = coworkee;
  const q = widget.query;
  const fields = sources.find((source) => source.entitySet === q?.entitySet)?.fields ?? [];
  const option = (value: string, text: string, current: unknown) => `<option value="${esc(value)}" ${String(current ?? '') === value ? 'selected' : ''}>${esc(text)}</option>`;
  const select = (path: string, label: string, items: Array<[string, string]>, current: unknown, empty?: string) =>
    `<div class="cw-field"><label>${esc(t(label))}<select data-path="${path}">${empty !== undefined ? option('', empty, current) : ''}${items.map(([value, text]) => option(value, text, current)).join('')}</select></label></div>`;
  const text = (path: string, label: string, value: unknown, type = 'text') => `<div class="cw-field"><label>${esc(t(label))}<input type="${type}" data-path="${path}" value="${esc(value ?? '')}"/></label></div>`;
  const check = (path: string, label: string, value: unknown) => `<div class="cw-field cw-check"><input type="checkbox" id="cw-rd-${path}" data-path="${path}" ${value ? 'checked' : ''}/><label for="cw-rd-${path}">${esc(t(label))}</label></div>`;
  const fieldItems = (list = fields): Array<[string, string]> => list.map((field) => [field.path, field.label]);
  const groupBy = (path: 'groupBy' | 'splitBy', label: string, optional: boolean) => {
    const value = q?.[path];
    const date = fields.find((field) => field.path === value?.field)?.kind === 'Date';
    return select(`query.${path}.field`, label, fieldItems(), value?.field, optional ? '-' : undefined)
      + (date ? select(`query.${path}.bucket`, 'By', buckets.map((bucket) => [bucket, t(bucket)]), value?.bucket === 'None' ? 'Day' : value?.bucket) : '');
  };
  const parts = [
    text('title', 'Title', widget.title), text('subtitle', 'Subtitle', widget.subtitle),
    select('type', 'Widget', types.map((type) => [type, t(typeLabel[type])]), widget.type),
    widget.type === 'Chart' ? select('options.chart', 'Chart', presets.filter((preset) => preset.type === 'Chart').map((preset) => [preset.chart, t(preset.name)]), widget.options.chart) : '',
  ];
  if (q && hasData(widget.type)) {
    parts.push(`<h4>${esc(t('Data'))}</h4>`, select('query.entitySet', 'Data source', sources.filter((source) => fits(widget.type, source))
      .map((source) => [source.entitySet, `${source.computed ? '∑ ' : ''}${t(source.label)}`]), q.entitySet));
    if (widget.type === 'Chart' || widget.type === 'Pivot') parts.push(groupBy('groupBy', widget.type === 'Pivot' ? 'Rows' : 'Group by', false), groupBy('splitBy', widget.type === 'Pivot' ? 'Columns' : 'Series', true));
    if (widget.type === 'Kpi' || widget.type === 'Chart' || widget.type === 'Pivot') {
      parts.push(`<div class="cw-field"><span>${esc(t('Values'))}</span>${(q.aggregates ??= []).map((aggregate, index) => `<div class="cw-aggregate">
          <select data-path="query.aggregates.${index}.function" aria-label="${esc(t('Values'))}">${functions.map((name) => option(name, t(humanize(name)), aggregate.function)).join('')}</select>
          ${aggregate.function === 'Count' ? '' : `<select data-path="query.aggregates.${index}.field" aria-label="${esc(t('Field'))}">${fieldItems(aggregate.function === 'DistinctCount' ? fields : fields.filter(isNumber)).map(([value, label]) => option(value, label, aggregate.field)).join('')}</select>`}
          ${q.aggregates!.length > 1 ? `<button type="button" data-remove-aggregate="${index}" aria-label="${esc(t('Remove'))}">&minus;</button>` : ''}</div>`).join('')}
        ${widget.type === 'Chart' && q.aggregates!.length < 8 && !q.splitBy ? `<button type="button" data-add-aggregate>${esc(t('Add value'))}</button>` : ''}</div>`);
    }
    if (widget.type === 'Table' || widget.type === 'DataTable') {
      parts.push(`<fieldset class="cw-field cw-multi"><legend>${esc(t('Columns'))}</legend>${fields.map((field) =>
        `<label><input type="checkbox" data-column="${esc(field.path)}" ${(q.columns ?? []).includes(field.path) ? 'checked' : ''}/> ${esc(field.label)}</label>`).join('')}</fieldset>`,
      select('query.orderBy', 'Sort by', fieldItems(), q.orderBy, '-'), check('query.descending', 'Descending', q.descending));
    }
    parts.push(select('query.dateField', "Date for the report's date range", fieldItems(fields.filter((field) => field.kind === 'Date')), q.dateField, '-'));
    if (widget.type === 'Chart' || widget.type === 'Pivot') {
      parts.push(select('query.sortBy', 'Sort by', [['Key', t('Category')], ['Value', t('Value')]], q.sortBy), text('query.top', 'Top', q.top, 'number'), check('query.descending', 'Descending', q.descending));
    }
    parts.push(`<h4>${esc(t('Filter'))}</h4>`, `<div class="cw-field"><label>OData<textarea data-path="query.filter" rows="2">${esc(q.filter ?? '')}</textarea></label></div>`);
  }
  if (widget.type === 'Kpi' || widget.type === 'Chart' || widget.type === 'Pivot') {
    parts.push(`<h4>${esc(t('Display'))}</h4>`, select('options.format', 'Number format', ['Number', 'Integer', 'Compact', 'Currency', 'Percent'].map((format) => [format, t(format)]), widget.options.format));
    if (widget.options.format === 'Currency') parts.push(text('options.currency', 'Currency', widget.options.currency));
    parts.push(text('options.target', 'Target', widget.options.target, 'number'));
  }
  if (widget.type === 'Kpi') {
    parts.push(check('options.compareToPrevious', 'Compare with the period before', widget.options.compareToPrevious), check('options.higherIsBetter', 'Higher is better', widget.options.higherIsBetter),
      select('options.sparkline', 'Curve over time', [['None', t('None')], ...buckets.map((bucket): [string, string] => [bucket, t(`By ${bucket.toLowerCase()}`)])], widget.options.sparkline));
  }
  if (widget.type === 'Chart') parts.push(check('options.showLegend', 'Legend', widget.options.showLegend), check('options.showDataLabels', 'Values on the chart', widget.options.showDataLabels));
  if (widget.type === 'Chart' || widget.type === 'Pivot') parts.push(check('options.crossFilter', 'Clicks filter the other widgets', widget.options.crossFilter));
  if (widget.type === 'Table') parts.push(select('options.view', 'View', ['Table', 'Cards', 'List'].map((view) => [view, t(view)]), widget.options.view));
  if (widget.type === 'Markdown') parts.push(`<div class="cw-field"><label>${esc(t('Text (Markdown)'))}<textarea data-path="options.text" rows="10">${esc(widget.options.text ?? '')}</textarea></label></div>`);
  if (widget.type === 'Image' || widget.type === 'Embed') parts.push(text('options.url', 'Address', widget.options.url));
  parts.push(`<div class="cw-actions"><button type="button" data-widget-action="duplicate">${esc(t('Duplicate'))}</button><button type="button" data-widget-action="delete">${esc(t('Delete'))}</button></div>`);
  return parts.join('');
}

/** Sets a value at a dotted path; numbers and booleans as the control gives them, empty text as null, grouping cleared with its field. */
export function setPath(widget: Widget, path: string, value: unknown) {
  const keys = path.split('.');
  if ((keys[1] === 'groupBy' || keys[1] === 'splitBy') && keys[2] === 'field') {
    (widget.query as Query)[keys[1]] = value ? { field: String(value), bucket: (widget.query as Query)[keys[1]]?.bucket ?? 'None' } : null;
    return;
  }
  let target: any = widget;
  for (const key of keys.slice(0, -1)) target = target[key] ??= {};
  target[keys.at(-1)!] = value === '' ? null : value;
  if (path.endsWith('.function') && value === 'Count') target.field = null;
}

/** Creates and changes reports: widgets on the 12 column grid (drag to move, the corner to resize), their data, filter and display, the report's settings. */
export const reportDesignerApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: reportDesignerAppId, title: coworkee.t('New report'), icon: icons.dashboardCustomize, accent: '#1565c0', width: 1280, height: 800, hidden: true,
  requiredPermissions: ['Reports.Manage'],
  render: async ({ host, intent, setTitle, setState, onClose }: AppContext) => {
    const { t, request } = coworkee;
    host.classList.add('cw-host');
    const [sources, loaded] = await Promise.all([request<Source[]>(`${reports}/sources`), intent?.id ? request<Schemas['ReportDto']>(`${reports}/${intent.id}`) : undefined]);
    const report: Report = loaded
      ? { id: loaded.id, name: loaded.name, description: loaded.description ?? null, icon: loaded.icon ?? null, visibility: loaded.visibility, roleIds: [...loaded.roleIds], definition: structuredClone(loaded.definition) }
      : { name: '', description: null, icon: null, visibility: 'Private', roleIds: [], definition: { widgets: [], filters: [], dateRange: null, refreshSeconds: 0 } as unknown as Schemas['ReportDefinition'] };
    const widgets = () => report.definition.widgets ??= [];
    let selected: string | undefined;
    const history: string[] = [JSON.stringify(report.definition)];
    let position = 0;
    host.innerHTML = `<div class="cw-app cw-designer"><div class="cw-toolbar">
        <button data-action="settings">${esc(t('Report settings'))}</button>
        <select data-add aria-label="${esc(t('Add widget'))}"><option value="">${esc(t('Add widget'))}</option>${presets.map((preset, index) => `<option value="${index}">${esc(t(preset.name))}</option>`).join('')}</select>
        <button data-action="undo" title="${esc(t('Undo'))}" aria-label="${esc(t('Undo'))}">&#x21b6;</button><button data-action="redo" title="${esc(t('Redo'))}" aria-label="${esc(t('Redo'))}">&#x21b7;</button>
        <span class="cw-spacer"></span>${rangeSelect(coworkee, report.definition.dateRange)}
        <button data-action="open">${esc(t('Open'))}</button><button data-action="save" class="cw-primary">${esc(t('Save'))}</button></div>
      <div class="cw-designer-main"><div class="cw-report cw-editing"></div><aside class="cw-designer-panel"><p class="cw-muted">${esc(t('Add widget'))}</p></aside></div></div>`;
    const grid = host.querySelector<HTMLElement>('.cw-report')!;
    const panel = host.querySelector<HTMLElement>('.cw-designer-panel')!;
    const title = () => setTitle(report.name || t('New report'));
    const draw = () => {
      renderGrid(coworkee, grid, widgets(), onClose, report.definition.dateRange, selected, sources);
      grid.querySelectorAll<HTMLElement>('[data-resize]').forEach((handle) => { handle.hidden = false; });
      if (widgets().length === 0) grid.innerHTML = `<p class="cw-muted">${esc(t('This report has no widgets yet.'))}</p>`;
    };
    const showPanel = () => {
      const widget = widgets().find((item) => item.id === selected);
      panel.innerHTML = widget ? `<h3>${esc(t('Widget'))}</h3>${panelHtml(coworkee, widget, sources)}` : `<p class="cw-muted">${esc(t('Add widget'))}</p>`;
    };
    const remember = () => {
      history.splice(position + 1);
      history.push(JSON.stringify(report.definition));
      position = history.length - 1;
    };
    const restore = (to: number) => {
      if (to < 0 || to >= history.length) return;
      position = to;
      report.definition = JSON.parse(history[to]);
      if (!widgets().some((widget) => widget.id === selected)) selected = undefined;
      draw();
      showPanel();
    };
    const refreshWidget = (widget: Widget) => {
      const element = grid.querySelector<HTMLElement>(`[data-widget="${CSS.escape(widget.id)}"]`);
      if (!element) return draw();
      element.querySelector('header')!.firstChild!.textContent = widget.title ?? '';
      const body = element.querySelector<HTMLElement>('.cw-widget-body')!;
      renderWidget(coworkee, widget, body, onClose, report.definition.dateRange, sources).catch((error) => { body.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; });
    };
    const settings = async () => formDialog(coworkee, {
      title: t('Report settings'),
      values: { ...report, icon: Object.entries(reportIcons).find(([, svg]) => svg === report.icon)?.[0] ?? '', refreshSeconds: String(report.definition.refreshSeconds ?? 0) },
      fields: [
        { name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' },
        { name: 'icon', label: 'Icon', type: 'select', options: [{ value: '', label: '' }, ...Object.keys(reportIcons).map((key) => ({ value: key, label: humanize(key) }))] },
        { name: 'refreshSeconds', label: 'Refresh automatically', type: 'select', options: refreshes.map(([seconds, label]) => ({ value: String(seconds), label: t(label) })) },
        ...(coworkee.can('Reports.Share') ? [
          { name: 'visibility', label: 'Shared with', type: 'select', options: [['Private', 'Only me'], ['Roles', 'Roles'], ['Organisation', 'Everybody in the organisation']].map(([value, label]) => ({ value, label: t(label) })) },
          { name: 'roleIds', label: 'Roles', type: 'multi', options: (await request<Schemas['ReportRoleDto'][]>(`${reports}/roles`).catch(() => [])).map((role) => ({ value: role.id, label: role.name })) },
        ] as const : []),
      ] as never,
      onSubmit: async (values) => {
        report.name = String(values.name);
        report.description = values.description as string | null;
        report.icon = values.icon ? reportIcons[String(values.icon)] : null;
        report.definition.refreshSeconds = Number(values.refreshSeconds ?? 0);
        if (values.visibility) report.visibility = values.visibility as Report['visibility'];
        if (values.roleIds) report.roleIds = values.roleIds as string[];
        title();
      },
    });
    const save = async () => {
      if (!report.name && !await settings()) return;
      const body = { name: report.name, description: report.description, icon: report.icon, visibility: report.visibility, roleIds: report.visibility === 'Roles' ? report.roleIds : [], definition: report.definition };
      try {
        const saved = await request<Schemas['ReportDto']>(report.id ? `${reports}/${report.id}` : reports, json(report.id ? 'PUT' : 'POST', body));
        report.id = saved.id;
        setState({ id: saved.id }, report.name);
        coworkee.notify(t('Saved'));
      } catch (error) {
        coworkee.notify(errorText(error), 'error');
      }
    };

    host.querySelector('.cw-toolbar')!.addEventListener('click', async (event) => {
      const action = (event.target as Element).closest<HTMLElement>('[data-action]')?.dataset.action;
      if (action === 'settings') await settings();
      else if (action === 'save') await save();
      else if (action === 'undo') restore(position - 1);
      else if (action === 'redo') restore(position + 1);
      else if (action === 'open' && report.id) coworkee.airWorks.openApp(reportAppId, { id: report.id });
    });
    host.querySelector<HTMLSelectElement>('[data-add]')!.addEventListener('change', (event) => {
      const select = event.target as HTMLSelectElement;
      if (!select.value) return;
      const widget = createWidget(presets[Number(select.value)], sources, widgets());
      widgets().push(widget);
      select.value = '';
      selected = widget.id;
      remember();
      draw();
      showPanel();
    });
    host.querySelector<HTMLSelectElement>('[data-range]')!.addEventListener('change', (event) => {
      const preset = (event.target as HTMLSelectElement).value;
      report.definition.dateRange = preset ? { preset: preset as never, from: null, to: null } : null;
      remember();
      draw();
    });

    // moving and resizing: pointer deltas in grid cells, applied to the element while dragging and to the widget on release
    grid.addEventListener('pointerdown', (event) => {
      const target = event.target as HTMLElement;
      const element = target.closest<HTMLElement>('[data-widget]');
      if (!element) return;
      const widget = widgets().find((item) => item.id === element.dataset.widget)!;
      if (selected !== widget.id) {
        selected = widget.id;
        grid.querySelectorAll('.cw-selected').forEach((item) => item.classList.remove('cw-selected'));
        element.classList.add('cw-selected');
        showPanel();
      }
      const resize = !!target.closest('[data-resize]');
      if (!resize && !target.closest('[data-drag]')) return;
      event.preventDefault();
      const cell = { x: (grid.clientWidth + 12) / 12, y: 72 + 12 };
      const start = { x: event.clientX, y: event.clientY };
      let next = place(widget, {});
      const move = (moved: PointerEvent) => {
        const dx = (moved.clientX - start.x) / cell.x;
        const dy = (moved.clientY - start.y) / cell.y;
        next = resize ? place(widget, { w: Number(widget.w) + dx, h: Number(widget.h) + dy }) : place(widget, { x: Number(widget.x) + dx, y: Number(widget.y) + dy });
        element.style.gridColumn = `${next.x + 1} / span ${next.w}`;
        element.style.gridRow = `${next.y + 1} / span ${next.h}`;
      };
      const up = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', up);
        if (next.x === Number(widget.x) && next.y === Number(widget.y) && next.w === Number(widget.w) && next.h === Number(widget.h)) return;
        Object.assign(widget, next);
        remember();
      };
      window.addEventListener('pointermove', move);
      window.addEventListener('pointerup', up);
    });

    let timer: ReturnType<typeof setTimeout> | undefined;
    panel.addEventListener('input', (event) => {
      const input = event.target as HTMLInputElement;
      const widget = widgets().find((item) => item.id === selected);
      if (!widget) return;
      if (input.dataset.column !== undefined) {
        const query = widget.query as Query;
        query.columns = [...panel.querySelectorAll<HTMLInputElement>('[data-column]:checked')].map((box) => box.dataset.column!);
      } else if (input.dataset.path) {
        setPath(widget, input.dataset.path, input.type === 'checkbox' ? input.checked : input.type === 'number' ? (input.value === '' ? null : Number(input.value)) : input.value);
      } else {
        return;
      }
      const structural = input.tagName === 'SELECT' || input.type === 'checkbox';
      const unfit = sources.find((source) => source.entitySet === widget.query?.entitySet && !fits(widget.type, source));
      if (input.dataset.path === 'type' && hasData(widget.type) && (!widget.query || unfit)) widget.query = createWidget(presets.find((preset) => preset.type === widget.type)!, sources, []).query;
      if (input.dataset.path === 'query.entitySet') Object.assign(widget.query!, { groupBy: null, splitBy: null, dateField: null, columns: [], orderBy: null, filter: null, rules: null, facets: [], aggregates: [{ function: 'Count', field: null, label: null }] });
      clearTimeout(timer);
      timer = setTimeout(() => { remember(); refreshWidget(widget); if (structural) showPanel(); }, structural ? 0 : 400);
    });
    panel.addEventListener('click', (event) => {
      const target = event.target as Element;
      const widget = widgets().find((item) => item.id === selected);
      if (!widget) return;
      const query = widget.query as Query;
      const action = target.closest<HTMLElement>('[data-widget-action]')?.dataset.widgetAction;
      if (target.closest('[data-add-aggregate]')) (query.aggregates ??= []).push({ function: 'Count', field: null, label: null });
      else if (target.closest('[data-remove-aggregate]')) query.aggregates?.splice(Number(target.closest<HTMLElement>('[data-remove-aggregate]')!.dataset.removeAggregate), 1);
      else if (action === 'delete') { widgets().splice(widgets().indexOf(widget), 1); selected = undefined; }
      else if (action === 'duplicate') {
        const copy = { ...structuredClone(widget), id: createWidget(presets[0], [], []).id, y: Math.max(...widgets().map((item) => Number(item.y) + Number(item.h))) };
        widgets().push(copy);
        selected = copy.id;
      } else return;
      remember();
      draw();
      showPanel();
    });
    title();
    draw();
  },
});
