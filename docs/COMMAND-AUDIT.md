# UNCAD 命令审计矩阵（2.10.0）

> 审计对象：`CommandIds.Registered` 全部 **35 条**公开命令（21 条正式命令 + 1 条别名 + 6 条命令族/扩展命令 + 7 条兼容旧名）。
> 依据：`AGENTS.md`、`docs/PROJECT_CONTEXT.md`、`docs/DECISIONS.md`、`docs/DEVELOPMENT.md`；以源码与测试作为观察实现。
> 结论：**35/35** 唯一注册、**35/35** 有帮助条目、Ribbon 覆盖全部正式命令（兼容旧名按设计不设 Ribbon 按钮）。
> 本轮发现并修复 9 项（均在 2.10.0-C 的写 scope 内），上报其它 scope 1 项，明确“不是缺陷”的既有行为 7 项。

## 1. 审计口径

| 检查项 | 判定标准 | 自动化校验 |
| --- | --- | --- |
| 注册 | 每条命令恰好一个 `[CommandMethod]`，注册集合 == `CommandIds.Registered` | `CommandRegistrationTests.SourceCommandMethods_RegisterEveryPublicCommandExactlyOnce` |
| 帮助 | `CommandHelpCatalog` 一条命令一条记录，Usage/Function/Notes 非空 | `CommandHelpCatalogTests.EveryRegisteredCommandHasOneHelpEntry` |
| Ribbon | 正式命令有按钮；兼容旧名/别名不重复设按钮 | `RibbonCatalogTests` |
| 事务 | CAD 写入必须说明事务边界与提交点；只读命令不得写图 | 本文件逐条人工核对 + 专项测试 |
| 失败路径 | 取消/ESC、配置缺失、目录不可写、清单未匹配、实体不合法都有中文提示且不留半写状态 | 逐条人工核对 |
| 配置 | 读取的 `ConfigKeys` 可由 U1SET 维护、有默认值、键名唯一 | `ConfigKeysTests` + 本文件矩阵 |
| 测试 | 每条命令至少有一个 Core/契约/失败分支测试 | 本文件矩阵“回归测试”列 |

2.10.0 起命令入口统一由 `Cad/CommandBase.Run` 包裹：`Log.Command(命令名)`（文件日志行带命令标签）+ `OperationLog.Begin(命令名, 摘要)`（成功 `Complete`、ESC `Cancel`、异常 `Fail` 后继续抛出）；异常统一打印到命令行并写日志，`finally` 清理隐式选择集，避免下一条 `UsePickSet` 命令复用失效 ID。

## 2. 注册 / 文档 / Ribbon 矩阵（35 条）

