'use strict';
const { fields } = require('../tools/create-ui-workbook');
function validateUISchema(configurations) {
  const table = configurations.find(config => config.name === 'UISettings');
  if (!table) return; // Generic exporters need not contain a UI table.
  if (table.type !== 'normal' || table.keyCount !== 2 ||
      JSON.stringify(table.fields.map(field => [field.name, field.type])) !== JSON.stringify(fields) ||
      table.fields.some(field => field.target !== 'sc'))
    throw new Error('UISettings must use the documented full UI schema and Profile/Id composite key.');
  if (!table.required) throw new Error('UISettings must be required.');
  for (const record of table.records) {
    const row = Object.fromEntries(table.fields.map((field, index) => [field.name, record.values[index]]));
    for (const name of ['Profile', 'Id', 'PrefabKey'])
      if (!row[name].trim()) throw new Error(`UISettings.${name} cannot be blank.`);
    for (const name of ['PrefabPackage', 'CustomTransitionId'])
      if (row[name] !== null && !row[name].trim()) throw new Error(`UISettings.${name} cannot be whitespace.`);
    if (![0,100,200,300,400,500,600,650,700,800].includes(row.Layer) ||
        row.TransitionType < 0 || row.TransitionType > 7) throw new Error('Invalid UI layer/transition.');
    for (const name of ['MaxPoolSize', 'PreloadCount', 'ShowDuration', 'HideDuration', 'PoolIdleTimeoutSeconds', 'SlideDistance'])
      if (row[name] < 0) throw new Error(`Negative UISettings.${name}`);
    if (row.StartScale <= 0 || row.PreloadCount > row.MaxPoolSize ||
        (row.PreloadCount > 0 && !row.CacheOnClose) ||
        (row.UseTransition && row.TransitionType === 7 && !row.CustomTransitionId))
      throw new Error('Invalid UI prewarm/scale/custom transition.');
  }
}
module.exports = { validateUISchema };
