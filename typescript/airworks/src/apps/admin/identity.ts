import type { Schemas } from '@coworkee/client';
import type { Coworkee } from '../../coworkee.ts';
import { choices, crudApp } from '../../crud.ts';
import { display, esc } from '../../dom.ts';
import { formDialog, infoDialog, type Field, type Values } from '../../form.ts';

type User = Schemas['UserDto'];
type Role = Schemas['RoleDto'];
type Group = Schemas['GroupDto'];
type Client = Schemas['ClientDto'];
type Scope = Schemas['ScopeDto'];
type ApiKey = Schemas['ApiKeyDto'];

const identity = '/api/v1/identity';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
const quiet = <T>(promise: Promise<T>, fallback: T) => promise.catch(() => fallback);
const check = (value: unknown) => value ? '&#x2713;' : '';

const roleChoices = async (coworkee: Coworkee) => choices(await quiet(coworkee.request<Role[]>(`${identity}/roles`), []), (role) => role.id, (role) => role.name);
// shortcut: pickers offer the first 200 groups and users (the API's largest page), page through them when apps get bigger
const groupChoices = async (coworkee: Coworkee) =>
  choices((await quiet(coworkee.request<{ items: Group[] }>(`${identity}/groups?Page=1&PageSize=200`), { items: [] })).items, (group) => group.id, (group) => group.name);

/** The permissions of a role, user or group as one checkbox list per permission group. */
export async function editGrants(coworkee: Coworkee, providerType: 'Role' | 'User' | 'Group', key: string, title: string) {
  const path = `${identity}/permissions/grants/${providerType}/${encodeURIComponent(key)}`;
  const [groups, granted] = await Promise.all([coworkee.request<Schemas['PermissionGroupDto'][]>(`${identity}/permissions/definitions`), coworkee.request<string[]>(path)]);
  const fields: Field[] = groups.map((group) => ({ name: group.name, label: group.displayName, type: 'multi', options: group.permissions.map((p) => ({ value: p.name, label: coworkee.t(p.displayName) })) }));
  const values = Object.fromEntries(groups.map((group) => [group.name, group.permissions.map((p) => p.name).filter((name) => granted.includes(name))]));
  await formDialog(coworkee, {
    title: `${coworkee.t('Permissions')} - ${title}`, fields, values,
    onSubmit: (chosen) => coworkee.request(path, json('PUT', { names: Object.values(chosen).flat() })),
  });
  return false;
}

