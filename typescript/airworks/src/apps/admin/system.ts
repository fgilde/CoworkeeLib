import { odataPage, type Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../../coworkee.ts';
import { choices, crudApp, renderCrud } from '../../crud.ts';
import { esc } from '../../dom.ts';
import { confirmDialog, formDialog, infoDialog, type Field, type Values } from '../../form.ts';

type Language = Schemas['LanguageDto'];
type Theme = Schemas['ThemeDto'];
type Backup = Schemas['BackupDto'];
type Edition = Schemas['EditionDto'];
type Tenant = { id: string; name: string; identifier: string; isActive: boolean; acceptsRegistrations: boolean };

const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
const check = (value: unknown) => value ? '&#x2713;' : '';
const paged = (page: number, pageSize: number, extra: Record<string, string> = {}) =>
  new URLSearchParams({ Page: String(page), PageSize: String(pageSize), ...Object.fromEntries(Object.entries(extra).filter(([, value]) => value)) });

const languageFields: Field[] = [
  { name: 'culture', label: 'Culture', required: true }, { name: 'name', label: 'Name', required: true },
  { name: 'isEnabled', label: 'Enabled', type: 'bool' }, { name: 'isDefault', label: 'Default', type: 'bool' },
];

export const languagesApp = crudApp<Language>({
  id: 'coworkee.admin.languages', group: 'Localization', title: 'Languages', icon: 'icon-balancesheet.png', accent: '#0277bd',
  permissions: ['Localization.Manage'],
  search: true,
  load: async ({ request }) => ({ items: await request<Language[]>('/api/v1/localization/languages/all') }),
  columns: [
    { field: 'culture', title: 'Culture' }, { field: 'name', title: 'Name' },
    { field: 'isEnabled', title: 'Enabled', format: (language) => check(language.isEnabled), html: true },
    { field: 'isDefault', title: 'Default', format: (language) => check(language.isDefault), html: true },
  ],
  create: { values: { isEnabled: true }, fields: languageFields, save: (values, { request }) => request('/api/v1/localization/languages', json('POST', values)) },
  edit: { fields: languageFields, when: (language) => !!language.id, save: (language, values, { request }) => request(`/api/v1/localization/languages/${language.id}`, json('PUT', values)) },
  remove: { when: (language) => !!language.id && !language.isDefault, run: (language, { request }) => request('/api/v1/localization/languages/delete', json('POST', { ids: [language.id] })) },
  actions: [{
    title: 'Switch', icon: '&#x23fc;',
    run: async (language, coworkee) => {
      const result = await coworkee.request<Schemas['LanguageSwitchDto']>(`/api/v1/localization/languages/${encodeURIComponent(language.culture)}/enabled`, json('PUT', { enabled: !language.isEnabled }));
      if (Number(result.translated) > 0) coworkee.notify(coworkee.t('{0} texts were translated.', result.translated));
    },
  }],
});

/** The texts of one language: the English text, the language's default and the own translation, editable; missing ones machine translated. */
export const translationsApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.admin.translations', group: 'Localization', title: coworkee.t('Translations'), icon: 'fn-help.png', accent: '#0277bd', width: 980, height: 620,
  requiredPermissions: ['Localization.Manage'],
  render: async ({ host, onClose }: AppContext) => {
    const { t, request } = coworkee;
    host.classList.add('cw-host');
    const languages = (await request<Language[]>('/api/v1/localization/languages/all')).filter((language) => language.isEnabled);
    host.innerHTML = `<div class="cw-app cw-stack"><div class="cw-toolbar"><label for="cw-tr-culture">${esc(t('Language'))}</label>
      <select id="cw-tr-culture">${languages.map((language) => `<option value="${esc(language.culture)}">${esc(language.name)}</option>`).join('')}</select></div><div class="cw-grow"></div></div>`;
    const select = host.querySelector('select')!;
    const panel = host.querySelector<HTMLElement>('.cw-grow')!;
    const show = () => {
      const culture = select.value;
      renderCrud<Schemas['TranslationRowDto']>(coworkee, panel, {
        id: 'translations', title: 'Translations', icon: '', search: true, pageSize: 100,
        load: async ({ request }) => ({ items: await request<Schemas['TranslationRowDto'][]>(`/api/v1/localization/translations/${encodeURIComponent(culture)}`) }),
        columns: [{ field: 'key', title: 'Text' }, { field: 'default', title: 'Default' }, { field: 'value', title: 'Translation' }],
        edit: {
          fields: [{ name: 'key', label: 'Text', readOnly: true }, { name: 'default', label: 'Default', readOnly: true }, { name: 'value', label: 'Translation', type: 'textarea', rows: 3 }],
          save: (row, values, { request }) => request('/api/v1/localization/translations', json('PUT', { culture, key: row.key, value: values.value })),
        },
        toolbar: [{
          title: t('Translate missing texts'),
          run: async (coworkee) => {
            const result = await coworkee.request<Schemas['TranslateMissingDto']>(`/api/v1/localization/translations/${encodeURIComponent(culture)}/translate`, json('POST'));
            coworkee.notify(result.translatorAvailable ? t('{0} texts were translated.', result.translated) : t('Set up a translation service in the settings (Translation) first.'), result.translatorAvailable ? 'info' : 'error');
          },
        }],
      }, onClose);
    };
    select.addEventListener('change', show);
    if (languages.length > 0) show();
  },
});

