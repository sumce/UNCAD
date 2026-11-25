# UNCAD 开发计划

> 目标：把"电气设计出图工具集"做成**配置驱动、可扩展、可测试**的 AutoCAD 插件。
> 原则：命令 = 交互 + 输出 + 配置 三件套；一切几何参数进配置中心；能用库的不造轮子。

## 0. 现状盘点

| 模块 | 状态 |
| --- | --- |
| 分层框架（Core/Cad/Infra/Ui/Features） | ✅ 完成 |
| CommandIds + Feature 元数据 + RibbonCatalog 任务布局 | ✅ 完成并测试 |
| 统一配置中心 UNC_SET（页签式） | ✅ 完成 |
| UNC_LINE 连续画线（L 命令式交互） | ✅ 完成并验证 |
| UNC_STAT / UNC_STAT_EX（统计+Excel） | ✅ 可用 |
| UNC_TRAY 选中生成桥架标注 | ✅ 完成并验证 |
| UNC_ARCH 拱桥开洞 | ✅ 完成并验证 |
| 分发（Bundle + 安装脚本） | ✅ 完成 |

## 1. 阶段规划

### 阶段一：命令定稿（已完成）
- [x] UNC_LINE：交互/贴线/逐段生成
- [x] UNC_TRAY：偏移、侧向、文字与预设规格配置化
- [x] UNC_ARCH：连续交互并接入配置
- [x] UNC_STAT：高度、每格毫米和文字样式配置化

### 阶段二：质量与测试（持续）
- [x] Core 层单元测试（xUnit）：Excel、填充、统计、文本和提交
- [x] RibbonCatalog 结构测试 + Autodesk Ribbon 控件构造冒烟测试
- [ ] 命令验收清单继续随功能扩展维护（见 §4）

### 阶段三：扩展蓝图（按需推进）
- [ ] 批量处理管道（选择集 → 逐项处理 → 汇总报告）
- [ ] 图层/样式管理 Feature
- [ ] 图纸检查规则引擎（Core 纯逻辑 + Cad 扫描）
- [ ] 多版本支持（net8.0 外壳，AutoCAD 2025+）

## 2. 命令规格（三件套定义）

### UNC_LINE — 连续画带标注线段 ✅
- 交互：同 AutoCAD L（点选连续、回车结束、U 撤销、ESC 清除）；每段立即生成
- 输出：线段 + 中心靠边占位文字
- 配置：UNL_TEXT / UNL_HEIGHT / UNL_POS(0居中 1靠边下 2靠边上) / UNL_OFFSET(0=贴线)

### UNC_TRAY — 选中线段生成桥架标注 🔧
- 交互：先选（可预选）或后选 → 对每条线段生成标注
- 输出：红色桥架线（与基线平行）+ 红色规格文字（贴红线）
- 配置：UNQ_TEXT / UNQ_HEIGHT / UNQ_LINE_OFF(0=自动0.25×字高) / UNQ_TEXT_OFF(0=贴线) / UNQ_SIDE(1=上方 0=下方)

### UNC_ARCH — 拱桥开洞 ⬜
- 交互：连续点击直线开洞（ESC/空格退出），与 L 系列一致的错误处理
- 配置：UNR_DIAMETER（并入 UNC_SET）

### UNC_STAT / UNC_STAT_EX — 统计汇总 ✅
- 交互：框选文字 → 点击放置 / 另存为 Excel
- 输出：图纸 MTEXT 或 xlsx 报表
- 配置：UNADD_HEIGHT / UNADD_MM_PER_GRID

## 3. 配置项总表（全部键 · 默认值 · 语义）

| 键 | 默认 | 语义 |
| --- | --- | --- |
| UNL_TEXT | 2000mm | 线段占位文字 |
| UNL_HEIGHT | 180 | 占位文字高度 |
| UNL_POS | 1 | 0=居中 1=靠边下 2=靠边上 |
| UNL_OFFSET | 0 | 文字距线偏移；0=贴线 |
| UNQ_TEXT | 桥架200*100 10格 | 桥架规格文字 |
| UNQ_HEIGHT | 180 | 规格文字高度 |
| UNQ_LINE_OFF | 0 | 红线距基线；0=默认15（贴线） |
| UNQ_TEXT_OFF | 0 | 文字距红线；0=贴线 |
| UNQ_SIDE | 1 | 标注侧：1=上方 0=下方 |
| UNR_DIAMETER | 300 | 拱桥直径 |
| UNADD_HEIGHT | 180 | 统计输出文字高度 |
| UNC_STYLE_NAME | UNC-标注 | 生成文字的文字样式名 |
| UNC_STYLE_FONT | msyh.ttf | 字体文件（微软雅黑，可配 shx） |
| UNC_STYLE_BIGFONT | (空) | SHX 大字体；空=纯 TTF |
| UNC_STYLE_WIDTH | 0.8 | 文字宽高比 |
| UNADD_MM_PER_GRID | 250 | 每格折算毫米 |

> 原则：距离类参数一律支持"0=自动（按字高比例）"，图纸比例变了不用改数值。

## 4. 命令验收清单（每个命令交付前过一遍）

- [ ] UNC_LINE：画 3 段 → 每段即时出现；U 撤销最后一段；Enter 保留；ESC 全清；文字贴线、方向随线段正向
- [ ] UNC_TRAY：选 2 条线 → 各生成红线+文字；改配置后重跑生效；下方侧正确；距离符合预期
- [ ] UNC_ARCH：连续开 2 个洞 → 弧向正确、直线被断；ESC/空格退出干净；系统变量恢复
- [ ] UNC_STAT：框选含"1200mm/3000mm/桥架200*100 6格"文字 → 汇总正确；UNC_STAT_EX 导出 Excel 可打开
- [ ] UNC_SET：改任意配置 → 保存 → 对应命令生效

## 5. 开发规范（引用 ARCHITECTURE.md）

- 命令命名：UNC_ 前缀规范（§3.6）
- 新增功能：Features/ 目录 + [Feature] 特性（§4）
- 依赖规则：Core 零 AutoCAD；Cad 不依赖 Feature；写配置只在配置中心