| 命令 | 类别 | `[CommandMethod]` 位置 | 帮助 | Ribbon 按钮 | 状态 |
| --- | --- | --- | --- | --- | --- |
| `U1L` | 正式 | `Features/Unl/UnlFeature.cs:26` | ✓ | 绘制线条 | 已审计 |
| `U1LX` | 正式 | `Features/Unl/QuickLineFeature.cs:26` | ✓ | 快速标注距离 | 已审计 |
| `U1L3D` | 正式 | `Features/Unl/QuickLine3dPreviewFeature.cs:37` | ✓ | U1L 三维预览 | 已审计 |
| `U13D` | 正式 | `Features/Unl/QuickLine3dPreviewFeature.cs:40` | ✓ | U13D 多图框预览 | 已审计 |
| `U1DT` | 正式 | `Features/Dimension/DimensionTextFeature.cs:16` | ✓ | 标注转文字 | 已审计 |
| `U1R` | 正式 | `Features/Unr/UnrFeature.cs:53` | ✓ | 开拱桥 | 已审计 |
| `U1Q1` | 正式 | `Features/Unq/UnqFeature.cs:22` | ✓ | 桥架 100 | 已审计 |
| `U1Q2` | 正式 | `Features/Unq/UnqFeature.cs:25` | ✓ | 桥架 200 | 已审计 |
| `U1Q4` | 正式 | `Features/Unq/UnqFeature.cs:28` | ✓ | 桥架 400 | 已审计 |
| `U1F` | 正式 | `Features/Fill/FillFeature.cs:36` | ✓ | U1F 生成 | 已审计（Lead 范围） |
| `U1U` | 正式 | `Features/Fill/FillFeature.cs:39` | ✓ | U1U 更新 | 已审计（Lead 范围） |
| `U1S` | 正式 | `Features/Submit/SubmitFeature.cs:19` | ✓ | U1S 提交 | 已审计（A 范围） |
| `U1C` | 正式 | `Features/Conduit/ConduitFeature.cs:20` | ✓ | 线管 默认 | 已审计 |
| `U1C20` | 命令族 | `Features/Conduit/ConduitFeature.cs:23` | ✓ | 线管 20 | 已审计 |
| `U1C25` | 命令族 | `Features/Conduit/ConduitFeature.cs:26` | ✓ | 线管 25 | 已审计 |
| `U1C38` | 命令族 | `Features/Conduit/ConduitFeature.cs:29` | ✓ | 线管 38 | 已审计 |
| `U1C51` | 命令族 | `Features/Conduit/ConduitFeature.cs:32` | ✓ | 线管 51 | 已审计 |
| `U1A` | 正式 | `Features/About/AboutFeature.cs:13` | ✓ | 关于与授权 | 已审计 |
| `U1SET` | 正式 | `Features/ConfigCenter/SettingsFeature.cs:19` | ✓ | U1SET 设置 | 已审计 |
| `U1DATA` | 正式 | `Features/ConfigCenter/MachineDataRefreshFeature.cs:14` | ✓ | U1DATA 刷新 | 已审计（A 范围） |
| `U1DWG` | 正式 | `Features/DwgExport/DwgExportFeature.cs:19` | ✓ | U1DWG 导出 | 已审计（A 范围） |
| `XLAYOUT` | 正式 | `Features/XLayout/XLayoutFeature.cs:30` | ✓ | XLAYOUT 排版 | 已审计 |
| `XSTS` | 正式 | `Features/Stat/XstsFeature.cs:32` | ✓ | XSTS 回路统计 | 已审计 |
| `Xmerge` | 正式 | `Features/XLayout/XmergeFeature.cs:14` | ✓ | Xmerge 合并 DWG | 已审计 |
| `U1HELP` | 正式 | `Features/Help/HelpFeature.cs:14` | ✓ | U1HELP 命令帮助 | 已审计 |
| `UNC_LOGIN` | 正式 | `Features/About/ClientLoginFeature.cs:23` | ✓ | 在线登录 | 已审计 |
| `U1PDF` | 正式 | `Features/PdfExport/PdfExportFeature.cs:39` | ✓ | U1PDF 出图 | 已审计（A 范围） |
| `U1D` | 别名 | `Features/DwgExport/DwgExportFeature.cs:22` | ✓ | 同 U1DWG（不重复设按钮） | 已审计 |
| `UNL` | 兼容旧名 | `Features/Unl/UnlFeature.cs:33` | ✓ | 无（转发 U1L） | 已审计 |
| `UNLX` | 兼容旧名 | `Features/Unl/QuickLineAliasCommand.cs:9` | ✓ | 无（转发 U1LX） | 已审计 |
| `UNR` | 兼容旧名 | `Features/Unr/UnrFeature.cs:57` | ✓ | 无（转发 U1R） | 已审计 |
| `UNQ1` | 兼容旧名 | `Features/Unq/UnqFeature.cs:31` | ✓ | 无（转发 U1Q1） | 已审计 |
| `UNQ2` | 兼容旧名 | `Features/Unq/UnqFeature.cs:32` | ✓ | 无（转发 U1Q2） | 已审计 |
| `UNQ4` | 兼容旧名 | `Features/Unq/UnqFeature.cs:33` | ✓ | 无（转发 U1Q4） | 已审计 |
| `UNADD` | 兼容旧名 | `Features/Unadd/UnaddFeature.cs:24` | ✓ | 图纸统计 | 已审计 |

Ribbon 覆盖 28/35：全部 21 条 `CommandIds.Canonical`、命令族 `U1C20/25/38/51`、`U1PDF`、`UNC_LOGIN`、`UNADD`（“图纸统计”按钮）；未覆盖的 7 条（`U1D` + 7 条兼容旧名中的 6 条转发入口）——即 `U1D`、`UNL`、`UNLX`、`UNR`、`UNQ1`、`UNQ2`、`UNQ4`，都是转发入口，按设计不增加重复按钮。

## 3. 事务 / 失败路径 / 配置 / 测试矩阵