const themeFields: Field[] = [
  { name: 'name', label: 'Name', required: true }, { name: 'isPublished', label: 'Published', type: 'bool' },
  { name: 'paletteLight', label: 'Light palette', type: 'textarea', rows: 6, hint: 'Json' }, { name: 'paletteDark', label: 'Dark palette', type: 'textarea', rows: 6, hint: 'Json' },
  { name: 'logo', label: 'App logo', type: 'textarea', rows: 2 }, { name: 'customCss', label: 'Plain CSS for the whole app, applied after the theme', type: 'textarea', rows: 4 },
];

const themeValues = (theme: Theme): Values => ({ ...theme, paletteLight: JSON.stringify(theme.paletteLight ?? {}, null, 2), paletteDark: JSON.stringify(theme.paletteDark ?? {}, null, 2) });
const themeRequest = (theme: Partial<Theme>, values: Values) => ({
  ...theme, ...values, paletteLight: JSON.parse(String(values.paletteLight || '{}')), paletteDark: JSON.parse(String(values.paletteDark || '{}')),
});

export const themesApp = crudApp<Theme>({
  id: 'coworkee.admin.themes', group: 'System', title: 'Themes', icon: 'icon-palette.png', accent: '#c2185b',
  permissions: ['Themes.Manage'],
  load: async ({ request }) => ({ items: await request<Theme[]>('/api/v1/themes') }),
  columns: [
    { field: 'name', title: 'Name' },
    { field: 'isDefault', title: 'Default theme', format: (theme) => check(theme.isDefault), html: true },
    { field: 'isPublished', title: 'Published', format: (theme) => check(theme.isPublished), html: true },
    { field: 'isGlobal', title: 'System', format: (theme) => check(theme.isGlobal), html: true },
  ],
  edit: { fields: themeFields, values: themeValues, save: (theme, values, { request }) => request(`/api/v1/themes/${theme.id}`, json('PUT', themeRequest(theme, values))) },
  remove: { when: (theme) => !theme.isDefault, run: (theme, { request }) => request(`/api/v1/themes/${theme.id}`, json('DELETE')) },
  actions: [{ title: 'Make default', icon: '&#x2605;', visible: (theme) => !theme.isDefault, run: (theme, { request }) => request(`/api/v1/themes/${theme.id}/default`, json('POST')) }],
  toolbar: [
    {
      title: 'New theme', primary: true,
      run: async (coworkee) => {
        const builtIn = await coworkee.request<Theme[]>('/api/v1/themes/built-in');
        return formDialog(coworkee, {
          title: coworkee.t('New theme'),
          fields: [{ name: 'name', label: 'Name', required: true }, { name: 'base', label: 'Theme', type: 'select', options: choices(builtIn, (theme) => theme.name, (theme) => theme.name) }],
          onSubmit: (values) => {
            const base = builtIn.find((theme) => theme.name === values.base) ?? builtIn[0];
            return coworkee.request('/api/v1/themes', json('POST', { ...base, name: values.name, isPublished: true }));
          },
        });
      },
    },
    {
      title: 'Apply to Keycloak',
      run: async (coworkee) => {
        const status = await coworkee.request<Schemas['KeycloakThemeStatusDto']>('/api/v1/themes/keycloak/apply', json('POST'));
        coworkee.notify(status.error ?? coworkee.t(status.configured ? 'Keycloak sign-in page updated' : 'The connection failed'), status.error || !status.configured ? 'error' : 'info');
        return false;
      },
    },
  ],
});

