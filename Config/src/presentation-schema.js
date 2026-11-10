'use strict';
const {fields}=require('../tools/create-presentation-workbook');
function validatePresentationSchema(configurations) {
  const table=configurations.find(c=>c.name==='UIPresentationConfig');
  if(!table)return;
  if(table.type!=='normal'||table.keyCount!==2||!table.required||
    JSON.stringify(table.fields.map(f=>[f.name,f.type]))!==JSON.stringify(fields)||table.fields.some(f=>f.target!=='sc'))
    throw new Error('UIPresentationConfig requires the documented Group/Key schema.');
  const themes=new Map(), kinds=new Map();
  for(const record of table.records) {
    const [group,key,kind,value,contrast,pkg]=record.values;
    if(!/^(theme\.[a-z][a-z0-9_-]*|locale\.(en|zh-CN|fr))$/.test(group)||!key.trim()||key!==key.trim()||
      !['Color','Font','Sprite','Material'].includes(kind)||!value.trim()||value!==value.trim()||
      pkg!==null&&(!pkg.trim()||pkg!==pkg.trim())) throw new Error('Invalid presentation token.');
    if(kinds.has(key)&&kinds.get(key)!==kind) throw new Error('Token kind must be consistent across themes/locales.');
    kinds.set(key,kind);
    if(kind==='Color') {
      if(!/^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$/.test(value)||contrast!==null&&!/^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$/.test(contrast))
        throw new Error('Colors must be #RRGGBB or #RRGGBBAA.');
    } else if(contrast!==null) throw new Error('HighContrast is a color-only override.');
    if(group.startsWith('locale.')&&kind!=='Font'&&kind!=='Sprite') throw new Error('Locales override Font/Sprite only.');
    if(group.startsWith('theme.')) { if(!themes.has(group))themes.set(group,new Set());themes.get(group).add(key); }
  }
  if(!themes.size) throw new Error('At least one theme is required.');
  const sets=[...themes.values()].map(s=>[...s].sort().join('\0'));
  if(sets.some(s=>s!==sets[0])) throw new Error('Themes must define identical token keys.');
  for(const r of table.records) if(!themes.values().next().value.has(r.values[1])) throw new Error('Locale token requires a theme default.');
}
module.exports={validatePresentationSchema};
