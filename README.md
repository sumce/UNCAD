# UNCAD Pro

面向 **AutoCAD 2022**（Windows x64 / .NET Framework 4.8）的电气线路算量、图框清单填充、BOQ 汇总导出与批量排版插件。

> 维护与开发约定请以 [AGENTS.md](AGENTS.md)、[项目状态地图](docs/PROJECT_CONTEXT.md)、[架构决策记录](docs/DECISIONS.md) 和 [开发规范](docs/DEVELOPMENT.md) 为准。

---

## 运行环境

- **CAD 版本**：AutoCAD 2022（Windows 64 位）
- **运行时**：.NET Framework 4.8
- **可视化组件**：在线登录与 `U1U` 3D 路由预览使用 Microsoft Edge WebView2；若未安装 WebView2 运行时，3D 预览自动回退至内置 WPF 3D 渲染（登录可切换手动账号验证）

---

## 安装与升级

发布包（`artifacts/Pro/UNCAD-Pro-v<版本>.zip`）解压后包含插件包 `bundle/UNCAD.bundle` 与安装脚本，支持当前用户和全机安装：

1. 保存图纸并**关闭 AutoCAD 2022**（避免 `UNCAD.dll` 被占用）。
2. 在解压目录双击 **`setup.bat`** 按菜单提示安装，或直接在 PowerShell 执行：
   - 当前用户安装：`.\installer.ps1 -Mode InstallUser`
   - 所有用户安装（管理员）：`.\installer.ps1 -Mode InstallAll`
   - 校验已安装文件完整性：`.\installer.ps1 -Mode VerifyUser`
   - 卸载：`.\installer.ps1 -Mode UninstallUser`
3. 重新打开 AutoCAD 2022，界面顶部会自动加载 `UNCAD` 功能区选项卡。

---

## 常用命令一览

| 类别 | 命令 | 用途说明 |
| --- | --- | --- |
| **设置与数据** | `U1SET` | 打开配置中心（机台数据源、BOQ 模板、预留规则、图层与统计规则、日志等） |
| | `U1DATA` | 手动刷新已配置的机台 Excel/网络台账到本地 SQLite 快照 |
| | `UNC_LOGIN` / `U1A` / `U1HELP` | 账号授权登录 / 关于与版本更新说明 / 命令帮助 |
| **布线与标注** | `U1L` / `U1LX` / `U1UL` | 绘制并标注电缆路线（含传统兼容命令 `UNL`、`UNLX`）、毫米标归位对齐 |
| | `U1R` / `U1B` | 软管参数与尺寸标注（兼容 `UNR`） |
| | `U1Q1` / `U1Q2` / `U1Q4` | 桥架尺寸与长度标注（兼容 `UNQ1`、`UNQ2`、`UNQ4`） |
| | `U1C` / `U1C20` / `U1C25` / `U1C38` / `U1C51` | 线管规格与长度标注（含固定 BOQ 管径快捷命令） |
| | `U1DT` / `Xauto` | 尺寸标注转文字 / 等轴测线路自动布线（兼容 `UNADD`） |
| **图框与统计** | `U1F` | **重新定义填充**：清空图框旧属性并按当前选择重新写入清单、块属性与 `FRAMEINFO` JSON |
| | `U1U` | **增量更新与批量核对**：单框或批量更新图框、3D 路由预览核对、支持跨 DWG 复制搬场重绑与同名回路选择 |
| | `U1S` / `XSTS` | 按整机校验提交 BOQ 与 `DATA` 汇总表 / 图框覆写与图纸覆盖率统计窗口 |
| **排版与出图** | `XLAYOUT` / `Xmerge` | 按机台/完整性/上游编号自动排版图框（支持导入清单筛选与结果导出）/ 跨文件批量合图 |
| | `U1DWG`（`U1D`）/ `U1PDF` / `U13D` | 按机台分夹导出 DWG 与 PDF / 多图框按轴位对齐及 3D 总览 |

---

## 开发与构建

- **按模块定位源码与测试**：`.\scripts\context.ps1 -Area <区域>`（如 `Fill`、`QuickLine`、`XLayout`）
- **本地全量测试**：`dotnet test UNCAD.slnx --no-restore`（或 `.\scripts\ci-local.ps1 -NoRestore`）
- **无本机 AutoCAD 的纯编译检查**：`dotnet build UNCAD.slnx -p:UseAutoCadNuGet=true`
- **完整发布打包（含真机 AutoCAD 门禁）**：`.\release.ps1 -NoRestore`（详见 [发布运维手册](docs/RELEASE-RUNBOOK.md)）
- **版本更新说明**：[RELEASE-3.8.0.md](docs/RELEASE-3.8.0.md)
