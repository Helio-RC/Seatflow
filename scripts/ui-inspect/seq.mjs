#!/usr/bin/env node
// 在单个 CDP 连接内执行交互序列（供 trace 采集时使用）。
// 用法: node seq.mjs '<json 数组>'
//   [{"click":[x,y]},{"wait":1200},{"repeat":{"click":[x,y],"times":15,"wait":400}},
//    {"wheel":[x,y,dy]},{"drag":[[x1,y1],[x2,y2]],"steps":10},{"key":"Tab"},{"shot":"/tmp/x.png"}]
import { withPage, click, pressKey, sleep, screenshot, send } from './cdp.mjs';

const spec = JSON.parse(process.argv[2] || '[]');

async function wheel(ws, x, y, deltaY, modifiers = 0) {
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseWheel', x, y, deltaX: 0, deltaY, modifiers });
}

async function drag(ws, [x1, y1], [x2, y2], steps = 10) {
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseMoved', x: x1, y: y1, button: 'none' });
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mousePressed', x: x1, y: y1, button: 'left', buttons: 1, clickCount: 1 });
  for (let i = 1; i <= steps; i++) {
    const x = x1 + ((x2 - x1) * i) / steps;
    const y = y1 + ((y2 - y1) * i) / steps;
    await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'left', buttons: 1 });
    await sleep(30);
  }
  await send(ws, 'Input.dispatchMouseEvent', { type: 'mouseReleased', x: x2, y: y2, button: 'left', buttons: 0, clickCount: 1 });
}

await withPage(async ws => {
  for (const step of spec) {
    if (step.click) await click(ws, step.click[0], step.click[1]);
    if (step.wait) await sleep(step.wait);
    if (step.wheel) { await wheel(ws, step.wheel[0], step.wheel[1], step.wheel[2], step.mods || 0); await sleep(step.wait || 120); }
    if (step.drag) await drag(ws, step.drag[0], step.drag[1], step.steps);
    if (step.key) { await pressKey(ws, step.key); await sleep(step.wait || 150); }
    if (step.repeat) {
      for (let i = 0; i < step.repeat.times; i++) {
        await click(ws, step.repeat.click[0], step.repeat.click[1]);
        await sleep(step.repeat.wait || 300);
      }
    }
    if (step.shot) await screenshot(ws, step.shot);
  }
});
console.log('sequence done');
