# UI 无头查看与采样工具（ui-inspect）

无头环境下对本项目 Avalonia WASM 界面进行**查看、交互与性能采样**的工具集
（源自 UI 重构 M0–M6 的验证链路，结论见 `docs/UI_REFACTOR.md`）。
浏览器由 chrome-devtools MCP 统一管理（内网 Chromium，`--browserUrl=http://localhost:3000`）。

## 为什么不用 MCP 的 uid 点击

Avalonia WASM 渲染在单个 `<canvas>` 上，DOM 无控件节点（a11y 树为空），MCP 无法生成 uid；
而页面内合成的 DOM 事件会因 Avalonia 的 pointer capture 机制失效。
因此**输入统一走 CDP `Input.*` 可信事件**（浏览器层生成），查看/截图/性能仍用 MCP。

## 前置条件

```bash
# 1) 发布并启动 WASM 站点（在项目根目录）
dotnet publish src/SeatFlow.Browser -c Release -o /tmp/seatflow-web
cd /tmp/seatflow-web && setsid nohup python3 -m http.server 8090 --bind 0.0.0.0 \
  < /dev/null > /tmp/seatflow-web.log 2>&1 &

# 2) 用 MCP 打开 http://localhost:8090/ 并 resize_page 到 1200x800
#    首次会话需关闭「遥测同意」与「启动引导」弹窗（见下方标定）
```

## 命令

```bash
node cdp.mjs list                     # 列出页面 target
node cdp.mjs nav http://localhost:8090/
node cdp.mjs click 70 169             # 视口坐标（CSS px）
node cdp.mjs key Tab
node cdp.mjs eval "document.title"
node cdp.mjs shot /tmp/x.png
node capture-baseline.mjs [输出目录]   # 批量抓取 7 页截图（默认 scripts/ui-inspect/baselines/，gitignored）
python3 png_analyze.py <shot.png> buttons|text   # canvas 控件坐标定位
```

环境变量：`CDP_HTTP`（默认 `http://localhost:3000`）、`PAGE_MATCH`（默认 `localhost:8090`）。

## 已知标定（1200×800 视口，中文，浅色主题）

左栏导航文字中心 y（新 IA 分组侧栏）：排座工作台 142、人员管理 211、会场与布局 247、
策略配置 317、历史快照 381；底部固定：设置 740、关于 775。点击 x=70。
工作台数据选择列表：会场项 y≈124 起每项约 34px；名单项 y≈321。
未保存修改对话框三按钮 y=448：「保存并离开」516 /「不保存直接离开」646 /「取消」746。

紧凑模式（≤900px）：左上「导航」按钮打开导航抽屉；工作台命令栏含「数据」/「面板」抽屉按钮。

首次运行弹窗（**780×493 视口**下标定；换视口需用 `png_analyze.py` 重新定位）：
- 遥测同意「暂不开启」：(538, 382)
- 引导卡关闭 ×：(574, 178)；退出确认「确定」：(452, 294)

## 注意事项

- **浏览器可能被重置**（CDP 端点重连后页面回到 `about:blank`、IndexedDB 清空）。
  脚本应设计为幂等：先 `nav`，等待 ~15s WASM 启动，再操作。
- 窗口高度过小时（<~700px），左栏中部导航曾溢出并覆盖底部「设置/关于」按钮
  （I-01，已在 M3 修复，见 `docs/UI_REFACTOR.md`）；采样请用 1200×800。
- 截图链路会把纯白压至 242（非 255），像素阈值按此校准。
- 截图状态保存在浏览器进程内；若怀疑状态漂移，重载页面（`nav`）后再采样。
