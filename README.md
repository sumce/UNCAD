# UNCAD — AutoCAD 2022 C# 插件集

**UNCAD · AutoCAD Engineering Tools**

Current 2.4.9 builds publish 25 commands and intentionally do not include the removed `U1X` 3D editor. Use `U1LX` (or legacy `UNLX`) for quick line annotation; `U1D` converts aligned-dimension labels to editable single-line text. Older `U1X` references below are historical release notes.

维护入口：先读 `AGENTS.md` 和 `docs/PROJECT_CONTEXT.md`；产品决策记录在
`docs/DECISIONS.md`，按模块收集源码与测试使用 `scripts/context.ps1`。

`XSTS` opens a GUI report for selected frame drawings, first identifies the selected machine IDs, then compares only those machines' selected circuits with the configured machine workbook, and exports an `.xlsx` report. `Xmerge` opens a drag-and-drop DWG picker, recursively expands folders, and imports the selected drawings into the active drawing using XLAYOUT spacing.

`U1F/U1U` update frame, device, upstream and BOQ data without creating connection geometry. Device-side blocks are normalized to the configured green and upstream-side blocks to the configured magenta. The removed automatic upstream connection behavior remains documented only in older release notes.

## v2.4.9

- 优化机台 ID 与设备名称匹配：忽略大小写、普通空格、Tab、换行、全角空格及文本内部空白，`M Q-01` 与 `MQ-01`、`设备 A` 与 `设备A` 可正确对应。
- 统一图框、Excel 快照、XSTS、XLAYOUT、DWG 导出、U1S/自动提交和 BOQ 分组的身份匹配规则；显示、写回和持久化仍保留原始文本。
- 修复图框属性中的机台/设备身份与设备块属性不一致时静默采用块名称的问题，现在会停止并提示不一致，避免错误填充。

## v2.4.8

- 修复桥架 BOQ 两行 MTEXT 在格式码、额外空白和大小写差异下的识别与求和；严格按“固定清单型号 + 纯长度”配对，避免孤立长度或带前缀文本误加入清单。
- 修复 `U1Q*` 在大坐标、多角度斜线下的端点、中点和偏移计算，批量标注可稳定生成。
- 统一机台表在前 51 个物理行内自动识别表头，兼容第二行表头；固定清单生成脚本使用同一规则。
- 移除未使用的 JSON 依赖，缩小发行包并减少宿主加载冲突。
- 修复 Windows 150%/200% 缩放及多显示器切换下窗口超出屏幕、导航和列表尺寸未跟随 DPI 的问题。

## v2.4.7

- `U1SET` 的 BOQ 自动填充新增独立“电盘”开关；电盘、断路器与插座盘分别控制。
- `I-Line盘` 按固定清单分别生成电盘和断路器，修复断路器开关代替电盘的问题。
- 修复长两行线管 MTEXT 被分配到相邻图框后，`U1U` 无法自动更新线管数量的问题。
- `U1F/U1U` 自动升级旧桥架标注为 BOQ 两行样式，并保留或修复标注角度。
- 优化 `U1SET`、授权与关于窗口布局，构建时间统一显示为 UTC+8。

## v2.4.6

- `U1Q1/U1Q2/U1Q4` 与 `U1C` 使用“固定清单型号 + 长度”的两行 MTEXT，统计读取时严格配对还原。
- 未匹配的桥架、线管和清单材料在 CAD/BOQ 写入前停止，避免无编码项目进入图纸。
- 修复 U1U 清单编码继承、旧桥架格数迁移和固定编码插座判定问题。

## v2.4.5

- `U1F/U1U/UNADD` 统计支持识别对齐/转角标注中人工输入的文字（如 `2000mm`）；自动测量的标注不参与统计，可在 `U1SET` 独立开关（默认开启）。
- 新增 `U1D` 对齐标注转文字：将标注文字转换为可编辑单行文字并保留原尺寸线。
- 修复 150% 及以上屏幕缩放下 U1SET 等窗口输入框和页面显示不全的问题。
- U1Q/U1LX 标注重跑不再重复生成，跳过原因逐项提示；Xmerge 保持块样式并增强批量导入。

## v2.4.4

- 修复 U1F/U1U/U1S 批量流程的 CAD/BOQ 固定清单编码一致性与回滚边界。
- 替代型号、图框身份和变更记录统一保存到 `frameinfo_json`，后续更新可复用用户确认结果。
- 强化旧图框与 xframe 迁移、XLAYOUT 身份读取、XSTS 异常统计和 Xmerge 块定义冲突处理。

## v2.4.3

