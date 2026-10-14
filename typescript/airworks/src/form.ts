import type { Coworkee } from './coworkee.ts';
import { errorText, esc } from './dom.ts';

export type FieldType = 'text' | 'textarea' | 'number' | 'bool' | 'select' | 'multi' | 'date' | 'password' | 'list' | 'email';

/** A field of a form: list holds one text per line, multi several options, date a date and time. */
export type Field = {
  name: string;
  label: string;
  type?: FieldType;
  options?: Array<{ value: string; label: string }>;
  required?: boolean;
  hint?: string;
  readOnly?: boolean;
  rows?: number;
};

export type Values = Record<string, unknown>;

export type FormOptions = {
  title: string;
  fields: Field[];
  values?: Values;
  submit?: string;
  /** Runs on submit; a failure stays in the dialog, so the user can correct the input. */
  onSubmit?: (values: Values) => Promise<unknown>;
};

const localDate = (value: unknown) => {
  if (!value) return '';
  const date = new Date(String(value));
  return Number.isNaN(date.getTime()) ? '' : new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
};

export function fieldHtml(field: Field, value: unknown, t: Coworkee['t']): string {
  const id = `cw-f-${field.name}-${Math.random().toString(36).slice(2, 8)}`;
  const common = `id="${id}" name="${esc(field.name)}" ${field.required ? 'required' : ''} ${field.readOnly ? 'disabled' : ''}`;
  const label = `<label for="${id}">${esc(t(field.label))}${field.required ? ' *' : ''}</label>`;
  const hint = field.hint ? `<small>${esc(t(field.hint))}</small>` : '';
  const text = value === null || value === undefined ? '' : String(value);
  switch (field.type) {
    case 'bool':
      return `<div class="cw-field cw-check"><input type="checkbox" ${common} ${value ? 'checked' : ''}/>${label}${hint}</div>`;
    case 'textarea':
      return `<div class="cw-field">${label}<textarea ${common} rows="${field.rows ?? 5}">${esc(text)}</textarea>${hint}</div>`;
    case 'list':
      return `<div class="cw-field">${label}<textarea ${common} rows="${field.rows ?? 3}">${esc(Array.isArray(value) ? value.join('\n') : text)}</textarea>${hint}</div>`;
    case 'select':
      return `<div class="cw-field">${label}<select ${common}>${(field.options ?? []).map((option) =>
        `<option value="${esc(option.value)}" ${option.value === text ? 'selected' : ''}>${esc(option.label)}</option>`).join('')}</select>${hint}</div>`;
    case 'multi': {
      const chosen = new Set(Array.isArray(value) ? value.map(String) : []);
      return `<fieldset class="cw-field cw-multi" data-multi="${esc(field.name)}"><legend>${esc(t(field.label))}</legend>${(field.options ?? []).map((option) =>
        `<label><input type="checkbox" value="${esc(option.value)}" ${chosen.has(option.value) ? 'checked' : ''} ${field.readOnly ? 'disabled' : ''}/> ${esc(option.label)}</label>`).join('')
        || `<span class="cw-muted">${esc(t('Nothing found.'))}</span>`}${hint}</fieldset>`;
    }
    case 'date':
      return `<div class="cw-field">${label}<input type="datetime-local" ${common} value="${esc(localDate(value))}"/>${hint}</div>`;
    case 'number':
      return `<div class="cw-field">${label}<input type="number" step="any" ${common} value="${esc(text)}"/>${hint}</div>`;
    case 'password':
      return `<div class="cw-field">${label}<input type="password" autocomplete="new-password" ${common} value="${esc(text)}"/>${hint}</div>`;
    case 'email':
      return `<div class="cw-field">${label}<input type="email" ${common} value="${esc(text)}"/>${hint}</div>`;
    default:
      return `<div class="cw-field">${label}<input type="text" ${common} value="${esc(text)}"/>${hint}</div>`;
  }
}

