# UNCAD Pro 3.3.2

发布日期：2026-11-17

## 更换

U1U 3D 预览内核切换为 HelixToolkit.Wpf（D-054 Amendment，feature 版）。
用户看了 3.3.1 的 Canvas 正交投影预览后两条现场反馈：

- 「轴位不应该这样显示吧」——3.3.1 沿重建路由的 X 单向插值标数字轴；
  实际柱位是两向正交柱网（数字轴×字母轴），且场景 X 是重建抽象轴，
  与现实柱网方向无固定对应。
- 「我旋转的时候明显感觉这不对 这不像是3D的 像是一个伪3D的」——
  正交投影无透视无线索。

主会话列完整选型表（HelixToolkit.Wpf / WPF 内置+3DTools / Canvas 透视
自绘 / WebView2+three.js）逐项与用户确认，拍板 **HelixToolkit.Wpf**
（真 WPF 3D 透视相机 + 成熟轨道交互，无浏览器进程/无 JS 边界；WebView2
方案作废）。

### 新预览（QuickLineHelixPreviewControl）

- 透视相机 + 内置轨道交互：左键拖动=旋转、滚轮=缩放、右键拖动=平移、
  双击=复位；米数标与轴位标签用 BillboardTextVisual3D（面向相机）。
- 路由线用 LinesVisual3D（等宽屏空线），轴向配色沿用 3.3.1
  （X 橙 / Y 绿 / Z 蓝）；桥架=红色双线+浅红半透明填充带（宽度/2 偏移，
  世界空间下限保远距可见）、线管=青色细双线、宽度解析不出按 200 缺省
  （图例注明）——归属、通道语义、200 缺省全部沿用 3.3.1。
- **两向柱网（修正轴位展示）**：地面 Z=0 数字轴×字母轴两向正交网格
  （4800mm 柱距，覆盖包络，可开关）。数字轴向=端点平面位移较大的那向
  （不硬编码数字=X）；两端数字可解析时插值标数（越界/重号跳过）。字母轴
  垂直于数字轴：两端同字母→锚定按柱距递推 U/V/W…；异字母→插值夹在两端
  之间；位置重合或解析失败→该向不标（不猜）。两端端点钉台账轴位原文；
  下游端点平手改取 max Y（3.3.1 的 min Y 让纯 Y 向路由两端钉叠在一起）。
- 窗口对半分 + GridSplitter、null=U1F 回退、批量窗点行切换场景等
  3.3.1 布局行为全部保留。

### 退役

Canvas 内核退役：QuickLineOrbitPreviewControl、QuickLineOrbitCamera
（正交投影数学）、QuickLineOrbitLabelPolicy（投影过短隐藏——透视 3D 下
Billboard 恒面向相机，无此概念）。数据模型与 BuildPreview 输入链不动。

### 发布链

- bundle 文件清单 24 → 25（新增 HelixToolkit.Wpf.dll；build.ps1 /
  installer.ps1 payload 允许清单与 BundleLoadingTests 契约同步，
  checksums.sha256 随 build.ps1 重算）。
- HelixToolkit.Wpf 2.27.0（NuGet 精确锁定；MIT；nuspec 无任何传递依赖，
  实测仅多 HelixToolkit.Wpf.dll 本体）。

## 验证

- dotnet test UNCAD.slnx：全部通过（1549/1549，基线 1547 只增不减：
  退役 5 条——正交相机投影数学 4 条+标签显隐阈值 1 条；新增 7 条——两向
  柱网与字母轴、端点平手取 max-Y、递推越界跳过、非法柱距、视口惰性创建。）
- 真机自检（accoreconsole）：UNCAD_MIGRATION_SELFTEST PASS；
  UNCAD_ROUTE_INSPECT_SELFTEST DONE（EntityIds=0 根因断言保留）。
