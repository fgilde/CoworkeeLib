import assert from 'node:assert/strict';
import { test } from 'node:test';
import { npmVersion } from './npm-version.mjs';

test('releases keep their version on npm where npm can take it, four parts fold into the third', () => {
  assert.equal(npmVersion('1.2.3'), '1.2.3');
  assert.equal(npmVersion('v1.2.3'), '1.2.3');
  assert.equal(npmVersion('0.1.0-dev.20261014170301'), '0.1.0-dev.20261014170301');
  assert.equal(npmVersion('0.0.1.20'), '0.0.1020');
  assert.equal(npmVersion('0.0.2.0'), '0.0.2000');
  assert.throws(() => npmVersion('0.0.1.1000'));
  assert.throws(() => npmVersion('latest'));
});
