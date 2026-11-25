# Release 2.4.2

## 批量性能

- U1U、XLAYOUT、XSTS、U1S、U1DWG、Xmerge 的多图框身份读取共用一个 CAD 读取事务，
  块定义与属性默认值按事务缓存（`CadBlockDefinitionReader`），不再每个图框重复扫描同一块定义。
- 批量 U1U 的配置/统计设置只读取一次；Ruanguan 长度与既有 BOQ 电缆型号在同一读取事务内处理；
  整批预检、单事务写入与失败回滚保持不变。
- U1LX 标签匹配改为按线序字典查找，密集图纸上的扫描更快。
- 图框识别支持旧版 `frame` 块（`frame_20260812`/`frame`/`xframe`），边框支持闭合多段线表示。

## 版本更新日志

- 新增：更新到新版本后首次打开 CAD 自动弹窗显示更新内容（`VersionChangeLog` +
  `UpdateNotesForm`），已展示版本记录在用户设置 `UNC_UPDATE_NOTES_SEEN_VERSION`；
  首次安装只显示最新版本的说明。`U1A` 关于窗仍可查看完整版本信息。

## 测试基建

- `UNCAD_PERFORMANCE_SELFTEST` 基准命令在复制的临时 DWG 上运行，不再修改用户原始图纸；
  集成测试程序集不进入发行包。

## 发布校验

- 统一版本：`2.4.2` / `2.4.2.0`。
- 使用新的版本专属 `ProductCode`，保持原 `UpgradeCode`。
- 仅生成 `UNCAD Pro` 在线授权包。
