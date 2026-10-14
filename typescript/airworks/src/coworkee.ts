import {
  apiRequest, applyTheme, createCoworkeeClient, getCurrentTheme, getTexts, getUser, logoUrl, palette, pickCulture, resolveMode, translator,
  type BffUser, type ColorMode, type CoworkeeClient, type Texts, type Theme, type Translate,
} from '@coworkee/client';
import type { AirWorks, AirWorksOptions, AppDefinition } from '@fgilde/airworks';
import { bffAuthProvider } from './auth.ts';
import { toast } from './dom.ts';
import { defaultMudExUrl } from './mudex.ts';

export type AirWorksApi = typeof AirWorks;

/** An app of the desktop; with probe it is added only when that GET succeeds for the signed-in user (the module is part of the app). */
export type CoworkeeApp = AppDefinition & { probe?: string };

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
  /** Address of the MudEx web components bundle for file previews. */
  mudExUrl?: string;
  /** Further AirWorks options; they win over the ones derived here. */
  airWorks?: AirWorksOptions;
};

export type Coworkee = {
  airWorks: AirWorksApi;
  /** Typed client of the library API. */
  api: CoworkeeClient;
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
  can: (permission: string) => boolean;
  notify: (message: string, kind?: 'info' | 'error') => void;
  /** Registers apps; factories get this object. */
  register: (...apps: AppFactory[]) => void;
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
    request: (path, init) => apiRequest(path, init),
    t: translator(texts),
    culture,
    user,
    theme,
    mode,
    settings,
    title: options.title,
    logo,
    mudExUrl: options.mudExUrl ?? defaultMudExUrl,
    can: (permission) => airWorks.can(permission),
    notify: toast,
    register: async (...factories) => {
      const added = await Promise.all(factories.flatMap((factory) => [factory(coworkee)].flat()).map(add));
      if (!options.airWorks?.initialShortcuts) {
        shortcuts.push(...added.filter((app): app is AppDefinition => !!app && !app.hidden).map((app) => app.id));
        airWorks.configure({ initialShortcuts: shortcuts });
      }
    },
  };

  const shortcuts: string[] = [];
  async function add({ probe, ...app }: CoworkeeApp): Promise<AppDefinition | undefined> {
    if (probe && !(user.isAuthenticated && await quietly(() => apiRequest(probe).then(() => true), false))) return undefined;
    airWorks.unregisterApp(app.id);
    airWorks.registerApp(app);
    return app;
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
  return coworkee;
}
