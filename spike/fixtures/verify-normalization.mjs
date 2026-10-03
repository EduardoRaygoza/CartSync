import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const fixtures = JSON.parse(await readFile(new URL('./name-normalization.json', import.meta.url), 'utf8'));
const normalizeName = value => value.normalize('NFKC').trim().replace(/\s+/gu, ' ').toUpperCase();
for (const fixture of fixtures) assert.equal(normalizeName(fixture.source), fixture.normalized);
console.log(`Verified ${fixtures.length} Angular/JavaScript normalization fixtures.`);
