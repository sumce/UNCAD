# UNCAD — AutoCAD 2022 C# 插件集

**UNSIAO Work™ | 云邵出品**

将 `./Max` 目录下的 4 个 AutoLISP 轻量插件移植为 C# .NET 插件。
目标环境：**AutoCAD 2022 / .NET Framework 4.8 / x64**，Visual Studio 2026。

## 命令一览

| 新命令（规范） | 旧命令 | 功能 |
| --- | --- | --- |
| `UNC_SET` | —（新增） | **统一配置中心**：一个页面管全部功能配置 |
| `UNC_FILL` | —（新增） | 按机台/设备回路填充清单表、图框和上下游属性块 |
| `UNC_SUBMIT` | —（新增） | 读取框选的填充信息并新增或覆盖 XLSX 提交记录 |
| `UNC_ABOUT` | —（新增） | 查看版本、构建时间、授权状态、所有权和联系方式 |
| `UNC_CONDUIT` | —（新增） | 生成紫色偏移线管及固定 2000mm 标注 |
| `UNC_STAT` | UNADD | 统计汇总 → 图纸 MTEXT |
| `UNC_STAT_EX` | UNADDX | 统计汇总 → **Excel 报表**（NPOI） |
| `UNC_LINE` | UNL | 连续画带标注线段（同 L 命令，每段立即生成占位文字） |
| `UNC_TRAY` | —（新增） | **选中线段生成桥架标注**（红线+规格文字，规格在 UNC_SET 配置） |
| `UNC_TRAY100` / `200` / `400` | UNQ1/2/4 | 选中线段生成桥架标注（预设规格） |
| `UNC_ARCH` | UNR | 拱桥开洞 |
| `*_SET` | 对应 OP* | 打开统一配置中心对应页签（UNC_SET） |

> 旧命令保留为**兼容别名**；Ribbon 使用中文任务名称，规格和设置集中在下拉菜单，不显示命令字符串。

## 命令命名规范

- **前缀**：`UNC_`（UN CAD 缩写，全部大写、下划线分隔）——已在 AutoCAD 2022 别名表确认无冲突（`UN`/UNI/UNCREASE 等短名已被占用，禁止使用）；
- **结构**：`UNC_<模块>[_<变体>][_SET]`
  - `<模块>`：功能英文全大写（STAT 统计 / LINE 线段 / TRAY 桥架 / ARCH 开洞）
  - `<变体>`：规格参数（TRAY100/200/400）或 EX（Excel 导出）
  - `_SET`：配置命令后缀
- 新增命令前先在 `acad.pgp` 别名表核对无冲突；
- 详见 ARCHITECTURE.md §3.6。

## 构建

- **VS 2026**：打开 `UNCAD.sln` 直接生成（F5 会启动 AutoCAD 2022 并附加调试器）。
- **命令行**：`.\build.ps1`（等价 `dotnet build UNCAD.slnx`）。依赖已还原时可用 `.\build.ps1 -NoRestore` 快速刷新 Bundle。
- AutoCAD 不在默认路径时：`dotnet build UNCAD.sln -p:AutoCADDir="你的 AutoCAD 目录"`。

## 加载与调试

1. VS 中按 **F5**（已配置启动 `acad.exe`）；
2. 或手动：启动 AutoCAD 2022 → 命令行输入 `NETLOAD` → 选择 `bin\Debug\net48\UNCAD.dll`。

## 最终用户部署（Bundle 分发）

开发调试用 VS/NETLOAD，但**给最终用户要用 AutoCAD 官方 Bundle 机制**——装好即自动加载，无需任何开发环境。

### 安装步骤（用户机器的操作）

1. 解压完整发布 ZIP，不要单独复制 DLL；
2. 完全退出 AutoCAD，双击 `setup.bat`；
3. 选择“Install or repair for current user”（推荐），安装器会检测 AutoCAD 2022 并校验清单、版本和全部文件的 SHA-256；
4. 看到 `INSTALLATION SUCCESSFUL` 后启动 AutoCAD 2022，打开 `UNCAD · UNSIAO Work™` Ribbon；
5. 可随时运行 `verify-install.bat` 独立验证已安装文件。

