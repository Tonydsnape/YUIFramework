'use strict';
const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID } = require('node:crypto');

async function exists(file) {
  try { await fs.lstat(file); return true; }
  catch (error) { if (error.code === 'ENOENT') return false; throw error; }
}

async function safeTarget(projectRoot, target) {
  const relative = path.relative(projectRoot, target);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative) ||
      relative.split(path.sep).length < 2)
    throw new Error(`Output must be a dedicated managed subdirectory of project: ${target}`);
  let current = projectRoot;
  for (const segment of relative.split(path.sep)) {
    current = path.join(current, segment);
    if (await exists(current) && (await fs.lstat(current)).isSymbolicLink())
      throw new Error(`Symlink outputs are not supported: ${current}`);
  }
}

async function rejectLinks(directory) {
  for (const entry of await fs.readdir(directory, { withFileTypes: true })) {
    if (entry.isSymbolicLink()) throw new Error(`Symlink in managed directory: ${entry.name}`);
    if (entry.isDirectory()) await rejectLinks(path.join(directory, entry.name));
  }
}

function validName(name) {
  if (typeof name !== 'string' || path.basename(name) !== name ||
      /[\\/:*?"<>|\x00-\x1f]/.test(name) || name === '.' || name === '..' ||
      name === '.config-owned.json') throw new Error(`Unsafe artifact name: ${name}`);
}

// A synchronous reader must not import during the swap. Failed swaps restore every
// directory, including its metas and unrelated files; process crashes are not a DB transaction.
async function commitDirectories(projectRoot, outputs, beforeSwap = async () => {}) {
  projectRoot = await fs.realpath(projectRoot);
  const targets = outputs.map(output => path.resolve(projectRoot, output.directory));
  for (const target of targets) await safeTarget(projectRoot, target);
  for (let a = 0; a < targets.length; a++) for (let b = a + 1; b < targets.length; b++) {
    const rel = path.relative(targets[a], targets[b]);
    const reverse = path.relative(targets[b], targets[a]);
    if (!rel || !rel.startsWith('..') || !reverse.startsWith('..'))
      throw new Error('Managed output directories must not overlap.');
  }
  const lock = path.join(projectRoot, 'Config', '.export-lock');
  await fs.mkdir(path.dirname(lock), { recursive: true });
  await fs.mkdir(lock); // Reject concurrent exporters, rather than overwrite each other's backup.
  const staged = [];
  let committed = false;
  let recoveryFailed = false;
  try {
    for (let index = 0; index < outputs.length; index++) {
      const target = targets[index];
      const token = randomUUID();
      const item = { target, staging: `${target}.staging-${token}`,
        backup: `${target}.backup-${token}`, old: false, installed: false };
      staged.push(item);
      await fs.mkdir(path.dirname(target), { recursive: true });
      if (await exists(target)) {
        await rejectLinks(target);
        await fs.cp(target, item.staging, { recursive: true });
      } else await fs.mkdir(item.staging);
      const marker = path.join(item.staging, '.config-owned.json');
      const previous = await exists(marker) ? JSON.parse(await fs.readFile(marker, 'utf8')) : [];
      if (!Array.isArray(previous)) throw new Error('Invalid managed artifact manifest');
      previous.forEach(validName);
      const files = outputs[index].files;
      const next = new Set(files.map(file => { validName(file.name); return file.name; }));
      if (next.size !== files.length) throw new Error('Duplicate output filename');
      for (const name of previous) if (!next.has(name)) {
        await fs.rm(path.join(item.staging, name), { force: true });
        await fs.rm(path.join(item.staging, `${name}.meta`), { force: true });
      }
      for (const file of files) {
        if (!previous.includes(file.name) && await exists(path.join(item.staging, file.name)))
          throw new Error(`Refusing to replace unmanaged file: ${target}/${file.name}`);
        await fs.writeFile(path.join(item.staging, file.name), file.content);
      }
      await fs.writeFile(marker, `${JSON.stringify([...next].sort(), null, 2)}\n`);
    }
    for (let index = 0; index < staged.length; index++) {
      const item = staged[index];
      await beforeSwap(index, item.target);
      if (await exists(item.target)) { await fs.rename(item.target, item.backup); item.old = true; }
      await fs.rename(item.staging, item.target);
      item.installed = true;
    }
    committed = true;
  } catch (error) {
    const errors = [error];
    for (const item of [...staged].reverse()) {
      try {
        if (item.installed) await fs.rm(item.target, { recursive: true });
        if (item.old) await fs.rename(item.backup, item.target);
      } catch (rollback) { errors.push(rollback); recoveryFailed = true; }
    }
    if (errors.length > 1) throw new AggregateError(errors, 'Export failed; preserve backup directories for recovery.');
    throw error;
  } finally {
    for (const item of staged) {
      if (await exists(item.staging)) await fs.rm(item.staging, { recursive: true });
      if (committed && await exists(item.backup)) await fs.rm(item.backup, { recursive: true });
    }
    if (!recoveryFailed) await fs.rmdir(lock);
  }
}
module.exports = { commitDirectories };
