import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { adminApps, defaultApps, entityApp, icons, type Coworkee } from '../src/index.ts';

const airWorksIcons = new Set(readdirSync(join(dirname(fileURLToPath(import.meta.resolve('@fgilde/airworks'))), 'icons')));
const coworkee = { t: (text: string) => text, can: () => true, apps: [], user: { appUrl: 'https://app.test' }, title: 'App', airWorks: { on() {} } } as unknown as Coworkee;

test('every app icon is an AirWorks icon or an SVG data URL', () => {
  const apps = [...defaultApps, ...adminApps, entityApp({ entitySet: 'Things' })].flatMap((factory) => factory(coworkee));

  assert.ok(apps.length > 30);
  for (const app of apps) {
    assert.ok(airWorksIcons.has(app.icon) || /^data:image\/svg\+xml,%3Csvg%20/.test(app.icon), `${app.id}: ${app.icon.slice(0, 40)}`);
  }
});

test('the icons are white SVGs without a slash, which AirWorks would read as an address', () => {
  for (const [name, url] of Object.entries(icons)) {
    const svg = decodeURIComponent(url.slice('data:image/svg+xml,'.length));
    assert.match(svg, /^<svg [^>]*viewBox="[-\d. ]+"><path fill="#fff" d="[^"]+"\/><\/svg>$/, name);
    assert.ok(!url.slice(url.indexOf(',')).includes('/'), name);
  }
});
