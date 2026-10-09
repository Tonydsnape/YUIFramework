'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const path = require('node:path');
const ExcelJS = require('exceljs');
const { decode } = require('@msgpack/msgpack');
const { loadConfigurations, createArtifacts } = require('../src/exporter');
const { generateCSharp } = require('../src/codegen');
const { exportClientPipeline } = require('../src/unity-pipeline');
const { commitDirectories } = require('../src/transaction');
const { createWorkbook, rows } = require('../tools/create-ui-workbook');
const { validateUISchema } = require('../src/ui-schema');

async function fixture(t) {
  const root = path.resolve(__dirname, '..', '.test-work');
  await fs.mkdir(root, { recursive: true });
  const project = await fs.mkdtemp(path.join(root, 'export-'));
  t.after(() => fs.rm(project, { recursive: true, force: true }));
  await fs.mkdir(path.join(project, 'Config'));
  return { projectRoot: project, rootDir: path.join(project, 'Config') };
}
async function protocol(root, mutate) {
  const book = new ExcelJS.Workbook();
  const base = book.addWorksheet('Base');
  base.addRow(['Name', 'Settings', 'Type', 'base']);
  base.addRow(['Key', 1]);
  base.addRow(['KeyName', 'Target', 'ValueType', 'Value']);
  base.addRow(['Title', 'c', 'string', 'hello']);
  base.addRow(['Maximum', 'sc', 'long', '9223372036854775807']);
  base.addRow(['SecretServerField', 's', 'bool', true]);
  const normal = book.addWorksheet('Rows');
  normal.addRow(['Name', 'ProtocolRows', 'Type', 'normal']);
  normal.addRow(['Key', 2, 'Required', true]);
  normal.addRow([]);
  normal.addRow(['sc','sc','c','sc','sc']);
  normal.addRow(['Group','Number','Label','Metadata','Enabled']);
  normal.addRow(['string','long','string?','json','bool']);
  normal.addRow(['group','-9223372036854775808',null,'{values: [1, 2]}',true]);
  normal.addRow(['a:1','9223372036854775807','label','{key: "value"}',false]);
  if (mutate) mutate(base, normal);
  await book.xlsx.writeFile(path.join(root, 'protocol.xlsx'));
}
async function inventory(root) {
  const output = {};
  async function walk(dir) {
    for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) await walk(full);
      else if (!entry.name.endsWith('.xlsx')) output[path.relative(root, full)] = (await fs.readFile(full)).toString('base64');
    }
  }
  await walk(root);
  return output;
}

test('base/normal composite keys, client/server filtering, long decimal strings and MessagePack equivalence', async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  const configs = await loadConfigurations(options.rootDir);
  for (const target of ['client','server']) for (const artifact of createArtifacts(configs, target))
    assert.deepEqual(decode(artifact.bytes), JSON.parse(artifact.json));
  const data = createArtifacts(configs, 'client').map(a => JSON.parse(a.json));
  assert.equal(data[0].Maximum, '9223372036854775807');
  assert.equal(data[0].SecretServerField, undefined);
  assert.equal(data[1].group['-9223372036854775808'].Number, '-9223372036854775808');
  assert.equal(data[1].group['-9223372036854775808'].Label, null);
  const code = generateCSharp(configs, 'Client.Tables');
  assert.match(code, /namespace Client.Tables/);
  assert.match(code, /Get\(string key0, long key1\)/);
  assert.match(code, /Row key fields do not match/);
  assert.doesNotMatch(code, /GameConfig|Publish\(|Clear\(/);
});
test('special object key is an own JSON field without prototype mutation', async t => {
  const options = await fixture(t);
  await protocol(options.rootDir, (b,s) => { s.getCell('A7').value = '__proto__'; });
  const artifact = createArtifacts(await loadConfigurations(options.rootDir), 'client')[1];
  assert.equal(Object.hasOwn(JSON.parse(artifact.json), '__proto__'), true);
  // JS decoder intentionally rejects this key; C# config codec accepts string map keys.
  assert.throws(() => decode(artifact.bytes), /__proto__/);
  assert.equal({}.Number, undefined);
});

for (const [name, mutate] of [
  ['duplicate composite key', (b,s) => s.addRow(s.getRow(7).values)],
  ['unsafe numeric long', (b,s) => { s.getCell('B7').value = 9007199254740992; }],
  ['long overflow', (b,s) => { s.getCell('B7').value = '9223372036854775808'; }],
  ['missing required field', (b,s) => { s.getCell('E7').value = null; }],
  ['formula', (b,s) => { s.getCell('B7').value = { formula: '1+1', result: 2 }; }],
  ['invalid JSON5 scalar', (b,s) => { s.getCell('D7').value = '42'; }],
  ['nested JSON5 nonfinite values', (b,s) => { s.getCell('D7').value = '{nested:[NaN, Infinity, -Infinity]}'; }],
  ['key target exclusion', (b,s) => { s.getCell('A4').value = 'c'; }],
  ['table case collision', (b,s) => { s.getCell('B1').value = 'settings'; }]
]) test(`rejects ${name} before publication`, async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  await exportClientPipeline(options);
  const before = await inventory(options.projectRoot);
  await protocol(options.rootDir, mutate);
  await assert.rejects(exportClientPipeline(options));
  assert.deepEqual(await inventory(options.projectRoot), before);
});

