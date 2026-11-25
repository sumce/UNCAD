# 历史发布说明（1.x / 2.x）

这里存放 1.x 与 2.x 发布线的归档说明，只读，不再维护。

- 现行发布线的说明（3.x）留在 `docs/RELEASE-X.Y.Z.md`：
  `release.ps1`、`BundleLoadingTests` 与 `.github/workflows/release.yml` 按
  `docs\RELEASE-<版本>.md` 查找当前版本的说明，移动会破坏发布门禁。
- 新版本说明的写法见 `docs/RELEASE-RUNBOOK.md`。
- D-032：只有功能版本（X.Y.Z）新建说明文件；补丁（第四位）复用当前版本的说明。
  早期 1.9.7.x 的逐补丁说明是这条规则确立前的遗留，仅作归档保留。

移动这里的文件或目录前，先确认没有脚本按路径读取它们。