- AutoCAD 启动后显示全屏品牌动画，由 .NET 在 5 秒后关闭；可在 `U1SET` 中关闭。
- 修复 `U1LX` 从末端线不同端点击时识别数量不一致的问题。
- `U1LX` 将末尾为 `00mm` 的标注继续视为可修改占位符。

## v2.4.1

- U1SET 只有在用户点击“刷新”时才解析本地或网络机台工作簿，并将结果写入本机 SQLite 快照。
- U1F/U1U/XSTS/XLAYOUT 只按需查询上次成功的 SQLite 快照；源 Excel 修改后必须再次手动刷新才会生效。
- 刷新失败保留上一次有效快照；统一程序集、产品元数据和 Bundle 版本为 `2.4.1` / `2.4.1.0`。

## v2.4.0

- Hardened U1F/U1U CAD and BOQ rollback so partial failures do not overwrite newer external workbook changes.
- Fixed mutually exclusive cable/tray classification, duplicate BOQ row matching, and Xmerge-compatible block recognition.
- Improved online-license resilience, network workbook caching, Ribbon rebuilding, and release package validation.
- Unified assembly, package, and product metadata versions at `2.4.0`.

## v2.3.0

- Fixed QuickLine text helpers so they can be tested and used without eagerly loading AutoCAD runtime classes.
- Fixed batch `U1U` so the comparison dialog is shown at most once per update.
- Unified assembly, package, and product metadata versions at `2.3.0`.

## v2.2.2 重点更新

- 版本号升级为 2.2.2（程序集/Bundle 版本 2.2.2.0），保持当前 24 个公开命令和已验证的 U1/U1Q/XLAYOUT/XSTS/Xmerge 功能。
- 只发布统一的 `UNCAD Pro`，安装包不再内置客户、授权人、授权模式或到期时间，也不再生成客户专版和试用版。
- 唯一发行包输出到 `artifacts\Pro\UNCAD-Pro-v2.2.2.0.zip`；服务器 Key 按授权码存放在 `artifacts\Pro\server-key`。
- 首次使用时由客户输入授权码，授权码只保存在客户机注册表而不嵌入程序；客户信息、授权模式、到期时间和通知全部从 Key 读取，并每 5 分钟后台刷新。密钥缺失、请求失败、状态停用或到期时可立即更换授权码；授权服务器地址不在界面、日志和发行说明中展示。
- 移除不适用于多形态 `upstream` 动态块的自动连线功能；U1F/U1U 不再创建或修改连接线。
- U1F/U1U 自动统一端点块颜色：设备块和设备轴位块（DS）默认使用绿色，`upstream`、上游信息/编号块和上游轴位块（US）默认使用洋红；两组 ACI 颜色可在 `U1SET → Excel 数据` 中通过色块下拉框配置。

开发者：**UNSIAO.Ltd** · 官网：**www.unsiao.com**

将 `./Max` 目录下的 4 个 AutoLISP 轻量插件移植为 C# .NET 插件。
目标环境：**AutoCAD 2022 / .NET Framework 4.8 / x64**，Visual Studio 2026。

开发模块边界、代码契约、事务、缓存、Layout和测试标准见 [开发一致性标准](docs/DEVELOPMENT.md)。

## v2.2.0 重点更新

- U1F 首次生成时按固定电缆清单建立电缆与软管直径关系；Ruanguan 动态块的有效长度（毫米或米）写入软管清单。
- U1U 选中图框时自动读取同框表格、统计文字、Device_Build20260716 和 Ruanguan；电缆型号依次按图框、现有清单和固定清单匹配，无法匹配时在写入前集中选择。
- U1U 自动更新电缆、桥架、线管数量；没有新的统计文字时保留现有数量，删除 Ruanguan 块时删除软管行。
- Device 插座状态只负责存在性：没有插座行时新增 1 个，已有插座行完整保留用户填写的数量和型号；设备状态恢复为设备时删除 8.x 插座行。
- U1C/U1F/U1U 会按电缆映射直径更新图框内 Ruanguan 长度标签，例如 `1500mm` 规范为 `20mm软管:1500mm`；只处理可识别的长度属性，不覆盖坐标等动态参数。
- U1Q 系列统一生成“桥架型号 + 总长度 mm”；旧的“格数格”输入会按 `UNADD_MM_PER_GRID` 转换为毫米；U1L 非法长度设置回退为 `2000mm`。
- U1F/U1U 会保存机台/设备信息、原始与 BOQ 电缆型号、最后修改时间及变更记录。新图框可使用独立 `frameinfo_json` 块；旧版 `frame_20260812` 若没有该块或只有空的 `FRAMEINFO_JSON` 属性，执行 U1F/U1U 时会自动迁移并填充元数据，保留原有可见几何和属性，不重复创建 JSON 记录。
- U1U 批量遇到未匹配固定清单的项目会一次性询问是否采用默认数据；确认后写入 CAD 表格，无编码的占位项不会被猜测提交到自动 BOQ。
- 版本：2.2.0；AutoCAD 2022 / .NET Framework 4.8 / x64。

