// Browser check of the starter specs page and its mini wizard, on a desktop and a phone, in English and German.
// Build the site, serve it, then: node website/tests/gallery.mjs [http://127.0.0.1:8765] [screenshot folder]
// Needs the playwright package (npm i playwright) where node finds it, or PLAYWRIGHT_MODULE=<path to its index.js>.
import { createRequire } from 'node:module';
import { mkdirSync } from 'node:fs';
import assert from 'node:assert/strict';

const require = createRequire(import.meta.url);
const { chromium, devices } = require(process.env.PLAYWRIGHT_MODULE ?? 'playwright');
const base = (process.argv[2] ?? 'http://127.0.0.1:8765').replace(/\/$/, '');
const shots = process.argv[3];
if (shots) mkdirSync(shots, { recursive: true });

const browser = await chromium.launch();
try {
  for (const [device, options] of [['desktop', { viewport: { width: 1280, height: 900 } }], ['phone', devices['iPhone 13']]]) {
    for (const lang of ['en', 'de']) {
      const context = await browser.newContext({ ...options, acceptDownloads: true });
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', e => errors.push(e.message));
      page.on('console', m => m.type() === 'error' && errors.push(m.text()));
      await page.goto(`${base}/${lang === 'de' ? 'de/' : ''}getting-started/gallery/`);

      const cards = page.locator('.cw-spec');
      await cards.first().waitFor();
      assert.equal(await cards.count(), 6);
      const visible = () => page.locator('.cw-spec:visible').count();

      await page.fill('#cw-starters input[type=search]', 'orders');
      assert.equal(await visible(), 1, 'search');
      await page.fill('#cw-starters input[type=search]', '');
      await page.click('.cw-starters__tags button:text-is("#sales")');
      assert.equal(await visible(), 2, 'tag');
      await page.click('.cw-starters__tags button:text-is("#sales")');
      assert.equal(await visible(), 6, 'tag off');
      assert.match(await page.locator('#spec-crm .cw-line code').textContent(), /^coworkee new MyApp --spec crm\.json$/);

      const href = await page.locator('#spec-shop a[download]').getAttribute('href');
      const shop = await (await page.request.get(href)).json();
      assert.equal(shop.name, 'Shop');

      // the mini wizard, picked from a card
      await page.locator('#spec-crm button.cw-btn').click();
      await page.waitForFunction(() => document.querySelector('#cw-wizard-json').textContent.includes('"Sales"'));
      await page.fill('#cw-wizard input[name=name]', 'Pipeline');
      await page.selectOption('#cw-wizard select[name=database]', 'sqlite');
      await page.selectOption('#cw-wizard select[name=ai]', 'off');
      await page.check('#cw-wizard input[value=fr]');
      const spec = JSON.parse(await page.locator('#cw-wizard-json').textContent());
      assert.equal(spec.name, 'Pipeline');
      assert.equal(spec.options.database, 'sqlite');
      assert.equal(spec.options.ai, false);
      assert.deepEqual(spec.options.languages, ['en', 'de', 'fr']);
      assert.equal(spec.modules[0].name, 'Sales');
      assert.equal(await page.locator('#cw-wizard-command code').textContent(), 'coworkee new Pipeline --spec pipeline.json');
      assert.equal(await page.locator('#cw-wizard-problems .cw-ok').count(), 1, 'valid');

      const [download] = await Promise.all([page.waitForEvent('download'), page.click('#cw-wizard-download')]);
      assert.equal(download.suggestedFilename(), 'pipeline.json');

      await page.fill('#cw-wizard input[name=name]', '1Pipeline');
      assert.equal(await page.locator('#cw-wizard-problems.cw-problems--bad').count(), 1, 'bad name');

      // blank start: only the options that differ from the defaults
      await page.selectOption('#cw-wizard select[name=starter]', '');
      await page.waitForFunction(() => !document.querySelector('#cw-wizard-json').textContent.includes('modules'));
      await page.fill('#cw-wizard input[name=name]', 'Blank');
      assert.deepEqual(JSON.parse(await page.locator('#cw-wizard-json').textContent()), { name: 'Blank' });

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      assert.ok(overflow <= 0, `horizontal overflow of ${overflow}px`);
      assert.deepEqual(errors, []);
      if (shots) {
        await page.locator('#cw-starters').screenshot({ path: `${shots}/gallery-${device}-${lang}.png` });
        await page.locator('#cw-wizard').screenshot({ path: `${shots}/wizard-${device}-${lang}.png` });
      }

      console.log(`ok  ${device} ${lang}`);
      await context.close();
    }
  }
} finally {
  await browser.close();
}