| 命令 | CAD 写入与事务边界 | 取消 / 失败路径 | 读取配置（`ConfigKeys`） | 回归测试 |
| --- | --- | --- | --- | --- |
| `U1F` | 写入阶段**一个**事务提交表格、块属性与 `frameinfo_json`；BOQ xlsx 在 CAD 提交后单独写，失败不回滚图纸并提示重跑 `U1S` | 未匹配清单行在写事务前中止（D-016）；模块失败由 `ModuleRunner` 带模块 ID/阶段编号；BOQ 写失败只提示 | `FillSettings` 整组（Excel 路径、表行/清空行、文字高度、预留、类别开关、Downstream 开关等） | `BatchFillUpdateContractTests`、`FillFeatureWorkbookSourceContractTests`、`TableGenerationModuleTests`、`CableReserveNewFillTests` |
| `U1U` | 多图框统一预检后一个批量写事务；替换型号/插座数量按 D-005/D-008/D-015 处理 | 预检失败整批不写；未匹配项先集中选择；取消不写图 | 同 `U1F` | `BatchFillUpdateContractTests`、`BatchCatalogSelectionFormTests`、`FillUpdateRowMergerTests`、`RuanguanUpdatePolicyTests` |
| `U1S` | 无 CAD 写入；只读图框清单后写 BOQ xlsx（输出目录占用预检在写盘前） | 图框识别失败/无清单项 → 中文提示且不写文件；写盘异常中止并提示图纸未修改 | `SubmitFolder`、`OutputByDrawing`、`OutputSeparateKinds` | `SubmitFeatureContractTests`、`SubmissionRecordExtractorTests`、`BoqWorkbookWriterTests` |
| `U1DWG` / `U1D` | 身份读取只读事务；导出为磁盘 IO（Wblock + SaveAs + 原子替换），不修改当前图纸 | 占用预检失败/无图框 → 中止且不生成文件；逐机台失败原因写日志+命令行 | `SubmitFolder`、`OutputByDrawing`、`OutputSeparateKinds` | `DwgExportLayoutTests`、`DwgPathDiscoveryTests` |
| `U1PDF` | 无 CAD 写入；打印设置逐字段恢复，失败按 `PdfPlotCommitPolicy` 回滚本次布局改动；占用预检在出图前 | 缺 “DWG To PDF.pc3”、图框身份无法解析、文件被占用 → 中止；单份出图失败聚合为首个诊断，不影响其它份 | `SubmitFolder`、`OutputByDrawing`、`OutputSeparateKinds` | `PdfExportTests`、`PdfPlotPipelineTests`、`PdfPlotCommitPolicyTests` |
| `XLAYOUT` | 身份读取只读事务 + 排版写事务；排版只移动图框/写机台 ID 文字 | 身份错误聚合后提示；重复执行追加机台 ID 文字是 **D-009 设计行为**，性能优化不得消除 | `FillExcelPath`（只用于快照路径） | `XLayoutFeatureContractTests`、`XLayoutLayoutTests`、`XLayoutDiagnosticTests`、`XLayoutSelectionFilterTests` |
| `XSTS` | 只读事务读取图框身份；随后写 xlsx 到 `<输出根>\UNCAD_XSTS_时间.xlsx`（D-035，直接放输出根），不写 CAD | 无图框/身份失败逐条列出；期望数据来自 SQLite 快照，未刷新时显式提示执行 `U1DATA`；目录准备失败/报告被占用在写盘前中文中止 | `FillExcelPath`、`SubmitFolder` | `XstsReportTests`、`XstsExcelReporterTests`、`XstsFormTests`、`XstsOutputLayoutTests` |
| `Xmerge` | `XmergeService` 内事务合并，失败整体回滚；源 DWG 只读不修改 | 未选文件 → 提示；同定义不兼容按 D-022 分离重试；未定位实体有数量提示 | 无 | `XmergeFeatureContractTests`、`FrameRegionCollectorTests` |
| `U1C` | **一个**事务同时写 Ruanguan 块属性与线管标注并提交 | 固定清单无对应型号 → 写图前中止（D-016）；预选图框但无 Ruanguan → 仍可只做曲线标注 | `ConduitDiameter`、`ConduitHeight/LineOff/TextOff/Side`（= UNQ_* 键） | `ConduitLabelFormatterTests`、`ConduitDiameterTests`、`FlexibleConduitCableMapTests`、`RuanguanLabelFormatterTests` |
| `U1C20/25/38/51` | 同 `U1C`，管径固定不读默认值 | 清单缺该管径 → 中止；标注长度固定 2000mm 占位（`DEVELOPMENT.md` 约定） | 同 `U1C`（不读 `ConduitDiameter`） | 同 `U1C` + `CommandRegistrationTests` |
| `U1Q1/Q2/Q4` + `UNQ1/2/4` | `ParallelCurveAnnotator` 内部**一个**事务生成红线+两行标注；旧单行标签就地替换（D-018） | 规格无法解析/固定清单缺型号 → 写图前中止（D-016）；未选到可偏移曲线 → 提示并取消 | `UnqText`（仅防御性默认分支）、`UnaddMmPerGrid`、`UnqHeight/LineOff/TextOff/Side` | `BridgeLabelFormatterTests`、`AnnotationLabelPairTests`、`BridgeLabelMigrationContractTests`、`BoqCatalogIndexTests` |
| `U1L` / `UNL` | `InteractiveDrawer` 每段一个事务（Line + 毫米文字 + 线/标注关联），Esc 结束并保留已完成段 | 长度配置非法 → 回退 2000mm 并提示；偏移 >2 倍字高 → 贴线处理；取消不写剩余段 | `UnlText`、`UnlHeight`、`UnlPos`、`UnlOffset` | `QuickLineMillimeterTextTests`、`QuickLineTraversalTests`、`QuickLineFeatureContractTests` |
| `U1LX` / `UNLX` | 每段写入一个事务（`QuickLineCadService.TryWriteSegmentMillimetre`）；仅处理选中线的端点连通分量 | 输入非法重试本段；Esc/取消保留已完成部分；单段写失败跳过并继续 | 无（沿用标注关联） | `QuickLineTraversalTests`、`QuickLineCadServiceTests`、`QuickLineFeatureContractTests` |
| `U1L3D` / `U13D` | 无 CAD 写入；只读事务收集 U1L 路由；只读 WebView2 预览（D-024/D-025） | 非完整图框/无 U1L 路由 → 提示；快照缺失时显示诊断；不联网不刷新 Excel | `FillExcelPath`（仅快照路径） | `QuickLine3dPreviewTests` |
| `U1DT` | 一个事务替换对齐标注文字为单行文字并提交，原尺寸线保留 | 未选择/非对齐标注 → 跳过并统计数量；文字高度取配置默认 | `UnlHeight` | `DimensionTextFeatureContractTests`、`DimensionTextFormatterTests` |
| `U1R` / `UNR` | 每次点击一个事务（替换 Line 或插入 Polyline 顶点）；Esc 退出但已开洞口保留 | 非 LINE/二维 Polyline、共面、长度不足、靠近端点等情况提示并跳过，不写半成品 | `UnrDiameter` | `GeoMathArchTests` |
| `UNADD` | 一个事务写入汇总 DBText 行 | 无命中/统计类别全关 → 不生成 0M 文字并提示；字高非法回退默认 | `UnaddHeight` + 统计开关整组 | `SummationModuleTests`、`StatCalculatorTests`、`TextParserTests` |
| `U1SET` | 无 CAD 写入；点“确定”后由窗体统一 `SetMany` 保存（失败按快照回滚并提示） | 取消不保存；路径/数值校验失败切页签并保留输入；保存异常写日志+命令行 | 全配置键（含 `LogLevel`、`LogRetentionDays`、`HistoryRetentionMonths`、输出结构、预留值） | `UnifiedSettingsFormTests`、`ConfigKeysTests` |
| `U1DATA` | 无 CAD 写入；后台刷新一次，失败保留旧 SQLite 快照 | 未配置数据源/已在刷新 → 明确提示；解析失败保留旧快照并提示 | `FillExcelPath` | `MachineDataRefreshCoordinatorTests`、`MachineWorkbookSourceTests` |
| `U1HELP` | 只读；用 `CommandHelpCatalog.All` 渲染只读窗体 | 无失败副作用；**不做授权拦截是设计行为**（授权过期仍可查看帮助） | 无 | `CommandHelpCatalogTests`、`CommandAuditTests` |
| `U1A` | 只读；“关于与授权”对话框 | 无失败副作用；**不做授权拦截是设计行为** | 无 | `ProductMetadataTests`、`LicenseUxContractTests` |
| `UNC_LOGIN` | 无 CAD 写入；登录窗口 + 心跳校验（账号 + 单设备在线） | 取消不改变授权状态；**必须永不调用 `EnsureCommandAllowed`**，否则过期用户无法重新登录（`ClientLoginFeature` 注释+契约测试锁定） | `OnlineAuthorizationCode`（账号语义） | `ClientLoginContractTests`、`OnlineLicenseMonitorTests` |

