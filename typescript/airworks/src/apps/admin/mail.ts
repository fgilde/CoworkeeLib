import type { Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../../coworkee.ts';
import { choices, crudApp, renderCrud } from '../../crud.ts';
import { display, errorText, esc } from '../../dom.ts';
import { confirmDialog, formDialog, infoDialog, type Field, type Values } from '../../form.ts';
import { settingsPageApp } from '../settings.ts';
import { userLookup } from './identity.ts';

type Template = Schemas['MailTemplateSummaryDto'];
type Campaign = Schemas['MailCampaignDto'];
type RecipientList = Schemas['RecipientListDto'];
type Source = Schemas['RecipientSourceDto'];

const mail = '/api/v1/mail';
const json = (method: string, body?: unknown): RequestInit => ({ method, body: body === undefined ? undefined : JSON.stringify(body) });
const quiet = <T>(promise: Promise<T>, fallback: T) => promise.catch(() => fallback);
export const mailTemplateAppId = 'coworkee.admin.mail-template';
export const mailLogAppId = 'coworkee.admin.mail-log';

const presets = ['AllActive', 'NotSignedInSince', 'NeverSignedIn', 'SignedUpWithin', 'EmailNotConfirmed', 'InRoles', 'Inactive'];
const presetLabels: Record<string, string> = {
  AllActive: 'All active users', NotSignedInSince: 'Not signed in for more than N days', NeverSignedIn: 'Never signed in', SignedUpWithin: 'Signed up in the last N days',
  EmailNotConfirmed: 'E-mail not confirmed', InRoles: 'Members of roles', Inactive: 'Deactivated users',
};

/** The fields that describe who gets a mail: a preset (with days or roles), an OData filter over the users or picked users. */
async function sourceFields(coworkee: Coworkee): Promise<Field[]> {
  const { t, request } = coworkee;
  const roles = await quiet(request<Schemas['RoleDto'][]>('/api/v1/identity/roles'), []);
  return [
    { name: 'kind', label: 'Recipients', type: 'select', options: [{ value: 'Preset', label: t('Smart list') }, { value: 'Query', label: t('Custom query') }, { value: 'Manual', label: t('Manual selection') }] },
    { name: 'preset', label: 'Smart list', type: 'select', options: presets.map((value) => ({ value, label: t(presetLabels[value]) })) },
    { name: 'days', label: 'Days', type: 'number' },
    { name: 'roleIds', label: 'Roles', type: 'multi', options: choices(roles, (role) => role.id, (role) => role.name) },
    { name: 'filter', label: 'Custom query', hint: 'Filter' },
    { name: 'userIds', label: 'Users', type: 'lookup', lookup: userLookup(coworkee) },
  ];
}

/** The recipient source the API takes from the form values: only the fields of the chosen kind. */
export const sourceOf = (values: Values): Source => ({
  kind: (values.kind ?? 'Preset') as Source['kind'],
  preset: values.kind === 'Preset' ? (values.preset ?? 'AllActive') as Source['preset'] : null,
  days: values.kind === 'Preset' ? values.days as number | null : null,
  roleIds: values.kind === 'Preset' ? values.roleIds as string[] : null,
  filter: values.kind === 'Query' ? values.filter as string | null : null,
  userIds: values.kind === 'Manual' ? values.userIds as string[] : null,
});

const sourceValues = (source?: Source | null): Values => ({ kind: 'Preset', preset: 'AllActive', ...(source ?? {}) });

/** How many users a source reaches, without those who unsubscribed, and the first of them. */
export async function previewRecipients(coworkee: Coworkee, source: Source) {
  const { t, culture } = coworkee;
  const preview = await coworkee.request<Schemas['RecipientPreviewDto']>(`${mail}/recipients/preview`, json('POST', source));
  await infoDialog(coworkee, t('Recipients'), `<p><strong>${esc(t('{0} recipients', preview.count))}</strong>${Number(preview.excluded) > 0 ? `<br/>${esc(t('{0} users excluded (no e-mail, locked, deactivated or unsubscribed)', preview.excluded))}` : ''}</p>
    <table class="cw-table"><thead><tr><th>${esc(t('Email'))}</th><th>${esc(t('Name'))}</th><th>${esc(t('Last sign-in'))}</th></tr></thead><tbody>${preview.first.map((row) =>
      `<tr><td>${esc(row.email)}</td><td>${esc(row.displayName)}</td><td>${esc(display(row.lastLoginAt, culture))}</td></tr>`).join('')}</tbody></table>`, true);
  return false;
}

export const mailTemplatesApp = crudApp<Template>({
  id: 'coworkee.admin.mail-templates', group: 'Communication', title: 'Mail templates', icon: 'fn-ideaportal.png', accent: '#00838f',
  permissions: ['Mail.Templates.Manage'],
  search: true,
  load: async ({ request }) => ({ items: await request<Template[]>(`${mail}/templates`) }),
  columns: [
    { field: 'displayName', title: 'Name', format: (template, { t }) => t(template.displayName) }, { field: 'name', title: 'Key' },
    { field: 'cultures', title: 'Languages', format: (template) => template.cultures.map((culture) => template.overriddenCultures.includes(culture) ? `${culture}*` : culture).join(', ') },
  ],
  create: {
    fields: async ({ request }) => [
      { name: 'displayName', label: 'Name', required: true }, { name: 'name', label: 'Key', required: true, hint: 'Letters, digits and . _ : - only' },
      { name: 'description', label: 'Description', type: 'textarea' },
      { name: 'copyFrom', label: 'Template', type: 'select', options: [{ value: '', label: '' }, ...choices(await request<Template[]>(`${mail}/templates`), (template) => template.name, (template) => template.displayName)] },
    ],
    save: (values, { request }) => request(`${mail}/templates`, json('POST', { ...values, copyFrom: values.copyFrom || null, global: false })),
  },
  open: (template, coworkee) => coworkee.airWorks.openApp(mailTemplateAppId, { name: template.name }),
  remove: { when: (template) => template.isCustom, run: (template, { request }) => request(`${mail}/templates/${encodeURIComponent(template.name)}`, json('DELETE')) },
  actions: [{
    title: 'Edit', icon: '&#x270e;',
    run: async (template, coworkee) => { coworkee.airWorks.openApp(mailTemplateAppId, { name: template.name }); return false; },
  }],
});

/** Subject and body of a template in one language with a live preview, a test mail to oneself and the way back to the default. */
export const mailTemplateApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: mailTemplateAppId, title: coworkee.t('Template'), icon: 'fn-ideaportal.png', accent: '#00838f', width: 1080, height: 680, hidden: true,
  requiredPermissions: ['Mail.Templates.Manage'],
  render: async ({ host, intent, setTitle }: AppContext) => {
    const { t, request } = coworkee;
    const name = intent?.name ?? '';
    host.classList.add('cw-host');
    const summary = (await request<Template[]>(`${mail}/templates`)).find((template) => template.name === name);
    const cultures = summary?.cultures ?? [intent?.culture ?? 'en'];
    const chosen = cultures.find((culture) => culture === intent?.culture) ?? cultures.find((culture) => coworkee.culture.startsWith(culture)) ?? cultures.find((culture) => culture === 'en') ?? cultures[0];
    host.innerHTML = `<div class="cw-app cw-split"><form class="cw-stack">
        <div class="cw-toolbar"><select aria-label="${esc(t('Language'))}">${cultures.map((culture) =>
          `<option ${culture === chosen ? 'selected' : ''}>${esc(culture)}</option>`).join('')}</select><span class="cw-spacer"></span>
          <button type="button" data-action="reset">${esc(t('Reset'))}</button><button type="button" data-action="test">${esc(t('Send test mail'))}</button>
          <button type="submit" class="cw-primary">${esc(t('Save'))}</button></div>
        <div class="cw-field"><label for="cw-mt-subject">${esc(t('Subject'))}</label><input id="cw-mt-subject" name="subject"/></div>
        <div class="cw-field cw-grow"><label for="cw-mt-body">${esc(t('Body'))}</label><textarea id="cw-mt-body" name="body" class="cw-code"></textarea></div>
      </form><iframe class="cw-mail-preview" title="${esc(t('Preview'))}" sandbox=""></iframe></div>`;
    const form = host.querySelector('form')!;
    const culture = host.querySelector('select')!;
    const subject = form.querySelector<HTMLInputElement>('[name=subject]')!;
    const body = form.querySelector<HTMLTextAreaElement>('[name=body]')!;
    const frame = host.querySelector('iframe')!;
    const path = () => `${mail}/templates/${encodeURIComponent(name)}/${encodeURIComponent(culture.value)}`;
    const preview = async () => {
      try {
        const rendered = await request<Schemas['RenderedMailDto']>(`${path()}/preview`, json('POST', { subject: subject.value, body: body.value }));
        frame.srcdoc = `<h3 style="font-family:sans-serif">${esc(rendered.subject)}</h3>${rendered.htmlBody}`;
      } catch (error) { frame.srcdoc = `<p style="color:#c62828">${esc(errorText(error))}</p>`; }
    };
    const load = async () => {
      const template = await request<Schemas['MailTemplateDto']>(path());
      subject.value = template.subject;
      body.value = template.body;
      setTitle(`${t(summary?.displayName ?? name)} (${culture.value})`);
      await preview();
    };
    let timer: ReturnType<typeof setTimeout> | undefined;
    form.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(preview, 500); });
    culture.addEventListener('change', () => void load());
    const run = async (work: () => Promise<unknown>, done: string) => { try { await work(); coworkee.notify(t(done)); } catch (error) { coworkee.notify(errorText(error), 'error'); } };
    form.addEventListener('submit', (event) => { event.preventDefault(); void run(() => request(path(), json('PUT', { subject: subject.value, body: body.value })), 'Saved'); });
    host.querySelector('[data-action=test]')!.addEventListener('click', () => void run(() => request(`${path()}/test`, json('POST')), 'Sent'));
    host.querySelector('[data-action=reset]')!.addEventListener('click', async () => {
      if (await confirmDialog(coworkee, `${t('Reset to default')}?`, t('Reset'))) await run(async () => { await request(path(), json('DELETE')); await load(); }, 'Saved');
    });
    await load();
  },
});

