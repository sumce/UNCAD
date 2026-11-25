# UNCAD 结构与模块规矩（V1.1）

状态：2026-11-26 六路只读审计后的结构决策底稿。
范围：只回答“代码怎么分层、模块怎么划、共享能力住哪里”。
不替代：`docs/DECISIONS.md`（业务决策）、`AGENTS.md`（硬约束）、
`docs/DEVELOPMENT.md`（工程细则）。

## 一句话

**依赖只能向下，能力必须横切；命令只做编排，共享能力各有归属。**

## 层契约

| 层 | 可以做 | 不可以做 | 允许依赖 |
|---|---|---|---|
| `Core` | 解析、匹配、规则、规划、格式化、稳定文件格式 | AutoCAD、WPF、WinForms、注册表、网络、进程全局状态 | 无 |
| `Cad` | AutoCAD 实体、事务、选择集、图框/线路适配 | Excel 匹配、业务规则、UI | `Core`；`Infra` 仅日志 |
| `Infra` | 设置、日志、授权、注册表、网络、元数据 | CAD 实体、业务规则、UI | `Core` |
| `Features` | 命令编排、阶段服务、事务边界 | 跨 Feature 内部引用、直接 `new Window`、Excel 解析、CAD 几何算法 | `Core` / `Cad` / `Infra` / `Features.Shared` |
| `UI` | WPF 视图、编辑传入模型 | 读 Excel、扫图纸、业务规则、引用 `Features` | `Core` DTO、UI 契约 |

补充规则：

- `Features` 可以调用 UI，但 **UI 不能反向引用 `Features`**；结果 DTO 下沉到 `Core`。
- `Features.X` 不能引用 `Features.Y` 的内部实现；共享下沉到 `Core`、`Cad` 或
  `Features.Shared`。
- 纯函数可以 `static`；带副作用的服务（设置、日志、授权、CAD 适配、弹窗）
  必须有可替换的接缝。
- `Cad` 不允许 `using UNCAD.Core.Excel`；需要写表时拆成“Core 规划 + Cad 落笔”。

## 模块

| 模块 | 命令 | 入口 | 主要共享能力 |
|---|---|---|---|
| Fill | U1F/U1U/U1S/U1DWG/U1PDF/U1SET/U1DATA | `Features/Fill` | Frame 管线、BOQ 输出、整机门禁 |
| QuickLine | U1L/U1LX/U1UL/U13D/Xauto | `Features/Unl`、`Features/XAuto` | Route 管线、QuickLine 扫描/标注 |
| Marking | U1Q/U1C/U1R/U1B/U1DT | `Features/Unq`、`Conduit`、`Unr`、`Hose`、`Dimension` | 平行标注、图层治理、几何 |
| Layout | XLAYOUT/Xmerge | `Features/XLayout` | Frame 快照、排版规则 |
| Stat | XSTS | `Features/Stat` | Frame 快照、统计报告 |
| Host | 启动/Ribbon/设置/授权 | `Infra`、`UI` | Settings、Log、License |

模块规则：

- 模块之间不互相 `using`；共享能力下沉到 `Core`、`Cad` 或 `Features.Shared`。
- 每个模块必须有：命令入口、阶段服务、Core 规则、行为测试、文档条目。
- 新增命令必须同步 `CommandIds`、`CommandMethod`、Ribbon、`CommandHelpCatalog`、
  bundle 和注册测试。

## 共享能力归属

| 能力 | 归属 | 当前状态 |
|---|---|---|
| 图框收集 | `Cad/Frame` | `Cad/FrameRegionCollector`，基本正确 |
| 图框身份/内容读取 | `Cad/Frame` | `Features/Submit/FrameIdentityReader`、`CadSubmissionReader` 待下沉 |
| 图框规则/身份键/回路配对 | `Core/Frame` | `Core/Fill` + `Core/Submission` 待收口 |
| 线路扫描/毫米标 | `Cad/QuickLine` + `Core/Route` | 基本正确；`ScanDetailed` 待拆 |
| 原子写 | `Core/IO` | 5 套实现待抽 `AtomicFileWriter` |
| Excel/固定清单/快照 | `Core/Excel` | 正确 |
| 输出布局 | `Core/Submission/OutputLayout` | 正确 |
| 交互弹窗 | `Features/Shared/IUserInteraction` + `UI/Wpf` | Features 直调窗口 54 处待收 |
| 设置/日志/授权 | `Infra` | 正确；`Settings` 缺注入接缝 |

一个能力只能有一个家。发现第二套图框读取、第二套原子写、第二套报表写入，
按结构债处理，不允许“先复制一份再说”。

## 新命令 / 新模块 Definition of Done

1. 入口类 ≤200 行，阶段逻辑在独立 Stage/服务。
2. Core 规则有测试，失败分支有测试，注册/帮助/Ribbon/bundle 有契约测试。
3. 不引用其他 Feature 的内部实现；不直接 `new Window`（走 `Features.Shared`）。
4. 共享能力有明确归属；没有新增第二套实现。
5. 文件 ≤800 行、方法 ≤200 行（逐步收紧到 120）；超了拆或进白名单并写原因。
6. 只更新一个事实源（`ARCHITECTURE.md` / `DECISIONS.md` / `STATUS.md`），
   不在 README/AGENTS/CLAUDE 里重复。

## 迁移顺序

| 步 | 动作 | 状态 |
|---|---|---|
| 0 | characterization + `SourceContract` trait | 待做 |
| 1a | 纯死代码清理 | **完成**（删除 5 个源文件/1,430 行 + 3 个测试文件；全量 1,882 通过） |
| 1b | 抽 `Core/IO/AtomicFileWriter` | 待做 |
| 2 | 破 Submit↔Fill 环：共享读取器下沉 `Cad/Frame` | 待做 |
| 3 | `FrameSnapshot` L1/L2 + `FrameScope`；先 XSTS | 待做 |
| 4 | 拆 U1F/U1U 事务链：`Execute`/`Preflight.Build`/`ExecuteCore`/`Resolve` | 待做 |
| 5 | 拆 QuickLine：`ScanDetailed` + 2,177 行主服务 | 待做 |
| 6 | UI DTO / 最小网关 / 守卫 / Core 独立项目 / coverage | 待做 |

约束：每一步先补 characterization，再搬迁；源码字符串契约测试加
`[Trait("Category","SourceContract")]`，重构时过滤，不当行为网。

## 明确不做

- 不重写、不先拆项目、不先改命名空间/目录。
- 不引入 DI 容器；只做 UI、时钟、设置、授权四个接缝。
- 不把纯函数接口化；不把 U1L/U1LX 塞进 `FrameSnapshot`。
- 不在同一次提交里混“搬迁 + 行为变更 + 文档 + 版本号”。

## 审计依据

- 层依赖矩阵、跨 Feature 引用、UI↔Features 环。
- 37 条命令的模块归属与共享能力调用点。
- 315 个源文件 / 67,726 行（1a 清理前基线）的复杂度、死代码、重复实现。
- `FrameIdentity`/`frameinfo_json`/台账快照/回路键/线路扫描/BOQ/输出布局
  的领域契约。
- 1,891 个测试的构成、源码契约、隔离问题与覆盖缺口。
- 授权/原子写/回滚/WebView2/图层/性能/版本门禁等运行时硬约束。
