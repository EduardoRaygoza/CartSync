import { readFile } from 'node:fs/promises';
import { parse } from 'yaml';

const checked = parse(await readFile('docs/api/openapi.yaml', 'utf8'));
const generated = JSON.parse(await readFile(
  process.argv[2] ?? 'artifacts/generated/aspnet-openapi.json',
  'utf8',
));
const fail = message => { throw new Error(`ASP.NET OpenAPI drift: ${message}`); };
const same = (left, right, label) => {
  if (JSON.stringify(left) !== JSON.stringify(right)) fail(label);
};

const checkedOperation = checked.paths?.['/sync/bootstrap']?.get;
const generatedOperation = generated.paths?.['/api/v1/sync/bootstrap']?.get;
if (!checkedOperation || !generatedOperation) fail('getBootstrapPage is missing.');

same(generatedOperation.operationId, checkedOperation.operationId, 'operationId differs.');
same(generatedOperation.summary, checkedOperation.summary, 'summary differs.');
same(Object.keys(generatedOperation.responses).sort(), Object.keys(checkedOperation.responses).sort(), 'response statuses differ.');

const resolveParameter = parameter => parameter.$ref
  ? checked.components.parameters[parameter.$ref.split('/').at(-1)]
  : parameter;
same(
  generatedOperation.parameters.map(parameter => parameter.name).sort(),
  checkedOperation.parameters.map(resolveParameter).map(parameter => parameter.name).sort(),
  'query parameters differ.',
);

const checkedSchema = checked.components.schemas.BootstrapPage;
const generatedSchema = generated.components.schemas.BootstrapPage;
same(Object.keys(generatedSchema.properties).sort(), Object.keys(checkedSchema.properties).sort(), 'BootstrapPage properties differ.');
same([...generatedSchema.required].sort(), [...checkedSchema.required].sort(), 'BootstrapPage required properties differ.');
for (const [name, schema] of Object.entries(checkedSchema.properties)) {
  const generatedTypes = [generatedSchema.properties[name].type].flat().filter(type => type !== 'null');
  if (generatedTypes.includes('integer') && generatedTypes.includes('string'))
    generatedTypes.splice(generatedTypes.indexOf('string'), 1);
  const checkedTypes = [schema.type].flat().filter(type => type !== 'null');
  same([...new Set(generatedTypes)].sort(), [...new Set(checkedTypes)].sort(), `BootstrapPage.${name} type differs.`);
}

process.stdout.write('ASP.NET walking-skeleton contract matches docs/api/openapi.yaml.\n');