const campaignFields = async (coworkee: Coworkee): Promise<Field[]> => {
  const [templates, lists] = await Promise.all([
    coworkee.request<Template[]>(`${mail}/templates`),
    quiet(coworkee.request<RecipientList[]>(`${mail}/recipient-lists`), []),
  ]);
  return [
    { name: 'name', label: 'Name', required: true },
    { name: 'templateName', label: 'Template', type: 'select', options: choices(templates, (template) => template.name, (template) => coworkee.t(template.displayName)) },
    { name: 'list', label: 'Recipient lists', type: 'select', options: [{ value: '', label: '' }, ...choices(lists, (list) => list.id, (list) => list.name)] },
    ...await sourceFields(coworkee),
  ];
};

const campaignRequest = async (coworkee: Coworkee, values: Values) => {
  const list = values.list ? (await coworkee.request<RecipientList[]>(`${mail}/recipient-lists`)).find((item) => item.id === values.list) : undefined;
  return { name: values.name, templateName: values.templateName, recipients: list?.source ?? sourceOf(values) };
};

const editable = (campaign: Campaign) => campaign.status === 'Draft';

export const mailCampaignsApp = crudApp<Campaign>({
  id: 'coworkee.admin.mail-campaigns', group: 'Communication', title: 'Mail campaigns', icon: 'fn-dashboard.png', accent: '#d84315', width: 980,
  permissions: ['Mail.Campaigns.Send'],
  search: true,
  load: async ({ request }) => ({ items: await request<Campaign[]>(`${mail}/campaigns`) }),
  columns: [
    { field: 'name', title: 'Name' }, { field: 'templateName', title: 'Template' }, { field: 'status', title: 'Status', format: (campaign, { t }) => t(campaign.status) },
    { field: 'scheduledFor', title: 'Date' },
    { field: 'sent', title: 'Sent', format: (campaign) => `${campaign.sent} / ${campaign.total}${Number(campaign.failed) > 0 ? ` (${campaign.failed} !)` : ''}` },
  ],
  create: { fields: campaignFields, values: sourceValues(), save: async (values, coworkee) => coworkee.request(`${mail}/campaigns`, json('POST', await campaignRequest(coworkee, values))) },
  edit: {
    fields: campaignFields, when: editable,
    values: (campaign) => ({ ...campaign, ...sourceValues(campaign.recipients) }),
    save: async (campaign, values, coworkee) => coworkee.request(`${mail}/campaigns/${campaign.id}`, json('PUT', await campaignRequest(coworkee, values))),
  },
  remove: { when: (campaign) => campaign.status !== 'Sending', run: (campaign, { request }) => request(`${mail}/campaigns/${campaign.id}`, json('DELETE')) },
  actions: [
    { title: 'Recipients', icon: '&#x1f465;', run: (campaign, coworkee) => previewRecipients(coworkee, campaign.recipients) },
    {
      title: 'Send test mail', icon: '&#x2709;', visible: editable,
      run: async (campaign, coworkee) => { await coworkee.request(`${mail}/campaigns/${campaign.id}/test`, json('POST')); coworkee.notify(coworkee.t('Sent')); return false; },
    },
    {
      title: 'Schedule', icon: '&#x1f680;', visible: editable,
      run: (campaign, coworkee) => formDialog(coworkee, {
        title: `${coworkee.t('Schedule')} - ${campaign.name}`, submit: coworkee.t('Send'),
        fields: [{ name: 'at', label: 'Date', type: 'date', hint: 'Send now' }],
        onSubmit: (values) => coworkee.request(`${mail}/campaigns/${campaign.id}/schedule`, json('POST', { at: values.at ?? null })),
      }),
    },
    {
      title: 'Cancel', icon: '&#x23f9;', visible: (campaign) => campaign.status === 'Scheduled' || campaign.status === 'Sending',
      run: (campaign, { request }) => request(`${mail}/campaigns/${campaign.id}/cancel`, json('POST')),
    },
    { title: 'Mail log', icon: '&#x1f4dc;', visible: (campaign) => !editable(campaign), run: async (campaign, coworkee) => { coworkee.airWorks.openApp(mailLogAppId, { campaignId: campaign.id, campaign: campaign.name }); return false; } },
  ],
});