## v2.1.6 重点更新

- XLAYOUT 机台 ID 改用固定左侧定位距离 300000，所有行共用同一文字起点，不再依赖不同字符串长度的动态对齐。
- 修复 U1U 多选图框时对象同时归属多个图框的问题，U1U/U1S/U1DWG/XLAYOUT 统一使用稳定的实体锚点归属。
- 保留图框排版、鼠标指定位置、回车使用默认原点和 25000 文字高度功能。
- 历史版本曾提供 `U1LX`/`U1X` 的 3D 编辑器；当前 2.2.2 使用 `U1LX`（兼容命令 `UNLX`）在命令行中选择已有 U1L 线段并逐段填写真实毫米距离，已移除的 `U1X` 仅保留在历史说明中。

## v2.1.5 重点更新

- 修复 XLAYOUT 机台 ID 文字对齐不稳定的问题：文字加入当前数据库后显式刷新 AutoCAD 对齐状态。
- 不同长度的机台 ID 统一使用首图框左侧的右对齐锚点，较长文本向左扩展，不再压入图框内的机台 ID。
- 保留 v2.1.4 的鼠标点击排版位置、回车使用默认原点和 25000 文字高度功能。

## v2.1.4 重点更新

- XLAYOUT 排版前支持鼠标点击指定排版左上角位置；直接回车使用原点默认位置，取消则不修改图纸。
- 每行第一个图框左侧自动生成机台 ID 文字，文字高度固定为 25000，使用右对齐锚点适配较长机台 ID。
- 机台 ID 文字和图框移动在同一个 CAD 事务中提交，任一写入失败则整体回滚。
- 保留 v2.1.3 的多图框识别修复、通用服务和全部既有业务功能。

## v2.1.3 重点更新

- 修复 2.0 之后 U1DWG 和 XLAYOUT 多选图框时的误判：跨图框对象不再因外包框相交而被报告为多个归属。
- U1U/U1S 继续使用严格实体边界预检；U1DWG/XLAYOUT 恢复 1.9x 的实体锚点归属策略，并对相邻图框边界做确定性分配。
- 新增多图框回归契约测试，覆盖导出/排版与批量更新的两种归属策略。
- 保留 v2.1.2 的通用服务、U1HELP 帮助页面和全部既有业务功能。

## v2.1.2 重点更新

- 新增 U1HELP 命令帮助页面，逐项说明全部 22 个公开命令的使用方法、功能和注意事项。
- 抽取 FrameIdentityReader 和 CommandHelpCatalog 等通用服务，U1S、U1DWG、XLAYOUT 统一复用图框身份读取和命令契约。
- 补充模块边界、依赖方向、复用规则、事务责任和代码评审门槛，作为后续专业开发标准。
- 保留 v2.1.1 的 XLAYOUT 图框自动排版及全部既有业务功能。

## v2.1.1 重点更新

- 新增 XLAYOUT：复用 U1U/U1S 的 frame_20260812 图框识别，按机台 ID 分行自动排版。
- 同一机台的全部图框位于同一行，不同机台进入下一行，横向和纵向间距均为 10000。
- 排版完成后弹出 GUI，显示每个机台 ID 及其选中图框对应的回路数量。
- 保留 v2.1.0 的全部命令和业务功能。

## v2.1.0 重点更新

- 保留 v1.9.7.16 的全部 18 个已注册命令和业务功能。
- 修复选择集解析失败后仍继续写图、批量 BOQ/DWG 输出部分成功、图框归属只看中心点、统计分类开关失效和 U1L 取消清理静默失败等问题。
- U1F/U1U 的 BOQ 读取进入当前 CAD 事务；多文件输出增加批次备份恢复。
- 版本：2.1.0；AutoCAD 2022 / .NET Framework 4.8 / x64。

## v1.9.7.16 重点更新

- `U1S` 改为只读提交：选择一个或多个当前 `frame_20260812` 图框，将清单项目编码、单位、数量及 `M` 米数提交到按机台输出的 BOQ Excel，不读取机台数据 Excel，也不修改 CAD。
- 配置中心迁移为 `U1SET`；Ribbon、安装清单和所有当前设置提示同步更新。
- `U1F/U1U` 原有 CAD 更新与自动 BOQ 同步行为保持不变，`U1S` 用于独立重复提交当前图框状态。

## v1.9.7.15 重点更新

