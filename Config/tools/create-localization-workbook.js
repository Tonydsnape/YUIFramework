'use strict';
const ExcelJS = require('exceljs');
const fs = require('node:fs/promises');
const path = require('node:path');
const rows = [
  ['sample.greeting', '你好，{0}！', 'Hello, {0}!', 'Bonjour, {0} !'],
  ['sample.title', '文本本地化示例', 'Text localization sample', 'Exemple de localisation'],
  ['sample.switch', '切换语言', 'Switch language', 'Changer de langue'],
  ['sample.fallback', '此条法语为空，将回退到英语。', 'French is empty; this is the English fallback.', null],
  ['sample.theme', '切换主题', 'Switch theme', 'Changer de thème'],
  ['sample.size', '调整字号', 'Text size', 'Taille du texte'],
  ['sample.contrast', '高对比度', 'High contrast', 'Contraste élevé'],
  ['sample.motion', '减少动画', 'Reduce motion', 'Réduire les animations'],
  ['sample.close', '关闭', 'Close', 'Fermer']
];
async function createWorkbook(destination) {
  const book = new ExcelJS.Workbook();
  const sheet = book.addWorksheet('Text');
  sheet.addRow(['Name','LocalizationTextConfig','Type','normal']);
  sheet.addRow(['Key',1,'Required',true]);
  sheet.addRow(['Text only. Blank translations explicitly request fallback.']);
  sheet.addRow(['sc','sc','sc','sc']);
  sheet.addRow(['Key','zh-CN','en','fr']);
  sheet.addRow(['string','string?','string?','string?']);
  rows.forEach(row => sheet.addRow(row));
  await book.xlsx.writeFile(destination);
}
if (require.main === module) (async () => {
  const destination = path.resolve(__dirname,'..','LocalizationTextConfig.xlsx');
  try { await fs.access(destination); throw new Error('Workbook already exists; refusing to overwrite translations.'); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  await createWorkbook(destination);
  console.log(destination);
})().catch(error => { console.error(error); process.exitCode = 1; });
module.exports = { createWorkbook, rows };