export const recipientListsApp = crudApp<RecipientList>({
  id: 'coworkee.admin.recipient-lists', group: 'Communication', title: 'Recipient lists', icon: 'ratio-personal.png', accent: '#d84315',
  permissions: ['Mail.Campaigns.Send'],
  search: true,
  load: async ({ request }) => ({ items: await request<RecipientList[]>(`${mail}/recipient-lists`) }),
  columns: [{ field: 'name', title: 'Name' }, { field: 'source', title: 'Recipients', format: (list, { t }) => t(list.source.kind === 'Preset' ? presetLabels[list.source.preset ?? 'AllActive'] : list.source.kind) }],
  create: {
    values: sourceValues(),
    fields: async (coworkee) => [{ name: 'name', label: 'Name', required: true }, ...await sourceFields(coworkee)],
    save: (values, { request }) => request(`${mail}/recipient-lists`, json('POST', { name: values.name, source: sourceOf(values) })),
  },
  edit: {
    fields: async (coworkee) => [{ name: 'name', label: 'Name', required: true }, ...await sourceFields(coworkee)],
    values: (list) => ({ name: list.name, ...sourceValues(list.source) }),
    save: (list, values, { request }) => request(`${mail}/recipient-lists/${list.id}`, json('PUT', { name: values.name, source: sourceOf(values) })),
  },
  remove: { run: (list, { request }) => request(`${mail}/recipient-lists/${list.id}`, json('DELETE')) },
  actions: [{ title: 'Recipients', icon: '&#x1f465;', run: (list, coworkee) => previewRecipients(coworkee, list.source) }],
});

