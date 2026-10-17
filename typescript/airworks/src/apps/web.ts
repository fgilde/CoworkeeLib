import type { AppContext } from '@fgilde/airworks';
import type { AppFactory, Coworkee, CoworkeeApp } from '../coworkee.ts';
import { esc } from '../dom.ts';
import { icons } from '../icons.ts';

export const webPageAppId = 'coworkee.web-page';

/** The address of a page of the app's web client in the embedded shell (no app bar, no menu); undefined while the BFF does not know the web client. */
export function embedUrl(appUrl: string | null | undefined, path: string): string | undefined {
  if (!appUrl) return undefined;
  const url = new URL(path, appUrl);
  url.searchParams.set('embed', '1');
  return url.href;
}

/** A page of the web client in the window, with a way out into a browser tab when the page asks for a sign-in the frame cannot show. */
function frame(coworkee: Coworkee, host: HTMLElement, path: string, title: string) {
  const url = embedUrl(coworkee.user.appUrl, path)!;
  host.classList.add('cw-host');
  host.innerHTML = `<div class="cw-app cw-web"><iframe src="${esc(url)}" title="${esc(title)}"></iframe>
    <a class="cw-web-out" href="${esc(new URL(path, coworkee.user.appUrl!).href)}" target="_blank" rel="noopener" title="${esc(coworkee.t('Open in a new tab'))}" aria-label="${esc(coworkee.t('Open in a new tab'))}">&#x2197;</a></div>`;
}

/**
 * A page of the web client as a desktop app, e.g. embedApp({ id: "shop.orders", title: "Sales orders", path: "/sales/orders" }):
 * the Blazor page with all its tools (facets, query builder, history) in a window instead of a rebuild. Left out while the BFF does not know the web client.
 */
export const embedApp = (options: EmbedOptions) =>
  (coworkee: Coworkee): CoworkeeApp[] => coworkee.user.appUrl ? [{
    id: options.id, title: coworkee.t(options.title), icon: options.icon ?? icons.web, accent: options.accent ?? '#35496b',
    width: options.width ?? 1100, height: options.height ?? 700, requiredPermissions: options.requiredPermissions, group: options.group,
    render: ({ host }: AppContext) => frame(coworkee, host, options.path, coworkee.t(options.title)),
  }] : [];

export type EmbedOptions = { id: string; title: string; path: string; icon?: string; accent?: string; width?: number; height?: number; requiredPermissions?: string[]; group?: string };

/** The page of the web client (the full editor) while the BFF knows the web client, otherwise the lighter app built here. */
export const embedOr = (options: EmbedOptions, fallback: AppFactory): AppFactory => (coworkee) =>
  coworkee.user.appUrl ? embedApp(options)(coworkee) : fallback(coworkee);

/** Any page of the web client in a window, opened with the intent { path, title }: links of notifications and approvals. */
export const webPageApp = (coworkee: Coworkee): CoworkeeApp[] => coworkee.user.appUrl ? [{
  id: webPageAppId, title: coworkee.title, icon: icons.web, accent: '#35496b', width: 1100, height: 700, hidden: true,
  render: ({ host, intent, onIntent, setTitle, setState }: AppContext) => {
    const show = (target?: Record<string, string>) => {
      if (!target?.path) return;
      if (target.title) setTitle(target.title);
      setState({ path: target.path, ...(target.title ? { title: target.title } : {}) }, target.title);
      frame(coworkee, host, target.path, target.title ?? coworkee.title);
    };
    onIntent(show);
    show(intent);
  },
}] : [];

/** Opens a page of the web client (a local path like /catalog/products) in a window, or in a browser tab without the embedded shell. */
export function openWebPage(coworkee: Coworkee, path: string, title?: string) {
  if (!coworkee.user.appUrl) return;
  if (coworkee.airWorks.openApp(webPageAppId, { path, ...(title ? { title } : {}) }) === false) window.open(new URL(path, coworkee.user.appUrl).href, '_blank', 'noopener');
}
