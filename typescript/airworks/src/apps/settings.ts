import type { Schemas } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { errorText, esc } from '../dom.ts';
import {
  changes, groupsFor, isEnabled, reloadSettings, scopePaths, valuesOf, type SettingDefinition, type SettingGroup, type SettingScope, type SettingValue,
} from '../settings-model.ts';

const manage = 'Settings.Manage';

function scopes(coworkee: Coworkee): Array<[SettingScope, string]> {
  const result: Array<[SettingScope, string]> = [['User', coworkee.t('My settings')]];
  if (coworkee.can(manage)) result.push(['Tenant', coworkee.t('Organisation')]);
  if (coworkee.can(manage) && coworkee.user.systemTenant) result.push(['Global', coworkee.t('System')]);
  return result;
}

function field(coworkee: Coworkee, setting: SettingDefinition, value: string, languages: Schemas['LanguageDto'][]): string {
  const { t } = coworkee;
  const name = esc(setting.name);
  const id = `cw-setting-${name.replace(/\W/g, '-')}`;
  const label = `<label for="${id}">${esc(t(setting.displayName))}</label>`;
  const hint = setting.description ? `<small>${esc(t(setting.description))}</small>` : '';
  const option = (choice: string, text: string) => `<option value="${esc(choice)}" ${choice === value ? 'selected' : ''}>${esc(text)}</option>`;
  let input: string;
  switch (setting.type) {
    case 'Bool':
      return `<div class="cw-field cw-check"><input id="${id}" type="checkbox" data-setting="${name}" ${value.toLowerCase() === 'true' ? 'checked' : ''}/>${label}${hint}</div>`;
    case 'Text':
      input = `<textarea id="${id}" data-setting="${name}" rows="4">${esc(value)}</textarea>`;
      break;
    case 'Int':
      input = `<input id="${id}" type="number" data-setting="${name}" value="${esc(value)}"/>`;
      break;
    case 'Secret':
      input = `<input id="${id}" type="password" autocomplete="new-password" data-setting="${name}" value="${esc(value)}"/>`;
      break;
    case 'Choice':
      input = `<select id="${id}" data-setting="${name}">${(setting.choices ?? []).map((choice) => option(choice, t(choice))).join('')}</select>`;
      break;
    case 'Language':
      input = `<select id="${id}" data-setting="${name}">${option('', t('Default'))}${languages.filter((language) => language.isEnabled).map((language) => option(language.culture, language.name)).join('')}</select>`;
      break;
    default:
      input = `<input id="${id}" type="text" data-setting="${name}" value="${esc(value)}"/>`;
  }
  return `<div class="cw-field">${label}${input}${hint}</div>`;
}

/** A button next to Save that checks the values on the form before they are saved, e.g. a test mail or a call to the AI model. */
export type SettingsTest = { title: string; run: (coworkee: Coworkee, values: Record<string, string>, changed: Record<string, string>) => Promise<string> };

