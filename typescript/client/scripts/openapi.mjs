// Refreshes openapi.json from the OpenAPI document of a Coworkee app (an address like https://localhost:7301/openapi/v1.json or a file):
// keeps the library's endpoints, drops those of the app and the schemas nothing refers to any more.
import { readFile, writeFile } from 'node:fs/promises';

const source = process.argv[2];
if (!source) {
  console.error('usage: npm run openapi -- <url or file of /openapi/v1.json>');
  process.exit(1);
}

// endpoints of the template app and its samples, not of the library
const appPaths = [/^\/api\/v1\/system\//, /^\/api\/v1\/(brands|products|dashboard|documents|document-types|sales-orders)(\/|$)/];

const text = /^https?:/.test(source)
  ? await (await fetch(source)).text()
  : await readFile(source, 'utf8');
const document = JSON.parse(text);

document.paths = Object.fromEntries(Object.entries(document.paths).filter(([path]) => !appPaths.some((pattern) => pattern.test(path))));

const used = new Set();
const visit = (node) => {
  if (Array.isArray(node)) return node.forEach(visit);
  if (!node || typeof node !== 'object') return;
  for (const [key, value] of Object.entries(node)) {
    if (key === '$ref' && typeof value === 'string' && value.startsWith('#/components/schemas/')) {
      const name = value.slice('#/components/schemas/'.length);
      if (!used.has(name)) {
        used.add(name);
        visit(document.components.schemas[name]);
      }
    } else {
      visit(value);
    }
  }
};
visit(document.paths);
document.components.schemas = Object.fromEntries(Object.keys(document.components.schemas).sort().filter((name) => used.has(name)).map((name) => [name, document.components.schemas[name]]));
document.info = { ...document.info, title: 'Coworkee API', version: 'v1' };
delete document.servers;

await writeFile(new URL('../openapi.json', import.meta.url), JSON.stringify(document, null, 2) + '\n');
console.log(`openapi.json: ${Object.keys(document.paths).length} paths, ${used.size} schemas`);
