'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { propose } = require('../tools/propose-ui-page');

test('UI authoring proposes an additive workbook without modifying input and is idempotent', async () => {
  const folder = await fs.mkdtemp(path.join(os.tmpdir(), 'yui-authoring-'));
  try {
    const file = path.join(folder, 'UISettings.xlsx');
    const original = await fs.readFile(path.join(__dirname, '..', 'UISettings.xlsx'));
    await fs.writeFile(file, original);
    const request = { Profile: 'authoring', Id: 'GeneratedPage', PrefabKey: 'GeneratedPage' };
    const proposed = await propose(file, request);
    assert.deepEqual(await fs.readFile(file), original);
    await fs.writeFile(file, proposed);
    assert.deepEqual(await propose(file, request), proposed);
    await assert.rejects(propose(file, { ...request, PrefabKey: 'Changed' }), /refusing/);
    await assert.rejects(propose(file, { ...request, Id: ' ' }), /trimmed/);
  } finally { await fs.rm(folder, { recursive: true, force: true }); }
});
