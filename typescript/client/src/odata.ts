import { apiRequest, type CoworkeeClientOptions } from './client.ts';

export type ODataQuery = {
  filter?: string;
  orderBy?: string;
  top?: number;
  skip?: number;
  select?: readonly string[];
  expand?: string;
  count?: boolean;
};

export type ODataPage<T> = { items: T[]; count?: number };

export type EntitySetInfo = { name: string; url: string };

export function odataQuery(query: ODataQuery): string {
  const parts: Array<[string, string | undefined]> = [
    ['$filter', query.filter], ['$orderby', query.orderBy], ['$select', query.select?.join(',')], ['$expand', query.expand],
    ['$top', query.top?.toString()], ['$skip', query.skip ? query.skip.toString() : undefined], ['$count', query.count ? 'true' : undefined],
  ];
  const text = parts.filter(([, value]) => value).map(([name, value]) => `${name}=${encodeURIComponent(value!)}`).join('&');
  return text ? `?${text}` : '';
}

/** An OData string literal: single quotes doubled. */
export const literal = (text: string) => `'${text.replace(/'/g, "''")}'`;

/** Rows whose text fields contain the words, case-insensitive: every word in one of the fields. */
export function searchFilter(fields: readonly string[], text: string): string | undefined {
  const words = text.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (fields.length === 0 || words.length === 0) return undefined;
  return words.map((word) => `(${fields.map((field) => `contains(tolower(${field}),${literal(word)})`).join(' or ')})`).join(' and ');
}

export async function odataPage<T = Record<string, unknown>>(entitySet: string, query: ODataQuery = {}, options?: CoworkeeClientOptions): Promise<ODataPage<T>> {
  const page = await apiRequest<{ value?: T[]; '@odata.count'?: number }>(`/odata/${encodeURIComponent(entitySet)}${odataQuery(query)}`, {}, options);
  return { items: page?.value ?? [], count: page?.['@odata.count'] };
}

/** The entity sets the OData service document lists. */
export async function entitySets(options?: CoworkeeClientOptions): Promise<EntitySetInfo[]> {
  const document = await apiRequest<{ value?: Array<{ name: string; kind?: string; url: string }> }>('/odata', {}, options);
  return (document?.value ?? []).filter((item) => (item.kind ?? 'EntitySet') === 'EntitySet').map(({ name, url }) => ({ name, url }));
}
