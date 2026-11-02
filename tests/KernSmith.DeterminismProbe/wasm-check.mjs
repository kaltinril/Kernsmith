#!/usr/bin/env node
// Runs the published Blazor WASM sample headless and compares the determinism hash it reports
// with the committed manifest: the browser-wasm leg of the Native determinism check.
//
//   node tests/KernSmith.DeterminismProbe/wasm-check.mjs --dir <publish>/wwwroot \
//     --manifest tests/KernSmith.Rasterizers.Native.Tests/Determinism/manifest.json \
//     [--case roboto/32px/grayscale/default] [--timeout 300] [--channel chrome]
//
// Needs the `playwright` package (npm i playwright; npx playwright install chromium). --channel
// chrome uses an installed Google Chrome instead of Playwright's Chromium. Exit 0 on a match,
// 1 on a mismatch or generation error, 2 on a setup problem.

import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import { extname, join, normalize, resolve } from 'node:path';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, all) => {
  if (a.startsWith('--')) acc.push([a.slice(2), all[i + 1] && !all[i + 1].startsWith('--') ? all[i + 1] : 'true']);
  return acc;
}, []));

const dir = args.dir ? resolve(args.dir) : null;
const manifestPath = args.manifest ? resolve(args.manifest) : null;
const caseId = args.case ?? 'roboto/32px/grayscale/default';
const timeoutSeconds = Number(args.timeout ?? 300);
if (!dir || !manifestPath) {
  console.error('usage: wasm-check.mjs --dir <wwwroot> --manifest <manifest.json> [--case <id>] [--timeout <s>] [--channel chrome]');
  process.exit(2);
}

const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const expected = manifest.cases?.[caseId]?.sha256;
if (!expected) {
  console.error(`case '${caseId}' is not in ${manifestPath}`);
  process.exit(2);
}

// Blazor needs application/wasm for streaming compilation; everything else is best effort.
const mime = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json',
  '.wasm': 'application/wasm', '.css': 'text/css', '.ttf': 'font/ttf', '.otf': 'font/otf', '.woff2': 'font/woff2',
  '.dat': 'application/octet-stream', '.pdb': 'application/octet-stream', '.dll': 'application/octet-stream',
  '.webcil': 'application/octet-stream', '.ico': 'image/x-icon', '.png': 'image/png', '.svg': 'image/svg+xml',
};

const server = createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    let path = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '');
    let file = join(dir, path);
    if (!file.startsWith(dir)) throw new Error('outside root');
    let info = await stat(file).catch(() => null);
    if (!info || info.isDirectory()) { file = join(dir, 'index.html'); info = await stat(file); }
    res.writeHead(200, { 'Content-Type': mime[extname(file).toLowerCase()] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
    res.end(await readFile(file));
  } catch {
    res.writeHead(404);
    res.end();
  }
});
await new Promise((ok) => server.listen(0, '127.0.0.1', ok));
const port = server.address().port;
const pageUrl = `http://127.0.0.1:${port}/?backend=native&font=fonts/Roboto-Regular.ttf`;

let exitCode = 2;
let browser;
try {
  const { chromium } = await import('playwright');
  browser = await chromium.launch(args.channel ? { channel: args.channel } : {});
  const page = await browser.newPage();
  page.on('console', (m) => { if (m.type() === 'error') console.error(`[browser] ${m.text()}`); });
  page.on('pageerror', (e) => console.error(`[browser] ${e.message}`));
  console.log(`loading ${pageUrl}`);
  await page.goto(pageUrl, { waitUntil: 'load' });
  const outcome = await Promise.race([
    page.waitForSelector('#determinism-hash', { state: 'attached', timeout: timeoutSeconds * 1000 }).then(() => 'hash'),
    page.waitForSelector('#generation-error', { state: 'attached', timeout: timeoutSeconds * 1000 }).then(() => 'error'),
  ]);
  if (outcome === 'error') {
    console.error(`generation failed in the browser:\n${await page.locator('#generation-error').innerText()}`);
    exitCode = 1;
  } else {
    const actual = (await page.locator('#determinism-hash').innerText()).trim();
    const backend = (await page.locator('#generated-backend').innerText().catch(() => '?')).trim();
    const elapsed = (await page.locator('#generation-elapsed').innerText().catch(() => '?')).trim();
    console.log(`backend ${backend}, generated in ${elapsed} ms`);
    console.log(`expected ${expected}\nactual   ${actual}`);
    if (backend !== 'Native') {
      console.error(`the sample generated with ${backend}, not Native`);
      exitCode = 1;
    } else if (actual === expected) {
      console.log(`OK ${caseId}: the browser's Native output matches the manifest`);
      exitCode = 0;
    } else {
      console.error(`MISMATCH ${caseId}: the browser's Native output differs from the manifest`);
      exitCode = 1;
    }
  }
} catch (e) {
  console.error(e?.stack ?? String(e));
  exitCode = exitCode === 2 ? 2 : 1;
} finally {
  await browser?.close();
  server.close();
}
process.exit(exitCode);