export const usersApp = crudApp<User>({
  id: 'coworkee.admin.users', group: 'Identity', title: 'Users', icon: 'startmenu-user.png', accent: '#1565c0', width: 920,
  permissions: ['Identity.Users.View'],
  search: true, serverSearch: true,
  load: async ({ request }, { page, pageSize, search }) => {
    const result = await request<Schemas['PagedResultOfUserDto']>(`${identity}/users?${new URLSearchParams({ Page: String(page), PageSize: String(pageSize), Search: search })}`);
    return { items: result.items, total: Number(result.totalCount) };
  },
  columns: [
    { field: 'email', title: 'Email' },
    { field: 'name', title: 'Name', format: (user) => [user.firstName, user.lastName].filter(Boolean).join(' ') },
    { field: 'roles', title: 'Roles', format: (user) => user.roles.map((role) => role.name).join(', ') },
    { field: 'isActive', title: 'Active', format: (user) => check(user.isActive), html: true },
  ],
  create: {
    permission: 'Identity.Users.Manage',
    values: { isActive: true },
    fields: async (coworkee) => [
      { name: 'email', label: 'Email', type: 'email', required: true },
      { name: 'firstName', label: 'First name' }, { name: 'lastName', label: 'Last name' },
      { name: 'password', label: 'Initial password', type: 'password' },
      { name: 'mustChangePassword', label: 'Must change password', type: 'bool' },
      { name: 'isActive', label: 'Active', type: 'bool' },
      { name: 'roleIds', label: 'Roles', type: 'multi', options: await roleChoices(coworkee) },
    ],
    save: (values, { request }) => request(`${identity}/users`, json('POST', values)),
  },
  edit: {
    permission: 'Identity.Users.Manage',
    fields: async (coworkee) => [
      { name: 'userName', label: 'User name' }, { name: 'firstName', label: 'First name' }, { name: 'lastName', label: 'Last name' },
      { name: 'phoneNumber', label: 'Phone' },
      { name: 'isActive', label: 'Active', type: 'bool' }, { name: 'emailConfirmed', label: 'Email confirmed', type: 'bool' },
      { name: 'mustChangePassword', label: 'Must change password', type: 'bool' },
      { name: 'roleIds', label: 'Roles', type: 'multi', options: await roleChoices(coworkee) },
      ...(coworkee.can('Identity.Groups.View') ? [{ name: 'groupIds', label: 'Groups', type: 'multi', options: await groupChoices(coworkee) } as Field] : []),
    ],
    values: async (user, { request }) => {
      const detail = await request<Schemas['UserDetailDto']>(`${identity}/users/${user.id}`);
      return { ...detail, roleIds: detail.roles.map((role) => role.id), groupIds: detail.groups.map((group) => group.id) };
    },
    save: async (user, values, coworkee) => {
      const detail = await coworkee.request<Schemas['UserDetailDto']>(`${identity}/users/${user.id}`);
      await coworkee.request(`${identity}/users/${user.id}`, json('PUT', {
        firstName: values.firstName, lastName: values.lastName, userName: values.userName, phoneNumber: values.phoneNumber, address: detail.address ?? null,
        isActive: values.isActive, emailConfirmed: values.emailConfirmed, mustChangePassword: values.mustChangePassword,
      }));
      await coworkee.request(`${identity}/users/${user.id}/roles`, json('PUT', { ids: values.roleIds }));
      if (values.groupIds) await coworkee.request(`${identity}/users/${user.id}/groups`, json('PUT', { ids: values.groupIds }));
    },
  },
  remove: { permission: 'Identity.Users.Manage', run: (user, { request }) => request(`${identity}/users/${user.id}`, json('DELETE')), name: (user) => user.email },
  actions: [
    {
      title: 'Permissions', icon: '&#x1f6e1;', permission: 'Identity.Permissions.Manage',
      run: (user, coworkee) => editGrants(coworkee, 'User', user.id, user.email),
    },
    {
      title: 'Set password', icon: '&#x1f511;', permission: 'Identity.Users.Manage',
      run: async (user, coworkee) => !!await formDialog(coworkee, {
        title: `${coworkee.t('Set password')} - ${user.email}`,
        fields: [{ name: 'password', label: 'New password', type: 'password', required: true }, { name: 'mustChangePassword', label: 'Must change password', type: 'bool' }],
        onSubmit: (values) => coworkee.request(`${identity}/users/${user.id}/password`, json('PUT', values)),
      }) && false,
    },
    { title: 'Lock', icon: '&#x1f512;', permission: 'Identity.Users.Manage', run: (user, { request }) => request(`${identity}/users/${user.id}/lock`, json('POST', { until: null })) },
    { title: 'Unlock', icon: '&#x1f513;', permission: 'Identity.Users.Manage', run: (user, { request }) => request(`${identity}/users/${user.id}/unlock`, json('POST')) },
    {
      title: 'Send password reset', icon: '&#x2709;', permission: 'Identity.Users.Manage',
      run: async (user, coworkee) => { await coworkee.request(`${identity}/users/${user.id}/password-reset`, json('POST')); coworkee.notify(coworkee.t('Password reset mail queued for {0}.', user.email)); return false; },
    },
    {
      title: 'Sign out everywhere', icon: '&#x23fb;', permission: 'Identity.Users.Manage',
      run: async (user, coworkee) => { await coworkee.request(`${identity}/users/${user.id}/sign-out`, json('POST')); coworkee.notify(coworkee.t('The user was signed out everywhere.')); return false; },
    },
  ],
  live: () => ['tenant:users'],
});

