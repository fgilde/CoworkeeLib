import assert from 'node:assert/strict';
import { test } from 'node:test';
import { sessionOf } from '../src/auth.ts';
import { bytes, display, esc } from '../src/dom.ts';
import { changes, groupsFor, isEnabled, valuesOf, type SettingDefinition, type SettingGroup } from '../src/settings-model.ts';
import { columnsOf, nextOrder, textFields } from '../src/table.ts';

const setting = (name: string, scopes: string[], extra: Partial<SettingDefinition> = {}) =>
  ({ name, displayName: name, type: 'String', scopes, defaultValue: null, choices: null, ...extra }) as unknown as SettingDefinition;

test('a signed-in BFF user becomes an AirWorks session without a token', () => {
  const session = sessionOf({ isAuthenticated: true, name: 'Ada Admin', email: 'ada@shop.test', userId: 'u1', roles: ['Administrator'] });

  assert.deepEqual(session?.identity, { id: 'ada@shop.test', displayName: 'Ada Admin', email: 'ada@shop.test' });
  assert.deepEqual(session?.roles, ['Administrator']);
  assert.equal(session?.token, undefined);
  assert.equal(sessionOf({ isAuthenticated: false, roles: [] }), undefined);
});

test('settings groups keep the settings of the scope, values fall back to the defaults', () => {
  const groups = [
    { name: 'Theme', displayName: 'Theme', settings: [setting('Theme.Mode', ['User', 'Tenant'], { defaultValue: 'system' }), setting('Theme.Id', ['Tenant'])] },
    { name: 'Mail', displayName: 'Mail', settings: [setting('Mail.Host', ['Global'])] },
  ] as unknown as SettingGroup[];

  const paged = [...groups, { name: 'Ai', displayName: 'AI', page: 'AI', settings: [setting('Ai.Model', ['Global'])] }] as unknown as SettingGroup[];
  assert.deepEqual(groupsFor(paged, 'Global').map((group) => group.name), ['Mail']);
  assert.deepEqual(groupsFor(paged, 'Global', 'AI').map((group) => group.name), ['Ai']);

  const user = groupsFor(groups, 'User');
  assert.deepEqual(user.map((group) => group.settings.map((s) => s.name)), [['Theme.Mode']]);
  assert.deepEqual(valuesOf(user[0].settings, []), { 'Theme.Mode': 'system' });
  assert.deepEqual(valuesOf(user[0].settings, [{ name: 'Theme.Mode', value: 'dark', hasValue: true }]), { 'Theme.Mode': 'dark' });
});

test('conditions enable settings, only changes are sent', () => {
  const smtp = setting('Mail.Smtp.Host', ['Global'], { enabledWhen: { setting: 'Mail.UseOwnServer', values: ['true'] } } as Partial<SettingDefinition>);

  assert.equal(isEnabled(smtp, { 'Mail.UseOwnServer': 'True' }), true);
  assert.equal(isEnabled(smtp, { 'Mail.UseOwnServer': 'false' }), false);
  assert.deepEqual(changes({ a: '1', b: '********' }, { a: '2', b: '********' }), { a: '2' });
});

test('table columns are the simple fields, the id last; text fields drive the search', () => {
  const rows = [{ Id: 1, Name: 'Chair', Price: 9.5, Brand: { Name: 'X' }, '@odata.etag': 'w' }, { Id: 2, Name: 'Desk', Sku: null }];

  const columns = columnsOf(rows);
  assert.deepEqual(columns.map((c) => c.field), ['Name', 'Price', 'Sku', 'Id']);
  assert.deepEqual(textFields(rows, columns), ['Name']);
  assert.equal(nextOrder(undefined, 'Name'), 'Name');
  assert.equal(nextOrder('Name', 'Name'), 'Name desc');
  assert.equal(nextOrder('Name desc', 'Name'), undefined);
});

test('values are shown for people', () => {
  assert.equal(esc('<a href="x">\'</a>'), '&lt;a href=&quot;x&quot;&gt;&#39;&lt;/a&gt;');
  assert.equal(bytes(0, 'en'), '0 B');
  assert.equal(bytes(1536, 'en'), '1.5 KB');
  assert.equal(bytes(25 * 1024 * 1024, 'en'), '25 MB');
  assert.equal(display(true), '✓');
  assert.equal(display(null), '');
  assert.equal(display('2026-10-14', 'en-US'), '10/14/2026');
  assert.equal(display({ a: 1 }), '{"a":1}');
});

test('numbers of report widgets follow the widget format', async () => {
  const { formatNumber } = await import('../src/apps/reports.ts');
  assert.equal(formatNumber(1234.567, 'Integer', 'en'), '1,235');
  assert.equal(formatNumber(0.256, 'Percent', 'en'), '25.6%');
  assert.equal(formatNumber(12, 'Currency', 'en', 'USD'), '$12.00');
  assert.equal(formatNumber(1500, 'Compact', 'en'), '1.5K');
  assert.equal(formatNumber(null, undefined, 'en'), '0');
});

