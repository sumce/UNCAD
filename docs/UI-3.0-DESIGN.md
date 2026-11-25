# UNCAD 3.0 UI 设计语言（D-038）

所有 3.0 窗体遵循本文。改本文 = 改设计契约，需与决策文档同步。

## 技术底线（不可协商）

- WPF + WPF-UI（lepoco）4.x，Fluent 风格；net48 唯一目标。
- 主题字典**按窗口合并**（`UiShell.ApplyFluentTheme`），绝不写
  `Application.Current` —— AutoCAD 进程的 WPF Application 属于 CAD 自己，
  污染它等于改 CAD 的界面。
- 模态弹窗一律走 AutoCAD 宿主 API（`Application.ShowModalWindow`），
  保证窗口置顶于 CAD、随文档关闭。
- 窗体仍然只编辑传入模型，返回用户决定；不读 Excel、不扫图纸。

## 设计令牌（Design Tokens）

| 令牌 | 值 | 用途 |
| --- | --- | --- |
| 主字体 | Microsoft YaHei UI, Segoe UI | 全部界面文字 |
| 等宽字体 | Cascadia Mono, Consolas | CABLE_INFO 公式、编码、句柄 |
| 间距网格 | 8 / 16 / 24 / 32 | 卡片内边距与区块间距只用这四档 |
| 卡片圆角 | 8 | `ui:Card` 默认，不自定义 |
| 强调色 | 系统强调色（WPF-UI 主题） | 主按钮、选中态、进度 |
| 状态色 | InfoBar 四级语义色 | 成功/信息/警告/错误，不自造颜色 |
| 深浅色 | 跟随 U1SET 外观页（默认浅色） | 全部窗体同步切换 |

## 布局模式（每个窗体归入一种）

- **P1 导航枢纽** —— 左 `NavigationView` + 右内容页，页内卡片纵向滚动。
  适用：设置中心（10 页归位）。
- **P2 工作台** —— 顶部上下文条（机台/图纸/胶囊标签）+ 中部 `DataGrid`
  + 底部操作条（说明文字在左，主按钮右下）。
  适用：清单评审、批量确认、Xmerge、XLayout 选择、XSTS、机台选择、
  目录选择（批量/电缆）、更新对比。
- **P3 事务确认** —— 单表格 + 醒目后果说明（InfoBar）+ 主/次按钮右下；
  破坏性操作必须用警告色 InfoBar 明说影响范围。
  适用：设备内预留、配置分享预览、XLayout 汇总。
- **P4 聚焦输入** —— 窄窗（≤460px）、大主按钮、最少输入项。
  适用：登录、在线授权。
- **P5 内容阅读** —— 文档流 + 目录侧栏；帮助/更新日志可走 WebView2（只读，
  低风险位），3D 预览维持 D-025。
  适用：帮助、更新日志、U1L3D 预览。
- **P6 瞬时提示** —— 启动画面等一次性窗口，无交互。

## 交互原则

1. **不打断命令**：命令行/选择进行中绝不抢焦点弹窗（2.9.0.7 的教训）。
2. **ESC = 取消**，永不销毁用户已完成的输入数据（与 D-036 同精神）。
3. 表格必须可键盘操作：Enter 确认行、方向键移动、F2 编辑。
4. 主按钮永远右下；一处窗体只有一个 Primary。
5. 窗口尺寸/位置记忆走既有 `ConfigKeys` 机制，不新增存储路径。
6. 权威校验永远在"确认"按钮里做，控件级校验只给即时反馈（无头测试
   CellValidating 不触发的教训）。

## 21 个窗体的迁移归位（2.11.0 全部完成）

| 窗体 | 模式 | 状态 |
| --- | --- | --- |
| BatchFillConfirmationWindow | P2 | ✅ 2.11.0 |
| DeviceReserveWindow | P3 | ✅ 2.11.0 |
| XLayoutSummaryWindow / BatchUpdateResultWindow / DwgExportResultWindow / PdfExportResultWindow / XLayoutSelectionWindow | P2 | ✅ 2.11.0 |
| XstsWindow / XmergeWindow | P2 | ✅ 2.11.0 |
| OnlineLicenseWindow / ClientLoginWindow | P4 | ✅ 2.11.0（WebView2 用 WPF 控件） |
| CommandHelpWindow / UpdateNotesWindow（+共用 VersionNotesView） | P5 | ✅ 2.11.0 |
| U1L3DPreviewWindow（内容维持 WebView2，D-025） | P5 | ✅ 2.11.0 |
| StartupSplashWindow（保守迁移，时序零改动） | P6 | ✅ 2.11.0 |
| AboutWindow | P7 | ✅ 2.11.0 |
| SettingsWindow（11 页导航枢纽 + ConfigSharePreviewWindow） | P1/P3 | ✅ 2.11.0 |
| FillReviewWindow + ManualListItemWindow | P2/嵌入 | ✅ 2.11.0 |
| Batch/CableCatalogSelectionWindow | P2 | ✅ 2.11.0 |
| MachinePickerWindow / FillUpdateCompareWindow | P2 | ✅ 2.11.0 |

迁移纪律执行记录：一窗体一提交（新窗体上线 + 旧文件删除 + 测试改写同批），
21 个窗体全部按此完成；全量门禁 1267/1267。

### 遗留的 WinForms 基础设施（3.8.0 已清理）

DpiAwareForm 已删（零派生）。3.8.0 结构清理已删除 UiKit、DialogLayout、
BrandMarkControl 以及 UiKitTests/DpiScalingContractTests；UiTheme 仍被
RibbonIconFactory、StartupSplashWindow 的色板/字体引用，暂留。

迁移纪律：一窗体一提交（新窗体上线 + 旧文件删除 + 测试改写同提交）；
每完成一批跑全量门禁；中途可停，WinForms 与 WPF 过渡期共存但不新增
WinForms 窗体。
