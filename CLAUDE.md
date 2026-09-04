# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

UNCAD:AutoCAD 2022 的 C# .NET 插件集(net48 / x64),由 UNSIAO.Ltd 开发。U1 系列命令(U1F/U1U/U1S/U1DWG/XLAYOUT/U1LX 等)负责清单生成、批量更新、BOQ 提交、图框排版与快速标注。用户与客户为中文使用者,代码注释和 UI 文案用中文。

## 构建与测试

```powershell
# 构建(Release,含 bundle 更新与 WebView2-free 产物校验)
powershell -File build.ps1 -SkipBundle   # 快速构建
powershell -File build.ps1               # 完整构建(更新 bundle/UNCAD.bundle)

# 全量测试(需要本机装有 AutoCAD 2022)
dotnet test tests/UNCAD.Tests/UNCAD.Tests.csproj -c Release -p:AutoCADDir="D:\Program Files\Autodesk\AutoCAD 2022"

# 单个测试类
dotnet test tests/UNCAD.Tests/UNCAD.Tests.csproj -c Release -p:AutoCADDir="D:\Program Files\Autodesk\AutoCAD 2022" --filter QuickLineFeatureContractTests

# 统一专业版安装包（客户、模式和期限由 pro.key 决定）
powershell -File release.ps1             # 输出 artifacts\Pro\UNCAD-Pro-v*.zip
```

- 每次**提交前必须跑全量测试**(当前 454 个)。
- 修改 `bundle/UNCAD.bundle/PackageContents.xml` 时,`installer.ps1` 的 `$expectedCommands` 和测试会强制校验与 `CommandIds.Registered` 一致。
- 构建脚本(build.ps1 / release*.ps1 / installer.ps1)被 `BundleLoadingTests` 按源码文本断言,改动脚本会直接影响测试。

## 架构(分层红线,见 docs/DEVELOPMENT.md)

```
src/UNCAD/
  Core/       纯业务:解析、匹配、统计、格式化;禁止依赖 AutoCAD/WinForms/注册表
  Cad/        AutoCAD 适配:事务、选择集、实体;禁止包含 Excel 匹配规则
  Infra/      配置(ConfigKeys 唯一定义点)、日志、品牌、命令 ID、授权
  Features/   命令编排:读一次配置快照 → Core 规划 → 单事务提交 CAD 变更
  UI/         WinForms 窗体:只编辑传入模型并返回用户决定,不重读 Excel/扫图纸
  Web/        (已删除)WebView2 3D 编辑器遗留,U1X 已整体移除
```

关键机制:
- **命令注册**:`[Feature]` 特性类 + `[CommandMethod]`,`FeatureRegistry` 启动扫描输出诊断清单;命令 ID 全部在 `Infra/CommandIds.cs`,Canonical(公开 U1 族)与 Registered(含 UN* 兼容别名)两个列表被 `CommandRegistrationTests`/`installer.ps1`/bundle manifest 三方交叉校验。
- **授权**:只发布统一 `UNCAD Pro`;`Infra/OnlineLicenseMonitor.cs` 从服务器 Key 读取客户、授权模式和期限;`TrustedClock` 做防回拨(NTP 网络时间 + 三处持久化水位线 + 构建时间下限)。
- **固定清单**:`Resources/embedded_catalog.tsv` 内嵌程序集;改源表后必须跑 `scripts/GenerateEmbeddedCatalog.ps1` 并升级版本号。
- **提交对账**:`AutomaticSubmissionService` 对无法匹配固定清单的手动材料行**硬报错中止**;`BoqWorkbookWriter.Write` 写后回读核对,不一致抛异常回滚批次。
- **框内归属**:`Cad/FrameRegionCollector.cs` 用实体"锚点"判定图框归属(块用几何外包框中心,取不到回退插入点);U1DWG 导出与 XLAYOUT 共用同一套锚点归属(有契约测试锁定)。
- **UI 主题**:`UI/UiTheme.cs` 是唯一色板/字体/间距来源,窗体禁止裸写 `Color.FromArgb`/字体字符串。
- **日志**:`%APPDATA%\UNCAD\logs\uncad.log`,带 `[Feature]` 前缀定位阶段耗时与异常。

## 测试红线(最容易踩的坑)

- 大量 UI 测试通过**控件树递归 + `Control.Name` + 控件类型**断言(如 `FillReviewFormTests`、`UnifiedSettingsFormTests`):重构窗体时控件 Name 与类型不可随意改。
- 部分契约测试按**源码文本顺序**断言(如 `QuickLineFeatureContractTests` 要求"选实体 → 建遍历计划 → 写标注"顺序),改 Feature 代码结构会碰它们。
- `QuickLine3dEditorProtocolTests` 已随 3D 功能删除;`QuickLineSchematicLayoutTests` 等同理,只在对应代码存在时有效。
- `FillReviewForm` 的 `_synchronizing` 双向同步逻辑脆弱,改动需逐行对照 `FillReviewFormTests`。

## 发布与安装注意事项

- 安装/卸载脚本会清理 AutoCAD 每配置文件的 `Applications\UNCAD` 注册表命令缓存(历次升级累积陈旧条目会造成"未知命令");`BundleLoadingTests` 锁定了这一行为。
- `install-user.bat` 的命令行参数被忽略,固定安装仓库 `bundle/UNCAD.bundle`。
- `release-temp.ps1` 和 `release-jswy.ps1` 已停用,只能用 `release.ps1` 生成统一 Pro 包。
- **版本号硬性规则**:每新增一个功能,必须把 `ProductMetadata.VersionSuffix` 递增(SP1→SP2→…),`ConfigKeysTests` 的版本断言同步更新;纯 bug 修复可不递增但构建时间自动更新。