export const backupsApp = crudApp<Backup>({
  id: 'coworkee.admin.backups', group: 'System', title: 'Backups', icon: 'fn-planning.png', accent: '#455a64',
  permissions: ['Backups.Manage'], hostOnly: true,
  load: async ({ request }) => ({ items: await request<Backup[]>('/api/v1/backups') }),
  columns: [
    { field: 'name', title: 'Name' }, { field: 'createdAt', title: 'Created' }, { field: 'tables', title: 'Tables' },
    { field: 'size', title: 'Size', format: (backup) => `${(Number(backup.size) / 1048576).toFixed(1)} MB` }, { field: 'migration', title: 'Migration' },
  ],
  toolbar: [{ title: 'Create backup', primary: true, run: ({ request }) => request('/api/v1/backups', json('POST')) }],
  remove: { run: (backup, { request }) => request(`/api/v1/backups/${backup.id}`, json('DELETE')) },
  actions: [
    { title: 'Download', icon: '&#x2b07;', run: async (backup) => { window.open(`/api/v1/backups/${backup.id}/download`, '_blank'); return false; } },
    {
      title: 'Restore', icon: '&#x21ba;',
      run: async (backup, coworkee) => await confirmDialog(coworkee, coworkee.t('Replace every table with the backup {0}? Changes made since then are lost.', backup.name), coworkee.t('Restore'))
        ? coworkee.request(`/api/v1/backups/${backup.id}/restore`, json('POST')) : false,
    },
  ],
});

export const servicesApp = crudApp<Schemas['ServiceDto']>({
  id: 'coworkee.admin.services', group: 'System', title: 'Services', icon: 'fn-workflow.png', accent: '#2e7d32',
  permissions: ['Settings.Manage'], hostOnly: true,
  load: async ({ request }) => ({ items: (await request<Schemas['ServiceDirectoryResult']>('/api/v1/services')).services }),
  columns: [
    { field: 'title', title: 'Name' },
    { field: 'url', title: 'Address', format: (service) => `<a href="${esc(service.url)}" target="_blank" rel="noopener">${esc(service.url)}</a>`, html: true },
    { field: 'health', title: 'Status', format: (service, { t }) => t(service.health) }, { field: 'source', title: 'App' },
  ],
});

const featureFields = async (coworkee: Coworkee): Promise<Field[]> =>
  (await coworkee.request<Schemas['FeatureGroupDto'][]>('/api/v1/features/definitions')).flatMap((group) => group.features.map((feature) => ({
    name: `feature:${feature.name}`, label: feature.displayName, hint: feature.description ?? undefined, type: feature.type === 'Bool' ? 'bool' : feature.type === 'Int' ? 'number' : 'text',
  } as Field)));
const featureValues = (values: Record<string, string | null | undefined>, fields: Field[]) =>
  Object.fromEntries(fields.map((field) => [field.name, field.type === 'bool' ? values[field.name.slice(8)] === 'true' : values[field.name.slice(8)]]));
