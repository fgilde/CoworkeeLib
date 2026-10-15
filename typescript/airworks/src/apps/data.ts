import { entitySets } from '@coworkee/client';
import type { AppContext } from '@fgilde/airworks';
import type { Coworkee, CoworkeeApp } from '../coworkee.ts';
import { errorText, esc } from '../dom.ts';
import { icons } from '../icons.ts';
import { renderEntityTable, type EntityTableOptions } from '../table.ts';

/** Every OData entity set of the app the user may read, each as a table. */
export const dataApp = (coworkee: Coworkee): CoworkeeApp => ({
  id: 'coworkee.data',
  title: coworkee.t('Data'),
  icon: 'fn-reporttable.png',
  accent: '#167d79',
  width: 980,
  height: 620,
  probe: '/odata',
  render: async ({ host, intent, setState }: AppContext) => {
    const { t } = coworkee;
    host.classList.add('cw-host');
    const sets = (await entitySets()).sort((a, b) => a.name.localeCompare(b.name));
    host.innerHTML = `<div class="cw-app cw-data"><nav class="cw-list">${sets.map((set) =>
      `<button data-set="${esc(set.name)}">${esc(t(set.name))}</button>`).join('') || `<p class="cw-muted">${esc(t('Nothing found.'))}</p>`}</nav><section class="cw-panel"></section></div>`;
    const panel = host.querySelector<HTMLElement>('.cw-panel')!;
    const open = (name: string) => {
      host.querySelectorAll<HTMLElement>('[data-set]').forEach((button) => button.setAttribute('aria-current', String(button.dataset.set === name)));
      setState({ set: name }, t(name));
      try { renderEntityTable(coworkee, panel, { entitySet: name }); } catch (error) { panel.innerHTML = `<p class="cw-error">${esc(errorText(error))}</p>`; }
    };
    host.querySelector('nav')!.addEventListener('click', (event) => {
      const set = (event.target as Element).closest<HTMLElement>('[data-set]')?.dataset.set;
      if (set) open(set);
    });
    const first = intent?.set && sets.some((set) => set.name === intent.set) ? intent.set : sets[0]?.name;
    if (first) open(first);
  },
});

export type EntityAppOptions = EntityTableOptions & {
  title?: string;
  icon?: string;
  accent?: string;
  requiredPermissions?: string[];
};

/** One entity set as its own desktop app, e.g. entityApp({ entitySet: 'Products', requiredPermissions: ['Catalog.Products.View'] }). */
export const entityApp = (options: EntityAppOptions) => (coworkee: Coworkee): CoworkeeApp => ({
  id: `coworkee.entity.${options.entitySet}`,
  title: coworkee.t(options.title ?? options.entitySet),
  icon: options.icon ?? icons.table,
  accent: options.accent ?? '#35496b',
  width: 900,
  height: 580,
  requiredPermissions: options.requiredPermissions,
  render: ({ host, onClose }: AppContext) => {
    host.classList.add('cw-host');
    host.innerHTML = '<div class="cw-app"></div>';
    renderEntityTable(coworkee, host.firstElementChild as HTMLElement, options, onClose);
  },
});
