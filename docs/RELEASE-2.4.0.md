# Release 2.4.0

## 稳定性修复

- 强化 U1F/U1U 的 CAD 与 BOQ 回滚流程，失败时不再覆盖用户并发保存的新版工作簿。
- 电缆与桥架按编码和名称互斥分类；重复 BOQ 行按出现次数一对一匹配。
- Ruanguan、设备、上游、图框及 `frameinfo_json` 支持 Xmerge 重命名后的块名。
- 改进在线授权离线宽限、网络工作簿缓存、Ribbon 重建和发行载荷校验。

## 发布校验

- 统一版本：`2.4.0` / `2.4.0.0`。
- 使用新的版本专属 `ProductCode`，保持原 `UpgradeCode`。
- 仅生成 `UNCAD Pro` 在线授权包。
- 测试与打包命令见 `docs/RELEASE.md`。