async function renderScope(coworkee: Coworkee, panel: HTMLElement, scope: SettingScope, page?: string, test?: SettingsTest) {
  const { t, request } = coworkee;
  panel.innerHTML = `<p class="cw-muted">${esc(t('MudDataGrid_Loading'))}</p>`;
  const [groups, stored, languages] = await Promise.all([
    request<SettingGroup[]>(scopePaths[scope].definitions),
    request<SettingValue[]>(scopePaths[scope].values),
    request<Schemas['LanguageDto'][]>('/api/v1/localization/languages').catch(() => []),
  ]);
  const visible = groupsFor(groups, scope, page);
  const definitions = visible.flatMap((group) => group.settings);
  const original = valuesOf(definitions, stored);
  const current = { ...original };
  panel.innerHTML = `<form class="cw-settings-form">${visible.map((group) => `<fieldset><legend>${esc(t(group.displayName))}</legend>
      ${group.settings.map((setting) => field(coworkee, setting, original[setting.name], languages)).join('')}</fieldset>`).join('')
      || `<p class="cw-muted">${esc(t('Nothing found.'))}</p>`}
    <div class="cw-actions">${test ? `<button type="button" data-test>${esc(t(test.title))}</button>` : ''}<button type="submit" class="cw-primary">${esc(t('Save'))}</button></div></form>`;
  const form = panel.querySelector('form')!;
  const inputs = [...form.querySelectorAll<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>('[data-setting]')];
  const sync = () => {
    for (const input of inputs) current[input.dataset.setting!] = input instanceof HTMLInputElement && input.type === 'checkbox' ? String(input.checked) : input.value;
    for (const input of inputs) input.disabled = !isEnabled(definitions.find((definition) => definition.name === input.dataset.setting)!, current);
  };
  sync();
  form.addEventListener('input', sync);
  form.querySelector('[data-test]')?.addEventListener('click', async () => {
    try { coworkee.notify(await test!.run(coworkee, current, changes(original, current))); } catch (error) { coworkee.notify(errorText(error), 'error'); }
  });
  form.addEventListener('submit', async (event) => {
    event.preventDefault();
    const changed = changes(original, current);
    if (Object.keys(changed).length === 0) return;
    try {
      await request(scopePaths[scope].values, { method: 'PUT', body: JSON.stringify({ values: changed }) });
      Object.assign(original, changed);
      coworkee.notify(t('Saved'));
      if (scope === 'User' && Object.keys(changed).some((name) => reloadSettings.includes(name))) location.reload();
    } catch (error) {
      coworkee.notify(errorText(error), 'error');
    }
  });
}

/** Tabs per scope over one settings form. */
export function renderSettings(coworkee: Coworkee, host: HTMLElement, tabs: Array<[SettingScope, string]>, page?: string, test?: SettingsTest) {
  host.classList.add('cw-host');
  host.innerHTML = `<div class="cw-app cw-settings"><nav class="cw-tabs" role="tablist">${tabs.map(([scope, title], index) =>
    `<button role="tab" data-scope="${scope}" aria-selected="${index === 0}">${esc(title)}</button>`).join('')}</nav><section class="cw-panel"></section></div>`;
  const panel = host.querySelector<HTMLElement>('.cw-panel')!;
  const show = (scope: SettingScope) => {
    host.querySelectorAll<HTMLElement>('[data-scope]').forEach((tab) => tab.setAttribute('aria-selected', String(tab.dataset.scope === scope)));
    renderScope(coworkee, panel, scope, page, test).catch((error) => { panel.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; });
  };
  host.querySelector('nav')!.addEventListener('click', (event) => {
    const tab = (event.target as Element).closest<HTMLElement>('[data-scope]');
    if (tab) show(tab.dataset.scope as SettingScope);
  });
  if (tabs[0]) show(tabs[0][0]);
}

/** The settings of the user and, for administrators, of the organisation and the system, built from the setting definitions. */
export const settingsApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.settings',
  title: coworkee.t('Settings'),
  icon: 'fn-preferences.png',
  accent: '#555b66',
  width: 760,
  height: 620,
  singleInstance: true,
  render: ({ host }: AppContext) => renderSettings(coworkee, host, scopes(coworkee)),
});

export type SettingsPageOptions = {
  id: string; title: string; icon: string; accent?: string; page: string; group?: string; hostOnly?: boolean; probe?: string; test?: SettingsTest;
  /** Scopes the page offers, of those the user may manage (default: organisation and system). */
  scopes?: SettingScope[];
};

/** The settings groups that have an own page (AI, mail) as an app for administrators: organisation and, for the system organisation, system values. */
export const settingsPageApp = (options: SettingsPageOptions) => (coworkee: Coworkee): CoworkeeApp => ({
  id: options.id,
  title: coworkee.t(options.title),
  icon: options.icon,
  accent: options.accent ?? '#555b66',
  width: 760,
  height: 640,
  singleInstance: true,
  requiredPermissions: [manage],
  hostOnly: options.hostOnly,
  probe: options.probe,
  group: options.group,
  render: ({ host }: AppContext) => renderSettings(coworkee, host, scopes(coworkee).filter(([scope]) => (options.scopes ?? ['Tenant', 'Global']).includes(scope)), options.page, options.test),
});