# UNCAD — AutoCAD 2022 C# 插件集

**UNSIAO Work™ | 云邵出品**

将 `./Max` 目录下的 4 个 AutoLISP 轻量插件移植为 C# .NET 插件。
目标环境：**AutoCAD 2022 / .NET Framework 4.8 / x64**，Visual Studio 2026。

开发模块边界、代码契约、事务、缓存、Layout和测试标准见 [开发一致性标准](docs/DEVELOPMENT.md)。

## v1.8.2 重点更新

- 固定清单已内嵌到插件程序集，随版本发布，用户不再提供清单Excel；`UNC_SET` 只需配置机台/设备表。
- 内嵌清单与源数据保持逐字段一致（当前157项），`32mm → 3.3 / 38mm`迁移规则随包内置。
- 清单更新流程：修改源表后运行 `scripts/GenerateEmbeddedCatalog.ps1` 重新生成资源并升级版本号。

## v1.8.1 重点更新

- `UNC_FILL`固定清单支持`类 / 项次编码 / 项目名称 / 项目特征 / 单位 / 别名 / 别名1`模板；主别名未命中时按全局唯一`别名1`迁移到指定数据库行。
- 手动添加的含义是从固定清单数据库选择现有项目并填写数量，不存在自由创建材料身份；未匹配项目固定禁止生成。
- `UNC_FILL`按“检测 → 实际求和 → 默认清单 → 用户评审 → 清除模板 → 连续写入”六阶段执行，并在代码和日志中明确阶段边界。
- 电缆固定清单未匹配时可搜索并选择替代型号；替代只影响清单，设备块和图框继续保留原电缆型号。
- 用户可从固定清单数据库手动加入项目并填写数量，例如选择数据库中的插座后填写`2个`；不存在自由创建材料名称、特征、单位或编码。
- 所有自动清单类别使用统一异常代码和确认规则；空固定清单、空输出、表格容量不足及提交无明细都会明确提示。
- `UNC_SUBMIT`详细导出同时记录设备原电缆、清单替代电缆及完整材料明细。
- 新代码强制注释模块责任、状态分离、回滚和异常分支的原因。
- 明确拆分 `SUM-STAT/求和统计` 与 `BOQ-TABLE/清单表格` 模块；日志和错误均显示模块ID、阶段及耗时。
- 填充配置改为单次不可变快照；未匹配项目固定禁止生成，旧版“未匹配管材默认选择”配置不再生效。
- BOQ分类规格建立一次索引；机台与固定清单使用自动失效缓存。
- 清单表和全部属性块共享一个AutoCAD事务，避免部分写入。
- 统一管径输入与严格型号匹配；`UNC_CONDUIT`标注使用真实曲线长度。
- 配置中心、机台选择和填充确认统一DPI/Layout、范围校验和错误定位。

## 命令一览

| 新命令（规范） | 旧命令 | 功能 |
| --- | --- | --- |
| `UNC_SET` | —（新增） | **统一配置中心**：一个页面管全部功能配置 |
| `UNC_FILL` | —（新增） | 按机台/设备回路填充清单表、图框和上下游属性块 |
| `UNC_FILL_UPDATE` | —（新增） | 读取已填充的机台ID和设备名称，按当前长度统计重新填充 |
| `UNC_SUBMIT` | —（新增） | 读取框选的填充信息并新增或覆盖 XLSX 提交记录 |
| `UNC_ABOUT` | —（新增） | 查看版本、构建时间、授权状态、所有权和联系方式 |
| `UNC_RIBBON` | —（新增） | 显示Ribbon并重新注册隐藏的UNCAD页签 |
| `UNC_CONDUIT` | —（新增） | 生成紫色偏移线管及按实际曲线长度计算的标注 |
| `UNC_STAT` | UNADD | 统计汇总 → 图纸 MTEXT |
| `UNC_STAT_EX` | UNADDX | 统计汇总 → **Excel 报表**（NPOI） |
| `UNC_LINE` | UNL | 连续画带标注线段（同 L 命令，每段立即生成占位文字） |
| `UNC_TRAY` | —（新增） | **选中线段生成桥架标注**（红线+规格文字，规格在 UNC_SET 配置） |
| `UNC_TRAY100` / `200` / `400` | UNQ1/2/4 | 选中线段生成桥架标注（预设规格） |
| `UNC_ARCH` | UNR | 拱桥开洞（直接生成圆弧并截断直线，不依赖 ARC/BREAK 命令调用） |
| `*_SET` | 对应 OP* | 打开统一配置中心对应页签（UNC_SET） |

