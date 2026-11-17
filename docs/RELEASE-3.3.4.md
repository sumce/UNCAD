# UNCAD Pro 3.3.4

发布日期：2026-11-18

## 定位

图层收口版（feature 版，D-054 Amendment 3.3.4）：全命令审计（37 条命令四路
只读审计）对三个新功能灰区按用户口径「主要针对新功能/图层问题」定案收口。
本版无新命令；行为补全 + 已知限制登记。

## 修复与补全

**① U1B 软管矫正通道打通（D-054）**

3.3.3 的 `U_HOSE` 法定图层与「软管 h」计数在旧接线下不可达：软管三件套
（轮廓/引线/文字）`SOURCE`=轮廓自引用，而轮廓被扫描器按元数据命中跳过、
永不进宿主池——按宿主判框必然落空，`HoseEntities` 恒 0，用户手动挪层（或
建层失败回退当前层）后 U1U/U1F 永不纠正软管。

- 归属消费端 `FilterOutputsByHostFrame` 给 `KIND=U1B` 留「宿主落空→按图框
  边界几何直通」通道：几何范围中心在界内即属本框（打不开/已删/空几何=不属，
  不猜）；其余 KIND 宿主未知仍跳过——D-054 不猜测条款不变。
- 批量 U1U 图层汇总补软管列：计数加 `HoseEntities`（3.3.3 只改了单框侧，
  整批仅软管待矫正时底部报告行会整体消失）、收尾文案补 `U_HOSE`、无法判定
  口径改「非 U1Q/U1C/U1B」。

**② U1F 图框块选集带上图框边界**

严格分区 EntityIds 从来不含电缆线，U1F 选一个图框块时路由体检按 EntityIds
包含判归属、对图框内容整体空转（连通性无段、图层矫正无目标）。修复挂在
选集展开路径（`ExpandSingleFrame` 展开时顺带设 `selection.Region`，编排器
零增长）：U1F 图框块选集的连通性报告/图层矫正/（U1U 语义的）路由体检自此
生效。机会式——分区失败/空分区/部分显式选集回退既有语义；U1U 行为不变。

**③ 已知限制登记（KNOWN-ISSUES）**

- 元数据时代（D-018/D-050）之前的 U1Q/U1C 输出：无法在不猜色/猜文字的前提
  下与手画电缆线区分（D-054 禁止）。这类图纸上的旧偏移线会被当电缆段移进
  `U_CABLE`（色不变）、参与连通性点名；重跑 U1Q/U1C 无法回收旧输出——请
  手动清理。登记为已知限制，不做猜测性识别。
- 无机台快照时 U1S/U1DWG 的整机回路门禁静默跳过（无法判定应画回路集）；
  配置 `U1DATA` 刷新一次即启用门禁。

**顺手修（审计 P3 新功能项）**

- U1S/U1DWG 帮助文案图框世代补 `FRAME_20261102`。
- U1B 软管直径下界收紧到 1（旧判只拒 ≤0，与「应在 1–1000」文案不符）。
- U1B 取点注释对齐实际 ESC 行为（零实体终止，非「返回已取点」）。
- U1L3D 单框场景构建失败改为礼貌退出文案（对齐 U13D/U1U 预览降级口径）。
- README 命令一览表补齐 9 条（U1L3D/U13D/Xauto/U1PDF/XLAYOUT/XSTS/Xmerge/
  U1HELP/UNC_LOGIN）。

## 锁死与验证

- 契约 +3：`HoseOutputs_ReachLayerGovernorViaBoundary` /
  `BatchLayerSummary_CountsHoseAndMentionsAllLayers` /
  `U1F_SingleFrameSelection_GetsFrameBoundary`。
- `UNCAD_ROUTE_INSPECT_SELFTEST` 扩 U1B 断言：框内软管替身（KIND=U1B、
  SOURCE=自引用）必须 `HoseEntities=1` 且进 `U_HOSE` 移层计划；三段电缆链/
  预览/连通性断言原样通过（软管替身被扫描器跳过，不搅扰电缆链）。
- 全量 1570/1570（基线 1567）。
- 真机：`UNCAD_ROUTE_INSPECT_SELFTEST` PASS + `UNCAD_MARKER_SELFTEST` /
  `UNCAD_HOSE_SELFTEST` / `UNCAD_UNR_SELFTEST` 回归 PASS。

## 上一版速览

- 3.3.3：新命令 `U1B` 绘制软管 + `U_HOSE` 法定图层（见 `RELEASE-3.3.3.md`）。
- 3.3.3.1/3.3.3.2：横排毫米标识别修复 / 全命令审计批次四项修复。
