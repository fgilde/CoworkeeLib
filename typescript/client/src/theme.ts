import { apiRequest, type CoworkeeClientOptions } from './client.ts';
import type { components } from './schema.ts';

export type Theme = components['schemas']['ThemeDto'];
export type ColorMode = 'light' | 'dark';

/** MudBlazor's defaults, which a theme's palette overrides key by key (themes store only what they change). */
const defaults: Record<ColorMode, Record<string, string>> = {
  light: {
    primary: '#594ae2', primaryContrastText: '#ffffff', secondary: '#ff4081', secondaryContrastText: '#ffffff', background: '#ffffff', surface: '#ffffff',
    textPrimary: '#424242', textSecondary: '#00000089', linesDefault: '#0000001e', appbarBackground: '#594ae2', appbarText: '#ffffff',
    error: '#f44336', success: '#00c853', warning: '#ff9800', info: '#2196f3',
  },
  dark: {
    primary: '#776be7', primaryContrastText: '#ffffff', secondary: '#ff4081', secondaryContrastText: '#ffffff', background: '#32333d', surface: '#373740',
    textPrimary: '#ffffffb2', textSecondary: '#ffffff80', linesDefault: '#ffffff1e', appbarBackground: '#27272f', appbarText: '#ffffffb2',
    error: '#f64e62', success: '#0bba83', warning: '#ffa800', info: '#3299ff',
  },
};

const variables: Array<[string, string]> = [
  ['primary', 'primary'], ['primary-text', 'primaryContrastText'], ['secondary', 'secondary'], ['secondary-text', 'secondaryContrastText'],
  ['background', 'background'], ['surface', 'surface'], ['text', 'textPrimary'], ['text-secondary', 'textSecondary'], ['lines', 'linesDefault'],
  ['appbar', 'appbarBackground'], ['appbar-text', 'appbarText'], ['error', 'error'], ['success', 'success'], ['warning', 'warning'], ['info', 'info'],
];

const camel = (key: string) => key.charAt(0).toLowerCase() + key.slice(1);

const record = (value: unknown): Record<string, unknown> => value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {};

/** The colors of a theme in one mode, MudBlazor's defaults filled in; keys in camel case (primary, textSecondary, ...). */
export function palette(theme: Theme | undefined, mode: ColorMode): Record<string, string> {
  const stored = record(mode === 'dark' ? theme?.paletteDark : theme?.paletteLight);
  return { ...defaults[mode], ...Object.fromEntries(Object.entries(stored).filter(([, value]) => typeof value === 'string').map(([key, value]) => [camel(key), value as string])) };
}

function fontOf(typography: unknown, name: string): string | undefined {
  const family = record(record(typography)[name]).FontFamily ?? record(record(typography)[camel(name)]).fontFamily;
  return Array.isArray(family) && family.length > 0 ? family.map((font) => /[\s"]/.test(String(font)) ? `"${String(font).replace(/"/g, '')}"` : String(font)).join(', ') : undefined;
}

/** CSS custom properties of the theme: --cw-primary, --cw-surface, --cw-text, ..., --cw-font, --cw-heading-font, --cw-radius. */
export function cssVariables(theme: Theme | undefined, mode: ColorMode): Record<string, string> {
  const colors = palette(theme, mode);
  const result: Record<string, string> = Object.fromEntries(variables.map(([name, key]) => [`--cw-${name}`, colors[key]]));
  const font = fontOf(theme?.typography, 'Default');
  if (font) result['--cw-font'] = font;
  const heading = fontOf(theme?.typography, 'H1') ?? font;
  if (heading) result['--cw-heading-font'] = heading;
  const layout = record(theme?.layoutProperties);
  const radius = layout.DefaultBorderRadius ?? layout.defaultBorderRadius;
  if (typeof radius === 'string' && radius) result['--cw-radius'] = radius;
  return result;
}

/** Light or dark: the setting Theme.Mode (light, dark, system) or the operating system's. */
export function resolveMode(setting?: string | null, prefersDark = typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches): ColorMode {
  return setting === 'dark' || setting === 'light' ? setting : prefersDark ? 'dark' : 'light';
}

/** An address for an img: SVG markup becomes a data URL, data URLs and addresses stay as they are. */
export function logoUrl(logo?: string | null): string | undefined {
  const value = logo?.trim();
  if (!value) return undefined;
  return value.startsWith('<') ? `data:image/svg+xml;charset=utf-8,${encodeURIComponent(value)}` : value;
}

export function applyTheme(theme: Theme | undefined, mode: ColorMode, root: HTMLElement = document.documentElement) {
  for (const [name, value] of Object.entries(cssVariables(theme, mode))) root.style.setProperty(name, value);
  root.style.colorScheme = mode;
  root.dataset.cwMode = mode;
}

/** The theme of the signed-in user's organisation, or the default one (anonymous). */
export const getCurrentTheme = (options?: CoworkeeClientOptions) => apiRequest<Theme>('/api/v1/themes/current', {}, options);
