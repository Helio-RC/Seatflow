#!/usr/bin/env node
// SeatFlow UI 重构工作流工具：通过 CDP 直接驱动内网 Chromium（与 chrome-devtools MCP 同一浏览器实例）。
//
// 背景：Avalonia WASM 渲染在 canvas 上，DOM 无控件节点，MCP 的 uid 点击不可用；
// 且合成 DOM 事件会因 pointer capture 失效。因此输入统一走 CDP Input.* 可信事件。
//
// 用法：
//   node cdp.mjs list                        列出页面 target
//   node cdp.mjs nav <url>                   导航到 URL 并等待
//   node cdp.mjs click <x> <y>               在视口坐标点击（CSS px）
//   node cdp.mjs key <Tab|Enter|Escape|...>  发送按键
//   node cdp.mjs eval "<expression>"         在页面执行 JS 并打印结果（支持 await）
//   node cdp.mjs shot <out.png>              保存视口截图
//   node cdp.mjs size <w> <h>                设置视口尺寸（替代 MCP resize，确定性更高）
//
// 环境变量：CDP_HTTP（默认 http://localhost:3000）、PAGE_MATCH（默认 localhost:8090）

import { writeFileSync } from 'node:fs';

const CDP_HTTP = process.env.CDP_HTTP || 'http://localhost:3000';
const PAGE_MATCH = process.env.PAGE_MATCH || 'localhost:8090';

async function createPage(url) {
  // Chrome 需要 PUT /json/new?url=... 新建标签页
  const res = await fetch(`${CDP_HTTP}/json/new?${encodeURIComponent(url)}`, { method: 'PUT' });
  if (!res.ok) throw new Error(`create page failed: ${res.status}`);
  return await res.json();
}

export async function findPage(match = PAGE_MATCH) {
  const res = await fetch(`${CDP_HTTP}/json/list`);
  const targets = await res.json();
  const page = targets.find(t => t.type === 'page' && t.url.includes(match))
    ?? targets.find(t => t.type === 'page');
  if (page) return page;

  // 浏览器被重置后自动补一个页面（R-01 缓解：脚本幂等）
  const created = await createPage(process.env.PAGE_URL || 'about:blank');
  await sleep(1500);
  return created;
}

export function connect(wsUrl) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(wsUrl);
    ws.addEventListener('open', () => resolve(ws));
    ws.addEventListener('error', () => reject(new Error('ws connect failed: ' + wsUrl)));
  });
}

export function send(ws, method, params = {}, timeoutMs = 15000) {
  return new Promise((resolve, reject) => {
    const id = send._id = (send._id || 0) + 1;
    const timer = setTimeout(() => {
      ws.removeEventListener('message', onMsg);
      reject(new Error(`CDP timeout: ${method}`));
    }, timeoutMs);
    const onMsg = ev => {
      const m = JSON.parse(ev.data);
      if (m.id === id) {
        clearTimeout(timer);
        ws.removeEventListener('message', onMsg);
        m.error ? reject(new Error(`${method}: ${JSON.stringify(m.error)}`)) : resolve(m.result);
      }
    };
    ws.addEventListener('message', onMsg);
    ws.send(JSON.stringify({ id, method, params }));
  });
}

export const sleep = ms => new Promise(r => setTimeout(r, ms));

export async function withPage(fn) {
  const page = await findPage();
  const ws = await connect(page.webSocketDebuggerUrl);
  try {
    return await fn(ws, page);
  } finally {
    ws.close();
  }
}

export async function click(ws, x, y) {
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none' });
  await sleep(60);
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1 });
  await sleep(40);
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', buttons: 0, clickCount: 1 });
}

const KEYMAP = {
  Tab: { code: 'Tab', key: 'Tab', vk: 9 },
  Enter: { code: 'Enter', key: 'Enter', vk: 13 },
  Escape: { code: 'Escape', key: 'Escape', vk: 27 },
  Space: { code: 'Space', key: ' ', vk: 32 },
  Backspace: { code: 'Backspace', key: 'Backspace', vk: 8 },
};

export async function pressKey(ws, name) {
  const k = KEYMAP[name] ?? { code: name, key: name, vk: 0 };
  await send(ws, 'Input.dispatchKeyEvent', { type: 'keyDown', code: k.code, key: k.key, windowsVirtualKeyCode: k.vk, nativeVirtualKeyCode: k.vk });
  await send(ws, 'Input.dispatchKeyEvent', { type: 'keyUp', code: k.code, key: k.key, windowsVirtualKeyCode: k.vk, nativeVirtualKeyCode: k.vk });
}

export async function evaluate(ws, expression) {
  const r = await send(ws, 'Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  return r.result?.value;
}

export async function screenshot(ws, outPath) {
  const r = await send(ws, 'Page.captureScreenshot', { format: 'png' });
  writeFileSync(outPath, Buffer.from(r.data, 'base64'));
  return outPath;
}

// ---- CLI ----
const isMain = process.argv[1] && import.meta.url === `file://${process.argv[1]}`;
if (isMain) {
  const [cmd, ...args] = process.argv.slice(2);
  await withPage(async (ws, page) => {
    switch (cmd) {
      case 'list':
        console.log('current page:', page.url);
        break;
      case 'nav': {
        await send(ws, 'Page.navigate', { url: args[0] });
        await sleep(Number(process.env.NAV_SETTLE_MS || 3000));
        console.log('navigated:', args[0]);
        break;
      }
      case 'size': {
        await send(ws, 'Emulation.setDeviceMetricsOverride', {
          width: Number(args[0]), height: Number(args[1]), deviceScaleFactor: 1, mobile: false,
        });
        console.log(`viewport ${args[0]}x${args[1]}`);
        break;
      }
      case 'click': {
        await click(ws, Number(args[0]), Number(args[1]));
        console.log(`clicked (${args[0]},${args[1]}) on ${page.url}`);
        break;
      }
      case 'key': {
        await pressKey(ws, args[0]);
        console.log('pressed', args[0]);
        break;
      }
      case 'eval': {
        console.log(JSON.stringify(await evaluate(ws, args.join(' ')), null, 2));
        break;
      }
      case 'shot': {
        console.log('saved', await screenshot(ws, args[0]));
        break;
      }
      default:
        console.error('unknown command. use: list|nav|click|key|eval|shot|size');
        process.exit(1);
    }
  });
  process.exit(0);
}