const featureRequest = (values: Values) => Object.fromEntries(Object.entries(values).filter(([name, value]) => name.startsWith('feature:') && value !== null).map(([name, value]) => [name.slice(8), String(value)]));

export const editionsApp = crudApp<Edition>({
  id: 'coworkee.admin.editions', group: 'System', title: 'Editions', icon: 'ratio-cash.png', accent: '#f57f17',
  permissions: ['Features.Editions'], hostOnly: true,
  load: async ({ request }) => ({ items: await request<Edition[]>('/api/v1/editions') }),
  columns: [{ field: 'name', title: 'Name' }, { field: 'description', title: 'Description' }, { field: 'tenantCount', title: 'Tenants' }],
  create: {
    fields: async (coworkee) => [{ name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' }, ...await featureFields(coworkee)],
    save: (values, { request }) => request('/api/v1/editions', json('POST', { name: values.name, description: values.description, values: featureRequest(values) })),
  },
  edit: {
    fields: async (coworkee) => [{ name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' }, ...await featureFields(coworkee)],
    values: async (edition, coworkee) => ({ ...edition, ...featureValues(edition.values as Record<string, string>, await featureFields(coworkee)) }),
    save: (edition, values, { request }) => request(`/api/v1/editions/${edition.id}`, json('PUT', { name: values.name, description: values.description, values: featureRequest(values) })),
  },
  remove: { when: (edition) => Number(edition.tenantCount) === 0, run: (edition, { request }) => request(`/api/v1/editions/${edition.id}`, json('DELETE')) },
});

const tenantFields: Field[] = [
  { name: 'name', label: 'Name', required: true }, { name: 'identifier', label: 'Identifier', required: true },
  { name: 'isActive', label: 'Active', type: 'bool' }, { name: 'acceptsRegistrations', label: 'Registration', type: 'bool' },
];

export const tenantsApp = crudApp<Tenant>({
  id: 'coworkee.admin.tenants', group: 'System', title: 'Tenants', icon: 'icon-desktop.png', accent: '#283593',
  permissions: ['Features.Tenants'], hostOnly: true,
  search: true,
  load: async (_, { page, pageSize }) => {
    const result = await odataPage<Record<string, unknown>>('Tenants', { top: pageSize, skip: (page - 1) * pageSize, count: true, orderBy: 'Name' });
    // OData names properties like the entity (Name, IsActive), the API like the contracts (name, isActive)
    const camel = (row: Record<string, unknown>) => Object.fromEntries(Object.entries(row).map(([key, value]) => [key.charAt(0).toLowerCase() + key.slice(1), value])) as Tenant;
    return { items: result.items.map(camel), total: result.count };
  },
  columns: [
    { field: 'name', title: 'Name' }, { field: 'identifier', title: 'Identifier' },
    { field: 'isActive', title: 'Active', format: (tenant) => check(tenant.isActive), html: true },
    { field: 'acceptsRegistrations', title: 'Registration', format: (tenant) => check(tenant.acceptsRegistrations), html: true },
  ],
  create: {
    values: { isActive: true },
    fields: [...tenantFields, { name: 'adminEmail', label: 'Admin email', type: 'email' }, { name: 'adminPassword', label: 'Admin password', type: 'password' }],
    save: (values, { request }) => request('/api/v1/tenants', json('POST', values)),
  },
  edit: { fields: tenantFields, save: (tenant, values, { request }) => request(`/api/v1/tenants/${tenant.id}`, json('PUT', values)) },
  actions: [{
    title: 'Features', icon: '&#x2699;',
    run: async (tenant, coworkee) => {
      const [editions, details, fields] = await Promise.all([
        coworkee.request<Edition[]>('/api/v1/editions'),
        coworkee.request<Schemas['TenantDetailsDto'][]>('/api/v1/tenants/details', json('POST', { ids: [tenant.id] })),
        featureFields(coworkee),
      ]);
      const detail = details[0];
      await formDialog(coworkee, {
        title: coworkee.t('Features of {0}', tenant.name),
        fields: [{ name: 'editionId', label: 'Edition', type: 'select', options: [{ value: '', label: coworkee.t('No edition') }, ...choices(editions, (edition) => edition.id, (edition) => edition.name)] }, ...fields],
        values: { editionId: detail?.editionId ?? '', ...featureValues((detail?.overrides ?? {}) as Record<string, string>, fields) },
        onSubmit: (values) => coworkee.request(`/api/v1/tenants/${tenant.id}/features`, json('PUT', { editionId: values.editionId || null, overrides: featureRequest(values) })),
      });
      return false;
    },
  }],
});

const changesTable = (coworkee: Coworkee, changes: Schemas['AuditChangeDto'][]) => `<table class="cw-table"><thead><tr><th>${esc(coworkee.t('Field'))}</th>
  <th>${esc(coworkee.t('Changes'))}</th></tr></thead><tbody>${changes.map((change) =>
  `<tr><td>${esc(change.property)}</td><td>${esc(change.oldValue)} &rarr; ${esc(change.newValue)}</td></tr>`).join('')}</tbody></table>`;

export const auditApp = crudApp<Schemas['AuditEntryDto']>({
  id: 'coworkee.admin.audit', group: 'Monitoring', title: 'Audit log', icon: 'fn-reporttable.png', accent: '#5d4037', width: 960,
  permissions: ['Audit.View'],
  search: true, serverSearch: true,
  load: async ({ request }, { page, pageSize, search }) => {
    const result = await request<Schemas['PagedResultOfAuditEntryDto']>(`/api/v1/audit?${paged(page, pageSize, { EntityType: search })}`);
    return { items: result.items, total: Number(result.totalCount) };
  },
  columns: [
    { field: 'occurredAt', title: 'When' }, { field: 'actorName', title: 'Who' }, { field: 'action', title: 'Action' },
    { field: 'entityType', title: 'Entity' }, { field: 'entityId', title: 'Key' },
  ],
  actions: [{
    title: 'Changes', icon: '&#x2139;',
    run: async (entry, coworkee) => { await infoDialog(coworkee, `${entry.entityType} ${entry.entityId}`, changesTable(coworkee, entry.changes), true); return false; },
  }],
});

export const aiToolCallsApp = crudApp<Schemas['AiToolCallDto']>({
  id: 'coworkee.admin.ai-tool-calls', group: 'Monitoring', title: 'AI tool calls', icon: 'pilot2.png', accent: '#6a1b9a', width: 960,
  permissions: ['Ai.Audit'], probe: '/api/v1/ai/status',
  search: true, serverSearch: true,
  load: async ({ request }, { page, pageSize, search }) => {
    const result = await request<Schemas['PagedResultOfAiToolCallDto']>(`/api/v1/ai/tool-calls?${paged(page, pageSize, { Search: search })}`);
    return { items: result.items, total: Number(result.totalCount) };
  },
  columns: [
    { field: 'at', title: 'When' }, { field: 'tool', title: 'Tool' }, { field: 'channel', title: 'Channel' },
    { field: 'succeeded', title: 'OK', format: (call) => check(call.succeeded), html: true }, { field: 'durationMs', title: 'Time', format: (call) => `${call.durationMs} ms` },
  ],
  actions: [{
    title: 'Details', icon: '&#x2139;',
    run: async (call, coworkee) => {
      await infoDialog(coworkee, call.tool, `<h3>${esc(coworkee.t('Input'))}</h3><pre class="cw-code">${esc(call.input)}</pre>
        ${call.output ? `<h3>${esc(coworkee.t('Result'))}</h3><pre class="cw-code">${esc(call.output)}</pre>` : ''}${call.error ? `<p class="cw-error">${esc(call.error)}</p>` : ''}`, true);
      return false;
    },
  }],
});

export const systemApps = [languagesApp, translationsApp, themesApp, backupsApp, servicesApp, editionsApp, tenantsApp, auditApp, aiToolCallsApp];
