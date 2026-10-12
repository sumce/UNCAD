# UNCAD Pro 2.7.0

发布日期：2026-10-11

本版把 **Downstream 块的自动添加**从通用类别开关里解耦出来，并重构了 U1SET 配置中心。

## 新增

**1. Downstream 自动添加的独立开关。**

以前 Downstream 的自动补行借用「BOQ 自动填充类别」里的**通用**插座盘/电盘开关，导致
"只想开 Downstream、不想开上游"做不到。现在：

| 开关（U1SET → Downstream 自动添加） | 作用 |
| --- | --- |
| socket box 时自动加一行 4.x 插座箱/盘 | 仅控制 Downstream = socket box |
| I-line_Panel 时自动加一行电盘（不带断路器） | 仅控制 Downstream = I-line_Panel |

- 新键：`UNC_FILL_DOWNSTREAM_SOCKET_ENABLED` / `UNC_FILL_DOWNSTREAM_PANEL_ENABLED`，默认**开**。
- **与通用类别开关完全解耦**：「清单与表格」里的插座盘/电盘只管**上游**规则；
  关掉它们不影响 Downstream，反之亦然。
- 断路器规则不变：Downstream 永不添加断路器（只由上游 `U_上游类型 = I-Line盘` 产生）。

**2. U1SET 配置中心重构（10 页侧边栏）。**

概览 / 绘制标注（U1L·U1Q·U1C·U1R 合并）/ 统计汇总 / 文字样式 / 界面与启动 /
机台数据 / 清单与表格 / Downstream 自动添加 / 颜色 / 自动输出。

- 新增**概览页**：版本、数据源提示、Downstream 三态规则一屏看完。
- 内容归位：启动开关从"文字样式"移到「界面与启动」；颜色独立成页；原来挤在一页的
  机台数据/表格/Downstream/类别/输出拆成 5 页。
- 布局修复：字段标签列由**写死 190px** 改为自适应（长中文标签不再被省略号裁切）；
  侧边栏导航项 44×176 → 52×208。

## 验证

- `dotnet test UNCAD.slnx`：1061 项全部通过。
  新增/更新：`SocketBox_IsControlledOnlyByItsOwnSwitch`、
  `ILinePanel_IsControlledOnlyByItsOwnSwitch`（证明独立于通用开关）、
  新配置键契约、配置中心 10 页与 Downstream 页 3 块/2 开关的 UI 契约。
- 真机验收缺口（如实标注）：CAD 自检无法程序化创建带可见性状态的动态块定义，
  Downstream 状态读取此前已用现场图纸（59 个 Downstream 块）验证；本版只改开关归属。
