'use strict';
const ExcelJS = require('exceljs');
const fs = require('node:fs/promises');
const path = require('node:path');
const fields = [['Group','string'],['Key','string'],['Kind','string'],['Value','string'],['HighContrast','string?'],['Package','string?']];
const rows = [];
for (const theme of ['light','dark']) {
  rows.push([`theme.${theme}`,'text','Color',theme === 'light' ? '#202020' : '#F0EEE8',theme === 'light' ? '#000000' : '#FFFFFF',null]);
  rows.push([`theme.${theme}`,'panel','Color',theme === 'light' ? '#F4EFE4' : '#202830',theme === 'light' ? '#FFFFFF' : '#000000',null]);
  rows.push([`theme.${theme}`,'body','Font','YUIPresentation/WenKai',null,null]);
  rows.push([`theme.${theme}`,'badge','Sprite','YUIPresentation/BadgeEn',null,null]);
  rows.push([`theme.${theme}`,'surface','Material','YUIPresentation/Panel',null,null]);
}
for (const [locale, badge] of [['en','En'],['zh-CN','Zh'],['fr','Fr']]) {
  rows.push([`locale.${locale}`,'body','Font','YUIPresentation/WenKai',null,null]);
  rows.push([`locale.${locale}`,'badge','Sprite',`YUIPresentation/Badge${badge}`,null,null]);
}
async function createWorkbook(destination) {
  const book = new ExcelJS.Workbook(); const sheet = book.addWorksheet('Presentation');
  sheet.addRow(['Name','UIPresentationConfig','Type','normal']);
  sheet.addRow(['Key',2,'Required',true]); sheet.addRow(['Theme tokens and localized asset overrides']);
  sheet.addRow(fields.map(()=>'sc')); sheet.addRow(fields.map(f=>f[0])); sheet.addRow(fields.map(f=>f[1]));
  rows.forEach(row=>sheet.addRow(row)); await book.xlsx.writeFile(destination);
}
if(require.main === module) (async()=>{
  const destination=path.resolve(__dirname,'..','UIPresentationConfig.xlsx');
  try { await fs.access(destination); throw new Error('Presentation workbook exists; refusing overwrite.'); }
  catch(e) { if(e.code !== 'ENOENT') throw e; }
  await createWorkbook(destination);
})().catch(e=>{console.error(e);process.exitCode=1;});
module.exports={fields,rows,createWorkbook};
