import type { AppContext } from '@fgilde/airworks';
import type { AppFactory, Coworkee, CoworkeeApp } from '../../coworkee.ts';
import { esc } from '../../dom.ts';
import { aiSettingsApp } from '../assistant.ts';
import { identityApps } from './identity.ts';
import { mailApps } from './mail.ts';
import { systemApps } from './system.ts';

export const administrationAppId = 'coworkee.administration';
const sections = ['Identity', 'System', 'Communication', 'Localization', 'Monitoring'];
/** No user holds it: the administration app stays out of sight while none of its apps is allowed. */
const nobody = 'Coworkee.Administration.None';

const allowed = (coworkee: Coworkee) => coworkee.apps.filter((app) => app.group && !app.hidden && (app.requiredPermissions ?? []).every(coworkee.can));

/** Every administration app the user may open, grouped like the administration menu of the web app. */
export const administrationApp = (coworkee: Coworkee): CoworkeeApp => {
  const definition = (visible: boolean): CoworkeeApp => ({
    id: administrationAppId, title: coworkee.t('Administration'), icon: 'startmenu-settings.png', accent: '#35496b', width: 760, height: 560, singleInstance: true,
    requiredPermissions: visible ? [] : [nobody],
    render: ({ host }: AppContext) => {
      const { t } = coworkee;
      const apps = allowed(coworkee);
      host.classList.add('cw-host');
      host.innerHTML = `<div class="cw-app cw-admin">${[...sections, ...new Set(apps.map((app) => app.group!))].filter((section, index, all) => all.indexOf(section) === index)
        .map((section) => [section, apps.filter((app) => app.group === section)] as const).filter(([, items]) => items.length > 0)
        .map(([section, items]) => `<section><h3>${esc(t(section))}</h3><div class="cw-tiles">${items.map((app) =>
          `<button data-open="${esc(app.id)}" style="--cw-tile:${esc(app.accent ?? '#35496b')}"><img src="${esc(/[/:]/.test(app.icon) ? app.icon : `/icons/${app.icon}`)}" alt=""/><span>${esc(app.title)}</span></button>`).join('')}</div></section>`).join('')
        || `<p class="cw-muted">${esc(t('Nothing found.'))}</p>`}</div>`;
      host.addEventListener('click', (event) => {
        const id = (event.target as Element).closest<HTMLElement>('[data-open]')?.dataset.open;
        if (id) coworkee.airWorks.openApp(id);
      });
    },
  });
  // hidden until the access of the signed-in user is known; every change of the access decides again
  let visible = false;
  const update = () => {
    const now = allowed(coworkee).length > 0;
    if (now === visible) return;
    visible = now;
    coworkee.airWorks.unregisterApp(administrationAppId);
    coworkee.airWorks.registerApp(definition(now));
  };
  coworkee.airWorks.on('access-changed', update);
  return definition(false);
};

/** The administration: users, groups, roles, applications, scopes, API keys, themes, languages, translations, mail, backups, audit and more. */
export const adminApps: AppFactory[] = [administrationApp, ...identityApps, ...systemApps, ...mailApps, aiSettingsApp];

export * from './identity.ts';
export * from './mail.ts';
export * from './system.ts';
