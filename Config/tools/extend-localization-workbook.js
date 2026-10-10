'use strict';
const ExcelJS = require('exceljs');
const path = require('node:path');
const { rows } = require('./create-localization-workbook');
(async () => {
  const file = path.resolve(__dirname, '..', 'LocalizationTextConfig.xlsx');
  const book = new ExcelJS.Workbook(); await book.xlsx.readFile(file);
  const sheet = book.getWorksheet('Text');
  const keys = new Set();
  sheet.eachRow((row, i) => { if (i >= 7) keys.add(row.getCell(1).value); });
  for (const row of rows) if (!keys.has(row[0])) sheet.addRow(row);
  await book.xlsx.writeFile(file);
})().catch(error => { console.error(error); process.exitCode = 1; });
