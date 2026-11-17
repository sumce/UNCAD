using System;
using System.Collections.Generic;
using System.Linq;

namespace UNCAD.Infra
{
    /// <summary>One released version and its user-facing update notes.</summary>
    public sealed class VersionChangeLogEntry
    {
        public VersionChangeLogEntry(string version, string dateUtc,
            params string[] changes)
        {
            Version = version;
            DateUtc = dateUtc;
            Changes = (changes ?? Array.Empty<string>())
                .Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        }

        public string Version { get; }
        public string DateUtc { get; }
        public string[] Changes { get; }
    }

    /// <summary>
    /// Hard-coded update notes shown after upgrading. Newest version first; the
    /// newest entry must match <see cref="ProductMetadata.VersionLabel"/> — the
    /// version contract test enforces this so a release cannot ship stale notes.
    /// </summary>
    public static class VersionChangeLog
    {
        public static readonly IReadOnlyList<VersionChangeLogEntry> Entries =
                new List<VersionChangeLogEntry>
            {
                                                new VersionChangeLogEntry("3.3.7", "2026-11-18",
                    "新增（D-054 Amendment，用户两轮原话「这些 你完全可以自己做一个模型 包括每层的地面 等 你就光弄个线条」→「3楼地面是华夫孔洞（圆孔） 二楼地面是环氧树脂 并且一楼应该也有地面 第一/二/三层母线应该用半透明玻璃展示 柱子每 4.8M 一个柱位、4 个柱位之间有 9 个华夫孔」）：U1U 3D 预览参照物从水平线框升级为**简化建筑模型**——配置语法升级「标签=毫米=类型」（第三段可选，中英关键词归一化：地面/环氧(树脂)→地坪、华夫→圆孔板、工字钢/钢梁/型钢/钢架→钢梁、葡萄架/格栅/栅格/网格→格栅、高架、设备/盘/箱/柜、母线；缺省或认不出=3.3.6 琥珀线框，旧清单零改动兼容；四段式条目跳过），默认 12 条按本单语义带类型（2F地面=环氧地坪、3F地面=华夫板，数值与 3.3.6 完全一致）。页面按类型画实体几何（Lambert/Phong+双灯+轻雾）：环氧地坪=绿色泽面（Phong 高光表达树脂光泽）；华夫板=真圆孔穿透板（ShapeGeometry 挖孔，**每 4 柱位围成的格间 3×3 共 9 孔**——孔位按柱网分间布，无柱网退化为均匀阵列兜底）；楼板/高架板=半透明板材；工字钢梁阵=上翼缘+腹板+下翼缘；葡萄架=格栅条阵；设备=沿底边排开的实体盒（I-L盘/插座盘，高度准确位置示意）；**母线三层=半透明琥珀玻璃层**横贯场景标记高度（用户口径「告诉用户这个高度是几层母线」，取代 3.3.7 开发中的铜塔方案）；**结构柱=柱网两向交点**（U1L 柱网即 ≈4.8m 柱位）从地面落地到最高参照层（包络内、上限 400 根防炸）；**1F 地面=场景零平面自动铺板**+标签「1F地面 0」（归零口径：最低线段端点 Z=0 即 1F 地面；不占配置条目，解析器仍拒绝 ≤0）。配套：取景随模型高度扩展（参照层把场景顶到 15m+，相机包络不含它则整栋楼挤上半区、下半空屏）；管线半径 0.0012→0.0019 档（20m+ 场景被模型+雾衬得快看不见——模型要醒目，路由更是主角）；板/梁/格栅角标签左右交替锚避同角叠屏；图例按实际出现类型标注（灰=楼板 绿=环氧地坪 华夫=圆孔板 银=钢梁 架=葡萄架 蓝灰=高架板 白=设备 琥珀玻璃=母线层）；U1SET 提示文案同步三段式语法。锁死：PreviewReferenceLevelsTests 默认表类型逐条（2F→epoxy/3F→waffle）+归一化扩展（华夫/环氧）+三轮新断言；序列化契约含 Kind 键。三轮用户口径收尾：①钢梁/葡萄架默认隐藏（新「结构 开/关」独立开关，柱/板/母线玻璃仍默认显示）；②空间改推算——horizontalExtent 从 6% 边距改为「回路footprint 扩到整跨柱网、每侧外扩一跨」（无柱网退化为边距；「分析回路会占用多大的空间 然后渲染出来更大的空间」）；③「本层=2F地面」指令行（TryParseBaseFloor，不进清单）+信封 BaseFloorZ——页面整栋楼按本层重定位（本层地面=场景 0，上游就在本层；3F=7200、1F=-7800，标签显示本层起算高度；无指令=3.3.6 绝对标高兼容）；④条目第四段锚定「标签=毫米=类型=锚定标签」（信封单跳解析：母线 3660 锚 2F地面=7800+3660=11460 绝对，重定位后正好悬在本层地面上方 3660/4220/4900——「第一/2/3层母线放到二层」）；五段式条目跳过；顺手修 3.3.5 起的存量标签 bug：updateLabels 每帧重写 display 把柱网/参照开关隐藏的标签下一帧又放回来，帧内判定改含分组可见性。浏览器视觉迭代后按用户要求停止浏览器自查、由用户验收。全量 1591/1591（基线 1585）。"),
                new VersionChangeLogEntry("3.3.6", "2026-11-18",
                    "新增（D-054 Amendment，用户提供的建筑标高链与设备高度原话清单「1F地面——1F工字钢顶面：2850mm … 第三层母线高度：4900mm」，要求「能把这些参照物也附进去吗？3D的页面内」）：U1U 3D 预览**参照层**——在 3.3.5 的 WebView2+three.js 页面里画出建筑标高与设备高度的水平参照框（琥珀色矩形框横贯场景包络+边角标签「标签 高度mm」，如「2F地面 7800」「第三层母线 4900」），供电缆路由爬升高度对照；右上角新增「参照 开/关」按钮（柱网开关旁），图例补「黄=标高参照」，参照标签锚点外推到框外角避让端点轴位钉。数值不写死页面：清单是 U1SET 新配置 UNC_PREVIEW_REF_LEVELS（绘制标注页新卡「3D 预览参照层（U1U）」，多行文本，格式「标签=毫米」分号/换行分隔，非法条目自动跳过、清空=不画），默认值=用户本单提供的 12 条（楼层链按累计标高折算：2F工字钢顶面=7800+3200=11000、2F葡萄架顶面=13300、3F地面=15000、3F高架地板面=15600；设备高度按原值：I-L盘顶面 2200、插座盘顶面 1000、母线三层 3660/4220/4900、插座盘安装 1000）——不同项目标高不同直接改 U1SET。高度语义：相对场景地面（最低线段端点 Z=0 的既有归零口径）。架构分层不变：Core 新增 PreviewReferenceLevels 纯函数解析器（默认表+Parse：分隔符/空白容忍、缺=/空标签/非有限正数跳过不炸）→UI 层读设置传入信封（QuickLineOrbitPreviewEnvelope 新增 ReferenceLevels 字段）→页面哑渲染（参照框+标签走 ref 组可整组开关；标签键名 ReferenceLevels 进 C#/JS 序列化契约）。锁死：PreviewReferenceLevelsTests 6 条（默认表 12 条全解析+累计标高逐值 11000/13300/15000/15600+设备原值/分隔符空白容忍/非法条目跳过清单/空输入空清单/信封携带与 null 空/序列化键名契约）；SettingsWindow 冻结键集 54→55（PreviewReferenceLevels）。浏览器端到端实测（注入带 8 层参照的信封）：零控制台报错、参照标签全渲染、图例含黄=标高参照；视觉三轮迭代——首版框线 0.45 档亚像素不可见提到 0.9（视觉确认 6 框阶梯清晰）、框角标签与端点钉 48/V 重叠外推避让（终验确认分开且无新缺陷）。全量 1583/1583（基线 1577）。",
                    "修复（3.3.6.1，现场「展示3D的区域是空白的」）：3D 预览舱白屏——QuickLineWebPreviewControl 的 WebView2 以 Collapsed 起步、等首个非空模型才切 Visible，而 WebView2(WPF) 的视觉绑定在控件 Loaded 时进行，Collapsed 状态经历 Loaded 的控件此后永不再绑定画面（页面内部一切健康：DOM complete、canvas 尺寸正确、ready/信封握手全通，唯独屏幕纯白）。定位过程立了两个真 WebView2 探针测试（控件补 WebRootOverride 供测试宿主注入源码树页面目录——测试输出目录没有 Web/；页面内侧 ExecuteScriptAsync 探针要边泵边等，裸 Wait 会堵死 STA 泵）：白屏探针复现（状态全通+像素纯白 255）；对照实验同宿主跑生产验证过的 U1L3DPreviewWindow（同为 WebView2/FluentWindow/Backdrop None）正常渲染深色 13,19,26——环境与页面排除，锁定控件差异=初始 Collapsed。修复=WebView2 永不 Collapsed（空态/致命态用深色 DefaultBackgroundColor 兜底+居中提示文字；顺带把空态/错误态从左上角一行小灰字改成居中醒目——现场把整舱留白+小字读作「空白」）；全链路诊断日志（页面目录/内核就绪/页面就绪/启动失败全落 %APPDATA% 下 UNCAD\\logs，致命错误带「不影响清单写入」指引）。验证：白屏探针像素采样 13,19,26（深色场景）+146 色彩桶（标签/文字/参照层全渲染）；全量 1585/1585。",
                    "修复（3.3.6.2，现场「3D显示还不正常」——3.3.6.1 装后用户连跑 6 次 U1U 全程看不到 3D）：3D 预览永远赶不上用户点确认。诊断日志（页面目录/内核就绪/页面就绪全链路，3.3.6.1 装的）给出铁证：WebView2「页面目录→内核就绪」要 6~10 秒（03:54:11→03:54:21=9.6s、03:54:47→03:54:55=7.8s），用户在审阅窗点确认只要 5~10 秒——信封还没发窗口就关了；且 6 次里仅 1 次到达「页面就绪」、那次的宿主窗还先崩了。根因=三个 WebView2 宿主（U1U 预览/U1L 3D/登录页）各自开窗各自 CreateAsync、共用同一用户数据目录 %LOCALAPPDATA%\\UNCAD\\WebView2——上一轮窗口关闭后浏览器进程仍在退场并锁着数据目录，新一轮排队等锁再拉新进程（日志内证：上一轮进程尚活时第 2 次开窗只要 0.5 秒）。修复①SharedWebView2Environment 进程级共享环境：环境与浏览器进程随静态引用常驻整个 CAD 会话，三宿主全走共享，渲染进程崩溃 Invalidate 后下次重建（当窗仍走既有降级文案）；②Bootstrap 启动 Idle 预热后台拉起共享环境——首次 U1U 也吃到热进程，开窗 0.5 秒级就绪（预热失败只记日志，未预热时首个使用方照常自建，预热是优化不是正确性）；③关窗竞态收尾静默：窗口关闭后 BeginInitialize 收尾摸到已销毁内核的 InvalidOperationException 不再记「启动失败」WARN 误报。同场修复模态窗快关炸弹：WPF-UI 4.0.2 TitleBar.OnWindowContentRendered 的 HwndSource.CriticalFromHwnd 在窗口被极快关闭（ContentRendered 是投递到调度器队列的回调，先于其执行的关闭已销毁句柄）后以零句柄调用抛 ArgumentException 炸穿 ShowDialog——当日审阅窗 0.4 秒内被关、整条 U1U 报 failed（全库日志唯一一例）。新增 ModalWindowHost：27 处模态调用点全部收口，该竞态（指纹=堆栈含 OnWindowContentRendered 的 ArgumentException）发生时窗口必已被用户关闭，按取消返回不失败命令；其余异常照常上抛。锁死：AuditRoundContractTests +4（三宿主禁直呼 CreateAsync+必须 GetAsync+Bootstrap 必须 WarmUpForStartup；catch 里 _closed 静默先于 WARN；全仓库模态调用禁直呼 ShowModalWindow 必须走 ModalWindowHost；竞态指纹只认 TitleBar 回调来源的 ArgumentException）。全量 1589/1589（基线 1585；含两个真 WebView2 探针——两探针现共用一个浏览器进程，共享环境真机自证）。"),
new VersionChangeLogEntry("3.3.5", "2026-11-18",
                    "更换（D-054 Amendment，用户现场反馈原话「这生成的 3D 有严重问题！放弃这个反感转 WebV 吧」——看过 3.3.2/3.3.4 的 HelixToolkit 预览后弃用该内核）：U1U 确认窗左侧 3D 预览内核换回 WebView2 + three.js（3.3.2 的 Helix 选型记录作废，HelixToolkit.Wpf 引用随 QuickLineHelixPreviewControl 退役一并退出 csproj/build.ps1/installer.ps1/bundle 契约）。架构分层不变——场景语义全部在 C# 侧算死：Core 新增渲染信封 QuickLineOrbitPreviewEnvelope（纯函数构建器，可独立单测），把 QuickLineOrbitPreviewModel 连同柱网规划（QuickLineColumnGridPlanner 两向正交）、桥架/线管通道双线偏移（宽/2 且不低于对角线 0.0012；线管 0.0006；竖直段法向回退 ŷ——与 Helix 控件同公式）、米数标/刻度/字母/端点钉锚点（对角线 0.016/0.013/0.030 比例）一次性解析成纯几何数据，JavaScriptSerializer 序列化成 JSON 喂给页面（PascalCase 属性名= C#/JS 两端契约，测试锁死）；Web/QuickLineOrbit 页面（新三件 index.html/app.js/styles.css，复用 QuickLine3D 已 vendor 的 three.js+OrbitControls，虚拟主机根映射到 Web 父目录）是哑渲染器：透视相机（方位-135°/俯仰30° 默认视角、OrbitControls 阻尼旋转/缩放/平移、双击与按钮复位）、管线用真实圆柱几何（不再有 1px 细线问题）、**标签走 HTML 叠加层——中文渲染交给 Chromium**（Helix Billboard 文字质量正是现场痛点之一）、桥架红双线+半透明填充带、线管青双线、两向柱网+数字/字母刻度+端点钉、图例/轴位摘要/柱网开关按钮叠加在视口上。宿主控件 QuickLineWebPreviewControl 与被删 Helix 控件 API 同形（SetModel/Model/柱网开关/复位），WebView2 接线与安全口径逐条对齐 U1L3DPreviewWindow（D-025：本地虚拟主机、非本地导航一律取消、无宿主对象/下载/权限、渲染进程异常降级为内嵌文案不炸确认窗），惰性初始化（首个非 null SetModel 才启动，U1F/无预览路径零开销）；数据级计数器（段/标签/柱网/刻度/字母/钉/桥架/线管）改为取自信封——SetModel 同步算好，与 WebView2 初始化、GPU 完全解耦（无头测试不装运行时不开页面，DefaultAutoInitialize 静态开关）。锁死：QuickLineOrbitPreviewEnvelopeTests 7 条（空模型拒绝/桥架线管双线逐值几何+竖直段法向回退/缺省宽度旗标/毫米标与轴位原文剥空白透传/柱网刻度字母端点钉进信封+钉几何方向/零长度段通道丢弃/序列化 PascalCase 键名契约+载荷可回解）；FillOrbitPreviewWindowTests 随内核改造（HelixStaRunner→WpfStaRunner 保留单 STA 串行并关 WebView2 自启，三条控件用例改信封计数口径，窗口级对半分/布局断言原样）；BundleLoadingTests/build.ps1/installer.ps1 payload 三处同步（25→24 文件：去 HelixToolkit.Wpf.dll、加 QuickLineOrbit 三件）。浏览器实测（本地 http 服务 + window.postMessage 注入信封冒烟）：模块加载零报错、8 条标签全渲染（毫米标×2/端点钉×2/刻度×4）、透视场景/桥架填充/柱网/按钮图例截图确认，并按视觉反馈微调（桥架填充不透明度 0.2→0.3、线管双线加粗 0.6→0.8 档）；开发中还抓掉两个页面缺陷：载荷解包（message.envelope||message 两形兼容）与端点钉标签键名。全量 1577/1577（基线 1570）。"),
new VersionChangeLogEntry("3.3.4", "2026-11-18",
                    "修复（图层收口，全命令审计的三个新功能灰区按用户口径定案「主要针对新功能/图层问题」）：①U1B 软管矫正通道打通——3.3.3 的 U_HOSE 法定图层与「软管 h」计数在旧接线下其实是死代码：软管三件套（轮廓/引线/文字）SOURCE=轮廓自引用，而轮廓被扫描器按元数据命中跳过、永不进宿主池，按宿主判框必然落空——HoseEntities 恒 0、U1B→U_HOSE 移层分支不可达（用户手动挪层/建层失败回退当前层后，U1U/U1F 永不纠正软管）。修复=归属消费端 FilterOutputsByHostFrame 给 KIND=U1B 留「宿主落空→按图框边界几何直通」通道（OutputCenterInsideBoundary：几何范围中心在界内即属本框；打不开/已删/空几何=不属，不猜），其余 KIND 宿主未知仍跳过（D-054 不猜测条款不变；3.3.3 Amendment「attribution still skips U1B outputs」相应作废）。批量 U1U 图层汇总同步补软管列（3.3.3 只改了单框侧：计数不加 HoseEntities——整批仅软管待矫正时底部报告行整体消失、收尾文案缺 U_HOSE、无法判定口径缺 U1B）。②U1F 图框块选集带上图框边界——严格分区 EntityIds 从来不含电缆线，U1F 选一个图框块时路由体检按 EntityIds 包含判归属、对图框内容整体空转（连通性无段、图层矫正无目标、3.3.0 的特性①②对 U1F 形同虚设；U1U 靠自己的分区赋值拿到 Boundary，U1F 没有）。修复挂在选集展开路径零增长编排器（ExpandSingleFrame 本来就 Collect 过分区，展开时顺带设 selection.Region；机会式：分区失败/空分区/部分显式选集不设 Region 回退既有语义，U1U 行为不变）。③旧版无元数据输出与无快照门禁登记 KNOWN-ISSUES——元数据时代（D-018/D-050）之前的 U1Q/U1C 输出不带 UNCAD_PARALLEL，与手画电缆线无法在不猜色/猜文字的前提下区分（D-054 明令禁止）：这类图纸上的旧偏移线会被当电缆段移进 U_CABLE（色不变）、参与连通性点名，且重跑 U1Q/U1C 无法回收旧输出（按句柄匹配需要元数据）——旧输出请手动清理，登记为已知限制而非硬修；无机台快照时 U1S/U1DWG 的整机回路门禁静默跳过（无法判定应画回路集）也一并登记（配置 U1DATA 刷新一次即启用门禁）。顺手修（审计 P3 新功能项）：U1S/U1DWG 帮助文案图框世代补 FRAME_20261102（收集器口径 frame_20260812/frame/xframe/xframe_v2/FRAME_20261102）；U1B 软管直径下界收紧到 1（旧判只拒 ≤0，0<d<1 的设置会照画细丝管，与「应在 1–1000」文案不符）；U1B 取点注释对齐实际 ESC 行为（ESC=UserBreak 静默终止命令零实体，非「返回已取点」）；U1L3D 单框场景构建失败从裸抛原生错误改为礼貌退出文案（对齐 U13D 逐帧与 U1U 路由预览的降级口径）；README 命令一览表补齐 9 条（U1L3D/U13D/Xauto/U1PDF/XLAYOUT/XSTS/Xmerge/U1HELP/UNC_LOGIN——代码侧五处清单有测试机械锁死，唯 README 人工表漂移）。锁死：AuditRoundContractTests +3（HoseOutputs_ReachLayerGovernorViaBoundary：归属消费端必须有 U1B 边界直通；BatchLayerSummary_CountsHoseAndMentionsAllLayers：批量求和含 HoseEntities+两处文案；U1F_SingleFrameSelection_GetsFrameBoundary：展开路径必须设 Region）；UNCAD_ROUTE_INSPECT_SELFTEST 扩 U1B 断言（框内软管替身挂 KIND=U1B/SOURCE=自引用：HoseEntities 必须=1 且进 U_HOSE 移层计划，同时三段电缆链/预览/连通性断言原样通过——软管替身被扫描器跳过不搅扰电缆链）。全量 1570/1570（基线 1567）；真机 UNCAD_ROUTE_INSPECT_SELFTEST PASS + UNCAD_MARKER_SELFTEST/UNCAD_HOSE_SELFTEST/UNCAD_UNR_SELFTEST 回归 PASS。"),
                new VersionChangeLogEntry("3.3.3", "2026-11-17",
                    "新增：U1B 绘制软管（D-054 Amendment；用户原话：「绘制软管 输入 U1B 然后让用户 自己来用类似 LINE 的方案来连线(支持多段) 回车后 用线条绘制一个平面的管子 管子直径默认100 绿色 然后要求用户鼠标点击一个位置 点击后 自动将这个多段线的中点连向这个位置并生成文本 软管xxxxxxx 注意需要处理好拐弯处 和首尾端」）。交互同 LINE：连续取点（U 回退上一点，回车/空格结束，重复点自动忽略；不足 2 点礼貌退出不画）→ 立即生成平面管子 → 点取标注位置 → 从路径**长度中点**拉引线 → 生成软管文本（默认值可改）。管子几何为解析式双线轮廓（Core 新增 HoseOutlinePlanner：中心线两侧偏移 ±直径/2，**不用** LWPolyline ConstantWidth；外拐角半径=管半径的圆弧过渡（切点=偏移端点天然相切）、内拐角偏移线交点截断（近折返急弯腿长超 4×半径退化为 bevel，与 OffsetPolylineBuilder 同哲学——有界不炸）、首尾半径=管半径的半圆端帽封口；共线连续点/重复点清理），输出一条闭合 LWPolyline（夹持编辑不散架）。直径新配置 UNC_HOSE_DIAMETER（默认 100，U1SET 绘制标注页可改），绿色 ACI 3，新法定图层 U_HOSE（全大写家族，色号 3）入 UncadLayerStandard（GovernedLayerNames/ColorIndexOf/TryResolveAnnotationLayer 同步；图层矫正文案加「软管 h」项）。管子/引线/文字全部挂 ParallelAnnotationMetadata KIND=U1B（SOURCE=轮廓句柄）：图层矫正与归属链自动认得（U1B→U_HOSE），扫描器 TryRead 命中即跳过——管子轮廓/引线绝不被当成电缆路由。标注默认文本沿用软管链路既有格式（RuanguanLabelFormatter「100mm软管:2500mm」，U1C/U1F/U1U 写进 Ruanguan 块的就是它，RuanguanLengthParser 读的也是它，长度取整到 10）；该格式整行不匹配电缆/桥架/线管求和判据、也匹配不上 D-043 预留标——不污染统计、不被误吸收；软管米数统计仍以 Ruanguan 软管块为准（求和引擎没有软管文字类别，U1B 的独立 DBText 不参与软管求和）。文字高度沿用 UNL_HEIGHT 口径。全程一个写事务：一次 U 连同管子/引线/文本全回退；取点阶段取消=未绘制任何实体，标注阶段取消=只留管子。锁死：HoseOutlinePlannerTests（直线段偏移/L 形外弧+内截断/端帽半圆/退化输入合并/PointAtDistance）+ HoseAnnotationDefaultsTests（默认文本=Ruanguan 格式可被 RuanguanLengthParser 读回、不匹配电缆/桥架/线管/D-043 判据）+ LayerGovernorContractTests 扩 U_HOSE/U1B 冻结 + UNCAD_HOSE_SELFTEST 真机自检（非交互直调几何服务：实体数/图层=U_HOSE/色号 3/元数据 KIND=U1B/轮廓闭合含端帽 bulge/Scan 不认它当电缆）。",
                    "修复（3.3.3.1，现场·高优）：U1U/U1F 识别不到横排毫米标（用户报告「又识别不到设备预留了」「识别不到水平的文本且末尾是 0mm 的单行文本」，附两日日志）——版本指纹定位：11-16 现场≤3.2.0.6 一切正常（全天 73 处吸收/守卫记录），11-17 升到 3.2.0.10 后 11 框批量 U1U 零候选零提示；凶手是 3.2.0.6 给 DeviceReserveMarkerReader 加的「带 U1LX 关联即排除」读侧口——旧判只看关联字符串非空，把垃圾关联一并当真：ObjectId.Null 存出的句柄字面量 0、线已删除的悬空句柄、跨文件/复制图框带来的陈旧句柄（UNCAD_MARKER_DIAG 在天车项目真图逐文字点名），现场横排毫米标沾上任何一种就永远进不了吸收候选。修复=排除口收窄为 AssociatedLineIsLive：关联句柄按 16 进制解析、经 source.Database.GetObjectId 反查本图实体，解析到活着的 Line/Polyline 才排除（真 U1LX 标注照旧放走，3.2.0.6 本意不回归）；0/解析失败/悬空/非线实体=不在役标注，按现场可见意图放行（D-052 附录 7 口径）。锁死：UNCAD_MARKER_SELFTEST 真机夹具（真实图框+三根横排毫米标：垃圾关联 0 的 3000mm 必须收、无关联的 2000mm 必须收、关联活 Line 的 5000mm 必须排除）+ 契约 MarkerReader_ExcludesU1LAssociatedLabels 升级双锚定（进度窗 3.2.0.8 同款教训：旧契约锁的正是病灶代码形状）+ UNCAD_MARKER_DIAG 法医命令留存（真实图纸逐文字报三道口死因：关联活性/水平性/判据）。全量 1562/1562；真机 MIGRATION/ROUTE_INSPECT/MARKER 三门禁 PASS。",
                    "修复（3.3.3.2，全命令执行顺序/逻辑审计批次·四项，37 条命令四路只读审计后逐一验收对源核实）：①XLAYOUT 身份归一化缺口（P1，违反 D-021）——排布上下文的 RowOrder（Excel 行序表）用默认比较器（区分大小写）、UpstreamOf（上游映射表）只忽略大小写，且键只去首尾空格；同文件的 ReadinessRank 却一直用 IdentityTextNormalizer（忽略全部 Unicode 空白+大小写，D-021 正因现场「M Q-01/MQ-01」而立）。图上文字与工作簿存在内部空格或大小写差异时：行内序静默退回图上位置序（D-045 行序失效）、上游模式把回路错分进「未配上游」行（图框被物理排错行）。两张表换 IdentityTextNormalizer.Comparer（填充侧仍走 MakeKey，两侧经比较器归一后命中）。②U1R 开洞换线元数据重挂（P2）——手工 U1R 把 Line 换成带洞 Polyline 后只做 AppendEntity+Erase，毫米标的 UNCAD_U1LX 关联与 U1Q/U1C 输出的 UNCAD_PARALLEL SOURCE/RUN 全部悬空（Xauto 的同形操作 3.2.0.6 起就两件都做）：重跑 U1Q/U1C 认不到旧输出（平行标注重复残留、图层矫正永不认领）；3.3.3.1 把 D-043 排除口收窄为「关联解析到活线才排除」后，洞穿线段的横排毫米标会被判「不在役」而误当设备预留吸收（文字被删、该段退出统计）——本修复在根因上闭环该窗口。实现：命令入口一次收集两张倒排表（ParallelCurveAnnotator.CollectBySourceHandle + 新增 QuickLineCadService.CollectLabelsByLineHandle，本命令运行中不新建这两类元数据、命令始收集不失效），换线后镜像 Xauto ApplyHops 收尾（RetargetHandles 改挂 SOURCE/RUN、TryLinkLineAndLabel 重挂毫米标保 COMPLETED；单个失败记日志不阻断开洞）。③批量 U1U 性能回归（P2，3.3.0.1 引入）——平行标注归属收集（全空间逐实体走查）此前在逐框 Inspect 内部各收一遍=O(图框数×空间实体数)；与 routeScanned 同一教训提到预检循环外整图收一次经 precollectedOutputs 下传复用（单框路径不传保持原行为）。④U1F 机台选择窗预览保护（P2）——预览构建链会主动抛（设备块状态冲突（D-030）/软管长度非法，主流程同因是优雅中止），WPF 事件处理器裸奔则成未处理异常炸窗；同 3.3.0.1 路由预览的降级口径包 try/catch 出「预览生成失败」文案+Log.Warn，选择与写入不受影响。锁死：XLayoutLayoutArrangeTests +2（工作簿「MQ-01」×图上「M Q-01/mq-01」行序照常命中/上游跨空格大小写照常同行）；AuditRoundContractTests +3（U1R 换线后必改挂两元数据且倒排表在点击循环前收集、批量归属收集协调器恰 1 处+预检 0 处+Inspect 空值回退保留、机台选择窗预览必包降级）；真机新自检 UNCAD_UNR_SELFTEST（accoreconsole 非交互直调 UnrFeature.TryReplaceLine 真实路径：夹具=横线+活关联横排毫米标+挂 U1Q 元数据的偏移线，断言换线前两张倒排表命中旧句柄、换线后关联/SOURCE 都改挂新 Polyline 活体、DeviceReserveMarkerReader 对该毫米标换线前后都零候选；失败先落结果文件再抛——accoreconsole 对未处理异常是直接断进程不打印；开发中还抓到 ParallelAnnotationMetadata.Set 对未注册 RegApp 的 XData 赋值静默吞错，夹具补 EnsureApplication 并写后读回验证）。全量 1567/1567（基线 1562）；真机 UNCAD_UNR_SELFTEST PASS + UNCAD_ROUTE_INSPECT_SELFTEST/UNCAD_MARKER_SELFTEST 回归 PASS。"),
                new VersionChangeLogEntry("3.3.2", "2026-11-17",
                    "更换（D-054 Amendment，用户现场两条反馈）：U1U 3D 预览内核 Canvas 正交投影退役、切换为 HelixToolkit.Wpf（NuGet 锁定 2.27.0，MIT，仅 HelixToolkit.Wpf.dll 本体、无任何传递依赖）。用户反馈①「轴位不应该这样显示吧」——3.3.1 沿重建路由的 X 单向插值标数字轴不对：柱位是两向正交柱网（数字轴×字母轴），且场景 X 是重建抽象轴、与现实柱网方向无固定对应；②「我旋转的时候明显感觉这不对 这不像是3D的 像是一个伪3D的」——正交投影无透视无线索。主会话列完整选型表（HelixToolkit.Wpf / WPF 内置+3DTools / Canvas 透视自绘 / WebView2+three.js）与用户逐项过：HelixToolkit.Wpf 拍板（真 WPF 3D 透视相机+成熟轨道交互，无浏览器进程/无 JS 边界；WebView2 方案作废，此前筹备未留半成品；WPF 内置缺现成控件、Canvas 透视仍是手搓投影矩阵）。新控件 QuickLineHelixPreviewControl：HelixViewport3D 内置 PerspectiveCamera+轨道交互（左键旋转/滚轮缩放/右键平移/双击复位），路由线=LinesVisual3D 等宽屏空线（轴向配色沿用 X橙/Y绿/Z蓝），米数标与轴位标签=BillboardTextVisual3D（面向相机、世界高度按场景对角线比例），桥架=两侧红色平行线（偏移=宽/2，世界空间下限=对角线千分之一保远距可见）+浅红半透明双面填充带，线管=青色细双线，图例/柱网开关/轴位摘要为叠加在视口上的 WPF 元素。",
                    "修正（两向柱网，替换 3.3.1 的单向数字轴插值）：地面 Z=0 数字轴×字母轴两向正交网格线（LightGray 细线，4800mm 柱距与 QuickLineAxisSpan 同口径，覆盖场景包络，柱网开关保留）。数字轴向=上下游端点平面位移较大的那向（平手取 X——不硬编码「数字=X」，正本清源用户反馈①）；两端数字轴都解析出来时按跨度线性插值标数（48/47/46…，越界/重号跳过，方向以端点标签为准）。字母轴=垂直于数字轴的那向：两端字母相同→以上游字母锚定按柱距递推邻居（U/V/W…，任务示例口径；多字母值按 26 进制索引推，递增方位约定 +坐标=A→Z）；两端字母不同→线性插值夹在两端之间（与数字轴同口径不外推）；两端字母位置重合或任一端解析不出→该向不标（不猜）。两端端点钉台账轴位原文不变；下游端点平手取 max Y（3.3.1 取 min Y 会让纯 Y 向路由两端钉叠在同一节点、位移恒为零——顺手修正）。QuickLineColumnGridPlanner 扩展为两向规划（新增 LetterLabels/NumericAxisIsX/包络四至），InterpolateTicks 数学保留。",
                    "退役（Canvas 内核）：QuickLineOrbitPreviewControl（Canvas 自绘）、Core 的 QuickLineOrbitCamera（正交投影数学）、QuickLineOrbitLabelPolicy（投影过短隐藏标签——透视 3D 下 Billboard 恒面向相机，无「投影过短」概念）删除；数据模型 QuickLineOrbitPreviewModel/Segment/Channel 与 BuildPreview 输入链不动（Features 阶段照旧构建传入，分层铁律不变）。工程细节：HelixViewport3D 惰性创建（U1F/无预览路径与 3D 完全无关）；Helix 主题字典含跨线程共享的未冻结 Freezable、Billboard 视觉订阅进程级弱渲染事件——已用独立最小复现证实第二个 STA 线程渲染视口必炸（与 UNCAD 代码无关，生产 AutoCAD 单 UI 线程不受影响），控件在窗体 Unloaded 时摘下全部场景子元素使逐帧回调失效，测试侧全部 Helix 视口收敛到共享单 STA 线程运行器（HelixStaRunner）。",
                    "发布链：bundle 文件清单 24→25（新增 HelixToolkit.Wpf.dll；build.ps1/installer.ps1 的 payload 允许清单与 BundleLoadingTests 契约同步，PackageReference DLL 照 WPF-UI 先例由 build.ps1 自动复制进 bundle、checksums.sha256 同步重算）。锁死：QuickLineOrbitPreviewEnhancementTests 改造为两向柱网（15 条：两向网格+数字刻度+字母锚定/同字母跨包络递推/异字母插值夹取/半边与解析失败不标/数字轴向跟随位移不跟场景 X/字母索引双射/插值取整越界重号/网格覆盖截断/桥架宽度解析/FromScene 带入×2/阶段类契约）；QuickLineOrbitCameraTests（7 条）随相机退役 4 条投影数学、存留 3 条迁为 QuickLineOrbitPreviewModelTests（地面归零×2+AxisSpan）；FillOrbitPreviewWindowTests 断言降为数据级（窗口宿主/模型传入/布局回退/数据级构建计数，不做 GPU 渲染断言——Helix 视口在无显卡环境可能飘）；真机 UNCAD_MIGRATION_SELFTEST PASS + UNCAD_ROUTE_INSPECT_SELFTEST DONE（EntityIds=0 根因断言保留）。全量 1549/1549（基线 1547：退役 5 条——相机投影数学 4+标签显隐阈值 1；新增 7 条——同字母递推/异字母插值/字母索引双射/平手取X与maxY端点/递推越界跳过/非法柱距/视口惰性创建，净增 2）。"),
                new VersionChangeLogEntry("3.3.1", "2026-11-17",
                    "新增（D-054 Amendment，用户现场四条反馈）：U1U 3D 预览增强——①窗口对半分：用户反馈「不要跟清单确认挤到一块 相当于一个窗口分成两半」，单框清单确认窗与批量确认窗的预览区改为左半（两个 Star 列 + 中间 GridSplitter 可拖动，两侧各约 30%/250px 最小宽度保护），废弃 3.3.0 的固定 300/320px 窄条；预览为 null（U1F/旧行为）时列宽回 0、分隔条隐藏，清单独占整行。②米数标记：用户反馈「每条线也没有米数标记」，每段中点标注其毫米标原文（固定屏幕字号 12px 不随缩放变小，浅底保证可读；段投影长度低于 24px 时该段标签自动隐藏防糊成一片——显隐判定是 Core 纯函数 QuickLineOrbitLabelPolicy）。③柱位进场景：用户反馈「3D预览内展示柱位 你没展示」，地面（Z=0）按 4800mm 柱距（与轴网口径一致）画浅色柱网覆盖场景包络（右上角小按钮可开关，默认开），路由两端端点上方钉上游/下游轴位标签（台账原文，解析失败显示原文不猜），两端数字轴都解析出来时按路由 X 跨度线性插值在柱网线标数（48、47、46…，越界/重号跳过；方向以端点标签为准对齐，48 在上游一侧）；规划是 Core 纯函数 QuickLineColumnGridPlanner。④桥架/线管双线通道（推翻 3.3.0「预览里不画桥架/线管叠加」的暂缓条款）：用户问「3D预览没有展示桥架 你感觉桥架怎么展示比较好？」，主会话与用户定方案=双线通道——归属到本框宿主电缆段的 U1Q 输出画电缆线两侧两条红色（#E8443C）平行线（偏移=桥架宽度/2，随缩放自适应但投影后至少 6px）+浅红半透明填充带；U1C 输出画青色（#2EA8C0）双线细间距不加填充；桥架宽度从 U1Q 标注文本解析（桥架200*100 2500mm→200，旧格数写法也认），解析不出用 200 缺省并在图例注明；归属只认 ParallelAnnotationOwnership 的宿主解析（宿主删了的没有，与图层矫正同口径不猜）；角落图例注明五色语义（橙X/绿Y/蓝Z/红=桥架(宽)/青=线管）。",
                    "修复（3.3.1 顺手）：滚轮缩放实际不起作用——3.3.0 的自动适配每帧按当前投影包络重算 fit，恰好把相机 Zoom 整除掉（滚轮在视觉上是空操作）；现 fit 按未缩放包络当量计算后统一乘 Zoom，复位画面与 3.3.0 一致，缩小/放大真实生效（缩小后米数标记按阈值隐藏、桥架双线偏移随之变窄/加宽）。",
                    "锁死：QuickLineOrbitPreviewEnhancementTests 10 条（标签显隐阈值/柱网线覆盖与锚定/数字轴插值取整·越界·重号/解析失败只画网格/纯字母无数字不画刻度/退化跨度无刻度/非法柱距拒绝/桥架宽度解析毫米+旧格数写法/FromScene 带入毫米标原文·通道·轴位原文/阶段类接线契约）+ FillOrbitPreviewWindowTests 增强（对半分 Star 列与 Splitter 显隐断言、预览隐藏回退、增强模型渲染计数、柱网开关、缩小标签隐藏、复位恢复）；真机 UNCAD_ROUTE_INSPECT_SELFTEST 同步加断言（逐段毫米标原文带入+轴位原文带入，EntityIds=0 根因断言保留）。全量 1547/1547。"),
                new VersionChangeLogEntry("3.3.0", "2026-11-17",
                    "新增（D-054 特性①）：法定图层规范+自动矫正——U_CABLE（电缆路由：U1L 线段+毫米标，色号 7）、U_TRAY（桥架：U1Q 输出，色号 1 红）、U_CONDUIT（线管：U1C 输出，色号 4 青），三个图层名一字不差全大写、无空格，缺失自动按色号创建。识别依据：电缆线=扫描器认出的 U1L 线段实体，毫米标=元数据关联或扫描器匹配到的标注文本→U_CABLE；带平行标注元数据的输出按既有 KIND 字段区分（U1Q→U_TRAY、U1C→U_CONDUIT；KIND 自 D-018/D-050 元数据引入起恒写入）。KIND 既非 U1Q 也非 U1C 的输出不移动、点名「无法判定」；无元数据的旧输出无法识别为插件实体——同样不移动、不猜测。U1L/U1LX 今后新生成的线+毫米标直接落在 U_CABLE。U1U/U1F 确认窗与批量预检列出「图层矫正：N 个实体移正（电缆 x/桥架 y/线管 z/无法判定 w）」，矫正动作放进既有写事务（一次 U 可回退）；U1SET 开关 UNC_FILL_LAYER_GOVERNOR 默认开。",
                    "新增（D-054 特性②）：连通性检查+Polyline 扫描——U1LX 扫描器支持开放 LWPOLYLINE（U1R/Xauto 开洞后的线=带 bulge 的开放 Polyline，作为一个段返回：起点=首顶点、终点=末顶点，弧段按弦长参与端点连接/交叉检测，毫米标经既有元数据关联照常取得；闭合 Polyline 不是电缆路由，跳过）；Xauto 对分量含开洞 Polyline 的整分量跳过重排并点名诊断——比旧版更安全（旧版这些线根本不进扫描，邻线重排后直接撕开草图）。新增纯分析器 CircuitConnectivityAnalyzer（端点容差与 U1LX 同口径）：输出每个连通分量的段集、孤立段（度数=0）与「每段至少一端相接」违规清单。U1U 批量确认窗新增「连通性」列（✔ N 段一条链 / ⚠ 孤立段：句柄…），批量预检同名报告进结果窗备注，U1F/U1U 单框命令行输出诊断。**报告级不拦截**（不 abort，现场反馈后再议升级）。",
                    "新增（D-054 特性③）：U1U 确认窗左侧 3D 路由预览——正交轨道相机（Core 纯数学 QuickLineOrbitCamera：方位角/俯仰角旋转+缩放，单测锁死投影值）+ WPF Canvas 自绘（**无第三方 3D 库**）。场景由 QuickLineIsometricSceneBuilder.BuildAll 按毫米标重建后整体地面归零（所有节点 Z 减去最小 Z，最低线段端点高度为 0）；线段按轴着色（X 橙/Y 绿/Z 蓝）。柱位摘要按台账 U_上游轴位/U_设备轴位 解析（容错「48/V」「48-V」「48」「V」，解析失败显示原文不猜），展示如「上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m」。交互：左键拖动=轨道旋转、滚轮=缩放、双击=复位。单框 U1U 确认窗与批量 U1U 确认窗（列表选中哪个图框行，预览切到那个图框的场景，窗开前按框预建）都有预览；遵守分层铁律——窗体只绘制传入模型，场景/相机模型在 Features 阶段构建后传入。",
                    "新增（D-054 特性④）：U1Q/U1C 归属识别 API——ParallelAnnotationMetadata 的 SOURCE/RUN → 宿主电缆段映射（ParallelAnnotationOwnership：按图框归组收集输出→宿主段解析），图层矫正（特性①）按 KIND 消费它。预览里不画桥架/线管叠加（控制范围，留后续）。",
                    "修复（3.3.0.1，现场）：U1U 确认窗 3D 预览不出现、电缆/桥架图层没被矫正——用户装上 3.3.0 后报告两者都不生效；UNCAD_ROUTE_DIAG 在天车项目 MSRTL01.dwg 真图复现：扫描 24 段（17 段带毫米标）但落入图框 EntityIds 的 = 0。根因：路由体检阶段错误假设图框实体集包含电缆线——FrameRegionCollector.Collect（U1U 单框/批量预检用的严格模式）的 TryAnchor 只把表格/文字/块归入 EntityIds，Line/Polyline 电缆线从来不在里面（填充统计的历史设计，本身无错；Xauto 走 CollectForLayout 全实体模式所以没踩）。修复=线段归属改按图框 Boundary 几何判定（中点在界内，EntityIds 仅作无边界回退）；平行标注输出改按宿主电缆段判框（宿主在框内→输出在框内，红线 Line 同样不在 EntityIds 里，按宿主归属才不漏移）；矫正计划只喂本框线段（绝不动图框外图形）；预览场景构建包 try/catch（方向不可分类的草图不再能炸掉 U1U 主流程）。修复后天车真图复验：矫正 17 个实体（电缆 17）+预览 5 段+连通性点名真实孤立段。锁死：UNCAD_ROUTE_INSPECT_SELFTEST 真机夹具自检（真实图框定义+框内 3 段正交链+3 毫米标，断言 EntityIds 严格模式扫描命中=0 根因存档、Boundary 归属后预览 3 段/一条链/3 线全进 U_CABLE 计划）+ UNCAD_ROUTE_DIAG 诊断命令留存（真实图纸逐环节报数）。全量 1536/1536。"),
                new VersionChangeLogEntry("3.2.0", "2026-11-15",
                    "新增：Xauto 自动优化命令（D-053）——框选完整图框，按毫米标注一键重排等测草图比例并自动开拱桥：每段图上长度重排为正比于其真实毫米值（未确认的占位段按当前长短等比缩放），短段钳到「洞+毫米标文字都放得下」的下限，整草图铺满图框可用空间并联合居中；重排后在最终几何上检测交叉，用等测 3D 重建的相机深度判定前后——靠近相机的线自动开半圆拱桥（直径沿用 U1SET 的 U1R 设置），无法判定深度的交叉跳过并点名（可手动 U1R 补）；分量内含未标注或不可分类线段时整分量跳过并点名，绝不撕烂草图；开过洞的线自动重挂 U1LX 关联（COMPLETED 保留）；整个命令一个写事务，一次 U 整体撤销；不改标注文字，不影响 U1F/U1U 统计。",
                    "共用：U1R 拱桥几何抽取为共享 ArchBridgePolyline（单洞/多洞统一规划），U1R 交互行为不变；等测投影与深度判定沉淀为 Core 纯逻辑 IsometricProjection，与 U1L3D/U13D 的场景重建共用同一套轴测方向约定。",
                    "改进：功能区图标统一单色（AutoCAD 原生风浅灰）——旧的一命令一色商务配色在深色主题下像彩虹盘，与原生功能区统一浅灰线性图标反差强烈（用户对照截图直言难看）；现全部图标统一 UiTheme.RibbonIcon 单色，命令区分回归形状+文字标签，锁定测试防分色回潮。",
                    "改进：版本展示一律详细四段号（如 3.2.0.0）——启动动画/关于页/更新弹窗标题/U1SET 概览/配置分享记录/授权心跳 UA 全部显示 X.Y.Z.第四段，现场排障一眼可知客户补丁位；更新日志比较与已看版本存储仍按三段，升级弹窗时序不受影响。",
                    "修复（3.2.0.1）：Xauto 重排两处现场报告——①短段比自己的毫米标还短：旧下限只保 1.5×洞径，没算标注文字宽度；现逐段下限 = max(1.5×洞径, 标注文字宽×1.15)。②重排挤在原图小范围里：旧算法 k 以原图包络封顶（画得小就永远小）；现按 D-053 修订以图框可用空间为目标（边界内缩一个洞径，如 75000×7500 的图框就铺满它）：整草图共用一个比例系数二分拟合联合包络、联合居中于图框中心、分量间相对位置不变；图框小到内缩为负时回退旧行为。",
                    "修复（3.2.0.2）：老框毫米标不被吸收的一类根因——D-043 扫描读 MText 用原始 Contents（含 {\\f字体;…}/对齐码等格式码），带格式码的 MText 毫米标（旧版软件常这么写）永远过不了纯数字判据；现与统计链路同口径先 CleanMText 再判据。真机自测阶段 2.5 新增三剧本锁死：从未填过的老框/带 v1 旧记录的老框（图纸标注覆盖旧 JSON 预留）/带格式码 MText 毫米标的老框，走生产选集链（分区→Split→FillReserveStage U1F 模式）全部吸收+登记删除+排除统计。注意：多处毫米标（点名提示人工确认）、U1SET 关闭自动吸收、复制框守卫（外来 JSON 点名）时仍按既有语义不吸收——命令行会写明原因。",
                    "修复（3.2.0.3）：Xauto 铺满压到框内表格（用户现场报告\"超了\"截图）——3.2.0.1 的目标空间用的是整框内部（只内缩一个洞径），框内右侧的清单表/制图信息表也在目标里，铺满即压表，且居中按全框中心算视觉偏斜；另有钳位下限撑大二遍包络、二分只按一遍估计的偏差。修复=①可用空间扣除表格占位（FrameUsableArea：整列表裁横向/横条表裁纵向/四角小块不裁/裁空回退旧行为），居中按扣除后区域；②Planner 装后校验收缩环——装完量真实联合包络，超了按比例收缩 k 重装（≤4 轮），最终几何保证落在目标矩形内。",
                    "变更（3.2.0.4，D-052 附录7）：U1F=重新定义——①第一步无条件清理图框所有旧信息（旧 JSON 记录不再用于恢复/预填：CABLE_INFO 恢复兜底与复制源预填只在 U1U；写事务清旧属性不再限于复制框）②横排·单行·末尾 0mm 的毫米标无条件自动转为设备内预留并删除源文本（复制框守卫移到仅 U1U——图上可见标记是现场意图）③正常求和（已吸收标记排除防双算）④写入。U1U 语义全部不变（守卫/恢复/复用/搬场重绑照旧）。毫米标判据同步收紧：必须整数以 0 结尾+mm 后缀（3000mm ✓；3000/555mm/小数/多行 ✗），收窄误吸收轴号序号。",
                    "修复（3.2.0.5，U1F/U1U 全链审计收口三修）：①FrameRedefineGuard 误丢毫米标吸收值——吸收产出的是空机台号会话合成记录，旧 Guard 同机分支当幽灵丢弃（命令行刚宣布\"计入设备内预留\"、确认窗却预填 0，还打印\"记录属于机台 \"+空名）、改机分支整体丢弃（违背 D-052 附录7\"标记是对所选机台的意图\"）；现空机台号记录在 Guard 入口原样放行（带机台号旧记录防御不变）。②FillOrMigrate 遗留独立块继承在 U1F 关闭——老图纸的独立 frameinfo_json 块此前会把旧替代型号/预留/历史从写侧暗道复活回来（\"清理所有旧信息\"未贯彻）；U1U 迁移语义不变。③搬场\"收下记录\"文案仅在 U1U 打印（U1F 收下又丢弃自相矛盾）+删除复位块死语句。锁死：FrameRedefineGuardTests +2（空机台记录同机/改机都保留）、契约 U1F_Redefine_AuditFixes、真机⑧增改机 Guard 保留断言。审计另确认：两链写事务逐行一致、U1U 批量链零 diff、17 条顺序契约全部满足、门禁矩阵与决策一致；文档 10 处漂移同步（AGENTS/PROJECT_CONTEXT/DEVELOPMENT/CLAUDE/D-043 Amendment）。",
                    "变更（3.2.0.6，D-050 Amendment）：U1C 线管标注默认车道改为与桥架完全一致（上侧、偏移 15）——用户点破\"有线管的线不会有桥架\"，线管与桥架在一条基准线上互斥，D-050 为\"可能共存\"预留的独立分离车道（下侧外圈 210）失去存在意义。独立 UNC_CONDUIT_* 配置键保留：显式改过的设置仍原样生效，字高仍共用 UNQ_HEIGHT，\"U1C 不读 UNQ_* 键\"约束不变。同步：U1SET 设置窗默认值/侧向兜底、设置页标签（紫线距基线 0 = 15）、U1HELP 命令说明、用户手册 U1C 卡与附录 B 配置表；真机自测⑤改为显式偏移几何证明；新增源级契约 ConduitLaneDefaultContractTests 锁死 ConduitFeature 与 U1SET 两处默认一致。",
                    "修复（3.2.0.6，六路深审 24 项）：六个并行子代理全仓深审（Fill 链/绘图标注+Xauto/导出提交统计/Infra 授权/UI 层/决策文档+测试盲点）后修复的确认缺陷——①Fill 链：U1F 写侧内嵌 FRAMEINFO 合并暗道关闭（无时间戳会话记录在 Merge 时间戳规则下恒败给旧记录，旧预留复活/刚吸收的毫米值丢失）；批量 U1U 补 FrameRedefineGuard 幽灵记录防御（与单框对齐）；\"00mm\" 等零值候选拒绝（吸收成 0 还删文字）；带 U1LX 线关联的 U1L/U1LX 毫米标不再被 D-043 误吸收（水平标注与判据同形，吸收会删标注+剔统计一箭三伤）；单框写表失败从静默 return 改抛出点名回滚（与批量同口径）；U1U 可见消息/弹窗标题不再硬编码 [U1F]/U1F 前缀。②Xauto/标注：重排时 U1Q/U1C 旧输出随源线平移、开洞后 SOURCE/RUN 句柄改挂到新 Polyline（旧输出不再孤儿化，重跑可整趟替换）；U1Q/U1C 批量生成按趟删除旧输出（一趟失败不再陪删全部旧标注）；重排收缩环诊断截断时序修正（第二轮诊断不再被误清）；毫米标重挂放宽到 MText。③导出：U1PDF 逐机台创建输出目录（首跑新机台不再必败）+机台ID非法字符点名跳过+残件清理改会话释放后执行；XSTS 报表临时文件+替换原子写；U1DWG 替换成功后备份清理改吞错（不再把成功批次误报失败并整批回滚）；FileBatchRollback 恢复失败的条目保留 .bak（旧版最后副本不再被销毁，异常点名备份路径）。④UI/Infra：在线授权窗验证成功后正确自动关闭（此前被自己的 Closing 守卫取消，点取消还会把已完成登录显示成失败）；启动动画 DispatcherTimer 关窗即停（不再每 16ms 永久空转+粒子/位图常驻内存）；进度窗禁用手动关闭；设置保存前数值框非法输入拦截（NumberBox 静默存下限值——待并行线合并）；配置分享码版本前向兼容（旧版码永远可导入，只拒比当前新的版本）；心跳 WebException 响应释放；U1DATA/U1SET 等命令门禁契约；Bump-Version 失配模式修复；文档漂移 6 处同步（README 3.2.0/36 命令/新默认、COMMAND-AUDIT 36、D-043/D-053 Amendment、DEVELOPMENT Guard 时序）。锁死：AuditRoundContractTests 16 条（3 行为+13 契约）。全量 1492/1492。",
                    "修复（3.2.0.7，现场三项）：①U1U 复制图框要跑两次才正确——用户报告并附日志：首跑因外来 JSON 守卫不吸收复制残留毫米标（正确），但首跑写侧同时把 JSON 重盖成本框句柄，\"外来\"信号就此消失，第二跑把同一根残留标当\"现场意图\"吸收；修复=首跑就把残留标值作为设备内预留建议值带出（清单确认窗/批量弹窗预填+\"复制残留毫米标，请核对\"备注，与 D-052-B 复制源预填同语义），首跑即得正确预留，文字仍保留。②XSTS 新增\"未上图机台\"名单（现场：清单 144 台只有 49 台上图、95 台盲区靠手工 VLOOKUP）——全图帧扫描对账档案，档案机台−全图机台=盲区名单，Excel 新增\"未上图机台\"页+结果窗新页签+徽章，按字母前缀分组摘要（PSP×10、OHVC×10…）；顺带近名报警：盲区机台与图上机台编辑距离≤2 时点名\"疑似画错名\"（现场实例：清单 MSRTL04↔图上 MPSTL04），图上选中机台在档案无记录且与档案近名时进异常清单。③XLAYOUT 筛选表达力升级——区间词 M603-M612（数字界锚定，M6030 不再被子串误捞）、前缀词 MS*、排除词 !MS*（可组合\"AMHS !MS*\"），普通词子串包含 OR 语义不变。锁死：XstsBlindAndFilterUpgradeTests 10 条（近名距离/盲区名单/近名 issue/分组摘要/筛选四组/U1U 预填契约）。全量 1502/1502。",
                    "修复（3.2.0.8，现场回归）：XLAYOUT 排版完成后进度窗永久滞留（用户报告\"都排好了还在显示\"）——3.2.0.6 给进度窗加的\"用户不可关闭\"守卫写成了无条件 Cancel，把命令收尾 finally 里的 Close() 也一并取消，窗口从此关不掉。修复=守卫只拦用户关闭（标题栏 X/Alt+F4），命令收尾统一走新的 CloseByOwner() 出口（XLAYOUT 两处/U1U 预检与写入/U1PDF 出图共 5 个调用点全部接线）；契约测试同步升级（只拦用户+CloseByOwner 出口双锚定，原测试锁的正是这个 bug）。全量 1502/1502。",
                    "修复（3.2.0.9，现场）：XLAYOUT 粘贴机台ID清单排版多出双胞胎机台——用户粘贴 AMHS机台清单.txt（49 台）筛选后排版，结果排出 56 台/236 框，多出的 7 台全是 \"PSPM651\"/\"OHVC-M651\"/\"PSP M623\" 这类同号异组机台：普通筛选词是子串包含，清单里的 \"M651\" 是它们 ID 的子串（与用户此前 XSTS 分析里 \"7 台非 AMHS 混入\" 同源）。修复=筛选词恰好等于某个完整机台号时升级为精确匹配（选择窗把已知机台号全集喂给 XLayoutSelectionFilter 新增的 knownMachineIds 参数，精确词从包含池拆出按精确语义处理；片段词/前缀词/区间词/排除词语义不变，不传全集的旧调用方完全不变）——精确命中的机台其全部回路可见，与机台节点口径一致；全部正向词皆精确且未命中时不再把空剩余池当\"无筛选\"放行。锁死：Filter_KnownMachineIdTerm_MatchesExactly_NotBySubstring（同号双胞胎排除/回路随机器/片段仍包含/混合 OR/旧调用兼容/通配不按精确/全精确未命中边界）。全量 1503/1503。",
                    "新增（3.2.0.10，用户点名）：XLAYOUT 选择窗支持**按台账 U_区域 筛选**——区域下拉（全部区域 + 图上各区域及台数，如 \"AMHS (49)\"）与关键词筛选 AND 生效，选了区域的机台节点标题带区域前缀（\"AMHS · M651（2 个回路）\"）；区域数据来自 SQLite 快照的 U_区域 列（早已采集、首次启用），快照缺失/读取失败时下拉隐藏、行为与旧版一致；XLayoutFeature 新增 LoadRegionByMachine 一次性取机台→区域映射。现场背景：用户手拼 AMHS 机台清单排版仍混入 7 台非 AMHS（经快照区域对账点名：MPOIY03=Litho、MPSTL04=LITHO、MTWDL04/05=Metal、MWSTA01/02/WSTD07=WET），XLAYOUT 排的 56 台与 XSTS 完全一致——区域才是可靠口径，不再靠手拼 ID。同步：XSTS「未上图机台」名单增加\"区域\"列（Excel 页与结果窗页签），盲区直接按区域看结构（AMHS 还剩几台没画一目了然）。锁死：Region_NarrowsTreeSelection_AndCombinesWithKeywordFilter（区域收窄/标题前缀/区域×关键词 AND/切换区域）+ Region_WithoutRegionData_HidesComboBoxAndKeepsLegacyBehaviour（无映射隐藏下拉/无前缀）。全量 1505/1505。",
                    "修复（3.2.0.11，现场）：Xauto 重排后线路被拉得过长——用户报告\"Xauto 有问题 拉的太长了，纵向的线 就2M 一下弄特别长\"：横幅图框（可用区约 75000×7500，宽高比 10:1）里二分拟合的比例系数 k 完全由宽度决定，一条 2m 竖管按 2m×k 画出来占半框高度，视觉尺寸失控；且未确认占位标（尾 00mm）的显示距离=当前画长，k>1 被拉长后，下一次 Xauto 把拉长后的长度当基线——越跑越长回不来（棘轮效应）。修复=①目标模式按占用率缩放：最终 scale = k_fit × fillRatio（新配置 UNC_XAUTO_FILL_RATIO，默认 0.65，合法域 0.2–1.0，超域即拒；1.0 恢复旧\"铺满图框可用空间\"行为；钳位撑爆→收缩重装的装后校验环不变，最终几何仍保证落在图框可用区内）；②占位标只跟缩不跟涨：k>1 时占位段最终画长 ≤ max(原画长, 可读下限)，只允许下限钳位把它抬到可读，棘轮消除；已确认真实毫米的段行为完全不变。锁死：XautoFillRatioAndPlaceholderTests 4 条（占用率包络收窄且仍居中/非法值抛 ArgumentOutOfRangeException 且 0.2 与 1.0 边界合法/fillRatio=1.0 与旧结果逐值一致/占位段 k>1 不被放大且真实段照常等比）。全量 1509/1509。"),
                new VersionChangeLogEntry("3.1.0", "2026-11-14",
                    "UNCAD 3.1 | 如切如磋，如琢如磨——工程加固版：基于 3.0.0.17 现场稳定代码，零命令行为变更，全部投入在测试、流程与文档的可信度上。",
                    "新增：真实台账脱敏测试夹具——真实客户汇总表（832 机台 / 4999 数据行 / 163 删除线行）整体脱敏后入库为固定夹具，并新增解析冒烟测试：统一 U_ 表头绑定、删除线行忽略、机台数与占位符模式逐项锁定；自动化测试从此覆盖真实文件格式（合并单元格 / 公式缓存 / 多 run 富文本），不再只依赖程序合成的理想夹具。",
                    "新增：本地与远端 CI 兜底——scripts/ci-local.ps1 一键全量测试（含「执行数=源码期望数」核对，防测试宿主静默跳过整程序集的假绿，日志落 tmp/ci/）；.github/workflows/tests.yml 提供 self-hosted runner 工作流，push / PR 自动跑全量单测（AutoCAD 程序集依赖本机安装，托管 runner 不可用）。",
                    "新增：发布运维手册 docs/RELEASE-RUNBOOK.md——发布七道门禁的顺序与产物、参数速查、门禁失败处置（结果变量 / 保留日志 / SABOTAGE 自证机制）、self-hosted runner 注册待办、运营连续性清单（发布密码异地备份 / 双远端核对）；docs/KNOWN-ISSUES.md 增设「悬置决策季度复查清单」（D-006 / D-049 / D-052 暂缓项）与「U1U 批量链 FrameRedefineGuard 前提风险」两条记录。",
                    "校准：文档与代码对齐——命令审计文档（COMMAND-AUDIT）移除易漂移的源码行号、补齐 3.0 行为关键词（D-042 / 044 / 045 / 047 / 048 / 050 / 051）；README 补 3.0.0.1–.17 补丁线摘要；CLAUDE.md 修正决策范围（D-052）与 UI 现状（全量 WPF）；DEVELOPMENT.md 记录 QuickLine3D 布局脚本用法。",
                    "修复（3.1.0.1）：启动动画每次闪烁——WebView2 网页动画就绪后从原生粒子动画手里「换手」，网页动画从 0 帧重播，每次启动都可见闪烁一下，WebView2 环境创建还让启动卡顿（2026-11-15 用户报告，2026-11-04 只修了晚换手、早换手照样闪）；现启动动画单路径化：原生粒子动画是唯一路径，不再创建 WebView2，启动更快更稳，点击/按键跳过直达关闭不再经浏览器层中转（此前换手后点击时灵时不灵）。",
                    "新增（3.1.0.1）：启动动画尾段淡入 3.1 版本标语「如切如磋，如琢如磨」（版本号下方居中展示）。"),
                new VersionChangeLogEntry("3.0.0", "2026-11-03",
                    "3.0 正式版（D-039 开发通道收官）。图框 JSON 存储重构（D-040，用户模板 FRAME_20261102）：JSON 改为图框块内 FRAMEINFO 文字属性，随图框复制/移动零丢失，U1F/U1U 自动把四代旧图框迁移到新模板，独立 frameinfo_json 块与旧内嵌属性降级为遗留读源；含幽灵机台全套修复（句柄绑定/身份仲裁/继承守卫）、UI 全量 WPF 迁移、FillFeature 拆分、性能与迁移双门禁、大批量进度窗与三色机台标签、安全加固。",
                    "改进：UI/UX 全面修缮（2026-11-03）——① 窗口自适应：所有窗体尺寸钳制到屏幕工作区 92% 并居中启动（小屏/低分屏不再被截断，SizeToContent 的窗不碰）；② WPF 统一设计令牌 FluentKit：11 个设置页与机台选择窗共用一套字号/间距/卡片样式（卡片内边距 18/16、下间距 12 统一，字段标签/说明文字/开关行一致），改观感只动一个文件；③ U1F 机台选择窗：相似建议改浮层下拉（不再把下方内容顶下去）、新增回路筛选框与计数（显示 X / N）、长文本省略号+悬浮看全文、列表与预览按 2:1 弹性随窗口拉伸；④ XSTS/XLAYOUT/批量/DWG/PDF 五个结果窗长文本列（路径/备注/原因/回路）省略号+悬浮提示；⑤ 修复清单与表格页网格缺行定义、统计页开关底距缺失等细节。",
                    "修复：XSTS/身份读取的幽灵机台——图框移动后遗留的 frameinfo_json 块会被几何归属错误认领（JSON 身份优先级最高，导致统计多出机台、更可能让 U1U 认错机台）；现写入时在 meta 打上图框句柄（frameHandle），读取时句柄不符即忽略外块并提示用户清理，老 JSON 无句柄保持兼容。",
                    "修复（3.0.0.1）：U1SET 页面控件样式退化——设置页宿主在 NavigationView 内容区，看不到窗口级合并的 WPF-UI 字典，开关/数字框/卡片退化成兜底模板（开关呈“蓝底按钮条”，开/关不可辨）；现设计令牌 FluentKit 内嵌 WPF-UI 字典随页合并，并补齐 Win11 风格开关模板（轨道+圆点，开蓝关灰），开关行改为“短标题 + 灰色说明”布局（统计汇总/清单与表格/Downstream 三页的长句说明移出行内）。",
                    "修复（3.0.0.2）：U1SET 整窗点不动——3.0.0.1 把页面挂到了 NavigationView 的 ContentOverlay，模板里它是铺满内容区、压在一切上方的浮层（Snackbar 语义），吃掉整个窗口的点击；现页面直接挂进模板的内容 Frame（PART_NavigationViewContentPresenter，正常内容区），页面切换、按钮、列表全部恢复可交互，并新增命中测试回归（按钮必须是命中顶层）防回潮。",
                    "新增：UI 全量迁移 WPF（Fluent 风格，D-038）——全部 21 个业务窗体完成迁移并删除 WinForms 旧版：批量确认/设备内预留/排版统计与选择/XSTS/Xmerge/机台选择/更新对比/清单评审/手动条目/目录选择×2/设置中心（11 页导航枢纽）/配置分享/在线授权/登录（WebView2 WPF 控件）/命令帮助/更新日志/关于/启动画面/3D 预览外壳；深浅色主题、卡片式布局、Enter 确认/Esc 取消；设计语言规范 docs/UI-3.0-DESIGN.md。",
                    "重构：FillFeature 上帝类 1425 行拆为 8 个阶段服务（求和/数据加载/机台选定/回路门禁/规划/评审/软管/电缆预留），降为 695 行；电缆预留纯逻辑下沉 Core 可独立单测；调用方与测试零改动；新增行数守卫测试防回潮。",
                    "新增：性能冒烟门禁——UNCAD_PERF_SMOKE 真机自检在 accoreconsole 生成大图（36 框+5136 实体）计时图框收集与统计求和（批量 U1U 同链），超预算即 FAIL；release.ps1 默认执行该门禁（-SkipPerf 可跳过）。",
                    "加固：登录凭证改为 Windows DPAPI 加密存储（旧明文首次读取自动升级，无感）；日志中的账户名统一脱敏（z***@域名）；在线登录页拦截一切新开窗口，防钓鱼跳转。",
                    "加固：分层守卫测试上线——Core 禁止依赖 AutoCAD/WinForms/WPF、Excel 读取(NPOI)限定 Core 层、UI 除 Ribbon 构造器外禁止依赖 AutoCAD、四代图框块名（frame/frame_20260812/xframe/xframe_v2）冻结为兼容契约。",
                    "新增（D-045）：XLAYOUT 排列方案三选一——按机台完整性（完整在前，默认）、按机台 ID 顺序、按上游编号（同一 U_上游编号 的回路排到一行，未配上游的归最后一行）；行内回路一律按机台 Excel 行顺序排列（快照不可用时退回图上位置）；选择窗新增排列方案下拉并记住上次选择；按上游模式行首标签显示上游编号。",
                    "变更（D-044）：U1F 缺回路与 U1U 同口径——整机回路校验未通过时图纸照常填写（机台ID/清单/块属性/预留），只跳过该机 BOQ，补全后执行 U1S 补清单；不再出现“图纸与 BOQ 均未修改”的空跑（U1S/U1DWG 保持硬门禁；U1SET 开关不变）。现场可以边画边填了。",
                    "新增（D-043）：设备内预留自动识别——U1F/U1U 在本次框选范围内识别现场老习惯画的横排毫米数标注（如设备旁手写的 2000），自动吸收为设备内预留并在写图时删除该标注（恰有一处才自动取值；发现多处不乱取，点名交人工确认，防把轴号/序号误当预留）；吸收值走既有链路（frameinfo_json 持久化 + CABLE_INFO 数值（设备内）+ 确认窗可改）；U1SET 清单页新增开关（默认开）。",
                    "改进：U1DWG / U1PDF 完成后弹结果窗——DWG 按机台列出图框数/回路/DWG 文件，PDF 每份一行（成功/失败/原因/文件），失败不再只给首个诊断；出图进度改进度窗，命令行只留一行摘要；U1SET 概览页改就绪仪表盘——机台数据源快照状态与输出目录状态两张实时卡（带跳转按钮）+ 现场四步工作流指引。",
                    "改进：XSTS / XLAYOUT 窗口顶部新增机台状态徽章行（完整/缺回路/多出/无基准 或 完整/警告/缺回路/未判断，各带台数与语义色），配下一步行动指引（缺的补画→重跑→U1U 出清单）；XSTS 命令行摘要同步输出机台分布，并修正把图框数写作“张图纸”的用词。",
                    "变更（D-042）：输出目录一个机台一个文件夹——BOQ / DWG / PDF 都写在 <输出根>\\<机台ID>\\ 里，不再按打开的图纸名或类别分层，U1SET 的两个布局开关移除；批量 U1U 结束后弹结果窗（每台一行：图框数/表格行/属性项/BOQ 状态与文件路径/备注，可一键打开输出目录），命令行只留一行摘要——逐步进度与逐个 BOQ 路径不再刷终端（进度在进度窗、明细在结果窗与日志）。",
                    "修复：XLAYOUT 与 XSTS 结论相反——回路齐全但有重复图框的机台，XLAYOUT 旧逻辑把“重复→黄”写在永远走不到的死分支里，实际标成红“缺回路”（XSTS 按去重集合比对显示不缺）；现红只留给“应有的回路本次没见到”，重复/多出归黄，统计窗新增“详情”列点名缺少/多出的回路（最多列 5 个），命令行与日志逐台输出原因；只排一部分回路时仍标红但列出没排的回路名。",
                    "修复：单框 U1U 变更对比失真——对比窗原来弹在清单确认窗之前，用户随后在确认窗里改设备内预留/电缆米数，先展示的米数变更就成了假数据；现改为确认窗之后、用最终清单行构建（批量 U1U 本就先编辑后对比，不受影响），取消确认窗时也不再白弹对比。",
                    "新增：网络机台数据源恢复（D-041）——U1SET/U1DATA 再次支持 HTTP/HTTPS 地址，只下载、零上传、100MB 上限、20 秒超时；下载或解析失败时保留上一次成功快照并给出中文原因；机台数据页重做（占位提示/主按钮刷新/数据源类型与快照状态分卡展示，每次刷新写日志）。绘图命令仍只读 SQLite 快照、永不联网；启动时也不自动联网（老版 D-023 的启动自动刷新不再恢复）。"),
                new VersionChangeLogEntry("2.10.0", "2026-11-01",
                    "新增：U1SET「输出与分享」支持配置分享码（Base64，可导出/预览/导入，敏感授权键永不分享）；「日志与记录」可查看最近操作记录（本地 JSONL，默认保留 6 个月），文件日志按天滚动、可配级别与保留天数。",
                    "新增：整机回路校验——U1F/U1U/U1S/U1DWG 生成 BOQ/DWG 前要求该机台所有回路都在本次选集中，缺回路即中止并列出缺少的回路，图纸与 BOQ 均不修改（U1SET 可关闭）；盘内预留（I-Line/插座盘/插接箱）可在 U1SET 分别调整。",
                    "变更：输出目录改为 <输出根>\\<图纸>\\BOQ|DWG|PDF\\<机台>，默认根为“我的文档\\UNCAD输出”，写盘前预检文件占用，PDF 按机台+回路命名并自动去重；UI 全面升级为统一组件库与商务风格，U1SET 重构为 11 页。",
                    "移除：全部云端上传（回路/等轴测/DWG/PDF/台账）、远程机台数据源（只支持本地/UNC 文件）与成图上传队列；账户验证与授权校验保留不变。",
                    "说明：批量 U1F/U1U 不再构建并上传等轴测场景，速度与内存显著改善；机台数据仍是 U1DATA/U1SET 刷新的本地 SQLite 快照，命令运行时不联网。"),
                new VersionChangeLogEntry("2.9.3", "2026-11-01",
                    "变更：CABLE_INFO 改为现场样式——全部 mm、明细统一写成「数值（说明）」（如 3400（盘内）+2000（设备内）+3000（预留）=总长mm）；预留规则按现场口径：I-Line 3400（盘内）、插座盘 1000（盘内）、插接箱 500（箱内），并新增业主统一预留 3000（预留），U1SET「清单与表格」可改。",
                    "说明：U1F/U1U 一致；清单与 BOQ 数量仍按米（总长含预留）；旧 CABLE_INFO 写法仍可读取，重跑 U1F/U1U 后改写为新样式；「电缆」前缀只用于图框显示，反向解析会剥掉，不影响清单匹配。"),
                new VersionChangeLogEntry("2.9.2", "2026-11-01",
                    "修复：U1F（新建填充）也支持电缆预留——盘内预留按上游类型（I-Line 3400 / 插座盘 1200 / 插接接口 500mm）、设备内预留从 frameinfo_json 复用或在清单确认窗填写；CABLE_INFO 与清单总长都计入预留，命令行汇总显示实测+盘内+设备=总长。",
                    "说明：2.9.1 只在 U1U 接入了预留，U1F 会漏掉；清单表表格本身的长度列仍是米（M）。批量 U1U 的设备内预留仍是一次 GUI 统一填写。"),
                new VersionChangeLogEntry("2.9.1", "2026-11-01",
                    "新增：图框模板升级为 xframe_v2（旧 frame_20260812/frame/xframe 自动迁移，移除新定义里没有的旧属性）；frameinfo_json 升级 v2（嵌套结构、changes 最多 20 条、整份上限 6000 字符、旧 v1 兼容读取）；U1U 电缆预留（盘内按上游类型 3400/1200/500mm + 设备内在清单确认窗填写，总长计入预留并写回 JSON）。",
                    "说明：CABLE_INFO 全部改为 mm 标注（含盘内/设备预留明细，多行属性自动换行）；批量 U1U 的设备内预留是统一一个值，只询问本批还没有记录的图框；迁移移除 BRIDGE_FRAME_INFO/CONDUIT_INFO 后图框不再显示旧文本（数据仍按统计重算并进清单/BOQ）；旧内嵌 FRAMEINFO_JSON 会在迁移前读出并迁移，不再丢失替代型号与历史。"),
                new VersionChangeLogEntry("2.9.0", "2026-10-29",
                    "新增：XLAYOUT 排版前的机台/回路选择窗——机台→回路两级树、两级复选框、关键词筛选（空格/逗号/分号/顿号分隔，AND 语义，机台ID+回路名匹配），只重排勾选且命中筛选的回路，其余图框保持原位。",
                    "说明：全选/全不选/反选只作用于当前筛选可见的回路；机台标签颜色与完成统计窗只反映本次重排的子集（相对台账应有回路仍会提示缺少回路）；不筛选直接确定与旧行为一致。"),
                new VersionChangeLogEntry("2.8.0", "2026-10-26",
                    "新增：生成的 BOQ 工作簿里增加 DATA 工作表——一行一个回路，记录电缆型号/长度、软管管径/长度、桥架信息/长度、线管信息/长度、上下游轴位、材料行数与更新时间，便于按回路追溯数据来源。",
                    "说明：DATA 表与 BOQ 同文件（[BOQ]机台ID.xlsx），不改模板、写入时自动创建；更新语义与 BOQ 设备列一致——按「机台ID+回路名」有则更新、无则追加，重复执行不会累积重复行；长度字段能解析为数字时写数值。"),
                new VersionChangeLogEntry("2.7.0", "2026-10-11",
                    "新增：Downstream 自动添加的**独立开关**（U1SET → Downstream 自动添加）。以前它借用「BOQ 自动填充类别」里的通用插座盘/电盘开关，做不到“只开 Downstream、不开上游”；现在两者完全解耦（新键 UNC_FILL_DOWNSTREAM_SOCKET_ENABLED / UNC_FILL_DOWNSTREAM_PANEL_ENABLED，默认开）。",
                    "新增：U1SET 配置中心重构为 10 页侧边栏（新增概览页；绘制标注合并 U1L/U1Q/U1C/U1R；启动、颜色、机台数据、清单与表格、Downstream、自动输出各自独立成页），并修复字段标签列被写死 190px 导致中文标签被裁切的布局问题。",
                    "说明：断路器规则不变——Downstream 永不添加断路器（只由上游 U_上游类型=I-Line盘 产生）；通用类别开关继续只控制上游规则。"),
                new VersionChangeLogEntry("2.6.9", "2026-10-11",
                    "新增：下游 Downstream 块状态为 I-line_Panel 时，自动追加一行电盘（默认 1000A，可在 U1SET 改）。仍然**不添加断路器**——断路器只由上游 U_上游类型=I-Line盘 产生。",
                    "说明：Downstream 三态物料规则现在是 socket box → 一行 4.x 插座箱/盘（默认 20A）；I-line_Panel → 一行电盘（默认 1000A）；busbar connector → 只上色。两行额定电流都在 U1SET「Excel 数据」里可改，分别受「插座盘」「电盘」自动填充开关控制。"),
                new VersionChangeLogEntry("2.6.8", "2026-10-11",
                    "修复：下游 Downstream 块状态为 socket box 时，那一行 4.x 插座盘的型号匹配改用固定额定电流（U1SET 新增“下游插座箱额定电流（A）”，默认 20A）。以前沿用 U_配电信息 的盘柜主开关电流（例如 100A）去匹配固定清单，匹配不到就整行变成未匹配（BOQ-CATALOG-MISSING）。",
                    "说明：只影响 Downstream 规则生成的那一行；上游 U_上游类型=插座盘 的规则不变；匹配不到时仍输出一行交给用户确认，不猜型号。永不添加断路器等既有规则不变。"),
                new VersionChangeLogEntry("2.6.7", "2026-10-11",
                    "新增：支持下游端 Downstream 块（精确块名，含 Xmerge 的 $0$ 后缀）。它与 upstream 共用同一套三态（I-line_Panel / socket box / busbar connector），按设备侧颜色上色。",
                    "说明：Downstream 状态为 socket box 时，在上游规则之外**额外加一套 4.x 插座箱/插座盘**（受“插座盘”自动填充开关控制，默认关闭）；不重复添加 8.x 插座行，**也永不添加断路器**——断路器只由上游 U_上游类型=I-Line盘 产生。",
                    "说明：同一图框内 Downstream 状态冲突会中止本次填充并提示，不会猜测；状态读不出来时不影响其它清单项。"),
                new VersionChangeLogEntry("2.6.6", "2026-10-11",
                    "新增：执行 U1U/U1F 时，该图框（= 该回路）的**等轴测（3D）场景**随清单一起自动上传；在官网项目页的「ISO 图」里可以直接在线查看，用的是与客户端 U1L3D 完全相同的渲染器（同样的 4800mm 轴距、上下游轴位、路由与桥架长度）。",
                    "说明：3D 与本地 U1L3D 是同一份场景数据，只是多了一条上传链路；图框内没有带毫米标注的 U1L 线段时不会有 3D 数据，只上传清单（属正常情况，命令行不报错）。",
                    "说明：场景构建失败或超过上限时只记日志、不影响清单上传与图纸，绝不因为 3D 让 U1U/U1F 变慢到不可用（构建耗时写日志，便于按需优化）。"),
                new VersionChangeLogEntry("2.6.5", "2026-10-11",
                    "加固 U1PDF 出图收尾：出图后先逐字段还原用户的打印设置，只有全部还原成功才提交；只要有一步没还原成功就整体回滚，绝不把临时的设备/纸张/旋转留给用户。",
                    "回滚只丢弃对布局的临时改动，不影响已经出好的 PDF 与自动上传。该行为的前提（不提交即回滚）已用真机探针 UNCAD_TXPROBE 验证。"),
                new VersionChangeLogEntry("2.6.4", "2026-10-11",
                    "修复：批量更新偶发的“文件不属于当前回滚批次”。计划的机台号与图纸里读出的机台号只差内部空白时，实际写出的文件没有进批次备份；现在这类文件会按需纳入备份与锁，回滚覆盖真正写过的每个文件。",
                    "修复：DWG 导出（U1DWG）不再把整个磁盘 IO 过程放在只读事务里；身份读取用短事务，导出阶段不持有任何 CAD 事务。",
                    "变更：BOQ 清单输出改到 CAD 提交之后执行（与开发规范一致）。提交后 BOQ 写入失败时图纸保持已更新，命令行与弹窗会明确提示重跑 U1S 重新导出清单。"),
                new VersionChangeLogEntry("2.6.3", "2026-10-11",
                    "新增：支持 Ruanguan_GW 等软管块变体——与 Ruanguan 是同一个软管对象，读长度、写型号/长度、U1F/U1U/U1C 的软管逻辑完全一致。",
                    "修复：两行标注被换序（长度在前）时长度漏进电缆合计、桥架/线管被静默丢弃；现在配对与顺序无关。",
                    "修复：无编码的既有插座行不再被当作已匹配——以前会写到 BOQ 阶段才整批中止，此时图纸表格已经改过。",
                    "修复：桥架尺寸带尾零（桥架200.0*100）不再导致整条命令中止；U1Q 不再写出统计读不出来的长度；Iline盘 等写法不再静默少生成电盘/断路器行。"),
                new VersionChangeLogEntry("2.6.2", "2026-10-11",
                    "修复批次输出回滚不幂等：批次已经正常收尾后再回滚，会拿已删除的备份去恢复输出文件，把完好的现场误报成“BOQ 文件回滚不完整”。现在回滚幂等，收尾后不再改动任何文件。",
                    "修复 BOQ 输出阶段没有模块标识：读取图框记录与写入 BOQ 工作簿两个阶段现在经 ModuleRunner 执行，日志新增 MODULE BOQ-XLSX/清单输出 的阶段与耗时；失败提示为“模块[BOQ-XLSX/清单输出]在[阶段]阶段失败”，可直接按模块定位。",
                    "修复 BOQ 工程量为负数等异常提示为英文的问题，统一改为中文并说明下一步动作。",
                    "修复安装/加载横幅命令清单漂移：U1L/U1R/U1Q 系列现在会把各自的兼容旧命令名（UNL/UNR/UNQ1/UNQ2/UNQ4）一并列出。",
                    "修复 U1DT/U1L3D/U13D 空选择时静默退出：现在会明确提示“未选择…，未执行”。"),
                new VersionChangeLogEntry("2.6.1", "2026-10-11",
                    "修复命令行被重复提示刷屏：同一条通知被反复写入命令行（通知取走后未清空，而 AutoCAD 的 Idle 事件会持续触发）。现在一条通知只提示一次。",
                    "修复 U1PDF 出图失败（eInvalidInput）：改为自研出图内核，按图框窗口直接输出 A3 彩色 PDF，不依赖任何第三方批量打印插件，不会接管 AutoCAD 界面。",
                    "U1PDF 改进：显式指定规范介质并自动匹配图框横竖方向、出图后把用户“页面设置”原样还原；出图产物先校验 %PDF-/%%EOF 才上传，失败时给出逐步诊断。",
                    "授权提示仍在命令行出现、不弹模态窗口，不影响 AutoCAD 使用；需要登录时运行 UNC_LOGIN。"),
            new VersionChangeLogEntry("2.6.0", "2026-10-11",
                    "新增：U1PDF 命令——框选图框后按**回路**（一个图框一份）输出彩色 PDF，并自动上传到服务端，可在回路详情页在线预览。",
                    "说明：PDF 与 DWG 的粒度不同——DWG 按机台（一份覆盖多个回路），PDF 按回路（一份对应一个图框）。",
                    "改进：U1PDF 出图使用 AutoCAD 自带“DWG To PDF.pc3”，保留图层颜色（不套用单色打印样式），批量出图时命令行逐份显示进度。",
                    "修复：PDF 上传失败或未登录时只在命令行提示，不影响出图结果与图纸本身，也不会弹窗挡住 AutoCAD。"),
                new VersionChangeLogEntry("2.5.1", "2026-10-11",
                    "修复：授权异常时**不再弹出模态窗口挡住 AutoCAD**——启动与周期心跳只在命令行提示。",
                    "修复：登录提示不再被永久去重。旧版弹过一次后登录窗口再也不会出现，用户卡在只剩一句错误的状态。",
                    "修复：被停用的命令会明确提示运行 UNC_LOGIN 登录或注册，并主动唤起登录窗口；每次操作都可重新唤起。",
                    "改进：提示文案明确说明授权问题只停用 UNCAD 命令，不影响 AutoCAD 本身的绘图与保存。"),
                new VersionChangeLogEntry("2.5.0", "2026-10-10",
                    "新增：授权改为账号制，并用 WebView2 登录一次后自动续期，不再使用授权码。",
                    "新增：同一账号同一时刻只允许一台设备在线；新设备登录会顶掉上一台并立即停用其命令。",
                    "新增：执行 U1U/U1F/U1DWG 时自动上传该回路清单与 DWG 到服务端，以回路（一个图框）为最小单位。",
                    "变更：取消离线宽限，授权必须联网验证；无法连接授权服务器时命令立即停用并给出中文原因。",
                    "优化：U1L3D/U13D 多图框预览使用合并轴网，并修正下游节点未对齐工作簿轴位的问题。"),
                new VersionChangeLogEntry("2.4.13", "2026-09-29",
                    "新增：U13D 可框选多个完整图框，按工作簿下游轴位和 4800mm 轴距统一对齐。",
                    "新增：只读 3D 预览展示全部 U1L 路由、电缆米数、桥架米数和每个图框的轴位偏移。",
                    "优化：U1Q 桥架带有源线元数据时可绑定到对应路线；旧标注无法唯一判断时显示未关联，不再猜测。",
                    "修复：BOQ 清单长描述不再撑大整张表；多行特征描述采用紧凑行距并保持固定数据行高。"),
                new VersionChangeLogEntry("2.4.12", "2026-09-28",
                    "新增：U1C20、U1C25、U1C38、U1C51 可直接按固定 BOQ 型号生成线管标注；U1C 继续使用 U1SET 默认规格。",
                    "优化：同一图框内的 20、25、38、51 mm 刚性线管按管径分别求和并写入对应 BOQ 行。",
                    "修复：线管多行 MTEXT 未精确配对时，裸长度不再误算进电缆求和；旧式两行线管标注仍可正确识别。",
                    "优化：更新记录按版本显示独立详情页；升级弹窗仅列出未读版本，U1A 可随时查看全部历史版本。"),
                new VersionChangeLogEntry("2.4.11", "2026-09-27",
                    "修复：版本更新记录窗口的正文容器不再被 WinForms 布局为零宽度，更新内容可正常显示、换行和滚动。",
                    "新增：U1L3D 可框选完整图框，将 U1L 等测草图重建为只读 3D 预览，并按机台上下游快照显示 4.8 米轴网。",
                    "修复：U1U 按文字锚点排他归属尺寸标注，异常尺寸外包范围不再把其他图框的电缆长度重复计入当前机台。",
                    "修复：BOQ 表格写入清除内容级自动缩放并统一生成行高度，降低 AutoCAD 原生表格访问冲突和崩溃风险。",
                    "修复：Xmerge 使用重命名隔离同名块后，不再因动态块重生成产生的外包范围变化而误判为不兼容。",
                    "优化：机台选择与复核窗口按可用工作区自适应布局，小屏、高 DPI 和多显示器下输入区保持完整。",
                    "修复：正式安装包包含 U1L3D 的本地 Web 资源，安装验证不再提示缺少 Web\\QuickLine3D\\index.html。"),
                new VersionChangeLogEntry("2.4.10", "2026-09-27",
                    "新增：U1L3D 可框选完整图框，将 U1L 等测草图重建为只读 3D 预览，并按机台上下游快照显示 4.8 米轴网。",
                    "修复：U1U 按文字锚点排他归属尺寸标注，异常尺寸外包范围不再把其他图框的电缆长度重复计入当前机台。",
                    "修复：BOQ 表格写入不再切换 AutoCAD 表格延迟重生成，清除内容级自动缩放并统一生成行高度，降低访问冲突崩溃风险。",
                    "修复：Xmerge 使用重命名隔离同名块后，不再因动态块重生成产生的外包范围变化而误判为不兼容。",
                    "优化：机台选择与复核窗口按可用工作区自适应布局，小屏、高 DPI 和多显示器下输入区保持完整。",
                    "修复：正式安装包包含 U1L3D 的本地 Web 资源，安装验证不再提示缺少 Web\\QuickLine3D\\index.html。"),
                new VersionChangeLogEntry("2.4.9", "2026-09-20",
                    "优化：机台 ID 与设备名称匹配忽略大小写、Unicode 空白及文本内部空格，M Q-01 与 MQ-01、设备 A 与设备A 可正确对应。",
                    "修复：统一图框、Excel 快照、XSTS、XLAYOUT、DWG 导出、U1S/自动提交和 BOQ 分组的身份匹配规则，同时保留用户原始显示文本。",
                    "修复：图框属性中的机台/设备身份与设备块属性不一致时不再静默采用设备块名称，改为停止并提示不一致，避免错误填充。"),
                new VersionChangeLogEntry("2.4.8", "2026-09-18",
                    "修复：桥架 BOQ 两行标注可识别 MTEXT 格式码及常见空白差异，长度不再误计为电缆，并恢复正常求和。",
                    "修复：两行标注严格按固定清单型号与纯长度配对，孤立长度或带前缀文本不会误加入桥架清单。",
                    "修复：U1Q 在大坐标、多角度斜线批量标注时改用稳定的直线端点计算，避免部分线段因 eInvalidInput 无法生成。",
                    "新增：统一机台表在前 51 个物理行内自动识别表头，兼容第二行表头；固定清单生成脚本同步支持。",
                    "修复：Windows 150%/200% 缩放及多显示器切换时，插件窗口按当前屏幕 DPI 和工作区自适应，不再超出屏幕。",
                    "维护：移除未使用的 JSON 依赖，减少宿主加载冲突并缩小发行包。"),
                new VersionChangeLogEntry("2.4.7", "2026-09-11",
                    "新增：U1SET 的 BOQ 自动填充新增独立“电盘”开关；电盘、断路器与插座盘分别控制，默认开启电盘与断路器。",
                    "修复：I-Line盘按固定清单分别生成电盘和断路器，不再用断路器开关代替电盘。",
                    "修复：长两行线管 MTEXT 按文字插入或对齐点归属图框，U1U 可重新识别并自动更新线管数量。",
                    "修复：U1F/U1U 自动把旧桥架标注升级为 BOQ 两行样式，并保留或修复标注角度。",
                    "优化：U1SET、在线授权与关于窗口采用更清晰的现代 WinForms 布局。",
                    "修正：构建时间统一按 UTC+8 显示。"),
                new VersionChangeLogEntry("2.4.6", "2026-09-11",
                    "新增：U1Q*/U1C 标注改为两行 MTEXT：第一行写固定清单「1.名称」的完整型号，第二行写长度；对齐仍为中下，锚点与原单行 DBText 同点。",
                    "新增：统计引擎按 MTEXT 为单位把两行配对还原成单行写法（桥架200*100 2500mm / ⌀20线管 2000mm）；配对严格——第一行必须是清单里的桥架/线管型号、第二行必须是纯长度，孤立的 2000mm 与老单行标注照旧按电缆统计。",
                    "新增：重跑 U1Q*/U1C 时自动把作用范围内的旧单行标注替换成两行；不重跑就不动。",
                    "修复：两行 MTEXT 行距固定为 0.75 倍字高（原默认 1.66 倍，两行显得太散）。",
                    "修复：U1U 更新时，规划行与旧行仅凭名称（如都叫“电缆”）匹配时不再继承旧行的清单编码与匹配标记，避免错编码进 CAD 表格并导出到 BOQ。",
                    "修复：桥架旧格数写法迁移成毫米时，若结果不以 0 结尾（如 12.5格×250=3125mm）则不迁移，保留旧格数写法——统计照样认，避免下一次 U1F/U1U 静默丢掉这条桥架。",
                    "修复：固定清单 8.11「插座漏电相序检测仪」名字含“插座”却被当成插座行，在清单确认后被策略静默删掉；有固定编码的行现在由编码判定是否插座。",
                    "修正：README 命令表把 U1C 从“绘制软管”改为“按管径绘制线管标注”。"),
                new VersionChangeLogEntry("2.4.5", "2026-09-09",
                    "新增：U1F/U1U/UNADD 统计支持识别对齐/转角标注中人工输入的文字（如 2000mm）；自动测量的标注不参与统计。",
                    "新增：U1SET 统计汇总提供“尺寸标注文字 (DIMENSION)”独立开关，默认开启。",
                    "新增：对齐标注转文字（当前命令 U1DT），将标注文字转换为可编辑单行文字并保留原尺寸线。",
                    "修复：150% 及以上屏幕缩放下 U1SET 等窗口输入框和页面显示不全的问题。",
                    "优化：U1Q/U1LX 标注重跑不再重复生成，跳过原因逐项提示；Xmerge 保持块样式并增强批量导入。"),
                new VersionChangeLogEntry("2.4.4", "2026-09-07",
                    "修复：U1F/U1U/U1S 批量流程统一使用固定清单项目编码，避免 CAD 与 BOQ 对账不一致。",
                    "修复：替代型号、图框身份和变更记录写入 frameinfo_json，后续 U1U 会复用已确认的数据。",
                    "修复：强化旧图框/xframe 识别、图框迁移、Xlayout 身份读取和 XSTS 统计异常报告。",
                    "修复：Xmerge 合并过程的块定义冲突与批量回滚问题。"),
                new VersionChangeLogEntry("2.4.3", "2026-09-06",
                    "新增：AutoCAD 启动后显示全屏 UNCAD 品牌动画，由 .NET 在 5 秒后自动关闭，可在 U1SET 中关闭。",
                    "修复 U1LX：从末端线任一端点击都能遍历完整连通路线。",
                    "U1LX 将末尾为 00mm 的标注继续视为可修改占位符，便于重新执行后纠正误填值。"),
                new VersionChangeLogEntry("2.4.2", "2026-09-06",
                    "性能优化：U1U、XLAYOUT、XSTS、U1S、U1DWG、Xmerge 批量读取共用一个 CAD 事务和块定义缓存，大幅减少大图批量操作的等待时间。",
                    "图框识别支持旧版 frame 块（frame_20260812/frame/xframe 均可）。",
                    "U1LX 快速标注在密集图纸上的标签匹配更快。",
                    "新增：更新到新版本后首次打开 CAD 会显示本更新日志。"),
                new VersionChangeLogEntry("2.4.1", "2026-09-05",
                    "机台数据改为每用户 SQLite 快照：U1SET 刷新是唯一解析入口，命令执行时不再重复加载整本 Excel。",
                    "U1F、U1U、XSTS、XLAYOUT 按需查询快照，不在业务命令中读取源文件。",
                    "刷新过程采用事务校验，失败时保留上一份有效快照。"),
                new VersionChangeLogEntry("2.4.0", "2026-09-05",
                    "强化 U1F/U1U 的 CAD 与 BOQ 回滚流程，失败时不再覆盖用户并发保存的新版工作簿。",
                    "电缆与桥架按编码和名称互斥分类；重复 BOQ 行按出现次数一对一匹配。",
                    "Ruanguan、设备、上游、图框及 frameinfo_json 支持 Xmerge 重命名后的块名。",
                    "改进在线授权离线宽限、网络工作簿缓存、Ribbon 重建和发行载荷校验。")
            };

