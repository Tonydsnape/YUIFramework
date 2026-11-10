'use strict';
const ExcelJS = require('exceljs');
const path = require('node:path');
const { fields, rows: originals } = require('./create-ui-workbook');

const rows = [
  ['SystemUISamplePage', 'SystemUISamplePage', 200, false],
  ['SystemLoading', 'SystemLoading', 650, true],
  ['SystemToast', 'SystemToast', 600, false],
  ['SystemConfirmation', 'SystemConfirmation', 400, true],
  ['SystemNotice', 'SystemNotice', 700, true]
].map(([Id, PrefabKey, Layer, Modal]) => ({
  ...originals[0], Profile: 'system', Id, PrefabKey, Layer, FullScreen: false,
  UseLayerModalPolicy: false, Modal, UseTransition: true, TransitionType: 1,
  ShowDuration: .1, HideDuration: .08
}));

async function extend(file) {
  const book = new ExcelJS.Workbook();
  await book.xlsx.readFile(file);
  const sheet = book.getWorksheet('UI');
  if (!sheet || JSON.stringify(sheet.getRow(5).values.slice(1)) !== JSON.stringify(fields.map(([name]) => name)))
    throw new Error('The existing UI workbook does not match the maintained schema.');
  const keys = new Set();
  sheet.eachRow((row, i) => { if (i >= 7) keys.add(`${row.getCell(1).value}/${row.getCell(2).value}`); });
  let changed = false;
  for (const row of rows) {
    if (keys.has(`${row.Profile}/${row.Id}`)) continue;
    sheet.addRow(fields.map(([name]) => row[name]));
    changed = true;
  }
  if (changed) await book.xlsx.writeFile(file);
}
if (require.main === module)
  extend(path.resolve(__dirname, '..', 'UISettings.xlsx')).catch(error => { console.error(error); process.exitCode = 1; });
module.exports = { extend, rows };
