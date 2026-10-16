const entities: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

/** Escapes text for HTML content and attribute values. */
export const esc = (value: unknown) => String(value ?? '').replace(/[&<>"']/g, (char) => entities[char]);

/** A file size for people: 0 B, 12 KB, 3.4 MB. */
export function bytes(size: number | string | null | undefined, locale?: string): string {
  const value = Number(size ?? 0);
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  const exponent = value > 0 ? Math.min(Math.floor(Math.log(value) / Math.log(1024)), units.length - 1) : 0;
  const scaled = value / 1024 ** exponent;
  return `${scaled.toLocaleString(locale, { maximumFractionDigits: exponent === 0 || scaled >= 10 ? 0 : 1 })} ${units[exponent]}`;
}

const isoDate = /^\d{4}-\d{2}-\d{2}(T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+-]\d{2}:\d{2})?)?$/;

/** A cell value for people: dates in the user's format, yes/no for booleans, numbers grouped, objects as JSON. */
export function display(value: unknown, locale?: string): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'boolean') return value ? '✓' : '–';
  if (typeof value === 'number') return value.toLocaleString(locale);
  if (typeof value === 'string' && isoDate.test(value)) {
    const date = new Date(value);
    if (!Number.isNaN(date.getTime())) return value.length <= 10 ? date.toLocaleDateString(locale) : date.toLocaleString(locale);
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

/** Shows a short message at the bottom of the screen. */
export function toast(message: string, kind: 'info' | 'error' = 'info') {
  const element = document.createElement('div');
  element.className = `cw-toast ${kind}`;
  element.setAttribute('role', kind === 'error' ? 'alert' : 'status');
  element.textContent = message;
  document.body.append(element);
  setTimeout(() => element.remove(), kind === 'error' ? 7000 : 3500);
}

export const errorText = (error: unknown) => error instanceof Error ? error.message : String(error);

/** A property or enum name for people: CreatedAt -> Created at, Last30Days -> Last 30 days. */
export const humanize = (value: string) => value.replace(/([a-z])([A-Z0-9])/g, '$1 $2').replace(/(\d)([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ ([A-Z])(?=[a-z])/g, (m) => m.toLowerCase());

/** Fields of a row nobody wants in a list: OData annotations, tenant and concurrency keys, long texts like image data. */
export const isNoise = (field: string, value: unknown) =>
  field.includes('@') || /^(TenantId|ConcurrencyStamp|RowVersion)$/i.test(field) || (typeof value === 'string' && value.length > 200);

/** What a record is called: its name, title, number, code or e-mail, otherwise its key. */
export function recordTitle(row: Record<string, unknown>): string {
  const key = ['Name', 'Title', 'DisplayName', 'Number', 'Code', 'Email', 'FileName'].find((name) => typeof row[name] === 'string' && row[name]);
  return String(key ? row[key] : row.Id ?? row.id ?? '');
}