const statuses = ['', 'Queued', 'Sending', 'Sent', 'Failed', 'Skipped'];

/** The mails the app sent or queued; opened from a campaign it shows that campaign's mails. */
export const mailLogApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: mailLogAppId, group: 'Communication', title: coworkee.t('Mail log'), icon: 'fn-reporttable.png', accent: '#00838f', width: 980, height: 600,
  requiredPermissions: ['Mail.Log.View'],
  render: ({ host, intent, onClose, setTitle }: AppContext) => {
    const { t } = coworkee;
    host.classList.add('cw-host');
    host.innerHTML = `<div class="cw-app cw-stack"><div class="cw-toolbar"><label for="cw-ml-status">${esc(t('Status'))}</label>
      <select id="cw-ml-status">${statuses.map((status) => `<option value="${status}">${esc(status ? t(status) : t('All'))}</option>`).join('')}</select></div><div class="cw-grow"></div></div>`;
    if (intent?.campaign) setTitle(`${t('Mail log')} - ${intent.campaign}`);
    const select = host.querySelector('select')!;
    const show = () => renderCrud<Schemas['OutgoingMailDto']>(coworkee, host.querySelector<HTMLElement>('.cw-grow')!, {
      id: 'mail-log', title: 'Mail log', icon: '', search: true, serverSearch: true,
      load: async ({ request }, { page, pageSize, search }) => {
        const query = new URLSearchParams({ Page: String(page), PageSize: String(pageSize) });
        if (search) query.set('Search', search);
        if (select.value) query.set('status', select.value);
        if (intent?.campaignId) query.set('campaignId', intent.campaignId);
        const result = await request<Schemas['PagedResultOfOutgoingMailDto']>(`${mail}/outgoing?${query}`);
        return { items: result.items, total: Number(result.totalCount) };
      },
      columns: [
        { field: 'queuedAt', title: 'Queued' }, { field: 'to', title: 'To' }, { field: 'subject', title: 'Subject' },
        { field: 'status', title: 'Status', format: (row, { t }) => t(row.status) }, { field: 'attempts', title: 'Attempts' }, { field: 'sentAt', title: 'Sent' },
      ],
      actions: [{
        title: 'Details', icon: '&#x26a0;', visible: (row) => !!row.lastError,
        run: async (row, coworkee) => { await infoDialog(coworkee, row.subject, `<pre class="cw-code">${esc(row.lastError)}</pre>`); return false; },
      }],
    }, onClose);
    select.addEventListener('change', show);
    show();
  },
});

export const mailSettingsApp = settingsPageApp({
  id: 'coworkee.admin.mail-settings', group: 'Communication', title: 'Mail settings', icon: 'startmenu-settings.png', accent: '#00838f', page: '/admin/mail-settings',
  test: {
    title: 'Test connection',
    run: async (coworkee, _, changed) => {
      const result = await coworkee.request<Schemas['MailConnectionTestDto']>(`${mail}/connection/test`, json('POST', { values: changed, cleared: null }));
      if (!result.success) throw new Error(result.error ?? coworkee.t('Failed'));
      return coworkee.t('Succeeded');
    },
  },
});

export const mailApps = [mailTemplatesApp, mailTemplateApp, mailCampaignsApp, recipientListsApp, mailLogApp, mailSettingsApp];
