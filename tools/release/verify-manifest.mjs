import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import Ajv2020 from 'ajv/dist/2020.js';

const path = process.argv[2] ?? 'release-manifest.json';
const manifest = JSON.parse(await readFile(path, 'utf8'));
const schema = JSON.parse(await readFile('release/release-manifest.schema.json', 'utf8'));
const validate = new Ajv2020({ allErrors: true, formats: { 'date-time': true } }).compile(schema);
if (!validate(manifest)) throw new Error(`Release manifest schema validation failed: ${JSON.stringify(validate.errors)}`);
const sha256 = value => createHash('sha256').update(value).digest('hex');
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const shaPattern = /^[0-9a-f]{64}$/;
const required = ['schemaVersion', 'releaseTag', 'sourceCommit', 'sourceTree', 'createdAt', 'toolchains', 'artifacts', 'manifestDigest'];
for (const key of required) if (!(key in manifest)) throw new Error(`Manifest field ${key} is missing.`);
assert(Object.keys(manifest).length === required.length, 'Manifest has unsupported top-level fields.');
assert(manifest.schemaVersion === 1, 'Unsupported manifest schema.');
assert(/^v\d+\.\d+\.\d+-dogfood\.\d+$/.test(manifest.releaseTag), 'Invalid release tag.');
assert(/^[0-9a-f]{40}$/.test(manifest.sourceCommit) && /^[0-9a-f]{40}$/.test(manifest.sourceTree), 'Invalid source identity.');
assert(!Number.isNaN(Date.parse(manifest.createdAt)), 'Invalid creation timestamp.');
assert(Object.keys(manifest.toolchains).sort().join(',') === 'dotnet,node,openApiGenerator', 'Invalid toolchain fields.');
for (const value of Object.values(manifest.toolchains)) assert(typeof value === 'string' && value.length > 0, 'Invalid toolchain version.');

const fileArtifactNames = ['web', 'migrator', 'bicep', 'openApi', 'generatedClient'];
const imageArtifactNames = ['apiImage', 'migratorImage'];
assert(Object.keys(manifest.artifacts).sort().join(',') === [...fileArtifactNames, ...imageArtifactNames].sort().join(','), 'Invalid artifact fields.');
for (const name of fileArtifactNames) {
  const artifact = manifest.artifacts[name];
  assert(Object.keys(artifact).sort().join(',') === 'file,sha256', `Invalid ${name} fields.`);
  assert(typeof artifact.file === 'string' && artifact.file.length > 0 && !artifact.file.includes('/'), `Invalid ${name} file.`);
  assert(shaPattern.test(artifact.sha256), `Invalid ${name} digest.`);
}
for (const name of imageArtifactNames) {
  const artifact = manifest.artifacts[name];
  assert(Object.keys(artifact).sort().join(',') === 'archive,digest,repository,sha256', `Invalid ${name} fields.`);
  assert(typeof artifact.repository === 'string' && artifact.repository.length > 0, `Invalid ${name} repository.`);
  assert(/^sha256:[0-9a-f]{64}$/.test(artifact.digest), `Invalid ${name} OCI digest.`);
  assert(typeof artifact.archive === 'string' && artifact.archive.length > 0 && !artifact.archive.includes('/'), `Invalid ${name} archive.`);
  assert(shaPattern.test(artifact.sha256), `Invalid ${name} archive digest.`);
}
const expected = manifest.manifestDigest;
manifest.manifestDigest = '';
const actual = `sha256:${sha256(JSON.stringify(manifest))}`;
if (actual !== expected) throw new Error(`Manifest digest mismatch: expected ${expected}, calculated ${actual}.`);

if (process.argv.includes('--verify-files')) {
  const directory = dirname(path);
  for (const name of fileArtifactNames) {
    const artifact = manifest.artifacts[name];
    assert(sha256(await readFile(join(directory, artifact.file))) === artifact.sha256, `${name} file digest mismatch.`);
  }
  for (const name of imageArtifactNames) {
    const artifact = manifest.artifacts[name];
    assert(sha256(await readFile(join(directory, artifact.archive))) === artifact.sha256, `${name} archive digest mismatch.`);
  }
}
process.stdout.write(`${expected}\n`);
