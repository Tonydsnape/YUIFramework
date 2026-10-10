'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const ExcelJS = require('exceljs');
const { decode } = require('@msgpack/msgpack');
const { createWorkbook } = require('../tools/create-localization-workbook');
const { exportClientPipeline } = require('../src/unity-pipeline');
const { loadConfigurations, createArtifacts } = require('../src/exporter');
const { argumentsIn } = require('../src/localization-schema');

async function fixture(t) {
  const root = path.resolve(__dirname, '..', '.test-work');
  await fs.mkdir(root, { recursive: true });
  const projectRoot = await fs.mkdtemp(path.join(root, 'localization-'));
  t.after(() => fs.rm(projectRoot, { recursive: true, force: true }));
  const rootDir = path.join(projectRoot, 'Config');
  await fs.mkdir(rootDir);
  const workbook = path.join(rootDir, 'LocalizationTextConfig.xlsx');
  await createWorkbook(workbook);
  return { projectRoot, rootDir, workbook };
}
async function outputs(root) {
  const found = {};
  async function walk(dir) {
    for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (!entry.name.endsWith('.xlsx')) found[path.relative(root, full)] = (await fs.readFile(full)).toString('base64');
    }
  }
  await walk(root);
  return found;
}
test('localization exports Unicode, explicit empty translations and a shared typed immutable catalog', async t => {
  const options = await fixture(t);
  await exportClientPipeline(options);
  const tables = await loadConfigurations(options.rootDir);
  for (const target of ['client','server']) {
    const [artifact] = createArtifacts(tables, target);
    const data = JSON.parse(artifact.json);
    assert.deepEqual(decode(artifact.bytes), data);
    assert.equal(data['sample.greeting']['zh-CN'], '你好，{0}！');
    assert.equal(data['sample.fallback'].fr, null);
  }
  const generated = await fs.readFile(path.join(options.projectRoot, 'Assets','YUIFramework','Examples','ConfigGenerated','ConfigBindings.g.cs'), 'utf8');
  assert.match(generated, /LocalizationTextCatalog.Table/);
  assert.doesNotMatch(generated, /public.*zh-CN/);
});
for (const [name, mutate] of [
  ['duplicate key', s => s.addRow(s.getRow(7).values)],
  ['duplicate locale', s => { s.getCell('D5').value = 'en'; }],
  ['noncanonical locale', s => { s.getCell('D5').value = 'EN-us'; }],
  ['unsupported locale', s => { s.getCell('D5').value = 'zz'; }],
  ['wrong locale type', s => { s.getCell('D6').value = 'string'; }],
  ['key padding', s => { s.getCell('A7').value = ' padded '; }],
  ['malformed format', s => { s.getCell('B7').value = 'broken {'; }],
  ['argument mismatch', s => { s.getCell('C7').value = 'Hello {1}'; }],
  ['unsupported format DSL', s => { s.getCell('B7').value = '{0:N2}'; }],
  ['optional table', s => { s.getCell('D2').value = false; }]
]) test(`invalid localization ${name} preserves all previously published artifacts`, async t => {
  const options = await fixture(t);
  await exportClientPipeline(options);
  const before = await outputs(options.projectRoot);
  const book = new ExcelJS.Workbook(); await book.xlsx.readFile(options.workbook);
  mutate(book.worksheets[0]);
  await book.xlsx.writeFile(options.workbook);
  await assert.rejects(exportClientPipeline(options));
  assert.deepEqual(await outputs(options.projectRoot), before);
});
test('literal brace escapes and supported argument bounds are deterministic', () => {
  assert.equal(argumentsIn('{{value}} {15} {0} {0}'), '0,15');
  for (const invalid of ['{16}', '{00}', '{-1}', '{name}', '{0,2}', '{', '}'])
    assert.throws(() => argumentsIn(invalid));
});
