'use strict';
const ExcelJS = require('exceljs');
const path = require('node:path');
const fs = require('node:fs/promises');

const fields = [
  ['Profile', 'string'], ['Id', 'string'], ['PrefabKey', 'string'], ['PrefabPackage', 'string?'],
  ['Layer', 'int'], ['CacheOnClose', 'bool'], ['MaxPoolSize', 'int'], ['PreloadCount', 'int'],
  ['PoolPriority', 'int'], ['PoolIdleTimeoutSeconds', 'float'], ['FullScreen', 'bool'],
  ['UseLayerModalPolicy', 'bool'], ['Modal', 'bool'], ['UseTransition', 'bool'],
  ['TransitionType', 'int'], ['ShowDuration', 'float'], ['HideDuration', 'float'],
  ['IgnoreTransitionTimeScale', 'bool'], ['SlideDistance', 'float'], ['StartScale', 'float'],
  ['CustomTransitionId', 'string?'], ['RefreshTransitionBaselineOnReuse', 'bool'], ['SuspendWhenCovered', 'bool']
];
const defaults = {
  PrefabPackage: null, Layer: 200, CacheOnClose: true, MaxPoolSize: 1, PreloadCount: 0,
  PoolPriority: 0, PoolIdleTimeoutSeconds: 0, FullScreen: true, UseLayerModalPolicy: true,
  Modal: false, UseTransition: true, TransitionType: 1, ShowDuration: .2, HideDuration: .15,
  IgnoreTransitionTimeScale: true, SlideDistance: 800, StartScale: .9, CustomTransitionId: null,
  RefreshTransitionBaselineOnReuse: true, SuspendWhenCovered: true
};
const rows = [
  { Profile: 'bootstrap', Id: 'HelloPage', PrefabKey: 'SampleHelloPage', UseTransition: false, TransitionType: 0 },
  { Profile: 'hello', Id: 'HelloPage', PrefabKey: 'SampleHelloPage' },
  { Profile: 'hello', Id: 'SecondSamplePage', PrefabKey: 'SecondSamplePage', CacheOnClose: false,
    TransitionType: 3, ShowDuration: .25, HideDuration: .2, SlideDistance: 900 },
  { Profile: 'hello', Id: 'VirtualListSamplePage', PrefabKey: 'VirtualListSamplePage', TransitionType: 2, StartScale: .92 },
  { Profile: 'hello', Id: 'MvvmSamplePage', PrefabKey: 'MvvmSamplePage', ShowDuration: .18 },
  { Profile: 'y2', Id: 'SampleHelloPage', PrefabKey: 'SampleHelloPage', MaxPoolSize: 2,
    PreloadCount: 2, PoolPriority: 10, PoolIdleTimeoutSeconds: 120 }
].map(row => ({ ...defaults, ...row }));

async function createWorkbook(destination) {
  const workbook = new ExcelJS.Workbook();
  const sheet = workbook.addWorksheet('UI');
  sheet.addRow(['Name', 'UISettings', 'Type', 'normal']);
  sheet.addRow(['Key', 2, 'Required', true]);
  sheet.addRow(['Profiles preserve the original independent startup examples.']);
  sheet.addRow(fields.map(() => 'sc'));
  sheet.addRow(fields.map(([name]) => name));
  sheet.addRow(fields.map(([, type]) => type));
  rows.forEach(row => sheet.addRow(fields.map(([name]) => row[name])));
  await workbook.xlsx.writeFile(destination);
}
if (require.main === module) {
  const destination = path.resolve(__dirname, '..', 'UISettings.xlsx');
  (async () => {
    try { await fs.access(destination); throw new Error('UISettings.xlsx already exists; edit it rather than overwrite designer data.'); }
    catch (error) { if (error.code !== 'ENOENT') throw error; }
    await createWorkbook(destination);
    console.log(destination);
  })().catch(error => { console.error(error); process.exitCode = 1; });
}
module.exports = { createWorkbook, fields, rows };