        /// <summary>Returns notes for every version newer than the given version label.</summary>
        public static List<VersionChangeLogEntry> EntriesNewerThan(string seenVersion)
        {
            Version seen = ParseOrNull(seenVersion);
            bool seenIsDev = IsDevLabel(seenVersion);
            IEnumerable<VersionChangeLogEntry> entries = Entries;
            if (seen != null)
                entries = entries.Where(entry =>
                {
                    // D-039：开发版条目只给开发版用户看；正式版客户的更新提示
                    // 只出现正式版本号（3.0.0 正式切出时条目名去掉 -dev）。
                    if (IsDevLabel(entry.Version) && !seenIsDev) return false;
                    Version parsed = ParseOrNull(entry.Version);
                    return parsed == null || parsed > seen;
                });
            else
                entries = entries.Take(1); // First install: only show the latest notes.
            return entries.ToList();
        }

        private static bool IsDevLabel(string value)
            => (value ?? "").IndexOf("-dev", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Version ParseOrNull(string value)
        {
            // 开发版通道（D-039）：3.0.0-dev 按 3.0.0 参与比较。
            string text = (value ?? "").Trim();
            int dash = text.IndexOf('-');
            if (dash >= 0) text = text.Substring(0, dash);
            Version parsed;
            return Version.TryParse(text, out parsed) ? parsed : null;
        }
    }
}