- U1F/U1U 的插座存在性改由 `Device_Build20260716` 动态状态决定：`插座5孔/插座3孔` 输出插座，`设备`或块不存在不输出；插座型号仍按 DETAIL 电流匹配 8.2/8.3。
- `upstream` 动态块按机台 NEXT 自动同步：`I-Line盘 → I-line_Panel`、`插座盘 → socket box`、`母线插接口 → busbar connector`。
- 单框和批量 U1U 使用同一规则；同图框 Device 状态冲突时预检失败并保持图纸不变。

## v1.9.7.14 重点更新

- 新增独立临时授权构建与发布包，有效期至北京时间 `2026-09-03 00:00:00 UTC+8`；正式版继续保持无期限，两个构建互不覆盖。
- 关于与授权页面按构建类型显示正式版或试用版状态；临时授权到期后继续允许查看授权和条款，但阻止 U1F、U1U、U1DWG 写入/导出。

## v1.9.7.13 重点更新

- 修复 AutoCAD 宿主中首次生成 BOQ 时从 acad.exe 目录查找模板的问题；现在始终从 UNCAD.dll 所在插件目录加载 BOQ_Template.xlsx，并在错误信息中显示实际搜索目录。

## v1.9.7.12 重点更新

- 修复 U1A 关于窗口中 UNSIAO 品牌标识在 AutoCAD 宿主下使用透明背景导致的“控件不支持透明的背景色”错误。

## v1.9.7.11 重点更新

- 品牌统一为 `UNCAD` / `UNSIAO.Ltd`，官网为 `www.unsiao.com`；关于窗口新增产品信息、授权状态、使用条款和公司页签。
- 授权模式、有效期和软件版本集中显示；当前发行版为正式版且无期限。有效期版本在授权到期后禁止 U1F/U1U CAD 写入、BOQ 输出和 U1DWG 导出。
- AutoCAD Ribbon 重排为“清单 / 标注 / 统计 / 系统”，首组突出 U1F 生成、U1U 更新和 U1DWG 导出，减少与 AutoCAD 原生功能区的视觉冲突。
- U1S、U1F 清单确认窗口统一间距、颜色和信息层级，明确软管直径来自电缆型号、软管长度来自 Ruanguan 动态块。

## v1.9.7.10 重点更新

- 软管直径改为完全根据电缆型号和插件内嵌对照表生成；支持完整电缆型号、空格及 `x/X/×` 规格写法。机台表中的软管型号/直径为空或旧值都不会参与直径判断，未知电缆也不会回退到旧直径或线管统计直径。
- `Ruanguan` 动态块仍只负责软管长度：有效毫米值换算为米；缺失或无效时不生成软管行。

## v1.9.7.9 重点更新

- `U1F/U1U`从图框内名为 `Ruanguan` 的动态块读取软管长度，支持 `2000mm`、`软管 2000mm` 等格式并换算为米；找不到有效值时不加入软管清单，不再使用默认长度。

## v1.9.7.8 重点更新

- 批量 `U1U` 允许多个物理图框使用相同的机台ID和设备名；同机台同名设备在 BOQ 中汇总到同一设备列。

## v1.9.7.7 重点更新

- `U1U`从现有清单回退电缆时，改用“项目特征”列匹配固定清单，匹配成功后使用固定清单的标准别名、编号和特征。

## v1.9.7.6 重点更新

- 批量 `U1U` 同样使用当前图框清单表回退电缆型号；固定清单错误会显示机台ID、设备名和图框句柄。

## v1.9.7.5 重点更新

- `U1U` 匹配电缆型号失败时，优先读取当前图框清单表中的用户确认型号；表内型号也无法匹配固定清单时，在 CAD 写入前抛出明确异常。

## v1.9.7.4 重点更新

- 机台信息改用绝对左对齐和基线定位，并为首个图框左侧预留独立文字区，避免文字挤入图框。

## v1.9.7.3 重点更新

- 修复 `U1DWG` 机台信息文字未明确右对齐导致挤入首个图框的问题；信息区右边界固定在首个图框左侧。

## v1.9.7.2 重点更新

- 修复 `U1DWG` 写入机台信息时的 `eWrongDatabase`：新增文字实体先绑定输出数据库默认值，再设置文字样式和图层。

## v1.9.7.1 重点更新

- 修复 `U1DWG` 导出时的 `eWrongDatabase`：同一机台一次性生成完整 Wblock，再在目标库内部完成排列。

## v1.9.7 重点更新

- `U1DWG`导出文件打开后以第一个图框为中心视角，首个图框左侧增加机台ID标题，文字高度 `25000`。
- 标题下方写入 `UNSIAO.Ltd`、生成时间、软件版本和 Windows 用户名。
- `U1U`和`U1DWG`统一使用 `U1SET`选择的输出根目录。

