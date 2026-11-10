'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const os = require('node:os');
const fs = require('node:fs/promises');
const path = require('node:path');
const { createWorkbook, rows: originals } = require('../tools/create-ui-workbook');
const { extend, rows } = require('../tools/extend-system-ui-workbook');
const { loadConfigurations, createArtifacts } = require('../src/exporter');
const { validateUISchema } = require('../src/ui-schema');

test('system profile adds five bounded/modal registrations without replacing original profiles or maintained rows', async t => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), 'yui-system-'));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const file = path.join(root, 'UISettings.xlsx');
  await createWorkbook(file);
  await extend(file);
  const before = await fs.readFile(file);
  await extend(file);
  assert.deepEqual(await fs.readFile(file), before);
  const configs = await loadConfigurations(root);
  validateUISchema(configs);
  const data = JSON.parse(createArtifacts(configs, 'client')[0].json);
  for (const row of [...originals, ...rows]) assert.deepEqual(data[row.Profile][row.Id], row);
  assert.equal(data.system.SystemToast.Modal, false);
  assert.equal(data.system.SystemLoading.Modal, true);
  assert.equal(data.system.SystemNotice.Layer, 700);
});
