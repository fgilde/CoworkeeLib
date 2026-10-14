import type { Schemas } from '@coworkee/client';

export type SettingGroup = Schemas['SettingGroupDto'];
export type SettingDefinition = Schemas['SettingDefinitionDto'];
export type SettingValue = Schemas['SettingValueDto'];
export type SettingScope = 'User' | 'Tenant' | 'Global';

/** Where each scope reads and writes its values. */
export const scopePaths: Record<SettingScope, { definitions: string; values: string }> = {
  User: { definitions: '/api/v1/settings/definitions/user', values: '/api/v1/settings/user' },
  Tenant: { definitions: '/api/v1/settings/definitions', values: '/api/v1/settings/tenant' },
  Global: { definitions: '/api/v1/settings/definitions', values: '/api/v1/settings/global' },
};

/** The groups with the settings that can be set in the scope; empty groups are left out, groups with an own page (AI, Mail) appear only there. */
export function groupsFor(groups: readonly SettingGroup[], scope: SettingScope, page?: string): SettingGroup[] {
  return groups
    .filter((group) => (group.page ?? undefined) === page)
    .map((group) => ({ ...group, settings: group.settings.filter((setting) => (setting.scopes as readonly string[]).includes(scope)) }))
    .filter((group) => group.settings.length > 0);
}

/** The value each setting shows: the stored one, else its default. */
export function valuesOf(definitions: readonly SettingDefinition[], stored: readonly SettingValue[]): Record<string, string> {
  const byName = new Map(stored.map((value) => [value.name, value]));
  return Object.fromEntries(definitions.map((definition) => {
    const value = byName.get(definition.name);
    return [definition.name, (value?.hasValue ? value.value : definition.defaultValue) ?? ''];
  }));
}

/** False while the setting's condition (another setting holding one of some values) is not met. */
export function isEnabled(definition: SettingDefinition, values: Record<string, string>): boolean {
  const condition = definition.enabledWhen;
  if (!condition) return true;
  const current = (values[condition.setting] ?? '').toLowerCase();
  return condition.values.some((value) => value.toLowerCase() === current);
}

/** Only what the user changed goes to the API, so untouched secrets and locked values stay as they are. */
export function changes(original: Record<string, string>, current: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(current).filter(([name, value]) => (original[name] ?? '') !== value));
}

/** Settings whose change needs the page to load again (theme mode, language). */
export const reloadSettings = ['Theme.Mode', 'Theme.Id', 'Localization.Culture'];
