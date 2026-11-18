# UNCAD Pro 3.4.0

发布日期：2026-11-18

## 变更（重构：3D 预览内核换 WPF 原生 Viewport3D）

影响命令：`U1U`（清单确认窗 3D 路由预览）、`U13D`（多图框三维预览）。

- 用户指令「算了 用 Viewport3D吧」——WebView2+three.js 内核（3.3.5~3.3.8）
  在本机被 WebView2 用户数据目录锁同步阻塞 STA 线程，「正在启动三维渲染
  器」白屏反复复发（2026-11-18 日志：10:32:35 控制器创建 30s 超时降级、
  15:02 预热窗 123ms 已建仍复白）。浏览器内核的外部进程/目录锁/airspace
  白窗/随窗退场四样病根无法根治，整条路退役。
- U1U/U13D 渲染内核换 **WPF 原生 Viewport3D**（.NET Framework 自带，零第
  三方包）：SetModel/构造 **同步** 出场景，不存在任何「启动中」中间态；
  参照建筑模型（楼板/环氧/华夫圆孔板/工字钢/葡萄架/设备/母线玻璃/结构柱/
  1F 地面）视觉公式与 3.3.7~3.3.8 页面逐条对齐，本层重定位与包络推算保留；
  U13D 正交相机/轴位网格/罗盘/回路·桥架·图框·诊断面板全保留。
- 退役并删除：QuickLineWebPreviewControl、OrbitPrewarmHost（预热+孤儿清
  扫）、Web 页面树（three.js vendor 10 文件）；payload allow-list 26→16 项；
  csproj 去 System.Management；Bootstrap 预热挂钩摘除。
- **登录页 WebView2 不动**（账户/许可校验，D-025 唯一网络功能）。
- 明确不做：不引入 HelixToolkit 或任何第三方 3D 库（3.3.5 条款仍守）；
  3D 预览仍然只读，不写 CAD 实体、不刷新工作簿、不访问网络。

## 验证

- dotnet test UNCAD.slnx：全部通过。
