'use strict';
const ExcelJS = require('exceljs');
const { fields, rows } = require('./create-ui-workbook');

async function propose(file, request) {
  for (const name of ['Profile', 'Id', 'PrefabKey'])
    if (typeof request[name] !== 'string' || !request[name].trim() || request[name] !== request[name].trim())
      throw new Error(`A trimmed ${name} is required`);
  const book = new ExcelJS.Workbook();
  await book.xlsx.readFile(file);
  const sheet = book.getWorksheet('UI');
  if (!sheet || JSON.stringify(sheet.getRow(5).values.slice(1)) !== JSON.stringify(fields.map(([name]) => name)))
    throw new Error('Existing UI workbook schema does not match the maintained schema');
  const wanted = { ...rows[0], ...request };
  let existing = null;
  sheet.eachRow((row, i) => {
    if (i >= 7 && row.getCell(1).value === wanted.Profile && row.getCell(2).value === wanted.Id) {
      if (existing) throw new Error('Duplicate workbook UI key');
      existing = row;
    }
  });
  if (existing) {
    if (fields.some(([name], index) => {
      const actual = existing.getCell(index + 1).value;
      return (actual == null ? null : actual) !== (wanted[name] == null ? null : wanted[name]);
    })) throw new Error('Existing designer row differs; refusing to overwrite it');
    return require('node:fs/promises').readFile(file);
  }
  sheet.addRow(fields.map(([name]) => wanted[name]));
  return Buffer.from(await book.xlsx.writeBuffer());
}

if (require.main === module) {
  let input = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', chunk => { input += chunk; });
  process.stdin.on('end', () => propose(process.argv[2], JSON.parse(input))
    .then(bytes => process.stdout.write(bytes.toString('base64')))
    .catch(error => { console.error(error); process.exitCode = 1; }));
}
module.exports = { propose };