> 旧命令保留为**兼容别名**；Ribbon 使用中文任务名称，规格和设置集中在下拉菜单，不显示命令字符串。
> 若命令可用但UNCAD页签未显示，执行 `UNC_RIBBON`；若 `UNC_ABOUT` 提示未知命令，请重新运行当前版本安装器。安装器会解除下载文件阻止并恢复命令触发加载。当前用户安装仅对执行安装的Windows账号生效，共用电脑应选择所有用户安装。

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

- **机台数据与固定清单 Excel**：均按完整路径、文件大小和最后修改时间缓存，文件一旦保存即自动失效并重新读取；缓存返回副本，避免预览编辑污染后续命令。
- 每次填充只建立一个固定清单索引，机台和回路切换预览不再重复扫描整张清单。
- 固定清单路径留空时，继续读取机台数据文件内的 Sheet2，兼容原有单文件工作簿。
- Sheet1 按表头名称绑定字段，允许调整列顺序；缺失或重复必需字段会中止并报告具体字段。
- 当前固定模板的规格列表头为空时，仍兼容第 6 列；建议后续将该表头明确命名为“规格”。
- 刚性线管和软管先按类别和主别名严格匹配；主别名未命中时，仅允许按数据库的全局唯一`别名1`迁移到指定行，例如`32mm`迁移到该行的`38mm`材料。两列都未命中时必须从固定清单替换或删除，不能直接生成。
- 选择设备/回路后会打开“填充确认”：自动匹配到的电缆、桥架、线管、软管、断路器、插座等默认全部勾选。
- 可删除或取消不需要的项目（例如插座盘不生成插座），也可修改基础信息、清单电缆型号/长度、软管直径及每行名称、特征、单位、数量和项目编码；删除后按当前清单顺序连续写入。修改软管直径时，名称、特征、单位和编码立即重新匹配，数量保持不变。
- 固定清单找不到设备电缆型号时会询问是否替换。替代型号只影响本次清单，图框和块属性继续保留设备原电缆型号；其他自动清单项未匹配时也会显示异常代码并要求明确确认。
- 软管始终生成，默认数量为可配置的 `1.5M`。Excel软管直径为空时，若本次只有一种线管规格则自动推断，否则生成可编辑的通用软管行；`DN32`、`Φ32`、`32mm`等输入统一规范为同一真实直径。
- 清单表、图框、设备块和上下游块在同一个AutoCAD事务中提交；任何未处理写入错误都会整体回滚，避免半完成数据。
- 写入前先清空配置的数据区（默认从 No.1 开始清空 11 行），随后严格按最终勾选数量逐行生成；全部取消时只清空、不生成清单。
- 已填充后修改了桥架或多处电缆长度，可执行 `UNC_FILL_UPDATE`：命令从图框/设备块自动读取机台ID和设备名称并定位Excel回路，不再显示机台选择窗口；后续统计、清单确认、清空和写入流程与 `UNC_FILL` 完全一致。
- 更新身份兼容新版 `MACHINEID-POWER + DEVICENAME` 和旧版 `MACHINEID-DEVICE`；无法唯一定位同机台多回路时会中止并提示使用普通填充。

## UNC_SUBMIT 提交记录

- 框选 UNC_FILL 已填充的图框、设备块、上下游块及清单表后执行 `UNC_SUBMIT`。
- 第一次提交选择文件夹，之后自动更新该目录下的 `UNCAD_Submissions.xlsx`。
- `提交记录`工作表输出机台ID、设备名称、盘柜类型、设备原电缆型号、清单替代电缆型号及米数、软管直径及米数、桥架/线管信息及米数、FR、配电详情、上下游轴位和时间。
- `清单明细`工作表逐项输出材料名称、特征描述、单位、数量和项目编码，覆盖电缆、桥架、线管、软管、断路器、插座和母线插接箱等全部清单行。
- 提交汇总以图纸清单表的当前值为准：用户在填充后手工修改电缆型号/长度、桥架或线管信息和米数，`UNC_SUBMIT`会读取修改后的值；图框属性仅在表格无对应行时后备使用。
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
| `UNC_FILL_TABLE_ROW` / `UNC_FILL_CLEAR_ROWS` / `UNC_FILL_TEXT_HEIGHT` | 清单起始数据行 / 每次清空行数 / 表格文字高度 | 1 / 11 / 500 |
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