## v1.9.6 重点更新

- 自动提交改为按机台输出正式 BOQ：`用户选择的文件夹\机台ID\[BOQ]机台ID.xlsx`。
- 每个 BOQ 文件从固定 `BOQ模板.xlsx`开始；从 `L`列起每个设备占一列，`E`列使用 `SUM`公式汇总设备工程量，保留模板项目特征、报价列、公式、合并单元格和样式。
- 同一机台多个图框的同一项目编码会汇总工程量；不同机台不会写入同一个文件。
- `U1U`不更新插座：现有 CAD 插座原样保留，用户删掉插座后不会根据机台 Excel 再次生成。
- 原 `UNCAD_Submissions.xlsx`历史写入器保留兼容，但不再作为 U1F/U1U 的正式输出。

## v1.9.5 重点更新

- 新版命令统一为 `U1` 前缀：`U1L`、`U1LX`、`U1X`、`U1R`、`U1Q1/2/4`、`U1F`、`U1U`、`U1C`、`U1A`、`U1S`、`U1DWG`。
- 仅保留七个兼容键盘命令：`UNL`、`UNLX`、`UNR`、`UNQ1/2/4`、`UNADD`；移除其余 `UNC_*`、`OPUN*` 和 `UNADDX` 注册。
- `U1F` 生成和 `U1U` 单框/批量更新按机台自动输出 `[BOQ]机台ID.xlsx`。
- `U1DWG` 框选多个 `frame_20260812` 后按识别的机台ID分组输出 `机台ID\机台ID.dwg`；同一机台的设备框横向排列，间距固定为 10000；同名设备也按独立图框分别保留。
- Ribbon 只显示新版任务入口；传统命令仅用于兼容既有键盘习惯，其中统计按钮继续调用 `UNADD`。

## v1.9.4 重点更新

- 用户命令统一为 `UNC_F`（单图框生成）和 `UNC_UPDATE`（已填图框更新，支持批量）；移除独立的 `UNC_SUBMIT`。
- `UNC_F` 和 `UNC_UPDATE` 在 CAD 事务成功后自动更新 `UNCAD_Submissions.xlsx`，无需再运行第二个命令。
- 批量更新一次写入每个图框对应的设备汇总和材料明细；重复运行按“机台ID + 设备名称”覆盖，不追加重复记录。
- 自动写入完成后，命令行明确显示新增、覆盖、材料明细数量和 Excel 完整路径。

## v1.9.3 重点更新

- `UNC_FILL_UPDATE` 支持一次选择多个 `frame_20260812` 已填图框，按每个图框的真实外框边线自动归属表格、统计文字、设备、轴位和上下游信息；逐框读取已有身份，不再逐个定位机台。
- 批量更新先统一预检，再用一个 AutoCAD 事务写入；任一图框失败则整批回滚。`UNC_FILL` 新生成仍保持单图框流程。
- `UNC_SUBMIT` 使用同一图框分区批量提取记录，并在一次文件锁、一次工作簿加载和一次目标替换中写入 Excel；重复提交会覆盖对应机台/设备，不追加重复行。
- 修复 AutoCAD `Table` 继承 `BlockReference` 导致清单表被误当成普通块、提交 Excel 缺少材料明细的问题。

## v1.9.2 重点更新

- `UNC_FILL` 自动匹配严格使用固定清单的“类 + 别名”：项目名称、项目特征和编号段不再参与类别推断；空“类”仍保持空白且绝不自动判定。
- 更新内嵌 `ts.xlsx` 清单，新增电缆迁移别名 `3*35+1*25 -> 1.20 / 3*35+1*16`，并继续保留 `32mm -> 3.3 / 38mm`。
- 手动补清单改为顶部类别标签浏览；处理未匹配电缆、桥架、线管等项目时自动打开对应类别，替换模式禁止跨类别选择。
- 未匹配项目不能因默认未勾选而静默遗漏，必须从固定清单选择对应项目或明确删除。

## v1.9.1 重点更新

- 修复 `UNC_FILL` 取消勾选插座后立即确认时仍可能写入插座的问题；最终写入严格使用确认时的可见勾选状态。
- `UNC_CONDUIT` 线管文字恢复固定 `2000mm` 占位，不再写入图上实测距离；修复配置读取的 AutoCAD 宿主栈溢出，损坏或退化曲线会被单独跳过并记录日志。

## v1.9.0 重点更新