export const groupsApp = crudApp<Group>({
  id: 'coworkee.admin.groups', group: 'Identity', title: 'Groups', icon: 'ratio-personal.png', accent: '#6a1b9a',
  permissions: ['Identity.Groups.View'],
  search: true, serverSearch: true,
  load: async ({ request }, { page, pageSize, search }) => {
    const result = await request<Schemas['PagedResultOfGroupDto']>(`${identity}/groups?${new URLSearchParams({ Page: String(page), PageSize: String(pageSize), Search: search })}`);
    return { items: result.items, total: Number(result.totalCount) };
  },
  columns: [
    { field: 'name', title: 'Name' }, { field: 'description', title: 'Description' },
    { field: 'memberIds', title: 'Members', format: (group) => String(group.memberIds.length) },
  ],
  create: {
    permission: 'Identity.Groups.Manage',
    fields: [{ name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' }],
    save: (values, { request }) => request(`${identity}/groups`, json('POST', values)),
  },
  edit: {
    permission: 'Identity.Groups.Manage',
    fields: async (coworkee) => {
      const users = await quiet(coworkee.request<{ items: User[] }>(`${identity}/users?Page=1&PageSize=200`), { items: [] });
      return [
        { name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' },
        { name: 'memberIds', label: 'Members', type: 'multi', options: choices(users.items, (user) => user.id, (user) => user.email) },
        { name: 'roleIds', label: 'Roles', type: 'multi', options: await roleChoices(coworkee) },
      ];
    },
    save: async (group, values, { request }) => {
      await request(`${identity}/groups/${group.id}`, json('PUT', { name: values.name, description: values.description }));
      await request(`${identity}/groups/${group.id}/members`, json('PUT', { ids: values.memberIds }));
      await request(`${identity}/groups/${group.id}/roles`, json('PUT', { ids: values.roleIds }));
    },
  },
  remove: { permission: 'Identity.Groups.Manage', run: (group, { request }) => request(`${identity}/groups/${group.id}`, json('DELETE')) },
  actions: [{ title: 'Permissions', icon: '&#x1f6e1;', permission: 'Identity.Permissions.Manage', run: (group, coworkee) => editGrants(coworkee, 'Group', group.id, group.name) }],
});

const roleFields: Field[] = [
  { name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' },
  { name: 'selectableForRegistration', label: 'Selectable for registration', type: 'bool' },
];

export const rolesApp = crudApp<Role>({
  id: 'coworkee.admin.roles', group: 'Identity', title: 'Roles', icon: 'fn-abcAnalysis.png', accent: '#ad1457',
  permissions: ['Identity.Roles.View'],
  search: true,
  load: async ({ request }) => ({ items: await request<Role[]>(`${identity}/roles`) }),
  columns: [{ field: 'name', title: 'Name' }, { field: 'description', title: 'Description' }, { field: 'isSystem', title: 'System', format: (role) => check(role.isSystem), html: true }],
  create: { permission: 'Identity.Roles.Manage', fields: roleFields, save: (values, { request }) => request(`${identity}/roles`, json('POST', values)) },
  edit: { permission: 'Identity.Roles.Manage', fields: roleFields, when: (role) => !role.isSystem, save: (role, values, { request }) => request(`${identity}/roles/${role.id}`, json('PUT', values)) },
  remove: { permission: 'Identity.Roles.Manage', when: (role) => !role.isSystem, run: (role, { request }) => request(`${identity}/roles/${role.id}`, json('DELETE')) },
  actions: [{ title: 'Permissions', icon: '&#x1f6e1;', permission: 'Identity.Permissions.Manage', run: (role, coworkee) => editGrants(coworkee, 'Role', role.id, role.name) }],
});

const clientFields: Field[] = [
  { name: 'clientId', label: 'Client id', required: true, hint: 'Letters, digits and . _ : - only' }, { name: 'displayName', label: 'Display name' },
  { name: 'clientType', label: 'Type', type: 'select', options: ['public', 'confidential'].map((value) => ({ value, label: value })) },
  { name: 'consentType', label: 'Consent', type: 'select', options: ['implicit', 'explicit', 'external', 'systematic'].map((value) => ({ value, label: value })) },
  { name: 'redirectUris', label: 'Redirect addresses', type: 'list', hint: 'Full addresses like https://app.example/callback' },
  { name: 'postLogoutRedirectUris', label: 'Addresses after sign-out', type: 'list', hint: 'Full addresses like https://app.example/callback' },
  { name: 'grantTypes', label: 'Type', type: 'list', hint: 'Pick a suggestion or type your own' },
  { name: 'scopes', label: 'Scopes', type: 'list', hint: 'Pick a suggestion or type your own' },
  { name: 'clientUri', label: 'Address' }, { name: 'logoUrl', label: 'Logo address' }, { name: 'description', label: 'Description', type: 'textarea' },
  { name: 'showInLauncher', label: 'Show in the app launcher', type: 'bool', hint: 'The app launcher of the sign-in server links here' },
];

const showSecret = async (coworkee: Coworkee, result: Schemas['ClientSecretDto'] | undefined) => {
  if (result?.clientSecret) await infoDialog(coworkee, coworkee.t('Client secret'), `<p>${esc(coworkee.t('Copy the secret now, it is not shown again:'))}</p><pre class="cw-secret">${esc(result.clientSecret)}</pre>`);
};

export const clientsApp = crudApp<Client>({
  id: 'coworkee.admin.clients', group: 'Identity', title: 'Applications', icon: 'fn-userweblink.png', accent: '#00695c', width: 940,
  permissions: ['Identity.Clients.Manage'], hostOnly: true,
  search: true,
  load: async ({ request }) => ({ items: await request<Client[]>(`${identity}/clients`) }),
  columns: [
    { field: 'clientId', title: 'Client id' }, { field: 'displayName', title: 'Display name' }, { field: 'clientType', title: 'Type' },
    { field: 'managed', title: 'Service client', format: (client) => check(client.managed), html: true },
  ],
  create: {
    values: { clientType: 'public', consentType: 'implicit', grantTypes: ['authorization_code', 'refresh_token'], scopes: ['openid', 'profile', 'email', 'offline_access'] },
    fields: clientFields,
    save: async (values, coworkee) => showSecret(coworkee, await coworkee.request<Schemas['ClientSecretDto']>(`${identity}/clients`, json('POST', values))),
  },
  edit: {
    fields: clientFields, when: (client) => !client.managed,
    save: async (client, values, coworkee) => showSecret(coworkee, await coworkee.request<Schemas['ClientSecretDto']>(`${identity}/clients/${client.id}`, json('PUT', { ...client, ...values }))),
  },
  remove: { when: (client) => !client.managed, run: (client, { request }) => request(`${identity}/clients/${client.id}`, json('DELETE')), name: (client) => client.clientId },
  actions: [
    {
      title: 'New secret', icon: '&#x1f511;', visible: (client) => client.clientType === 'confidential' && !client.managed,
      run: async (client, coworkee) => { await showSecret(coworkee, await coworkee.request(`${identity}/clients/${client.id}/secret`, json('POST'))); return false; },
    },
    {
      title: 'Duplicate', icon: '&#x2398;', visible: (client) => !client.managed,
      run: async (client, coworkee) => !!await formDialog(coworkee, {
        title: coworkee.t('Duplicate'), fields: clientFields, values: { ...client, clientId: `${client.clientId}-copy` },
        onSubmit: async (values) => showSecret(coworkee, await coworkee.request(`${identity}/clients`, json('POST', values))),
      }),
    },
  ],
});

const scopeFields: Field[] = [
  { name: 'name', label: 'Name', required: true }, { name: 'displayName', label: 'Display name' },
  { name: 'description', label: 'Description', type: 'textarea' }, { name: 'resources', label: 'Resources', type: 'list', hint: 'Audiences of the access token, e.g. https://api.example' },
];

export const scopesApp = crudApp<Scope>({
  id: 'coworkee.admin.scopes', group: 'Identity', title: 'Scopes', icon: 'icon-percent.png', accent: '#4e342e',
  permissions: ['Identity.Clients.Manage'], hostOnly: true,
  search: true,
  load: async ({ request }) => ({ items: await request<Scope[]>(`${identity}/scopes`) }),
  columns: [
    { field: 'name', title: 'Name' }, { field: 'displayName', title: 'Display name' }, { field: 'resources', title: 'Resources', format: (scope) => scope.resources.join(', ') },
    { field: 'builtIn', title: 'built-in', format: (scope) => check(scope.builtIn), html: true },
  ],
  create: { fields: scopeFields, save: (values, { request }) => request(`${identity}/scopes`, json('POST', values)) },
  edit: { fields: scopeFields, when: (scope) => !scope.builtIn, save: (scope, values, { request }) => request(`${identity}/scopes/${scope.id}`, json('PUT', values)) },
  remove: { when: (scope) => !scope.builtIn, run: (scope, { request }) => request(`${identity}/scopes/${scope.id}`, json('DELETE')) },
});

const inDays = (days: number) => new Date(Date.now() + days * 86400000).toISOString();

export const apiKeysApp = crudApp<ApiKey>({
  id: 'coworkee.admin.api-keys', group: 'Identity', title: 'API keys', icon: 'login-password.png', accent: '#37474f', width: 900,
  permissions: ['ApiKeys.Manage'],
  load: async ({ request }) => ({ items: await request<ApiKey[]>(`${identity}/api-keys`) }),
  columns: [
    { field: 'name', title: 'Name' }, { field: 'prefix', title: 'Key' }, { field: 'scopes', title: 'Scopes', format: (key) => key.scopes.join(', ') },
    { field: 'expiresAt', title: 'Expires' }, { field: 'lastUsedAt', title: 'Last used' },
    { field: 'revokedAt', title: 'Revoked', format: (key, { culture }) => display(key.revokedAt, culture) },
  ],
  create: {
    values: { expiresAt: inDays(90) },
    fields: async ({ request, t }) => {
      const groups = await request<Schemas['PermissionGroupDto'][]>(`${identity}/api-keys/scopes`);
      return [
        { name: 'name', label: 'Name', required: true }, { name: 'description', label: 'Description', type: 'textarea' },
        { name: 'expiresAt', label: 'Expires', type: 'date', required: true },
        { name: 'scopes', label: 'Scopes', type: 'multi', options: groups.flatMap((group) => group.permissions.map((p) => ({ value: p.name, label: `${t(group.displayName)}: ${t(p.displayName)}` }))) },
      ];
    },
    save: async (values: Values, coworkee) => {
      const created = await coworkee.request<Schemas['CreatedApiKeyDto']>(`${identity}/api-keys`, json('POST', values));
      void infoDialog(coworkee, coworkee.t('API key'), `<p>${esc(coworkee.t('Copy the key now: it is shown only this once.'))}</p><pre class="cw-secret">${esc(created.secret)}</pre>`);
    },
  },
  actions: [{ title: 'Revoke', icon: '&#x26d4;', visible: (key) => !key.revokedAt, run: (key, { request }) => request(`${identity}/api-keys/${key.id}/revoke`, json('POST')) }],
});

export const identityApps = [usersApp, groupsApp, rolesApp, clientsApp, scopesApp, apiKeysApp];