## 4. 发现与处理

### 4.1 已修复（本任务 scope 内）

| 编号 | 严重度 | 位置 | 问题 | 处理 |
| --- | --- | --- | --- | --- |
| C-01 | P1 | `Infra/CommandHelpCatalog.cs`（XSTS） | 帮助说“期望回路优先读取 U1SET 配置的机台 Excel”，实际读取刷新后的 SQLite 快照（D-012/D-023）；用户可能误以为命令会直接读 Excel | 已改为“读取 U1DATA/U1SET 刷新后的 SQLite 机台快照，命令执行时不读取 Excel、不联网” |
| C-02 | P2 | `Features/Unadd`、`Unl`、`Unq`、`Conduit`、`Unr`、`About` | 9 个无 `[CommandMethod]`、全仓无调用的旧 SET/OP 入口（`UncadLineSet`、`UncadTraySet`、`UncadConduitSet`、`UncadArchSet`、`OpUnl`、`OpUnr`、`OpUnq`、`UncadTray`、`UncadRibbon`），是旧 LISP 命令迁移残留 | 已删除；删除前全仓 grep 确认无调用（见第 5 节） |
| C-03 | P2 | `Features/ConfigCenter/SettingsFeature.cs` | `Show(0/1/2)` 页签映射只被已删除的旧 SET 入口调用；保留 1/2 两个死映射 | 收敛为无参 `Show()`，U1SET 是唯一入口 |
| C-04 | P2 | `Infra/CommandHelpCatalog.cs`（U1R） | 帮助未说明连续开洞的交互语义（逐次提交、Esc 退出保留已完成洞口、不合法线段跳过） | 已补充 Notes |
| C-05 | P2 | `Infra/CommandHelpCatalog.cs`（XLAYOUT） | 帮助未注明“重复执行追加机台 ID 文字”是 D-009 设计行为 | 已补充 Notes，避免后续性能清理误删 |
| C-06 | P2 | `Infra/CommandHelpCatalog.cs`（U1SET） | 2.10.0 新增日志/操作记录/配置分享页签后帮助未同步 | 已补充 Notes |
| C-07 | P2 | `tests/UNCAD.Tests` | 命令审计结论没有自动校验，文档会与注册表漂移 | 新增 `CommandAuditTests`：审计文档必须覆盖全部 `CommandIds.Registered`，且已删除的废弃入口不得回流 |
| C-08 | P1 | `Infra/CommandHelpCatalog.cs`（U1PDF/U1S/U1DWG/U1DATA） | 2.10.0 移除全部云端上传与远程数据源后，帮助仍写“上传到服务端供在线预览”“本地或远程机台工作簿”，与新硬约束冲突 | 已改为“本地输出目录，不上传、不联网”“只支持本地/UNC”；全帮助文本复查再无上传承诺（Ribbon 提示由 UI scope 同步） |
| C-09 | P2 | `Features/Stat/XstsFeature.cs` | XSTS 直接写 `UNC_SUBMIT_FOLDER` 根目录，未走统一 `OutputLayout`（按图纸/分类型）与写占用预检，输出结构与 U1S/U1DWG/U1PDF 不一致 | 已接入 `OutputLayout`（占用预检与默认根保留）；**2026-11-01 D-035 修订：分层目录部分撤销**，报告恢复直接写入输出根 `<输出根>\UNCAD_XSTS_时间.xlsx`，现场反馈按图纸套子文件夹等于"报表丢失" |

