import { apiRequest, type CoworkeeClientOptions } from './client.ts';

/** English text → text in the user's language, as the API serves them (library texts, app texts, translations made in the app). */
export type Texts = Record<string, string>;

export type Translate = (key: string, ...args: unknown[]) => string;

export async function getTexts(culture: string, options?: CoworkeeClientOptions): Promise<Texts> {
  const result = await apiRequest<{ texts?: Texts }>(`/api/v1/localization/texts/${encodeURIComponent(culture)}`, {}, options);
  return result?.texts ?? {};
}

/** Fills {0}, {1}, ... like string.Format; format suffixes ({0:N0}) are ignored. */
export function format(text: string, args: readonly unknown[]): string {
  return args.length === 0 ? text : text.replace(/\{(\d+)(:[^}]*)?\}/g, (match, index: string) => index in args ? String(args[Number(index)]) : match);
}

/** Looks a key up, falls back to the key (the English text) and fills the placeholders. */
export const translator = (texts: Texts): Translate => (key, ...args) => format(texts[key] || key, args);

/** The user's culture: the setting Localization.Culture, else the browser's first language, else English. */
export function pickCulture(setting?: string | null, browser: readonly string[] = typeof navigator === 'object' ? navigator.languages ?? [] : []): string {
  return setting?.trim() || browser.find(Boolean) || 'en';
}
