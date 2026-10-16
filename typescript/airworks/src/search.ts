import type { Intent, SearchProvider, SearchResult } from '@fgilde/airworks';
import type { Coworkee } from './coworkee.ts';
import { filePreviewAppId } from './apps/file-preview.ts';
import { reportAppId } from './apps/reports.ts';

/** A hit of GET /api/v1/search (GlobalSearchHit). */
export type GlobalSearchHit = {
  provider: string; group: string; title: string; subtitle?: string | null; url?: string | null;
  entitySet?: string | null; id?: string | null; score: number; thumbnail?: string | null; icon?: string | null;
};

/** Which app opens a hit, with which intent; undefined leaves the hit out. */
export type SearchTarget = (hit: GlobalSearchHit) => { appId: string; intent?: Intent } | undefined;

/** Reports, chats and files open their own app, records of OData sets the data app on their set. */
export function defaultSearchTarget(hit: GlobalSearchHit): { appId: string; intent?: Intent } | undefined {
  if (hit.provider === 'reports' && hit.id) return { appId: reportAppId, intent: { id: hit.id } };
  if (hit.provider === 'chats' && hit.id) return { appId: 'coworkee.chat', intent: { user: hit.id } };
  if (hit.provider === 'files' && hit.id) return { appId: filePreviewAppId, intent: { id: hit.id, name: hit.title, contentType: hit.subtitle ?? '' } };
  if (hit.entitySet) return { appId: 'coworkee.data', intent: { set: hit.entitySet } };
  return undefined;
}

/** The server's global search in the AirWorks search, best hits first; register it with airWorks.registerSearchProvider. */
export function globalSearchProvider(coworkee: Coworkee, target: SearchTarget = defaultSearchTarget, limit = 5): SearchProvider {
  return {
    id: 'coworkee.global-search',
    search: async (query, { signal }) => {
      if (query.trim().length < 2) return [];
      const result = await coworkee.request<{ hits: GlobalSearchHit[] }>(`/api/v1/search?${new URLSearchParams({ q: query, limit: String(limit) })}`, { signal });
      return toResults(result.hits, target);
    },
  };
}

export const toResults = (hits: GlobalSearchHit[], target: SearchTarget = defaultSearchTarget): SearchResult[] =>
  hits.flatMap((hit) => {
    const opens = target(hit);
    return opens ? [{ title: hit.title, description: hit.subtitle ?? hit.group, appId: opens.appId, intent: opens.intent }] : [];
  });
