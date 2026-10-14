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