test('UI workbook preserves all six original configurations and rejects invalid UI semantics', async t => {
  const options = await fixture(t);
  await createWorkbook(path.join(options.rootDir, 'UISettings.xlsx'));
  await exportClientPipeline(options);
  const artifact = createArtifacts(await loadConfigurations(options.rootDir), 'client')[0];
  const data = JSON.parse(artifact.json);
  for (const row of rows) assert.deepEqual(data[row.Profile][row.Id], row);
  const configs = await loadConfigurations(options.rootDir);
  configs[0].records[0].values[4] = 999;
  assert.throws(() => validateUISchema(configs), /layer/);
});
test('all output directories roll back including managed metas and unrelated files', async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  await exportClientPipeline(options);
  const generated = path.join(options.projectRoot, 'Assets','YUIFramework','Examples','ConfigGenerated');
  await fs.writeFile(path.join(generated, 'ConfigBindings.g.cs.meta'), 'preserved-guid');
  await fs.writeFile(path.join(generated, 'Unmanaged.cs'), 'user file');
  const before = await inventory(options.projectRoot);
  await protocol(options.rootDir, base => { base.getCell('D4').value = 'changed'; });
  for (let failAt = 0; failAt < 6; failAt++) {
    await assert.rejects(exportClientPipeline({ ...options, beforeSwap: async index => {
      if (index === failAt) throw new Error('injected swap failure');
    } }), /injected/);
    assert.deepEqual(await inventory(options.projectRoot), before);
  }
  await exportClientPipeline(options);
  assert.equal(await fs.readFile(path.join(generated, 'ConfigBindings.g.cs.meta'), 'utf8'), 'preserved-guid');
  assert.equal(await fs.readFile(path.join(generated, 'Unmanaged.cs'), 'utf8'), 'user file');
});
test('first install rollback, unmanaged collision, target escape and concurrent lock reject', async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  await assert.rejects(exportClientPipeline({ ...options, beforeSwap: () => { throw new Error('stop'); } }), /stop/);
  assert.deepEqual(await inventory(options.projectRoot), {});
  await assert.rejects(commitDirectories(options.projectRoot, [{ directory: '../outside', files: [] }]), /dedicated/);
  await fs.mkdir(path.join(options.projectRoot,'Config','.export-lock'));
  await assert.rejects(exportClientPipeline(options), /EEXIST/);
  await fs.rmdir(path.join(options.projectRoot,'Config','.export-lock'));
  const dest = path.join(options.projectRoot,'Config','Client','json');
  await fs.mkdir(dest, { recursive: true });
  await fs.writeFile(path.join(dest,'Settings.json'), 'user');
  await assert.rejects(exportClientPipeline(options), /unmanaged/);
  assert.equal(await fs.readFile(path.join(dest,'Settings.json'), 'utf8'), 'user');
});
test('invalid workbook and generated symbol collisions do not touch prior artifacts', async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  await exportClientPipeline(options);
  const before = await inventory(options.projectRoot);
  await fs.writeFile(path.join(options.rootDir, 'broken.xlsx'), 'not zip');
  await assert.rejects(exportClientPipeline(options));
  assert.deepEqual(await inventory(options.projectRoot), before);
  const configs = [{ name: 'ConfigValue', type:'base', entries: [] }];
  assert.throws(() => generateCSharp(configs), /collision/);
  assert.throws(() => generateCSharp([{ name:'Table', type:'base', entries:[] }]), /collision/);
  assert.throws(() => generateCSharp([{ name:'Data', type:'base', entries:[
    { name:'ConfigValue', type:'int', target:'c' }
  ] }]), /collides/);
  assert.throws(() => generateCSharp([], 'invalid-namespace'), /Namespace/);
});
test('generated name failures preserve already synchronized UI artifacts', async t => {
  const options = await fixture(t); await protocol(options.rootDir);
  await exportClientPipeline(options);
  const before = await inventory(options.projectRoot);
  await protocol(options.rootDir, base => { base.getCell('B1').value = 'Table'; });
  await assert.rejects(exportClientPipeline(options), /collision/);
  assert.deepEqual(await inventory(options.projectRoot), before);
});
test('obsolete managed files removed without removing adjacent user content', async t => {
  const options = await fixture(t);
  const directory = 'Config/output';
  await commitDirectories(options.projectRoot, [{ directory, files: [{ name:'old.json',content:'old' }] }]);
  await fs.writeFile(path.join(options.projectRoot,directory,'old.json.meta'),'old-guid');
  await fs.writeFile(path.join(options.projectRoot,directory,'user.txt'),'keep');
  await commitDirectories(options.projectRoot, [{ directory, files: [{ name:'new.json',content:'new' }] }]);
  assert.deepEqual((await fs.readdir(path.join(options.projectRoot,directory))).sort(),
    ['.config-owned.json','new.json','user.txt']);
});