/** Reads the fields back in their types: numbers, booleans, string lists, dates as ISO text, empty texts as null. */
export function readFields(root: ParentNode, fields: readonly Field[]): Values {
  const values: Values = {};
  for (const field of fields) {
    if (field.type === 'multi') {
      values[field.name] = [...root.querySelectorAll<HTMLInputElement>(`[data-multi="${CSS.escape(field.name)}"] input:checked`)].map((input) => input.value);
      continue;
    }
    const input = root.querySelector<HTMLInputElement>(`[name="${CSS.escape(field.name)}"]`);
    if (!input) continue;
    const raw = input.value;
    values[field.name] = field.type === 'bool' ? input.checked
      : field.type === 'number' ? (raw === '' ? null : Number(raw))
      : field.type === 'list' ? raw.split(/\r?\n|,/).map((line) => line.trim()).filter(Boolean)
      : field.type === 'date' ? (raw ? new Date(raw).toISOString() : null)
      : raw.trim() === '' ? null : raw;
  }
  return values;
}

function open(html: string): HTMLDialogElement {
  const dialog = document.createElement('dialog');
  dialog.className = 'cw-app cw-dialog';
  dialog.innerHTML = html;
  document.body.append(dialog);
  dialog.addEventListener('close', () => dialog.remove());
  dialog.showModal();
  return dialog;
}

/** A form in a modal dialog; resolves with the values once onSubmit succeeded, undefined when cancelled. */
export function formDialog(coworkee: Coworkee, options: FormOptions): Promise<Values | undefined> {
  const { t } = coworkee;
  return new Promise((resolve) => {
    const dialog = open(`<form method="dialog"><h2>${esc(options.title)}</h2><div class="cw-dialog-body">
        ${options.fields.map((field) => fieldHtml(field, options.values?.[field.name], t)).join('')}</div>
      <p class="cw-error" role="alert"></p>
      <div class="cw-actions"><button type="button" data-cancel>${esc(t('Cancel'))}</button><button type="submit" class="cw-primary">${esc(options.submit ?? t('Save'))}</button></div></form>`);
    let result: Values | undefined;
    dialog.addEventListener('close', () => resolve(result));
    dialog.querySelector('[data-cancel]')!.addEventListener('click', () => dialog.close());
    const form = dialog.querySelector('form')!;
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const values = readFields(form, options.fields);
      const button = form.querySelector<HTMLButtonElement>('[type=submit]')!;
      button.disabled = true;
      try {
        await options.onSubmit?.(values);
        result = values;
        dialog.close();
      } catch (error) {
        form.querySelector('.cw-error')!.textContent = errorText(error);
      } finally {
        button.disabled = false;
      }
    });
  });
}

/** A message with OK and Cancel; resolves true on OK. */
export function confirmDialog(coworkee: Coworkee, message: string, ok?: string): Promise<boolean> {
  const { t } = coworkee;
  return new Promise((resolve) => {
    const dialog = open(`<form method="dialog"><p>${esc(message)}</p><div class="cw-actions">
      <button value="cancel">${esc(t('Cancel'))}</button><button value="ok" class="cw-primary">${esc(ok ?? t('OK'))}</button></div></form>`);
    dialog.addEventListener('close', () => resolve(dialog.returnValue === 'ok'));
  });
}

/** Shows markup (already escaped by the caller) with a close button, e.g. a secret that is shown only once. */
export function infoDialog(coworkee: Coworkee, title: string, html: string, wide = false): Promise<void> {
  return new Promise((resolve) => {
    const dialog = open(`<form method="dialog"><h2>${esc(title)}</h2><div class="cw-dialog-body">${html}</div>
      <div class="cw-actions"><button class="cw-primary">${esc(coworkee.t('Close'))}</button></div></form>`);
    if (wide) dialog.classList.add('cw-wide');
    dialog.addEventListener('close', () => resolve());
  });
}

/** Asks for one text; resolves null when cancelled. */
export async function promptDialog(coworkee: Coworkee, title: string, label: string, value = ''): Promise<string | null> {
  const values = await formDialog(coworkee, { title, fields: [{ name: 'value', label, required: true }], values: { value } });
  return values ? String(values.value ?? '') : null;
}
