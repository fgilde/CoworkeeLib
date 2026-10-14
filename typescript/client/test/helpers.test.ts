import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cssVariables, format, loginUrl, logoUrl, odataQuery, palette, pickCulture, resolveMode, searchFilter, translator, type Theme } from '../src/index.ts';

const theme = {
  paletteLight: { Primary: '#1565c0', Surface: '#ffffff' },
  paletteDark: { Primary: '#64b5f6' },
  typography: { Default: { FontFamily: ['Segoe UI', 'Tahoma', 'sans-serif'] }, H1: { FontFamily: ['Georgia', 'serif'] } },
  layoutProperties: { DefaultBorderRadius: '6px' },
} as unknown as Theme;

test('the palette of a mode overrides the defaults key by key, in camel case', () => {
  assert.equal(palette(theme, 'light').primary, '#1565c0');
  assert.equal(palette(theme, 'dark').primary, '#64b5f6');
  assert.equal(palette(theme, 'dark').surface, '#373740');
  assert.equal(palette(undefined, 'light').primary, '#594ae2');
});

test('css variables carry colors, fonts and the radius', () => {
  const variables = cssVariables(theme, 'light');

  assert.equal(variables['--cw-primary'], '#1565c0');
  assert.equal(variables['--cw-text'], '#424242');
  assert.equal(variables['--cw-font'], '"Segoe UI", Tahoma, sans-serif');
  assert.equal(variables['--cw-heading-font'], 'Georgia, serif');
  assert.equal(variables['--cw-radius'], '6px');
});

test('the color mode follows the setting, system follows the operating system', () => {
  assert.equal(resolveMode('dark', false), 'dark');
  assert.equal(resolveMode('system', true), 'dark');
  assert.equal(resolveMode(undefined, false), 'light');
});

test('a logo of SVG markup becomes a data URL, addresses stay', () => {
  assert.equal(logoUrl('<svg/>'), 'data:image/svg+xml;charset=utf-8,%3Csvg%2F%3E');
  assert.equal(logoUrl('data:image/png;base64,AA'), 'data:image/png;base64,AA');
  assert.equal(logoUrl('  '), undefined);
});

test('texts fall back to the key and fill placeholders', () => {
  const t = translator({ 'Delete {0}?': '{0} löschen?' });

  assert.equal(t('Delete {0}?', 'Report.pdf'), 'Report.pdf löschen?');
  assert.equal(t('Upload'), 'Upload');
  assert.equal(format('{0:N0} of {1}', [3, 4]), '3 of 4');
  assert.equal(pickCulture(null, ['de-DE', 'en']), 'de-DE');
  assert.equal(pickCulture('fr', ['de']), 'fr');
  assert.equal(pickCulture(undefined, []), 'en');
});

test('OData queries are encoded, search spans fields and words', () => {
  assert.equal(odataQuery({ top: 25, skip: 0, orderBy: 'Name desc', count: true }), '?$orderby=Name%20desc&$top=25&$count=true');
  assert.equal(odataQuery({}), '');
  assert.equal(searchFilter(['Name', 'Sku'], "o'b x"), "(contains(tolower(Name),'o''b') or contains(tolower(Sku),'o''b')) and (contains(tolower(Name),'x') or contains(tolower(Sku),'x'))");
  assert.equal(searchFilter([], 'x'), undefined);
});

test('the login address only returns to local paths', () => {
  assert.equal(loginUrl('/files?x=1'), '/bff/login?returnUrl=%2Ffiles%3Fx%3D1');
  assert.equal(loginUrl('//evil.test'), '/bff/login?returnUrl=%2F');
});