- 大版本质量改进：固定清单解析改为严格失败，机台 Excel 只对真实 IO 失败重试并保留格式错误，`UNC_FILL` 容量预检与实际写入共用表头定位规则。
- 清理 1.8.3 以来的过期外部清单和特定办公软件占用假设；固定清单继续内嵌，用户只提供机台/设备 Excel。
- 发布前必须通过完整 Release 测试、包清单校验、AutoCAD 2022 宿主加载和安装/回滚验证。

## v1.8.6 重点更新

- 修复 WinForms 框架竞争崩溃“已添加了具有相同键的项”（ImeModeConversion 非线程安全惰性初始化）：插件加载时在主线程预热输入法状态表，消除与 AutoCAD 自身 UI 并发建控件的竞争窗口。

## v1.8.5 重点更新

- `UNC_SUBMIT` 读取不到清单明细时，提示改为可诊断信息：区分“没框到表格”“表格是文字画的”“表格已填充但列序不符”，并给出具体处理建议。

## v1.8.4 重点更新

- 机台 Excel 出现瞬时 IO 失败时，`UNC_FILL` 会自动延长重试；无法读取时提示文件被其他程序占用或正在保存，并将详细异常写入日志。

## v1.8.3 重点更新

- 清单选择对话框重新设计：支持连续添加（“添加并继续”、双击项目直接加入）、空格分词搜索（如 `32 软管`）、最近使用置顶并跨会话记住、类别快捷入口、表头排序、悬停显示完整特征。

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
- BOQ分类规格建立一次索引；机台数据只在用户点击“刷新”时解析并写入 SQLite，命令按需查询快照。
- 清单表和全部属性块共享一个AutoCAD事务，避免部分写入。
- 统一管径输入与严格型号匹配；`UNC_CONDUIT`标注使用固定 `2000mm` 占位。
- 配置中心、机台选择和填充确认统一DPI/Layout、范围校验和错误定位。

## 命令一览

| 新版命令 | 保留的传统命令 | 功能 |
| --- | --- | --- |
| `U1L` | `UNL` | 绘制带长度占位标注的线条 |
| `U1LX` | `UNLX` | 选择已有 U1L 线段，按端点连通顺序逐段填写毫米距离 |
| `U1D` | — | 将对齐标注文字转换为单行文字，保留原尺寸线 |
| `U1R` | `UNR` | 开拱桥并截断相交直线 |
| `U1Q1` / `U1Q2` / `U1Q4` | `UNQ1` / `UNQ2` / `UNQ4` | 绘制 100 / 200 / 400 mm 桥架标注 |
| `U1F` | — | 生成单图框清单和属性，完成后自动更新 XLSX |
| `U1U` | — | 单框或多图框批量更新，完成后自动更新 XLSX |
| `U1DWG` | — | 框选多个图框，按机台ID分组并横向导出 DWG |
| `U1C` | — | 按统一设置中的管径绘制线管标注（同时更新图框内 Ruanguan 软管块长度） |
| `U1A` | — | 查看版本、构建时间、授权和联系方式 |
| `U1S` | — | 只读一个或多个当前图框，将清单项目、数量和米数提交到 BOQ Excel |
| `U1SET` | — | 打开全部功能的统一设置中心 |
| — | `UNADD` | 统计汇总并在图纸中生成 MTEXT |

> Ribbon 只显示新版任务入口；传统命令仅作为键盘兼容入口。统计没有新增 U1 别名，继续使用 `UNADD`。
> 若 UNCAD 页签隐藏，执行 AutoCAD 原生命令 `RIBBON`。若 `U1A` 提示未知命令，请重新运行当前版本安装器。

## 命令命名规范

- 新版用户命令固定使用 `U1` 前缀和功能字母；已有线路编辑使用 `U1LX`，桥架规格在 `Q` 后追加 `1`、`2` 或 `4`。
- 所有设置统一进入 `U1SET`，不再为各模块注册独立设置命令；`U1S` 专用于当前图框 BOQ 提交。
- 兼容命令仅限 `UNL`、`UNLX`、`UNR`、`UNQ1`、`UNQ2`、`UNQ4`、`UNADD`，不得继续扩展。
- 新增或修改命令必须同步 `CommandIds`、CommandMethod、Ribbon、bundle、安装验证和测试。

## 构建

- **VS 2026**：打开 `UNCAD.slnx` 直接生成（F5 会启动 AutoCAD 2022 并附加调试器）。
- **命令行**：`.\build.ps1`（等价 `dotnet build UNCAD.slnx`）。依赖已还原时可用 `.\build.ps1 -NoRestore` 快速刷新 Bundle。
- **测试与发布**：`dotnet test UNCAD.slnx -c Release`；仅使用 `release.ps1` 生成统一 `UNCAD Pro` 包。发布脚本会把测试作为打包前置门禁，并校验 bundle DLL、安装清单和 BOQ 模板。
- AutoCAD 不在默认路径时：`dotnet build UNCAD.slnx -p:AutoCADDir="你的 AutoCAD 目录"`。

