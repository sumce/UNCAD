# UNCAD 插件架构设计（v3）

> This is a historical detailed architecture note. Start with
> `AGENTS.md` and `docs/PROJECT_CONTEXT.md`; current product decisions live in
> `docs/DECISIONS.md`.

> 定位：面向后续大量功能扩展的 AutoCAD .NET 插件框架。
> 业务域：电气设计出图（桥架 / 电缆 / 开洞 / 标注 / 统计）。
> 目标环境：AutoCAD 2022（net48 / x64），Visual Studio 2026。

## 1. 设计目标

| 目标 | 含义 |
| --- | --- |
| 可扩展 | 新增功能使用 Feature 元数据、CommandIds 命令目录和 RibbonCatalog 任务布局 |
| 可测试 | 业务逻辑（解析/统计/校验）与 AutoCAD API 解耦，纯 C# 可单测 |
| 一致体验 | 所有命令共用同一套事务/撤销/错误处理模板，交互风格统一 |
| 分层清晰 | Core（纯逻辑）→ Cad（AutoCAD 适配）→ Feature（业务功能）→ UI（界面） |

## 2. 分层架构

```
┌─────────────────────────────────────────────────────┐
│ UI 层        Ribbon 功能区 / 配置对话框 / 报表窗体     │
├─────────────────────────────────────────────────────┤
│ Feature 层   每个功能一个目录：命令入口 + 配置声明     │
│              (Unadd / Unl / Unq / Unr / 未来新功能)   │
├─────────────────────────────────────────────────────┤
│ Cad 层       AutoCAD 适配：事务/撤销/选择集/实体工厂   │
│              CommandBase（命令模板）、CadContext      │
├─────────────────────────────────────────────────────┤
│ Infra 层     配置存储(注册表)、日志、Feature 注册表    │
├─────────────────────────────────────────────────────┤
│ Core 层      纯 C#：文本解析、统计引擎、校验规则、模型  │
│              （零 AutoCAD 依赖，可单元测试）           │
└─────────────────────────────────────────────────────┘
依赖规则：上层依赖下层；Core 不依赖任何上层；Cad 不依赖 Feature。
```

## 3. 核心机制

### 3.1 命令目录、Feature 元数据与 Ribbon 布局

三个机制各自只有一个明确职责：

- `CommandIds`：命令字符串的唯一来源，供 `CommandMethod`、`Feature` 和 Ribbon 动作共同引用；
- `[Feature]` / `FeatureRegistry`：维护功能和规范命令清单，供启动诊断与后续扩展查询；
- `RibbonCatalog`：纯 C# 的任务化界面描述，显式组织主按钮、规格菜单和设置菜单。

```csharp
[Feature("unl", "带标注线段",
    Commands = CommandIds.LineFeatureCommands,
    Description = "连续绘制带标注线段，U 撤销上一段")]
public class UnlFeature : CommandBase { ... }
```

- **命令**：AutoCAD 仍由类内 `[CommandMethod]` 注册；规范名和兼容名都引用 `CommandIds`；
- **Ribbon**：`RibbonBuilder` 只把 `RibbonCatalog` 渲染为 Autodesk 控件，不包含业务布局；
- **配置**：键名集中在 `ConfigKeys`，配置中心统一读写；Feature 只消费配置；
- **测试**：纯目录测试检查标签、命令、菜单完整性，STA 冒烟测试实际构造 Autodesk Ribbon 控件。

### 3.2 CommandBase（命令模板）

所有命令继承 `CommandBase`，获得统一的：

- 文档上下文 `CadContext`（doc/ed/db/当前图层/当前空间封装）；
- 错误处理（ESC/UserBreak 静默、其他错误打印 + 记日志）；
- 事务模板（`using (var tr = ctx.Db.TransactionManager.StartTransaction())` 规范）；
- 撤销分组：**单事务 = 一个撤销步**（2022 无公开撤销标记 API，见技术决策）。

命令方法只写一行：`[CommandMethod("UNADD")] public void Unadd() => Run();`，业务在 `Execute(CadContext)` 里。

### 3.3 Core 层（可测的领域逻辑）

- `TextParser`：MTEXT 清理 / 换行拆分 / 长度、格数、规格正则提取（从 LISP 移植，纯字符串）；
- `TextFormatter`：数字格式化、字符串拼接；
- `StatCalculator`：UNADD 统计引擎（输入文本行 → 输出报表行），**完全无 AutoCAD 依赖**；
- 未来：`IInspectionRule` 校验规则、批处理管道模型都放这里。

### 3.4 配置中心

- `ISettingsStore` 抽象 + `RegistrySettingsStore` 注册表实现（键名与 LISP 版兼容）；
- `Settings` 静态门面：`Settings.GetDouble("UNL_HEIGHT", 180)`；
- **统一配置页面已落地**：`UNC_SET`（`Ui/UnifiedSettingsForm`）页签式管理全部功能配置；
  各功能 `*_SET` 命令通过 `SettingsFeature.Show(tabIndex)` 打开对应页签；
- 配置键唯一来源：`Infra/ConfigKeys.cs`（写入集中在配置中心，功能只读）；
- 后续可演进：`[ConfigItem]` 声明式配置 → 自动生成表单。

### 3.6 命令命名规范

