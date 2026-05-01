# A_Pair 设计规范

## 设计原则

- **简洁**：减少视觉噪音，每屏只呈现核心信息
- **扁平化**：无冗余阴影、渐变和纹理，使用纯色块和清晰边界
- **直觉化**：操作流程符合用户预期，图标 + 文字双重语义
- **类 Fluent UI**：参考 Microsoft Fluent Design System，轻量、通透、响应式

---

## 色彩系统

### 主题色（Accent）

采纳 Avalonia FluentTheme 默认蓝色系：

| Token | 值 | 用途 |
|-------|-----|------|
| Accent | `#2563EB` | 主按钮、选中态、链接 |
| AccentHover | `#1D4ED8` | 悬停 |
| AccentPressed | `#1E40AF` | 按下 |

### 中性色

| Token | Light | Dark | 用途 |
|-------|-------|------|------|
| 页面背景 | `#F5F5F5` | `#1E1E1E` | Window 背景 |
| 卡片/区域背景 | `#FFFFFF` | `#2D2D2D` | 内容面板 |
| 侧边栏背景 | `#F0F0F0` | `#252525` | 导航栏 |
| 分割线 | `#E0E0E0` | `#3D3D3D` | Separator |
| 主文字 | `#1A1A1A` | `#E8E8E8` | 标题、正文 |
| 次要文字 | `#666666` | `#9E9E9E` | 描述、标签 |
| 禁用文字 | `#9E9E9E` | `#666666` | 禁用态 |

### 语义色

| Token | 值 | 用途 |
|-------|-----|------|
| 成功 | `#16A34A` | 已分配座位、操作成功 |
| 警告 | `#F59E0B` | 冲突、警告 |
| 错误 | `#DC2626` | 验证失败、异常 |
| 信息 | `#2563EB` | 提示 |

---

## 排版

| 层级 | 字号 | 字重 | 用途 |
|------|------|------|------|
| PageTitle | 20px | Bold | 页面标题 |
| SectionTitle | 16px | SemiBold | 区块标题 |
| Body | 14px | Regular | 正文、列表 |
| Caption | 12px | Regular | 辅助信息、提示 |
| Small | 11px | Regular | 标签、徽标 |

字体：Inter（Avalonia FluentTheme 内置）

---

## 图标

使用 **FluentIcons.Avalonia** 库（Fluent UI System Icons），命名约定：`{IconName}24Regular`。

### 导航图标

| 页面 | 图标 | 名称 |
|------|------|------|
| 数据管理 | `DataUsage24Regular` | 数据 |
| 会场配置 | `Building24Regular` | 建筑/布局 |
| 策略配置 | `Options24Regular` | 策略选项 |
| 座位安排 | `Grid24Regular` | 网格/座位 |
| 历史快照 | `History24Regular` | 历史 |
| 插件管理 | `PuzzlePiece24Regular` | 拼图/插件 |
| 设置 | `Settings24Regular` | 齿轮 |
| 关于 | `Info24Regular` | 信息 |

### 操作图标

| 操作 | 图标 | 名称 |
|------|------|------|
| 导入 | `ArrowDownload24Regular` | 下载箭头 |
| 导出 | `ArrowUpload24Regular` | 上传箭头 |
| 保存 | `Save24Regular` | 软盘 |
| 撤销 | `ArrowUndo24Regular` | 撤销箭头 |
| 重做 | `ArrowRedo24Regular` | 重做箭头 |
| 添加 | `Add24Regular` | + |
| 删除 | `Delete24Regular` | 垃圾桶 |
| 刷新 | `ArrowSync24Regular` | 同步 |
| 折叠 | `PanelLeftContract24Regular` | 折叠箭头 |
| 展开 | `PanelLeftExpand24Regular` | 展开箭头 |

---

## 间距

| Token | 值 | 用途 |
|-------|-----|------|
| XS | 4px | 紧密元素、图标内边距 |
| SM | 8px | 相关元素间距 |
| MD | 12px | 标准元素间距 |
| LG | 16px | 区块间距、页面外边距 |
| XL | 24px | 大区块分隔 |

侧边栏：展开 200px，折叠 64px。

---

## 圆角

| Token | 值 | 用途 |
|-------|-----|------|
| SM | 4px | 按钮、输入框 |
| MD | 8px | 卡片、面板 |
| Full | 9999px | 圆角按钮（可选） |

---

## 阴影

遵循扁平化原则，仅使用最小阴影：

- 卡片/面板：`Elevation=1` 或 无阴影（边框替代）
- 弹窗：轻微阴影，5px 模糊

---

## 布局模式

```
┌──────────────────────────────────────────┐
│ 侧边栏 (200/64)  │  内容区 (填充剩余)      │
│                  │                        │
│ 导航按钮 × 8     │  PageTitle              │
│                  │  ────────────           │
│                  │  Content                │
│                  │                        │
└──────────────────────────────────────────┘
```

- 页面统一使用 `DockPanel`：顶部 Toolbar + 底部 StatusBar + 中间内容
- 每个页面 Margin = 16px
- 工具栏按钮使用 `WrapPanel` 排列

---

## 交互规范

- **悬停**：按钮轻微变色（FluentTheme 默认行为）
- **选中**：导航按钮高亮（Accent 背景 + 白色文字）
- **过渡**：侧边栏折叠使用隐式宽度动画（后续可实现）
- **焦点**：输入框聚焦显示 Accent 边框

---

## 工具依赖

- **UI 框架**：Avalonia 12（FluentTheme）
- **图标库**：`FluentIcons.Avalonia` v3.x
- **MVVM**：CommunityToolkit.Mvvm
- **字体**：Inter（内置）
