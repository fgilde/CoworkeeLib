import {
  apiRequest, applyTheme, createCoworkeeClient, createRealtime, realtimeEvents, realtimeTopics, getCurrentTheme, getTexts, getUser, logoUrl, palette, pickCulture, resolveMode, translator,
  type BffUser, type ColorMode, type CoworkeeClient, type Realtime, type Texts, type Theme, type Translate,
} from '@coworkee/client';
import type { AirWorks, AirWorksOptions, AppDefinition } from '@fgilde/airworks';
import { bffAuthProvider } from './auth.ts';
import { toast } from './dom.ts';
import { defaultMudExFrameUrl, defaultMudExUrl } from './mudex.ts';

export type AirWorksApi = typeof AirWorks;

/**
 * An app of the desktop; with probe it is added only when that GET succeeds for the signed-in user (the module is part of the app),
 * with hostOnly only for users of the system organisation.
 */
export type CoworkeeApp = AppDefinition & {
  probe?: string;
  hostOnly?: boolean;
  /** Section of the administration app (Identity, System, ...); such apps get no desktop shortcut of their own. */
  group?: string;
  /** false: no desktop shortcut at the start (the app stays in the start menu). */
  shortcut?: boolean;
};

export type AppFactory = (coworkee: Coworkee) => CoworkeeApp | CoworkeeApp[];

export type CoworkeeOptions = {
  /** Shown in the start menu, on the sign-in card and in the browser tab. */
  title: string;
  /** Logo address used while the theme has none (the theme's logo wins). */
  logo?: string;
  description?: string;
  version?: string;
  /** Where the AirWorks icons are served (the template copies them to /icons/). */
  assetBase?: string;
  /** Address of the MudEx web components bundle for loadMudEx (components in the page itself). */
  mudExUrl?: string;
  /** The frame page next to the self-hosted MudEx bundle that file previews open (default /mudex/coworkee-frame.html). */
  mudExFrameUrl?: string;
  /** Further AirWorks options; they win over the ones derived here. */
  airWorks?: AirWorksOptions;
};

export type Coworkee = {
  airWorks: AirWorksApi;
  /** Typed client of the library API. */
  api: CoworkeeClient;
  /** The realtime hub: notifications, chat, presence and entity changes; connects on the first subscription. */
  realtime: Realtime;
  /** Any endpoint, the app's own included. */
  request: <T = unknown>(path: string, init?: RequestInit) => Promise<T>;
  t: Translate;
  culture: string;
  user: BffUser;
  theme?: Theme;
  mode: ColorMode;
  /** The settings the API hands to clients (Theme.Mode, Localization.Culture, ...). */
  settings: Record<string, string>;
  title: string;
  logo: string;
  mudExUrl: string;
  mudExFrameUrl: string;
  can: (permission: string) => boolean;
  notify: (message: string, kind?: 'info' | 'error') => void;
  /** The apps registered so far that the user may have (probes and host checks passed). */
  apps: CoworkeeApp[];
  /** Registers apps; factories get this object. */
  register: (...apps: AppFactory[]) => void;
};

/** English of the MudBlazor keys the apps borrow; the API serves them only for other languages. */
const mudTexts: Texts = {
  MudDataGridPager_InfoFormat: '{0}-{1} of {2}', MudTablePager_PreviousPage: 'Previous page', MudTablePager_NextPage: 'Next page', MudDataGrid_Loading: 'Loading...',
};

const quietly = async <T>(load: () => Promise<T>, fallback: T) => { try { return await load(); } catch { return fallback; } };

function setFavicon(href: string) {
  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
  if (!link) {
    link = document.createElement('link');
    link.rel = 'icon';
    document.head.append(link);
  }
  link.href = href;
}

type Notification = { title: string; arguments?: string[] | null };

