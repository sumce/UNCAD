# UNCAD v1.9.7.15 CAD 动态状态驱动插座与上游盘型

- `Device_Build20260716` 的动态可见性成为插座清单的唯一存在性来源。
- 状态 `插座5孔` 或 `插座3孔` 输出插座；状态 `设备` 或未找到目标块时不输出。
- 插座型号继续由 DETAIL 最后一个 `xPyA` 的电流选择：10–16A 为 8.2，20–30A 为 8.3。
- `upstream` 动态块按 NEXT 自动同步：I-Line盘/Iline盘 -> I-line_Panel，插座盘 -> socket box，母线插接口 -> busbar connector。
- 单框 U1F/U1U 和批量 U1U 共用规则，并在同图框 Device 状态冲突时停止更新。
- 当前 AutoCAD 图纸已只读确认有效块名、当前动态状态及允许值；调试未修改图纸。
