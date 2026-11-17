# UNCAD Pro 3.3.5

发布日期：2026-11-18

## 定位

U1U 3D 预览内核更换版（feature 版，D-054 Amendment 3.3.5）。用户看过
3.3.2/3.3.4 的 HelixToolkit 预览后现场反馈：「这生成的 3D 有严重问题！
放弃这个方案转 WebV 吧」——HelixToolkit.Wpf 全线退役，U1U 确认窗左侧预览
换回 WebView2 + three.js（与 U1L3D 同一条技术管线）。

## 架构

- **场景语义在 C# 算死**：Core 新增纯函数信封构建器
  `QuickLineOrbitPreviewEnvelope`——柱网规划（两向正交）、桥架/线管通道
  双线偏移、米数标/刻度/字母/端点钉锚点全部解析成纯几何（公式与 Helix
  版逐条对齐），序列化成 JSON（PascalCase 键 = C#/JS 契约，测试锁死）。
- **页面是哑渲染器**：新页面 Web/QuickLineOrbit（复用 QuickLine3D 已
  vendor 的 three.js + OrbitControls）。透视相机（默认方位 -135°/俯仰
  30°，阻尼旋转/缩放/平移，双击复位）、管线用真实圆柱几何、**标签走
  HTML 叠加层（中文交给 Chromium）**、桥架红双线+半透明填充带、线管青
  双线、两向柱网、图例/轴位摘要/柱网开关。
- **宿主控件** `QuickLineWebPreviewControl`：API 与被删 Helix 控件同形；
  WebView2 接线与安全口径逐条对齐 U1L3DPreviewWindow（本地虚拟主机/
  非本地导航取消/无宿主对象/渲染进程异常降级文案）；惰性初始化（无预览
  路径零开销）；数据级计数器取自信封（与 WebView2/GPU 解耦）。

## 锁死与验证

- `QuickLineOrbitPreviewEnvelopeTests` 7 条：几何逐值（桥架偏移=宽/2、
  竖直段法向回退、线管对角线比例）+ 序列化键名契约。
- 窗体数据级测试随内核改造（信封计数口径），对半分布局断言原样。
- bundle payload 三处同步（build.ps1/installer.ps1/契约测试）：
  25→24 文件（去 HelixToolkit.Wpf.dll、加 QuickLineOrbit 三件）。
- 浏览器实测：注入信封后 8 条标签全渲染、透视场景/桥架填充/柱网截图
  确认，零控制台报错。
- 全量 1577/1577（基线 1570）。

## 上一版速览

- 3.3.4：图层收口（U1B 矫正直通、U1F 图框边界、批量软管列）。
- 3.3.3.x：U1B 软管 + 毫米标识别修复 + 全命令审计批次修复。
