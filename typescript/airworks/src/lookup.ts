import type { Translate } from '@coworkee/client';
import { esc } from './dom.ts';

export type LookupItem = { value: string; label: string };

/** Search with paging on the server for a field that picks several items, e.g. users out of thousands. */
export type LookupSource = {
  search: (text: string, page: number) => Promise<{ items: LookupItem[]; total: number }>;
  /** Names of the items a form starts with (only ids are stored). */
  resolve?: (ids: string[]) => Promise<Record<string, string>>;
};

const chip = (item: LookupItem, t: Translate) =>
  `<span class="cw-chip" data-value="${esc(item.value)}">${esc(item.label)}<button type="button" data-remove-chip aria-label="${esc(t('Remove'))} ${esc(item.label)}">&times;</button></span>`;

/** Values are ids, or items with their names where the form already knows them. */
export function lookupHtml(name: string, label: string, values: ReadonlyArray<string | LookupItem>, t: Translate): string {
  return `<fieldset class="cw-field cw-lookup" data-lookup="${esc(name)}"><legend>${esc(t(label))}</legend>
    <div class="cw-chips">${values.map((value) => chip(typeof value === 'string' ? { value, label: value } : value, t)).join('')}</div>
    <input type="search" placeholder="${esc(t('Search'))}" aria-label="${esc(t('Search'))} ${esc(t(label))}"/>
    <ul class="cw-lookup-results" role="listbox" aria-label="${esc(t(label))}"></ul>
    <button type="button" data-more hidden>${esc(t('More'))}</button></fieldset>`;
}

export const lookupValues = (root: ParentNode, name: string) =>
  [...root.querySelectorAll<HTMLElement>(`[data-lookup="${CSS.escape(name)}"] .cw-chip`)].map((element) => element.dataset.value!);

/** Brings a lookup field to life: names of the starting items, search as you type, more pages, add and remove. */
export function wireLookup(root: ParentNode, name: string, source: LookupSource, t: Translate) {
  const field = root.querySelector<HTMLElement>(`[data-lookup="${CSS.escape(name)}"]`);
  if (!field) return;
  const chips = field.querySelector<HTMLElement>('.cw-chips')!;
  const input = field.querySelector('input')!;
  const results = field.querySelector('ul')!;
  const more = field.querySelector<HTMLButtonElement>('[data-more]')!;
  let page = 1;
  let text = '';
  let shown = 0;
  const selected = () => new Set(lookupValues(field, name));

  const ids = [...chips.querySelectorAll<HTMLElement>('.cw-chip')].filter((element) => element.firstChild!.textContent === element.dataset.value).map((element) => element.dataset.value!);
  if (ids.length > 0 && source.resolve) {
    void source.resolve(ids).then((names) => chips.querySelectorAll<HTMLElement>('.cw-chip').forEach((element) => {
      const label = names[element.dataset.value!];
      if (label) element.firstChild!.textContent = label;
    })).catch(() => undefined);
  }

  const load = async (append: boolean) => {
    const result = await source.search(text, page);
    const taken = selected();
    const items = result.items.map((item) => `<li role="option" aria-selected="${taken.has(item.value)}"><button type="button" data-pick="${esc(item.value)}">${esc(item.label)}</button></li>`).join('');
    results.innerHTML = append ? results.innerHTML + items : items;
    shown = (append ? shown : 0) + result.items.length;
    more.hidden = shown >= result.total;
  };
  let timer: ReturnType<typeof setTimeout> | undefined;
  input.addEventListener('input', () => {
    clearTimeout(timer);
    timer = setTimeout(() => { text = input.value; page = 1; void load(false); }, 250);
  });
  input.addEventListener('keydown', (event) => { if (event.key === 'Enter') event.preventDefault(); });
  input.addEventListener('focus', () => { if (!results.childElementCount) void load(false); }, { once: true });
  more.addEventListener('click', () => { page++; void load(true); });
  field.addEventListener('click', (event) => {
    const target = event.target as Element;
    const pick = target.closest<HTMLElement>('[data-pick]');
    if (pick && !selected().has(pick.dataset.pick!)) {
      chips.insertAdjacentHTML('beforeend', chip({ value: pick.dataset.pick!, label: pick.textContent ?? pick.dataset.pick! }, t));
      pick.parentElement!.setAttribute('aria-selected', 'true');
    }
    if (target.closest('[data-remove-chip]')) target.closest('.cw-chip')!.remove();
  });
}
