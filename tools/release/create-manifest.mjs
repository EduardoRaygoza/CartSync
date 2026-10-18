import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { basename } from 'node:path';

const required = name => process.env[name] ?? (() => { throw new Error(`${name} is required`); })();
const digestFile = async path => createHash('sha256').update(await readFile(path)).digest('hex');
const fileArtifact = async path => ({ file: basename(path), sha256: await digestFile(path) });

const manifest = {
  schemaVersion: 1,
  releaseTag: required('RELEASE_TAG'),
  sourceCommit: required('SOURCE_COMMIT'),
  sourceTree: required('SOURCE_TREE'),
  createdAt: new Date().toISOString(),
  toolchains: {
    node: required('NODE_VERSION'),
    dotnet: required('DOTNET_VERSION'),
    openApiGenerator: '7.16.0',
  },
  artifacts: {
    web: await fileArtifact(required('WEB_ARCHIVE')),
    apiImage: {
      repository: required('API_REPOSITORY'), digest: required('API_DIGEST'),
      archive: basename(required('API_IMAGE_ARCHIVE')), sha256: await digestFile(required('API_IMAGE_ARCHIVE')),
    },
    migrator: await fileArtifact(required('MIGRATOR_ARCHIVE')),
    migratorImage: {
      repository: required('MIGRATOR_REPOSITORY'), digest: required('MIGRATOR_DIGEST'),
      archive: basename(required('MIGRATOR_IMAGE_ARCHIVE')), sha256: await digestFile(required('MIGRATOR_IMAGE_ARCHIVE')),
    },
    bicep: await fileArtifact(required('BICEP_ARCHIVE')),
    openApi: await fileArtifact(required('OPENAPI_FILE')),
    generatedClient: await fileArtifact(required('GENERATED_CLIENT_ARCHIVE')),
  },
  manifestDigest: '',
};

const normalized = JSON.stringify(manifest);
manifest.manifestDigest = `sha256:${createHash('sha256').update(normalized).digest('hex')}`;
await writeFile(process.argv[2] ?? 'release-manifest.json', `${JSON.stringify(manifest, null, 2)}\n`);
