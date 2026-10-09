'use strict';
const path = require('node:path');
const { loadConfigurations, createArtifacts } = require('./exporter');
const { generateCSharp } = require('./codegen');
const { commitDirectories } = require('./transaction');
const { validateUISchema } = require('./ui-schema');

async function exportClientPipeline({
  rootDir = path.resolve(__dirname, '..'), projectRoot = path.resolve(__dirname, '..', '..'),
  out = 'Config/Client', generated = 'Assets/YUIFramework/Examples/ConfigGenerated',
  namespace = 'YUIFramework.ConfigGenerated', sync = true, beforeSwap
} = {}) {
  const configurations = await loadConfigurations(rootDir);
  validateUISchema(configurations);
  const artifacts = createArtifacts(configurations, 'client');
  const csharp = generateCSharp(configurations, namespace);
  const json = artifacts.map(a => ({ name: `${a.name}.json`, content: a.json }));
  const bytes = artifacts.map(a => ({ name: `${a.name}.bytes`, content: a.bytes }));
  const code = [{ name: 'ConfigBindings.g.cs', content: Buffer.from(csharp) }];
  const outputs = [
    { directory: path.join(out, 'json'), files: json },
    { directory: path.join(out, 'bytes'), files: bytes },
    { directory: path.join(out, 'generated'), files: code }
  ];
  if (sync) outputs.push(
    { directory: 'Assets/YUIFramework/ConfigData/Editor/json', files: json },
    { directory: 'Assets/Resources/YUIConfig', files: bytes },
    { directory: generated, files: code });
  await commitDirectories(projectRoot, outputs, beforeSwap);
  return { configurations, projectRoot };
}
module.exports = { exportClientPipeline };
