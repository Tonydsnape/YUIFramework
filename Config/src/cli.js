#!/usr/bin/env node
'use strict';
const path = require('node:path');
const { exportClientPipeline } = require('./unity-pipeline');
const { loadConfigurations, createArtifacts } = require('./exporter');
const { commitDirectories } = require('./transaction');
const { validateUISchema } = require('./ui-schema');
const { generateCSharp } = require('./codegen');
const { validateLocalizationSchema } = require('./localization-schema');
const { validatePresentationSchema } = require('./presentation-schema');

async function main(argv) {
  const options = { rootDir: path.resolve(__dirname, '..'), projectRoot: path.resolve(__dirname, '..', '..') };
  let target = 'client', validate = false;
  const names = { '--input': 'rootDir', '--project-root': 'projectRoot', '--out': 'out',
    '--generated': 'generated', '--namespace': 'namespace' };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === '--validate') validate = true;
    else if (arg === '--no-unity-sync') options.sync = false;
    else {
      const name = names[arg];
      if (!name && arg !== '--target') throw new Error(`Unknown option ${arg}`);
      const value = argv[++i];
      if (!value || value.startsWith('--')) throw new Error(`Missing value for ${arg}`);
      if (arg === '--target') target = value;
      else options[name] = value;
    }
  }
  if (!['client', 'server'].includes(target)) throw new Error('Target must be client or server');
  if (validate) {
    const configurations = await loadConfigurations(options.rootDir);
    validateUISchema(configurations);
    generateCSharp(configurations, options.namespace);
    createArtifacts(configurations, target);
    console.log(`Valid: ${configurations.length} tables`); return;
  }
  if (target === 'client') {
    const result = await exportClientPipeline(options);
    console.log(`Exported ${result.configurations.length} tables and typed bindings`);
  } else {
    const configurations = await loadConfigurations(options.rootDir);
    validateLocalizationSchema(configurations);
    validatePresentationSchema(configurations);
    const artifacts = createArtifacts(configurations, 'server');
    await commitDirectories(options.projectRoot, ['json', 'bytes'].map(kind => ({
      directory: path.join(options.out || 'Config/Server', kind),
      files: artifacts.map(a => ({ name: `${a.name}.${kind}`, content: a[kind] }))
    })));
    console.log(`Exported ${artifacts.length} server tables`);
  }
}
if (require.main === module) main(process.argv.slice(2)).catch(error => { console.error(error); process.exitCode = 1; });
module.exports = { main };
