#!/usr/bin/env node
// 抓取 7 个页面的基线截图（新 IA：1200x800 / zh-CN / 浅色主题）。
// 明暗 × 中英完整矩阵见 08-implementation-log.md 的 M6 小节（emulate prefers-color-scheme + 语言注入）。
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
const OUT = resolve(process.argv[2] ?? resolve(here, '../assets/after'));
const APP_URL = process.env.APP_URL || 'http://localhost:8090/';

// 1200x800 视口下左栏导航项的文字中心 y（新 IA 分组侧栏，M6 重新标定）
const PAGES = [
  ['01-seating-workbench', 142],
  ['02-member-management', 211],
  ['03-venue-configuration', 247],
  ['04-strategy-configuration', 317],
  ['05-snapshot-history', 381],
  ['06-settings', 740],
  ['07-about', 775],
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