### 4.2 已上报，需其它 scope 处理

| 编号 | 严重度 | 位置 | 问题 | 上报对象 |
| --- | --- | --- | --- | --- |
| C-10 | P2 | `ConfigKeys.UnqText` | `U1Q1/U1Q2/U1Q4` 都传入固定规格，`Settings.Get(UnqText)` 只在 `state == null` 的防御分支可达；U1SET 里保存的“桥架标识”对已注册命令没有实际作用（历史遗留） | **已处理（2026-11-01）**：键删除，防御分支改回历史默认值；含 `CommandAuditTests` 防回流断言 |

### 4.3 确认不是缺陷（不得“顺手修”）

| 行为 | 依据 | 说明 |
| --- | --- | --- |
| XLAYOUT 重复执行追加机台 ID 文字 | D-009 | 审计/性能优化不得消除 |
| U1LX 输入距离不计入 U1U/U1F 统计 | D-006 | 仅显示/标注数据 |
| U1F/U1U 不创建上游自动连线几何 | D-007 | 几何归用户 |
| 已存在插座数量不覆盖、缺失才补 1 | D-008 | 见 `OutletPolicy85Tests` |
| U1L 单行毫米标签计入电缆长度 | D-028 | 不要把 U1L 标注当“非电缆”过滤 |
| U1Q/U1C 两行 MTEXT 由统计器严格配对读回 | D-018/D-019 | 只迁移本命令选择集内的旧标签 |
| `U1SET`、`U1HELP`、`U1A`、`UNC_LOGIN` 不做授权拦截 | `ClientLoginFeature` 注释 + 授权恢复流程 | 授权过期时用户仍需设置/帮助/登录入口 |