/** Live parts every desktop needs: new notifications as toasts, permissions that changed, a session ended elsewhere. */
function follow(coworkee: Coworkee, userId: string) {
  const { realtime, airWorks, t } = coworkee;
  realtime.subscribe(realtimeTopics.user(userId), async ({ type, payload }) => {
    if (type === realtimeEvents.sessionRevoked) return void airWorks.logout();
    const change = payload as { entityType?: string; action?: string };
    if (type !== realtimeEvents.entityChanged || change.entityType !== 'Notification' || change.action !== 'Created') return;
    const latest = await quietly(() => apiRequest<{ items: Notification[] }>('/api/v1/notifications?Page=1&PageSize=1&UnreadOnly=true'), { items: [] });
    const item = latest.items[0];
    if (item) toast(t(item.title, ...(item.arguments ?? [])));
  });
  for (const topic of [realtimeTopics.tenantPermissions, realtimeTopics.globalPermissions]) realtime.subscribe(topic, () => void airWorks.refreshAccess());
}

/**
 * Connects AirWorks to the Coworkee app it is served by: sign-in through the BFF, permissions, theme, logo, texts.
 * Then register apps (defaultApps and your own) and add &lt;air-desktop&gt; to the page.
 */
export async function useCoworkee(airWorks: AirWorksApi, options: CoworkeeOptions): Promise<Coworkee> {
  const [user, theme] = await Promise.all([quietly(() => getUser(), { isAuthenticated: false, roles: [] } as BffUser), quietly(() => getCurrentTheme(), undefined)]);
  const settings = user.isAuthenticated ? await quietly(() => apiRequest<Record<string, string>>('/api/v1/settings/client'), {}) : {};
  const culture = pickCulture(settings['Localization.Culture']);
  const texts = await quietly(() => getTexts(culture), {} as Texts);
  const mode = resolveMode(settings['Theme.Mode']);
  const logo = logoUrl(theme?.logo) ?? options.logo ?? '/icons/logo-air.png';
  applyTheme(theme, mode);
  setFavicon(logo);
  document.title = options.title;

  const coworkee: Coworkee = {
    airWorks,
    api: createCoworkeeClient(),
    realtime: createRealtime(),
    request: (path, init) => apiRequest(path, init),
    t: translator({ ...mudTexts, ...texts }),
    culture,
    user,
    theme,
    mode,
    settings,
    title: options.title,
    logo,
    mudExUrl: options.mudExUrl ?? defaultMudExUrl,
    mudExFrameUrl: options.mudExFrameUrl ?? defaultMudExFrameUrl,
    can: (permission) => airWorks.can(permission),
    notify: toast,
    apps: [],
    register: async (...factories) => {
      const added = await Promise.all(factories.flatMap((factory) => [factory(coworkee)].flat()).map(add));
      if (!options.airWorks?.initialShortcuts) {
        shortcuts.push(...added.filter((app): app is CoworkeeApp => !!app && !app.hidden && !app.group && app.shortcut !== false).map((app) => app.id));
        airWorks.configure({ initialShortcuts: shortcuts });
      }
    },
  };

  const shortcuts: string[] = [];
  async function add(definition: CoworkeeApp): Promise<CoworkeeApp | undefined> {
    const { probe, hostOnly, group, shortcut, ...app } = definition;
    if (hostOnly && !user.systemTenant) return undefined;
    if (probe && !(user.isAuthenticated && await quietly(() => apiRequest(probe).then(() => true), false))) return undefined;
    airWorks.unregisterApp(app.id);
    airWorks.registerApp(app);
    const known = coworkee.apps.findIndex((item) => item.id === app.id);
    if (known >= 0) coworkee.apps[known] = definition; else coworkee.apps.push(definition);
    return definition;
  }

  airWorks.registerAuthProvider(bffAuthProvider({ title: options.title, icon: logo, user }));
  airWorks.configure({
    assetBase: options.assetBase ?? '/icons/',
    locale: culture.toLowerCase().startsWith('de') ? 'de' : 'en',
    requireLogin: true,
    request: coworkee.request,
    accessProvider: async (session) => ({ roles: session?.roles ?? [], permissions: await quietly(() => apiRequest<string[]>('/api/v1/identity/permissions/me'), []) }),
    brand: { title: options.title, logo, description: options.description, version: options.version },
    defaults: { systemColor: palette(theme, mode).primary },
    storageKey: `coworkee.airworks.${location.host}`,
    webLinks: true,
    ...options.airWorks,
  });
  if (user.isAuthenticated && user.userId) follow(coworkee, user.userId);
  return coworkee;
}
