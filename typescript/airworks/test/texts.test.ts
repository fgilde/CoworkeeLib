import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { test } from 'node:test';

const source = join(import.meta.dirname, '..', 'src');
const languages = join(import.meta.dirname, '..', '..', '..', 'src', 'Coworkee.Localization', 'Localization');

/** Texts the apps translate that come from values, not literals: statuses, presets and the administration sections. */
const dynamic = [
  'Draft', 'Scheduled', 'Sending', 'Sent', 'Failed', 'Cancelled', 'Queued', 'Skipped', 'Healthy', 'Unhealthy',
  'All active users', 'Not signed in for more than N days', 'Never signed in', 'Signed up in the last N days', 'E-mail not confirmed', 'Members of roles', 'Deactivated users',
  'Identity', 'System', 'Communication', 'Localization', 'Monitoring', 'Online', 'Offline', 'Confirm to run:', 'Confirmed:', 'Declined:',
];

function literalKeys(): Set<string> {
  const keys = new Set(dynamic);
  const files = readdirSync(source, { recursive: true, encoding: 'utf8' }).filter((file) => file.endsWith('.ts'));
  for (const file of files) {
    const text = readFileSync(join(source, file), 'utf8');
    for (const match of text.matchAll(/\bt\('((?:[^'\\]|\\.)+)'/g)) keys.add(match[1].replace(/\\'/g, "'"));
    for (const match of text.matchAll(/\b(?:title|label|hint): '((?:[^'\\]|\\.)+)'/g)) keys.add(match[1].replace(/\\'/g, "'"));
  }
  return keys;
}

test('every text of the apps is translated in every shipped language of the library', () => {
  const keys = literalKeys();
  const missing: string[] = [];
  for (const file of readdirSync(languages).filter((name) => name.endsWith('.json'))) {
    const texts = JSON.parse(readFileSync(join(languages, file), 'utf8')) as Record<string, string>;
    for (const key of keys) if (!texts[key]) missing.push(`${file}: ${key}`);
  }
  assert.deepEqual(missing, []);
});