## 5. 废弃代码清理记录

删除前均已全仓 `grep`（`src` + `tests`）确认无调用、无 `[CommandMethod]`、无测试/文档引用。

| 废弃符号 | 原位置 | 原状态 | 处理 |
| --- | --- | --- | --- |
| `UncadLineSet()` / `OpUnl()` | `Features/Unl/UnlFeature.cs` | 无 `[CommandMethod]`，仅互相调用 | 已删除 |
| `UncadTraySet()` / `OpUnq()` / `UncadTray()` | `Features/Unq/UnqFeature.cs` | `UncadTraySet`/`OpUnq` 仅互相调用；`UncadTray()` 无特性且无调用（U1Q1/Q2/Q4 与 UNQ1/2/4 都走 `Run(规格)`） | 已删除 |
| `UncadConduitSet()` | `Features/Conduit/ConduitFeature.cs` | 无 `[CommandMethod]`，无调用 | 已删除 |
| `UncadArchSet()` / `OpUnr()` | `Features/Unr/UnrFeature.cs` | 旧 OPUNR 交互式设置入口，无 `[CommandMethod]`，无调用 | 已删除（拱桥直径统一在 U1SET 维护） |
| `UncadRibbon()` | `Features/About/AboutFeature.cs` | 无 `[CommandMethod]`，无调用（Ribbon 由 Bootstrap 注册） | 已删除（Lead 授权） |
| `SettingsFeature.Show(int tabIndex)` 的 0/1/2 映射 | `Features/ConfigCenter/SettingsFeature.cs` | 1/2 页签只被已删除的旧 SET 入口使用 | 收敛为 `Show()` |
| `ConfigKeys.FillIncludeUnmatchedConduits` | `Infra/ConfigKeys.cs` | 运行期固定忽略的历史兼容键 | Lead 已删除（含 `ConfigKeysTests` 断言） |
| `ConfigKeys.FillBridge` | `Infra/ConfigKeys.cs`、`Infra/FillSettings.cs`、UI | 图框块 BRIDGE_FRAME_INFO 写入内容（空=不写） | **已删除（2026-11-01）**：xframe_v2 已无 BRIDGE_FRAME_INFO 属性，U1SET 字段、`FillOptions.BridgeInfo` 与 `FrameBlockFiller.TagBridge` 写入链一并清除（桥架数据仍进 BOQ 表与 DATA 工作表）；`CommandAuditTests` 防回流 |

## 6. 验证

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 构建（串行锁） | `powershell -NoProfile -File scripts/build-lock.ps1 -Run build` | 通过：0 警告 0 错误 |
| 全量测试 | `powershell -NoProfile -File scripts/build-lock.ps1 -Run test` | 通过：**1179 通过 / 0 失败 / 0 跳过**（基线 1138 + 本轮与其它 2.10.0 并行任务的新增用例） |
| 本轮专项测试 | `-Filter "FullyQualifiedName~FileLogger\|FullyQualifiedName~ConfigShare"`（27 通过）、`-Filter "FullyQualifiedName~OperationLog\|FullyQualifiedName~CommandAudit\|FullyQualifiedName~CommandRegistration\|FullyQualifiedName~CommandHelpCatalog\|FullyQualifiedName~ModuleRunner\|FullyQualifiedName~RibbonCatalog"`（26 通过）、`-Filter "FullyQualifiedName~XstsOutputLayout\|FullyQualifiedName~OutputLayout"`（20 通过） | 覆盖日志级别/按天文件/保留清理/命令标签嵌套、操作记录读写并发+损坏行+清理+取消+写失败降级、分享码 roundtrip/坏码/未知键/敏感键/checksum、XSTS 输出布局/文件名/默认根/占用预检、审计文档覆盖 35 条命令、废弃入口不回流 |
| 命令注册契约 | `CommandRegistrationTests`、`CommandHelpCatalogTests`、`RibbonCatalogTests` | 35 条命令注册/帮助/Ribbon 不漂移 |
