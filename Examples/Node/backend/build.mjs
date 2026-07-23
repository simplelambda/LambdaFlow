import { cp, mkdir, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const target = process.argv[2];
if (!/^(win|linux)-(x64|arm64)$/.test(target ?? '')) {
  console.error('Usage: node build.mjs <win-x64|win-arm64|linux-x64|linux-arm64>');
  process.exitCode = 2;
} else {
  const sourceDir = dirname(fileURLToPath(import.meta.url));
  const outputDir = resolve(sourceDir, 'bin', target);
  await rm(outputDir, { recursive: true, force: true });
  await mkdir(outputDir, { recursive: true });
  await cp(resolve(sourceDir, 'backend.mjs'), resolve(outputDir, 'backend.mjs'));
}
