import assert from 'node:assert/strict';
import { test } from 'node:test';
import type { Coworkee, CoworkeeApp } from '../src/coworkee.ts';
import { mailTemplateEditorApp, recordPage, themeEditorApp, workflowDesignerApp } from '../src/index.ts';

const fake = (appUrl: string | null, hits: unknown[] = []) => {
  const requests: string[] = [];
  const coworkee = {
    t: (text: string) => text, can: () => true, apps: [], title: 'App', user: { appUrl },
    request: async (path: string) => { requests.push(path); return { hits }; },
  } as unknown as Coworkee;
  return { coworkee, requests };
};
const list = (apps: CoworkeeApp | CoworkeeApp[]) => [apps].flat();

test('the heavy admin editors are the web client pages while the BFF knows the web client, the apps built here otherwise', () => {
  const embedded = list(themeEditorApp(fake('https://app.test').coworkee));
  const local = list(themeEditorApp(fake(null).coworkee));

  assert.deepEqual(embedded.map((app) => [app.id, app.group, app.requiredPermissions]), [['coworkee.admin.themes', 'System', ['Themes.Manage']]]);
  assert.equal(local.length, 1);
  assert.equal(local[0].id, 'coworkee.admin.themes');
  assert.notEqual(local[0].render, embedded[0].render);
  assert.deepEqual(list(mailTemplateEditorApp(fake('https://app.test').coworkee)).map((app) => app.requiredPermissions), [['Mail.Templates.Manage']]);
  assert.deepEqual(list(workflowDesignerApp(fake(null).coworkee)).map((app) => app.requiredPermissions), [['Workflows.Elsa.Manage']]);
});

test('the page of a record in the web client is the link of its global search hit', async () => {
  const id = '0f8fad5b-d9cb-469f-a165-70867728950e';
  const hits = [
    { provider: 'brands', group: 'Brands', title: 'Other', entitySet: 'Brands', id, url: '/catalog/brands?brands=x', score: 1 },
    { provider: 'products', group: 'Products', title: 'Tea', entitySet: 'Products', id: id.toUpperCase(), url: '/catalog/products?products=y', score: 1 },
  ];
  const { coworkee, requests } = fake('https://app.test', hits);

  assert.equal(await recordPage(coworkee, 'Products', id), '/catalog/products?products=y');
  assert.equal(await recordPage(coworkee, 'Orders', id), undefined);
  assert.equal(await recordPage(coworkee, 'Products', 'not-a-guid'), undefined);
  assert.equal(await recordPage(fake(null, hits).coworkee, 'Products', id), undefined);
  assert.deepEqual(requests, [`/api/v1/search?q=${id}&limit=5`, `/api/v1/search?q=${id}&limit=5`]);
});