- 统一前缀 `UNC_`（已核对 AutoCAD 2022 别名表：`UN`/UNI/UNCREASE 等短名被内置命令占用，**禁止使用短命令名**）；
- 结构：`UNC_<模块>[_<变体>][_SET]`，全大写、下划线分隔；
- 语义自文档化：`UNC_TRAY100` 一看便知是"桥架 100 规格"；
- 旧命令（UNADD/UNL/UNQ/UNR 等）保留为**兼容别名**，不进入 Ribbon，后续版本可删除；
- 新增命令必须先核对 `acad.pgp` 与 AutoCAD 命令表。

### 3.5 日志

`ILogger` + `Log` 静态门面：命令错误写入 `%APPDATA%\UNCAD\logs\uncad.log`，为批量处理/图纸检查的审计做准备。

## 4. 新增一个功能的 6 步（扩展指南）

1. 在 `CommandIds` 增加规范命令常量；
2. 在 `src/UNCAD/Features/MyFeature/` 新建功能目录；
3. 写 `MyFeature.cs`：继承 `CommandBase`，用 `[Feature]` 声明功能和命令清单；
4. 用 `[CommandMethod(CommandIds.X)] => Run()` 注册命令并实现 `Execute(CadContext ctx)`；
5. 纯业务逻辑放 `Core/`，AutoCAD 适配放 `Cad/` 或功能目录内的 `Cad*` 协作者；
6. 按用户任务把入口加入 `RibbonCatalog`，补充目录测试和必要的功能测试。

## 5. 未来模块设计蓝图

### 5.1 批量处理管道（Batch）

```csharp
// 设计：收集 → 过滤 → 逐项处理（进度） → 汇总报告
public interface IProcessItem<T> { void Process(CadContext ctx, T item, IProgress p); }
// 例如：批量标注桥架、批量改图层、批量统计导出
```
所有现有命令天然可适配（选择集 = 收集，EntityFactory = 处理）。

### 5.2 图纸检查规则引擎（Inspection）

```csharp
public interface IInspectionRule
{
    string Id { get; }
    string DisplayName { get; }
    Severity Severity { get; }          // Error / Warning / Info
    IReadOnlyList<Issue> Check(CadContext ctx, InspectionScope scope);
}
// Issue: 位置、实体 Id、消息、修复建议 → 可跳转、可批量修复
```
规则实现放在 Core（纯逻辑 + 规则数据），Check 的 AutoCAD 遍历在 Cad 层。

### 5.3 图层/样式管理

以 Feature 形态实现（`LayerManager` Feature）：查询/创建/批量改图层，复用选择集 + 批处理管道。

### 5.4 统计报表

`StatCalculator` 已有纯逻辑基础，输出端从"图纸 MTEXT"扩展为"Excel/CSV/表格"——新增一个 Output 适配器即可，统计引擎不动。

## 6. 目录结构（目标态）

```
src/UNCAD/
├─ Bootstrap.cs               # IExtensionApplication：启动诊断 + Ribbon 注册
├─ Core/                      # 纯 C#，可单测
│  ├─ Contracts/              # ISettingsStore / ILogger / FeatureAttribute
│  ├─ Text/                   # TextParser / TextFormatter
│  └─ Stat/                   # StatCalculator（统计引擎）
├─ Cad/                       # AutoCAD 适配层
│  ├─ CadContext.cs           # 文档/编辑器/数据库/当前图层封装
│  ├─ CommandBase.cs          # 命令基类模板（错误/事务/日志）
│  ├─ EntityFactory.cs        # Line/MText/Arc 统一创建（图层/颜色默认）
│  ├─ SelectionService.cs     # 选择集封装（PickFirst/过滤）
│  └─ GeoMath.cs              # 极坐标/角度/中点
├─ Infra/
│  ├─ CommandIds.cs           # 规范命令与兼容命令的唯一来源
│  ├─ FeatureRegistry.cs      # [Feature] 元数据和诊断命令清单
│  ├─ Settings.cs             # 配置门面（注册表实现，键名兼容 LISP）
│  └─ Log.cs                  # 日志门面（文件 + 命令行）
├─ Ui/
│  ├─ RibbonDefinition.cs     # 纯 C# 任务布局（可单测）
│  ├─ RibbonBuilder.cs        # RibbonCatalog → Autodesk 控件
│  └─ WindowWrapper.cs
└─ Features/                  # 每个功能一个目录
   ├─ Unadd/  Unl/  Unq/  Unr/
   └─ (未来: LayerManager / Inspection / Report / ...)
```

## 7. 技术决策记录（ADR）

| 决策 | 理由 |
| --- | --- |
| 单事务 = 一个撤销步 | AutoCAD 2022 无公开撤销标记 API（已反射确认），事务分组是标准机制 |
| 静态门面（Settings/Log）而非 DI 容器 | AutoCAD 插件进程单例、无多态需求，静态门面简单可靠；接口保留供未来测试注入 |
| Core 层零 AutoCAD 依赖 | 统计/解析/校验可脱离 AutoCAD 单测，回归成本低 |
| Ribbon 使用任务目录而非逐命令自动生成 | 规格和设置属于菜单选项，避免向用户暴露命令名 |
| Ribbon 按钮用 CommandParameter 触发命令 | 官方推荐方式，按钮零代码绑定命令 |
| 保留命令名与 LISP 版一致 | 老用户习惯零迁移成本 |
