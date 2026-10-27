import { apiRequest, type CoworkeeClientOptions } from './client.ts';

/** The signed-in user as the BFF of a Coworkee app reports it (GET /bff/user). */
export type BffUser = {
  isAuthenticated: boolean;
  name?: string | null;
  email?: string | null;
  userId?: string | null;
  tenantId?: string | null;
  roles: string[];
  manageUrl?: string | null;
  systemTenant?: boolean | null;
  /** Address of the app's web client when the BFF knows it (Coworkee:Bff:WebAppUrl); pages of it can be embedded with ?embed=1. */
  appUrl?: string | null;
  /** App launcher of an AirWorks host (Coworkee:AirWorks:Launcher): menu or overlay. */
  launcher?: string | null;
  /** The user who signed in as this one ("Log in as"); back with /bff/stop-impersonation?returnUrl=/. */
  impersonatorId?: string | null;
  impersonatorName?: string | null;
};

export const getUser = (options?: CoworkeeClientOptions) => apiRequest<BffUser>('/bff/user', {}, options);

/** The address that starts the sign-in at the auth server and comes back to returnUrl (a local path). */
export function loginUrl(returnUrl = '/', loginHint?: string): string {
  const query = new URLSearchParams({ returnUrl: returnUrl.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/' });
  if (loginHint) query.set('loginHint', loginHint);
  return `/bff/login?${query}`;
}

/** Ends the session and returns where the browser goes next (the end-session page of the auth server). */
export async function logout(options?: CoworkeeClientOptions): Promise<string> {
  const result = await apiRequest<{ redirect?: string }>('/bff/logout', { method: 'POST' }, options);
  return result?.redirect || '/';
}