## 加载与调试

1. VS 中按 **F5**（已配置启动 `acad.exe`）；
2. 或手动：启动 AutoCAD 2022 → 命令行输入 `NETLOAD` → 选择 `bin\Debug\net48\UNCAD.dll`。

## 最终用户部署（Bundle 分发）

开发调试用 VS/NETLOAD，但**给最终用户要用 AutoCAD 官方 Bundle 机制**——装好即自动加载，无需任何开发环境。

### 安装步骤（用户机器的操作）

1. 解压完整发布 ZIP，不要单独复制 DLL；
2. 完全退出 AutoCAD，双击 `setup.bat`；
3. 选择“Install or repair for current user”（推荐），安装器会检测 AutoCAD 2022 并校验清单、版本和全部文件的 SHA-256；
4. 看到 `INSTALLATION SUCCESSFUL` 后启动 AutoCAD 2022，打开 `UNCAD` Ribbon；
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
- 每次 `build.ps1` 构建后会自动更新分发包内的 `UNCAD.dll`，默认使用 Release 配置；正式发布还会执行完整测试、bundle 校验和模板文件校验。

## U1F / U1U 数据源

`U1F` 和 `U1U` 将频繁更新的机台数据与固定 BOQ 清单分开管理：

- **机台数据 Excel**：用户在 `U1SET` 选择本地或 HTTP/HTTPS 工作簿并点击“刷新”后，程序解析结果写入本机 SQLite 快照；`U1F/U1U/XSTS/XLAYOUT` 只按需查询快照，不检查源文件时间戳、不自动下载或重新解析。源文件的修改会在下一次手动刷新后生效。
- 快照默认保存在 `%LOCALAPPDATA%\UNCAD\cache\machine-data.db`；删除快照不会删除源 Excel，但需要在 `U1SET` 再次点击“刷新”后才能执行依赖机台数据的命令。
- 机台数据只按表头绑定的 `U_` 列和 `回路名称` 列逐行读取；不展开、不继承合并单元格内容，空单元格保持为空。
- 统一数据表中“回路名称”带删除线的行视为已作废，不进入 `U1F/U1U/XSTS` 的机台和回路数据。
- 固定 BOQ 清单随插件程序集内嵌，启动填充时建立一次索引；用户不提供也不能配置外部清单文件。
- 每次填充只读取一个 SQLite 快照；机台与回路切换预览按需查询，不重复扫描整张源表。
- 机台数据由用户在 `U1SET` 中手动选择工作簿；读取器只接受包含统一 `U_` 字段和普通 `回路名称` 的数据表，缺失或重复必需字段会中止，绝不回退到旧字段或自动猜测其他表。
- 当前固定模板的规格列表头为空时，仍兼容第 6 列；建议后续将该表头明确命名为“规格”。
- 刚性线管和软管先按类别和主别名严格匹配；主别名未命中时，仅允许按数据库的全局唯一`别名1`迁移到指定行，例如`32mm`迁移到该行的`38mm`材料。两列都未命中时必须从固定清单替换或删除，不能直接生成。
- 选择设备/回路后会打开“填充确认”：自动匹配到的电缆、桥架、线管、软管、断路器、插座等默认全部勾选。
- 可删除或取消普通清单项目，也可修改基础信息、清单电缆型号/长度、软管直径及每行名称、特征、单位、数量和项目编码；插座是否输出由 `Device_Build20260716` 动态状态最终决定。删除后按当前清单顺序连续写入。
- 固定清单找不到设备电缆型号时会询问是否替换。替代型号只影响本次清单，图框和块属性继续保留设备原电缆型号；其他自动清单项未匹配时也会显示异常代码并要求明确确认。
- 软管长度只从图框内 `Ruanguan` 动态块读取，缺少有效长度时不生成软管行；软管直径只从电缆型号对照表读取，不回退到 Excel 软管列或线管统计直径。
- 清单表、图框、设备块和上下游块在同一个AutoCAD事务中提交；任何未处理写入错误都会整体回滚，避免半完成数据。
- 写入前先清空配置的数据区（默认从 No.1 开始清空 11 行），随后严格按最终勾选数量逐行生成；全部取消时只清空、不生成清单。
- 已填充后修改了桥架或多处电缆长度，可执行 `U1U`：命令从图框/设备块自动读取机台ID和设备名称并定位 Excel 回路；一次选中多个 `frame_20260812` 时按真实外框批量更新。
- 更新身份兼容新版 `MACHINEID-POWER + DEVICENAME` 和旧版 `MACHINEID-DEVICE`；无法唯一定位同机台多回路时会中止并提示使用普通填充。