快捷入口：`install-user.bat` 安装当前用户，`install-all.bat` 安装所有用户并请求 UAC，`uninstall.bat` / `uninstall-all.bat` 分别卸载。

### 安装器保证

- AutoCAD 正在运行时拒绝安装和卸载，防止覆盖已加载的 DLL；
- 安装前验证 AutoCAD 2022（R24.1）、PackageContents、DLL 版本和必需依赖；
- 先复制到目标目录内的暂存文件夹并验证所有 SHA-256，再原子切换到正式目录；
- 覆盖安装前备份旧 Bundle，任何步骤失败都会尝试恢复旧版本；
- 当前用户和所有用户安装不能同时存在，避免 AutoCAD 重复加载同一 ProductCode；
- 日志写入 `%TEMP%\UNCAD-Setup-*.log`，成功和失败都有明确退出码。

### 说明

- 安装位置：`%APPDATA%\Autodesk\ApplicationPlugins\`（当前用户）或 `%ProgramData%\Autodesk\ApplicationPlugins\`（所有用户）；
- 首次加载如出现安全提示，选择**始终加载**（或把插件目录加入受信任路径）；
- 分发包锁定 AutoCAD 2022（`SeriesMin/Max = R24.1`）；未来支持其他版本时，每个版本各打一个 bundle 即可；
- 每次 `build.ps1` 构建后会自动更新分发包内的 `UNCAD.dll`。

## UNC_FILL 数据源

UNC_FILL 将频繁更新的机台数据与固定 BOQ 清单分开管理：

- **机台数据 Excel**：每次执行命令都重新读取包含“机台ID”表头的工作表，确保 Sheet1 更新立即生效。
- **固定清单 Excel**：读取包含“项目特征”表头的工作表，并按完整路径、文件大小和最后修改时间缓存。
- 固定清单路径留空时，继续读取机台数据文件内的 Sheet2，兼容原有单文件工作簿。
- Sheet1 按表头名称绑定字段，允许调整列顺序；缺失或重复必需字段会中止并报告具体字段。
- 当前固定模板的规格列表头为空时，仍兼容第 6 列；建议后续将该表头明确命名为“规格”。

## UNC_SUBMIT 提交记录

- 框选 UNC_FILL 已填充的图框、设备块、上下游块及清单表后执行 `UNC_SUBMIT`。
- 第一次提交选择文件夹，之后自动更新该目录下的 `UNCAD_Submissions.xlsx`。
- `提交记录`工作表输出机台ID、设备名称、盘柜类型、电缆型号及米数、软管直径及米数、桥架/线管信息及米数、FR、配电详情、上下游轴位和时间。
- `清单明细`工作表逐项输出材料名称、特征描述、单位、数量和项目编码，覆盖电缆、桥架、线管、软管、断路器、插座和母线插接箱等全部清单行。
- “机台ID + 设备名称”相同时同时替换汇总和清单明细，保留首次提交时间；旧版Excel会自动追加新列，不删除已有记录。
- 可在 `UNC_SET → Excel 数据` 中修改提交文件夹。

## 统计匹配规则

- `TEXT`按单个文字实体统计；`MTEXT`按 `\P` 拆分后逐行统计，两种来源可在 `UNC_SET → 统计汇总` 分别开关。
- 电缆、桥架和线管类别可分别开关，默认全部开启。
- 电缆严格格式：`2000mm`；桥架严格格式：`桥架200*100 12格`；线管严格格式：`Φ20线管 2000mm`。
- 每行必须完整匹配，不接受 `(共用)`、`共用`、前后备注或中间附加文字。桥架只支持 `*`、`x`、`X` 作为规格分隔符。

## 设置持久化

所有配置**通过统一配置中心 `UNC_SET` 修改**（页签：线段/桥架/拱桥/统计），底层存注册表（键名与 LISP 版一致，位置：`HKCU\Software\Autodesk\AutoCAD\...\Variables`）：

| 键 | 含义 | 默认值 |
| --- | --- | --- |
| `UNL_TEXT` / `UNL_HEIGHT` / `UNL_POS` / `UNL_OFFSET` | 占位文字 / 高度 / 位置(0居中 1靠边下 2靠边上) / 偏移距离(0=贴线) | 2000mm / 180 / 1 / 0 |
| `UNQ_TEXT` / `UNQ_HEIGHT` / `UNQ_LINE_OFF` / `UNQ_TEXT_OFF` / `UNQ_SIDE` | 规格 / 高度 / 红线偏移(0=默认15贴线) / 文字偏移(0=贴线) / 侧(1上 0下) | 桥架200*100 10格 / 180 / 0 / 0 / 1 |
| `UNR_DIAMETER` | UNR 拱桥直径 | 300 |
| `UNC_STYLE_NAME` / `UNC_STYLE_FONT` / `UNC_STYLE_BIGFONT` / `UNC_STYLE_WIDTH` | 文字样式：样式名 / 字体文件 / 大字体(空=TTF) / 宽高比 | UNC-标注 / msyh.ttf / (空) / 0.8 |
| `UNADD_HEIGHT` / `UNADD_MM_PER_GRID` | 统计输出文字高度 / 桥架每格毫米数 | 180 / 250 |
| `UNADD_TEXT_ENABLED` / `UNADD_MTEXT_ENABLED` | 单行文字 / 多行文字参与统计 | 1 / 1 |
| `UNADD_CABLE_ENABLED` / `UNADD_BRIDGE_ENABLED` / `UNADD_CONDUIT_ENABLED` | 电缆 / 桥架 / 线管参与统计 | 1 / 1 / 1 |
| `UNC_FILL_EXCEL` | 每次重读的机台数据 Excel | 空 |
| `UNC_FILL_CATALOG_EXCEL` | 固定 BOQ 清单 Excel（空=同机台文件） | 空 |
| `UNC_FILL_TABLE_ROW` / `UNC_FILL_TEXT_HEIGHT` | 清单起始数据行 / 表格文字高度 | 1 / 500 |
| `UNC_SUBMIT_FOLDER` | UNC_SUBMIT 自动提交记录文件夹 | 空（首次提交时选择） |

## 移植差异说明

- **修复** LISP 版 `unl.lsp` 的 U 撤销 bug（原 `entmake ENTDEL` 无效，现直接删除实体）。
- 配置由 DCL 改为 **统一配置中心**（`UNC_SET`，WinForms 页签式）。
- 核心业务逻辑保留LISP兼容行为；统计文字改为可配置来源和严格整行匹配，避免备注文字误计。
- 交互绘制命令 UNC_LINE 行为同 AutoCAD L 命令：回车结束、U 撤销上一段、ESC 全部清除；UNC_TRAY 为选中线段批量生成。
- 每段独立事务立即提交（点一下生成一段）；撤销按段进行。

## 工程结构（分层架构）

> 完整架构设计见 **ARCHITECTURE.md**（分层原则、扩展指南、未来模块蓝图）。

```
UNCAD/
├─ UNCAD.slnx / build.ps1 / README.md / ARCHITECTURE.md
├─ bundle/UNCAD.bundle/        ← 用户分发包（PackageContents.xml + UNCAD.dll）
└─ src/UNCAD/
   ├─ UNCAD.csproj             (net48 / x64 / 引用 AutoCAD 2022 DLL + AdWindows)
   ├─ Bootstrap.cs             (启动诊断 + Ribbon 注册)
   ├─ Core/                    (纯 C# 可单测：解析 / 统计引擎 / 契约)
   ├─ Cad/                     (AutoCAD 适配：CommandBase / CadContext / 实体工厂 / 选择集)
   ├─ Infra/                   (CommandIds / 配置 / 日志 / Feature 元数据)
   ├─ Ui/                      (RibbonCatalog 任务布局 + Autodesk 渲染器)
   └─ Features/                (每个功能一个目录：Unadd / Unl / Unq / Unr)
```

**扩展新功能**：在 `CommandIds` 声明命令，在 `Features/MyFeature/` 实现功能，
再按用户任务将入口加入 `RibbonCatalog`；详见 ARCHITECTURE.md 第 4 节。

## 多版本扩展（预留）

当前仅目标 AutoCAD 2022（net48）。如需支持 2025+（net8.0），新增一个 SDK 工程引用对应版本的
`AcMgd.dll` 等即可复用全部源码（代码未使用版本专属 API）。
