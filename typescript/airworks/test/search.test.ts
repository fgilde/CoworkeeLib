import assert from 'node:assert/strict';
import { test } from 'node:test';
import { toResults, type GlobalSearchHit } from '../src/search.ts';

const hit = (provider: string, extra: Partial<GlobalSearchHit> = {}): GlobalSearchHit => ({ provider, group: provider, title: `${provider} hit`, score: 1, ...extra });

test('global search hits open the app that shows them, others are left out', () => {
  const results = toResults([
    hit('reports', { id: 'r1' }),
    hit('chats', { id: 'u2' }),
    hit('files', { id: 'f3', subtitle: 'application/pdf' }),
    hit('products', { entitySet: 'Products', id: 'p4', subtitle: '4006381333931' }),
    hit('remote'),
  ]);

  assert.deepEqual(results.map((r) => [r.appId, r.intent]), [
    ['coworkee.report', { id: 'r1' }],
    ['coworkee.chat', { user: 'u2' }],
    ['coworkee.file-preview', { id: 'f3', name: 'files hit', contentType: 'application/pdf' }],
    ['coworkee.record', { set: 'Products', id: 'p4', title: 'products hit' }],
  ]);
  assert.equal(results[3].description, '4006381333931');
});
