const origin = process.argv[2];
if (!origin) throw new Error('API origin is required.');
for (const path of ['/health/live', '/health/ready']) {
  const response = await fetch(`${origin}${path}`);
  if (!response.ok) throw new Error(`${path} returned ${response.status}.`);
  const text = await response.text();
  if (/token|connectionstring|product|trip/i.test(text)) throw new Error(`${path} exposed prohibited data.`);
}
process.stdout.write(`Smoke checks passed for ${origin}.\n`);
