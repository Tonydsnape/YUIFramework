'use strict';
const tableName = 'LocalizationTextConfig';
const supportedLocales = ['en', 'zh-CN', 'fr'];
function argumentsIn(text) {
  const indices = new Set();
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (c !== '{' && c !== '}') continue;
    if (text[i + 1] === c) { i++; continue; }
    if (c === '}') throw new Error('Unmatched closing brace in localized text.');
    const end = text.indexOf('}', i);
    const argument = text.slice(i + 1, end);
    if (end < 0 || !/^(?:[0-9]|1[0-5])$/.test(argument))
      throw new Error('Localized formats support {0}..{15} and escaped braces only.');
    indices.add(Number(argument));
    i = end;
  }
  return [...indices].sort((a,b) => a-b).join(',');
}
function validateLocalizationSchema(configurations) {
  const table = configurations.find(config => config.name === tableName);
  if (!table) return;
  if (table.type !== 'normal' || table.keyCount !== 1 || !table.required ||
      table.fields[0]?.name !== 'Key' || table.fields[0]?.type !== 'string' ||
      table.fields.some(field => field.target !== 'sc'))
    throw new Error('LocalizationTextConfig requires normal, required, Key/string, Key=1 and sc columns.');
  const locales = table.fields.slice(1);
  if (!locales.some(f => f.name === 'en') || !locales.some(f => f.name === 'zh-CN') ||
      locales.some(f => f.type !== 'string?' || !supportedLocales.includes(f.name)))
    throw new Error('Locale columns require string?, en/zh-CN and optional fr. Extend both validated locale allowlists for other locales.');
  for (const record of table.records) {
    const [key,...translations] = record.values;
    if (!key.trim() || key !== key.trim()) throw new Error('Localization keys must be nonempty and trimmed.');
    let signature;
    for (const text of translations) {
      if (text === null || !text.trim()) continue;
      const found = argumentsIn(text);
      if (signature !== undefined && signature !== found)
        throw new Error(`Localization ${key}: translations must have the same positional arguments.`);
      signature = found;
    }
  }
}
module.exports = { tableName, argumentsIn, validateLocalizationSchema };
