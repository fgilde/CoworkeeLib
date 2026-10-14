// Builds @coworkee/client and @coworkee/airworks and packs them as coworkee-client.tgz and coworkee-airworks.tgz into the given folder;
// the template ships them until the packages are on npm.
import { execSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, renameSync, rmSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';

const root = resolve(import.meta.dirname, '..');
const out = resolve(process.argv[2] ?? join(root, 'dist'));
const run = (command) => execSync(command, { cwd: root, stdio: 'inherit' });

// npm records each install in node_modules/.package-lock.json; a newer lock file means dependencies changed since
const installed = join(root, 'node_modules', '.package-lock.json');
if (!existsSync(installed) || statSync(join(root, 'package-lock.json')).mtimeMs > statSync(installed).mtimeMs) run('npm ci --no-audit --no-fund');
run('npm run build');
mkdirSync(out, { recursive: true });
for (const name of ['client', 'airworks']) {
  rmSync(join(out, `coworkee-${name}.tgz`), { force: true });
  run(`npm pack --workspace ${name} --pack-destination "${out}"`);
  const packed = readdirSync(out).find((file) => file.startsWith(`coworkee-${name}-`) && file.endsWith('.tgz'));
  renameSync(join(out, packed), join(out, `coworkee-${name}.tgz`));
}
