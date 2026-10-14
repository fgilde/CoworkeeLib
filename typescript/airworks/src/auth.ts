import { getUser, loginUrl, logout, type BffUser } from '@coworkee/client';
import type { AuthProvider, Session } from '@fgilde/airworks';

/** The AirWorks session of a user the BFF reports as signed in; the BFF keeps the tokens, so the session has none. */
export function sessionOf(user: BffUser): Session | undefined {
  if (!user.isAuthenticated) return undefined;
  const id = user.email || user.userId || 'user';
  return { identity: { id, displayName: user.name || user.email || id, email: user.email ?? undefined }, roles: [...user.roles], userId: user.userId, tenantId: user.tenantId };
}

/**
 * Signs in through the app's BFF: the browser goes to the auth server (and from there to Keycloak or another provider, as the
 * app's sign-in mode says) and comes back with a session cookie.
 */
export function bffAuthProvider(options: { title: string; icon?: string; user?: BffUser }): AuthProvider {
  let known = options.user;
  return {
    id: 'coworkee',
    title: options.title,
    icon: options.icon,
    login: async () => { location.assign(loginUrl(location.pathname + location.search)); },
    restore: async () => {
      const user = known ?? await getUser();
      known = undefined;
      return sessionOf(user);
    },
    logout: async () => { location.assign(await logout()); },
  };
}