test('a recipient source keeps only the fields of its kind', async () => {
  const { sourceOf } = await import('../src/apps/admin/mail.ts');
  const values = { kind: 'Preset', preset: 'InRoles', days: null, roleIds: ['r1'], filter: 'x', userIds: ['u1'] };
  assert.deepEqual(sourceOf(values), { kind: 'Preset', preset: 'InRoles', days: null, roleIds: ['r1'], filter: null, userIds: null });
  assert.deepEqual(sourceOf({ ...values, kind: 'Manual' }), { kind: 'Manual', preset: null, days: null, roleIds: null, filter: null, userIds: ['u1'] });
  assert.deepEqual(sourceOf({ ...values, kind: 'Query' }).filter, 'x');
});

test('report widgets: palette defaults, placement on the 12 column grid and the date range filter', async () => {
  const { createWidget, place, presets, resolveRange, widgetFilter } = await import('../src/apps/report-model.ts');
  const sources = [{ entitySet: 'Orders', label: 'Orders', topic: null, fields: [
    { path: 'Status', label: 'Status', kind: 'Enum' }, { path: 'Placed', label: 'Placed', kind: 'Date' }, { path: 'Total', label: 'Total', kind: 'Number' }] }] as never;
  const line = createWidget(presets.find((preset) => preset.chart === 'Line')!, sources, []);
  assert.deepEqual(line.query?.groupBy, { field: 'Placed', bucket: 'Month' });
  const kpi = createWidget(presets[0], sources, [line]);
  assert.equal(kpi.y, 5);
  assert.equal(kpi.query?.groupBy, null);
  assert.equal(createWidget(presets.find((preset) => preset.type === 'Markdown')!, sources, []).query, null);
  assert.deepEqual(place({ x: 10, y: 0, w: 4, h: 3 }, {}), { x: 8, y: 0, w: 4, h: 3 });
  assert.deepEqual(place({ x: 0, y: 0, w: 4, h: 3 }, { x: -2.4, y: 1.6, w: 20 }), { x: 0, y: 2, w: 12, h: 3 });
  const today = new Date(2026, 9, 14);
  assert.deepEqual(resolveRange({ preset: 'Last7Days' } as never, today), { from: '2026-10-08', to: '2026-10-15' });
  assert.deepEqual(resolveRange({ preset: 'ThisQuarter' } as never, today), { from: '2026-10-01', to: '2027-01-01' });
  assert.deepEqual(resolveRange({ preset: 'Custom', from: '2026-01-01', to: '2026-01-31' } as never, today), { from: '2026-01-01', to: '2026-02-01' });
  assert.equal(widgetFilter({ ...line.query!, filter: "Status eq 'Open'" }, { preset: 'Today' } as never, today),
    "(Status eq 'Open') and (Placed ge 2026-10-14T00:00:00Z and Placed lt 2026-10-15T00:00:00Z)");
  assert.equal(widgetFilter({ ...kpi.query!, dateField: null }, null), null);
});

test('the designer sets values at their paths and clears a grouping with its field', async () => {
  const { createWidget, presets } = await import('../src/apps/report-model.ts');
  const { setPath } = await import('../src/apps/report-designer.ts');
  const widget = createWidget(presets[1], [{ entitySet: 'Orders', label: 'Orders', fields: [{ path: 'Status', label: 'Status', kind: 'Enum' }, { path: 'Total', label: 'Total', kind: 'Number' }] }] as never, []);
  setPath(widget, 'options.chart', 'Pie');
  setPath(widget, 'query.aggregates.0.function', 'Sum');
  setPath(widget, 'query.aggregates.0.field', 'Total');
  setPath(widget, 'query.splitBy.field', 'Total');
  setPath(widget, 'query.groupBy.field', '');
  setPath(widget, 'title', '');
  assert.equal(widget.options.chart, 'Pie');
  assert.deepEqual(widget.query?.aggregates?.[0], { function: 'Sum', field: 'Total', label: null });
  assert.deepEqual(widget.query?.splitBy, { field: 'Total', bucket: 'None' });
  assert.equal(widget.query?.groupBy, null);
  assert.equal(widget.title, null);
  setPath(widget, 'query.aggregates.0.function', 'Count');
  assert.equal(widget.query?.aggregates?.[0].field, null);
});

test('records: noise stays out of the columns, a record is called by its name, web pages open in the embedded shell', async () => {
  const { isNoise, recordTitle } = await import('../src/dom.ts');
  const { embedUrl } = await import('../src/apps/web.ts');
  const rows = [{ Id: 'p1', Name: 'Chair', TenantId: 't1', ImageDataUrl: `data:image/png;base64,${'A'.repeat(300)}`, 'Brand@odata.navigationLink': 'x' }];

  assert.deepEqual(columnsOf(rows).map((c) => c.field), ['Name', 'Id']);
  assert.equal(isNoise('Description', 'short'), false);
  assert.equal(isNoise('BrandId', '01a148c0-a5d6-732f-92ac-99de9f786ca9'), true);
  assert.equal(recordTitle({ Id: 'o1', Number: 'SO-1' }), 'SO-1');
  assert.equal(recordTitle({ Id: 'o2' }), 'o2');
  assert.equal(embedUrl('https://app.test', '/sales/orders?status=Open'), 'https://app.test/sales/orders?status=Open&embed=1');
  assert.equal(embedUrl(null, '/sales/orders'), undefined);
});
