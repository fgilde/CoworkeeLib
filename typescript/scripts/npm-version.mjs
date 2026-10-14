// The npm version of a Coworkee release: NuGet's a.b.c and a.b.c-pre stay, a.b.c.d (npm knows no fourth part) becomes a.b.(c*1000+d).
//   node scripts/npm-version.mjs <version>                    prints the npm version
//   node scripts/npm-version.mjs --set <version>              writes it into both packages (@coworkee/airworks then needs that @coworkee/client)
//   node scripts/npm-version.mjs --template <version> <in> <out>  replaces COWORKEE_NPM_VERSION in a file (the template's package.json)
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

export function npmVersion(version) {
  const text = String(version ?? '').trim().replace(/^v/, '');
  if (/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(text)) return text;
  const four = /^(\d+)\.(\d+)\.(\d+)\.(\d+)$/.exec(text);
  if (four && Number(four[4]) < 1000) return `${four[1]}.${four[2]}.${Number(four[3]) * 1000 + Number(four[4])}`;
  throw new Error(`'${version}' is no version npm can take`);
}

function setVersion(version) {
  for (const name of ['client', 'airworks']) {
    const file = fileURLToPath(new URL(`../${name}/package.json`, import.meta.url));
    const manifest = JSON.parse(readFileSync(file, 'utf8'));
    manifest.version = version;
    if (manifest.dependencies?.['@coworkee/client']) manifest.dependencies['@coworkee/client'] = version;
    writeFileSync(file, JSON.stringify(manifest, null, 2) + '\n');
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const [mode, version, input, output] = process.argv.slice(2);
  if (mode === '--set') setVersion(npmVersion(version));
  else if (mode === '--template') writeFileSync(output, readFileSync(input, 'utf8').replaceAll('COWORKEE_NPM_VERSION', npmVersion(version)));
  else console.log(npmVersion(mode));
}
