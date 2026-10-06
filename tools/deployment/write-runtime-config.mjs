import { createHash } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';
import { dirname } from 'node:path';

const required = name => process.env[name] ?? (() => { throw new Error(`${name} is required`); })();
const target = process.argv[2] ?? 'runtime-config.json';
const config = {
  environment: required('CARTSYNC_ENVIRONMENT'),
  apiOrigin: required('CARTSYNC_API_ORIGIN'),
  externalIdAuthority: required('EXTERNAL_ID_AUTHORITY'),
  externalIdClientId: required('EXTERNAL_ID_SPA_CLIENT_ID'),
  externalIdScope: required('EXTERNAL_ID_SCOPE'),
  buildVersion: required('CARTSYNC_BUILD_VERSION'),
};
const content = `${JSON.stringify(config, null, 2)}\n`;
await mkdir(dirname(target), { recursive: true });
await writeFile(target, content);
process.stdout.write(`sha256:${createHash('sha256').update(content).digest('hex')}\n`);
