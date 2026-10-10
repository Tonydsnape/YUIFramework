'use strict';
const test=require('node:test'), assert=require('node:assert/strict'), fs=require('node:fs/promises'),path=require('node:path');
const {decode}=require('@msgpack/msgpack');
const {createWorkbook}=require('../tools/create-presentation-workbook');
const {loadConfigurations,createArtifacts}=require('../src/exporter');
const {validatePresentationSchema}=require('../src/presentation-schema');
const {generateCSharp}=require('../src/codegen');
const ExcelJS=require('exceljs');
const {exportClientPipeline}=require('../src/unity-pipeline');
test('presentation typed color/font/sprite/material groups roundtrip and reject malformed mappings',async t=>{
  const dir=path.resolve(__dirname,'..','.test-work','presentation');
  await fs.mkdir(dir,{recursive:true});t.after(()=>fs.rm(dir,{recursive:true,force:true}));
  await createWorkbook(path.join(dir,'UIPresentationConfig.xlsx'));
  const tables=await loadConfigurations(dir);validatePresentationSchema(tables);
  const [artifact]=createArtifacts(tables,'client');
  assert.deepEqual(decode(artifact.bytes),JSON.parse(artifact.json));
  assert.match(generateCSharp(tables),/UIPresentationCatalog.Table/);
  const ordinary=structuredClone(tables);ordinary[0].name='constructor';
  const generated=generateCSharp(ordinary);
  assert.match(generated,/constructorRow/);
  assert.doesNotMatch(generated,/native code/);
  for(const mutate of [
    t=>t.records[0].values[3]='#NOPE',
    t=>t.records[0].values[2]='Sprite',
    t=>t.records[0].values[0]='locale.zz',
    t=>t.records.splice(0,1),
    t=>t.records.at(-1).values[1]='unknown',
    t=>t.records.at(-1).values[5]='   '
  ]) {
    const copy=structuredClone(tables);mutate(copy[0]);assert.throws(()=>validatePresentationSchema(copy));
  }
});
test('invalid presentation workbook leaves every previously synchronized output byte intact',async t=>{
  const parent=path.resolve(__dirname,'..','.test-work');await fs.mkdir(parent,{recursive:true});
  const projectRoot=await fs.mkdtemp(path.join(parent,'presentation-atomic-'));
  t.after(()=>fs.rm(projectRoot,{recursive:true,force:true}));
  const rootDir=path.join(projectRoot,'Config');await fs.mkdir(rootDir);
  const workbook=path.join(rootDir,'UIPresentationConfig.xlsx');await createWorkbook(workbook);
  const options={projectRoot,rootDir};await exportClientPipeline(options);
  async function snapshot(dir,result={}) {
    for(const item of await fs.readdir(dir,{withFileTypes:true})){
      const file=path.join(dir,item.name);
      if(item.isDirectory())await snapshot(file,result);
      else if(!file.endsWith('.xlsx'))result[path.relative(projectRoot,file)]=(await fs.readFile(file)).toString('base64');
    }
    return result;
  }
  const before=await snapshot(projectRoot);
  const book=new ExcelJS.Workbook();await book.xlsx.readFile(workbook);
  book.worksheets[0].getCell('D7').value='not-a-color';await book.xlsx.writeFile(workbook);
  await assert.rejects(exportClientPipeline(options));
  assert.deepEqual(await snapshot(projectRoot),before);
});
