#!/usr/bin/env node
// 将 seed-demo-data.cs 生成的演示数据注入浏览器 IndexedDB（WASM 端存储）。
// 用法：先运行 `dotnet run seed-demo-data.cs`，再运行本脚本，然后重载页面。
import { readFileSync } from 'node:fs';
import { withPage, send, sleep } from './cdp.mjs';

const SEED_DIR = process.env.SEED_DIR || '/tmp/seatflow-seed';
const FILES = [
  { local: `${SEED_DIR}/demo-venue-01.venue.json`, storeKey: 'Venues/demo-venue-01.venue.json' },
  { local: `${SEED_DIR}/demo-venue-polar.venue.json`, storeKey: 'Venues/demo-venue-polar.venue.json' },
  { local: `${SEED_DIR}/demo-venue-freeform.venue.json`, storeKey: 'Venues/demo-venue-freeform.venue.json' },
  { local: `${SEED_DIR}/demo-venue-large.venue.json`, storeKey: 'Venues/demo-venue-large.venue.json' },
  { local: `${SEED_DIR}/demo-venue-huge.venue.json`, storeKey: 'Venues/demo-venue-huge.venue.json' },
  { local: `${SEED_DIR}/demo-roster-01.roster.json`, storeKey: 'Rosters/demo-roster-01.roster.json' },
  { local: `${SEED_DIR}/AppSettings.json`, storeKey: 'AppSettings.json' },
];

const payload = FILES.map(f => ({
  key: f.storeKey,
  b64: readFileSync(f.local).toString('base64'),
}));

const expression = `(async () => {
  const items = ${JSON.stringify(payload)};
  const open = indexedDB.open('seatflow', 1);
  const db = await new Promise((res, rej) => {
    open.onupgradeneeded = () => {
      const d = open.result;
      if (!d.objectStoreNames.contains('files')) d.createObjectStore('files', { keyPath: 'path' });
    };
    open.onsuccess = () => res(open.result);
    open.onerror = () => rej(open.error);
  });
  const bytes = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));
  for (const it of items) {
    await new Promise((res, rej) => {
      const t = db.transaction('files', 'readwrite');
      t.objectStore('files').put({ path: it.key, data: bytes(it.b64) });
      t.oncomplete = () => res();
      t.onerror = () => rej(t.error);
    });
  }
  return items.map(i => i.key);
})()`;

await withPage(async ws => {
  const r = await send(ws, 'Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails));
  console.log('injected:', r.result?.value);
  console.log('reloading page…');
  await send(ws, 'Page.reload', { ignoreCache: false });
  await sleep(18000);
});
console.log('done');
