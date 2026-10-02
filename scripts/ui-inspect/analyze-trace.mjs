#!/usr/bin/env node
// 分析 chrome-devtools MCP 保存的 trace（.json.gz），输出：
//   - 长任务（>50ms）汇总与明细
//   - Layout / UpdateLayoutTree / Paint / Commit / EventDispatch 总耗时
//   - 按函数聚合的自身耗时 Top N
// 用法：node analyze-trace.mjs <trace.json.gz> [longtask-ms=50] [topN=12]
import { readFileSync } from 'node:fs';
import { gunzipSync } from 'node:zlib';

const [file, ltArg, topArg] = process.argv.slice(2);
if (!file) { console.error('usage: node analyze-trace.mjs <trace.json.gz>'); process.exit(1); }
const LT = (Number(ltArg) || 50) * 1000; // µs
const TOP = Number(topArg) || 12;

const raw = file.endsWith('.gz') ? gunzipSync(readFileSync(file)) : readFileSync(file);
const data = JSON.parse(raw.toString('utf8'));
const events = Array.isArray(data) ? data : (data.traceEvents ?? []);
console.log(`trace events: ${events.length}`);

const longTasks = [];
const catSums = new Map();
const fnSelf = new Map();
const byName = new Map();

for (const e of events) {
  const dur = e.dur || 0;
  if (!dur) continue;
  if (e.ph !== 'X') continue;
  const name = e.name || '';
  byName.set(name, (byName.get(name) || 0) + dur);
  if (name === 'RunTask' || name === 'Task') {
    if (dur >= LT) longTasks.push({ name, dur, ts: e.ts });
  }
  if (['Layout', 'UpdateLayoutTree', 'Paint', 'Commit', 'CompositeLayers', 'EventDispatch', 'HitTest', 'RecalculateStyles'].includes(name)) {
    catSums.set(name, (catSums.get(name) || 0) + dur);
  }
  if (name === 'FunctionCall' || name === 'EvaluateScript' || name === 'v8.callFunction') {
    const key = `${e.args?.data?.functionName || e.args?.functionName || name} @ ${(e.args?.data?.url || e.args?.url || '').split('/').pop()}:${e.args?.data?.lineNumber ?? ''}`;
    fnSelf.set(key, (fnSelf.get(key) || 0) + dur);
  }
}

const ms = us => (us / 1000).toFixed(1);
longTasks.sort((a, b) => b.dur - a.dur);
console.log(`\n== 长任务（>${LT / 1000}ms）: ${longTasks.length} 个 ==`);
let totalLT = 0;
for (const t of longTasks.slice(0, TOP)) totalLT += t.dur;
const allLT = longTasks.reduce((s, t) => s + t.dur, 0);
console.log(`长任务总时长: ${ms(allLT)}ms（Top${Math.min(TOP, longTasks.length)}: ${ms(totalLT)}ms）`);
for (const t of longTasks.slice(0, TOP)) console.log(`  ${ms(t.dur)}ms  @ ${ms(t.ts)}`);

console.log('\n== 渲染/布局类目总耗时 ==');
[...catSums.entries()].sort((a, b) => b[1] - a[1]).forEach(([k, v]) => console.log(`  ${k}: ${ms(v)}ms`));

console.log(`\n== 函数自身耗时 Top${TOP} ==`);
[...fnSelf.entries()].sort((a, b) => b[1] - a[1]).slice(0, TOP).forEach(([k, v]) => console.log(`  ${ms(v)}ms  ${k}`));

console.log(`\n== 事件名总耗时 Top${TOP} ==`);
[...byName.entries()].sort((a, b) => b[1] - a[1]).slice(0, TOP).forEach(([k, v]) => console.log(`  ${ms(v)}ms  ${k}`));
