#!/usr/bin/env node
// 抓取 9 个页面的基线截图（默认 zh-CN / 浅色主题 / 1200x800 视口）。
//
// 前置条件：
//   1. WASM 站点已在本机 8090 端口服务（见 tools/README.md）
//   2. 内网 Chromium 可访问 localhost:8090
//   3. 首次运行需先关闭遥测同意弹窗与启动引导（否则会进入截图）
//
// 用法：node capture-baseline.mjs [输出目录]

import { mkdirSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { withPage, click, sleep, screenshot, evaluate, send } from './cdp.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const OUT = resolve(process.argv[2] ?? resolve(here, '../assets/before'));
const APP_URL = process.env.APP_URL || 'http://localhost:8090/';

// 1200x800 视口下左栏导航项的文字中心 y（由 png_analyze.py 标定）
const PAGES = [
  ['01-home', 117],
  ['02-member-management', 169],
  ['03-venue-configuration', 221],
  ['04-freeform-management', 273],
  ['05-strategy-configuration', 325],
  ['06-seating-arrangement', 377],
  ['07-snapshot-history', 429],
  ['08-settings', 715],
  ['09-about', 767],
];

mkdirSync(OUT, { recursive: true });

await withPage(async (ws, page) => {
  const vp = await evaluate(ws, '({ w: innerWidth, h: innerHeight })');
  console.log('viewport:', JSON.stringify(vp));
  if (vp?.w !== 1200 || vp?.h !== 800) {
    console.warn('⚠️  视口不是 1200x800，导航坐标可能不准确。请先用 MCP resize_page 调整。');
  }
  if (!page.url.includes('8090')) {
    console.log('页面不在应用上，先导航…');
    await send(ws, 'Page.navigate', { url: APP_URL });
    await sleep(18000);
  }
  for (const [name, y] of PAGES) {
    await click(ws, 70, y);
    await sleep(1600);
    const out = `${OUT}/${name}.png`;
    await screenshot(ws, out);
    console.log('captured', out);
  }
});
console.log('done:', OUT);