## 自动 Excel 记录

- 每次 `U1F` 或 `U1U` 成功写图后仍自动输出该机台的 BOQ；也可执行 `U1S`，只读取一个或多个当前图框并独立重复提交，且不修改 CAD。
- `U1S` 不读取或校验机台数据 Excel，机台ID、设备名、项目编码、单位和当前数量（包括 `M` 米数）全部来自图框与当前清单。
- 输出根目录在 `U1SET → Excel 数据 → 自动记录文件夹` 中设置；为空时默认使用当前用户“文档”目录。
- 输出路径固定为 `输出根目录\机台ID\[BOQ]机台ID.xlsx`，首次生成时复制固定 `BOQ模板.xlsx`。
- 模板的 `Sheet1` 中，插件在 `L`列之后为每个设备建立工程量列，`E`列按行 `SUM`所有设备列；项目名称、特征、单位、价格栏、模板小计公式和样式均保留。
- 多个图框属于同一机台时，同一项目编码的工程量在该机台 BOQ 中累加；批量操作按机台分别输出文件。
- 自动输出结束后，命令行显示处理记录数、覆盖文件数、材料明细数和完整文件路径。

## 统计匹配规则

- `TEXT`按单个文字实体统计；`MTEXT`按换行拆分后逐行统计。多行文字识别可在`U1SET → 统计汇总`开启，默认关闭。
- 电缆、桥架和线管类别可分别开关，默认全部开启。
- 电缆严格格式：`2000mm`；桥架严格格式：`桥架200*100 12格`；线管严格格式：`Φ20线管 2000mm`，并兼容旧图的`线管20 2000mm`。
- 每行必须完整匹配，不接受 `(共用)`、`共用`、前后备注或中间附加文字。桥架只支持 `*`、`x`、`X` 作为规格分隔符。

## 设置持久化

所有配置**通过统一设置中心 `U1SET` 修改**（页签：线段/桥架/拱桥/统计），底层存注册表（键名与 LISP 版一致，位置：`HKCU\Software\Autodesk\AutoCAD\...\Variables`）：

| 键 | 含义 | 默认值 |
| --- | --- | --- |
| `UNL_TEXT` / `UNL_HEIGHT` / `UNL_POS` / `UNL_OFFSET` | 占位文字 / 高度 / 位置(0居中 1靠边下 2靠边上) / 偏移距离(0=贴线) | 2000mm / 180 / 1 / 0 |
| `UNQ_TEXT` / `UNQ_HEIGHT` / `UNQ_LINE_OFF` / `UNQ_TEXT_OFF` / `UNQ_SIDE` | 规格 / 高度 / 红线偏移(0=默认15贴线) / 文字偏移(0=贴线) / 侧(1上 0下) | 桥架200*100 10格 / 180 / 0 / 0 / 1 |
| `UNR_DIAMETER` | UNR 拱桥直径 | 300 |
| `UNC_STYLE_NAME` / `UNC_STYLE_FONT` / `UNC_STYLE_BIGFONT` / `UNC_STYLE_WIDTH` | 文字样式：样式名 / 字体文件 / 大字体(空=TTF) / 宽高比 | UNC-标注 / msyh.ttf / (空) / 0.8 |
| `UNADD_HEIGHT` / `UNADD_MM_PER_GRID` | 统计输出文字高度 / 桥架每格毫米数 | 180 / 250 |
| `UNADD_TEXT_ENABLED` / `UNADD_MTEXT_ENABLED` / `UNADD_DIMENSION_ENABLED` | 单行文字 / 多行文字 / 对齐·转角标注文字参与统计（标注只读人工输入的文字覆盖，自动测量不计） | 1 / 0 / 1 |
| `UNADD_CABLE_ENABLED` / `UNADD_BRIDGE_ENABLED` / `UNADD_CONDUIT_ENABLED` | 电缆 / 桥架 / 线管参与统计 | 1 / 1 / 1 |
| `UNC_FILL_EXCEL` | 用户选择的机台数据源（内容由 U1SET“刷新”导入 SQLite） | 空 |
| `UNC_FILL_TABLE_ROW` / `UNC_FILL_CLEAR_ROWS` / `UNC_FILL_TEXT_HEIGHT` | 清单起始数据行 / 每次清空行数 / 表格文字高度 | 1 / 11 / 500 |
| `UNC_SUBMIT_FOLDER` | `U1F` / `U1U` 自动记录文件夹（保留旧键名兼容已有设置） | 空（默认“文档”目录） |

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
