# UNCAD Pro 2.6.9

发布日期：2026-10-11

本版把下游 `Downstream` 块的 **I-line 态**接上物料规则。

## 新增

**Downstream = `I-line_Panel` → 自动追加一行电盘。**

- 额定电流取自 `U1SET → Excel 数据 → 下游电盘额定电流（A）`，默认 **1000**，范围 1–10000。
- 受「电盘」自动填充开关控制（与其他电盘行同一个开关）。
- **不添加断路器**：断路器只由上游 `U_上游类型 = I-Line盘` 产生。这条是用户明确要求，
  已用测试锁死（`ILinePanel_NeverAddsABreaker`）。

## 现在的完整三态规则

| Downstream 状态 | 物料 | 额定电流来源 |
| --- | --- | --- |
| `socket box` | 一行 4.x 插座箱/插座盘 | U1SET「下游插座箱额定电流」，默认 20A |
| `I-line_Panel` | 一行电盘 | U1SET「下游电盘额定电流」，默认 1000A |
| `busbar connector` | 无（只上色） | — |

三态都不产生断路器，也不添加 8.x 插座行（那条由 `Device_Build20260716` 负责）；
两个额定电流都不再从 `U_配电信息` 提取（2.6.8 已修）。

## 验证与已知环境缺陷

- 行为验证通过：`ILinePanel_AddsExactlyOneElectricalPanelUsingTheConfiguredRating`、
  `ILinePanel_NeverAddsABreaker`、`ILinePanel_RespectsThePanelSwitch` 等全部通过。
- **环境缺陷（本版未修，但已查明）**：本机默认 VSTest 宿主会以 x86 建 AppDomain 加载测试
  程序集，`AcDbMgd` 解析失败后**静默跳过全部测试**（"没有可用测试"），而 `dotnet test`
  **仍然返回退出码 0** —— 也就是发布门禁的测试步骤存在**假绿**风险。
  与被测代码无关（干净 HEAD 上曾正常跑出 1055/1055；64 位 PowerShell 直接
  `LoadFrom` 测试程序集也能取到 310 个类型）。
  当前可用的绕开方式：`dotnet test UNCAD.slnx -- RunConfiguration.TargetPlatform=x64
  RunConfiguration.DisableAppDomain=true`（该开关会少发现约 9 项测试，因此**没有**写进
  `tests.runsettings`，不拿覆盖换方便）。
- 因此本版**未出包、未安装**：`release.ps1` 用的正是默认 `dotnet test`，在宿主问题解决前
  不签发 2.6.9（避免"门禁绿但测试没跑"的发布）。
