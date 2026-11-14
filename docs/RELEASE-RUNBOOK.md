# RELEASE Runbook（发布运维手册）

面向 UNCAD Pro 的正式/补丁发布。事实来源：`release.ps1`、`build.ps1`、
`installer.ps1`、`tests/UNCAD.CadIntegration/run.ps1`、`scripts/Bump-Version.ps1`、
`tools/publish-release.ps1`（以脚本为准，本手册只做操作化）。

## 1. 前置条件

| 条件 | 说明 |
| --- | --- |
| AutoCAD 2022 本机安装 | 真机门禁走 `accoreconsole.exe`。默认路径 `D:\Program Files\Autodesk\AutoCAD 2022`，不同机器用 `run.ps1 -AutoCADDir` 覆盖（`release.ps1` 不透传，必要时先单独跑 `run.ps1` 验证） |
| dotnet SDK | 能构建 `UNCAD.slnx`（net48 / x64），并运行 `dotnet test` |
| 干净工作树 | `git status --short` 无未预期改动；正在发布的提交已打 tag（正式版门禁要求） |
| 服务端管理员密码 | 仅上传发布用（`tools/publish-release.ps1`），打包本身不需要 |

## 2. 常规发布步骤

```powershell
git status --short          # 确认干净
.\scripts\Bump-Version.ps1 -Kind Patch     # 或 -Kind Feature -Label X.Y.Z（见第 7 节）
dotnet test UNCAD.slnx --no-restore        # 先跑一遍全量，尽早暴露
.\release.ps1 -NoRestore                   # 门禁全跑，出包
```

`release.ps1` 内部门禁按以下顺序执行，任何一步失败即终止：

| 顺序 | 门禁 | 内容与产物 |
| --- | --- | --- |
| 1 | 构建完整性 | `build.ps1` clean+build，同步 bundle（DLL、WebView2 loader、Web、BOQ 模板、XFrameTemplate.dwg），生成 `bundle\UNCAD.bundle\checksums.sha256`；随后校验 bundle 与构建输出逐文件 SHA256 一致（UNCAD.dll、XFrameTemplate.dwg、全部 `*.dll`） |
| 2 | 全量单测 | `dotnet test UNCAD.slnx -c Release --no-build` |
| 3 | PERF_SMOKE | `run.ps1 -Command UNCAD_PERF_SMOKE`（36 图框 + 3600 线大图上计时两条 U1U 热点读路径；预算默认 30s） |
| 4 | MIGRATION_SELFTEST | `run.ps1 -Command UNCAD_MIGRATION_SELFTEST`（四代图框迁移链断言：身份不丢、表格行列不改、旧属性移除等） |
| 5 | 包校验 | `installer.ps1 -Mode VerifyPackage`，对 bundle 与 staging 包各跑一次（payload 清单、checksums、命令清单、版本一致、LicenseExpiresUtc 必须为空） |
| 6 | 版本一致性 | `PackageContents.xml AppVersion` 与 `ProductMetadata.VersionLabel` 前三段一致（D-032）；Name=`UNCAD Pro`、LicenseMode=Online；正式版另需 `docs\RELEASE-<X.Y.Z>.md` 非空 + tag `v<X.Y.Z>` 存在且为 HEAD 祖先 |
| 7 | 打包 | 压缩为 `artifacts\Pro\UNCAD-Pro-v<版本>[-Suffix].zip`，打印 SHA-256；包内含 bundle 与 8 个分发文件（setup.bat、installer.ps1、install-/uninstall-/verify-install 脚本、README.md） |

## 3. release.ps1 参数速查

| 参数 | 语义 | 约束 |
| --- | --- | --- |
| `-Suffix <string>` | 追加到包名后缀（默认空） | 仅命名，不影响门禁 |
| `-Configuration <string>` | 默认 `Release` | 仅允许 Release，其他值直接抛错 |
| `-NoBuild` | 跳过构建，复用现有输出 | 任何源文件/脚本/manifest/docs.html/bundle 内容比 `UNCAD.dll` 新即拒绝（防旧产物出包）；bundle 与输出不一致也会拒绝 |
| `-NoRestore` | build/test 加 `--no-restore` | 需先还原过包 |
| `-SkipPerf` | 跳过 PERF_SMOKE 门禁 | 仅紧急发布；**须在发布记录中注明原因** |
| `-SkipMigration` | 跳过 MIGRATION_SELFTEST 门禁 | 仅紧急发布；**须在发布记录中注明原因** |
| `-Dev` | D-039 开发版通道：跳过"发布页 + git tag"门禁，其余门禁照常 | 仅 `VersionLabel` 为 `X.Y.Z-dev` 时可用；正式版携带 `-Dev` 直接抛错 |

`run.ps1` 侧还有 `-AutoCADDir` / `-InputDrawing` / `-Command`（默认 `UNCAD_COLOR_SELFTEST`；perf/migration 门禁的输入图纸取自 Release 构建输出的 `Resources\XFrameTemplate.dwg`）。

## 4. 门禁失败处置

| 线索 | 在哪读 |
| --- | --- |
| `%UNCAD_CAD_INTEGRATION_RESULT%` | `run.ps1` 设置的"结果文件路径"，夹具命令把 `PASS`（或 `FAIL+明细`）写入该文件；脚本要求内容恰为 `PASS`。结束后变量恢复原值、结果文件删除——排查靠下面的保留现场 |
| 保留的控制台日志 | 失败时 `run.ps1` 把 accoreconsole 完整控制台日志复制到 `tmp\<Command>-<runId>.cadlog.txt`，并在控制台打印"结果文件内容：[…]" |
| `%UNCAD_MIGRATION_SELFTEST_REPORT%` | 迁移自检的逐条断言明细写入该变量指向的文件；缺省写到临时目录。想固定位置，跑 `release.ps1` 之前先 `setx`/`$env:` 设置它 |
| `%UNCAD_PERF_SMOKE_REPORT%` | 性能冒烟各阶段耗时明细，同上，缺省临时目录；预算可用 `%UNCAD_PERF_SMOKE_BUDGET_MS%` 覆盖（默认 30000） |

`%UNCAD_MIGRATION_SELFTEST_SABOTAGE%` 支持 `RowHeight`（改错期望行高）与
`DropPreRead`（跳过迁移前 previous 读取）两种人为破坏模式。这是**断言自证
机制**——验证门禁的 FAIL 路径真的会拦（分别证明行高与"先读后迁"顺序契约
有守门），**不是修复开关，也不是绕过手段**；正常发布与排障都不要设置它。

失败处置顺序：看 `release.ps1` 控制台报错 → 读 `tmp\` 保留日志 / 报告文件 →
修复 → 原命令重试。紧急发布才允许 `-SkipPerf` / `-SkipMigration`，且必须在
发布记录中注明跳过原因。

## 5. 本地 CI 与远端兜底

| 层 | 载体 | 说明 |
| --- | --- | --- |
| 本地全量 | `scripts/ci-local.ps1` | 本地全量单测 + 执行数核对，日志落 `tmp/ci/` |
| 远端兜底 | `.github/workflows/tests.yml` | GitHub Actions，`runs-on: [self-hosted, Windows, X64]`。必须 self-hosted：AutoCAD 程序集靠本机 HintPath 引用，托管 runner 无 AutoCAD 2022，编译都不通过 |

Self-hosted runner 尚未配置。注册待办：

- [ ] GitHub 仓库 → Settings → Actions → Runners → New self-hosted runner（选 Windows/x64）
- [ ] 在开发机按引导下载并 `config.cmd` 注册，`svc.cmd` 装成服务（保开机在线）
- [ ] 给 runner 打 `Windows`、`X64` 标签（与工作流 `runs-on` 匹配）
- [ ] 确认 runner 机器装有 AutoCAD 2022 与 dotnet SDK，首跑 `tests.yml` 绿后勾掉

## 6. 运营连续性清单

- `tools/publish-release.ps1` 上传需要管理员密码（`$env:UNCAD_ADMIN_PASSWORD`
  或 `-Password`）。密码必须安全异地备份——丢了就无法发新版/轮换最新标记。
- 双 remote 都要有最新推送：
  - `origin` = `https://github.com/sumce/UNCAD.git`
  - `server` = `uncad-deploy:/root/uncad-client.git`
- `pro.key` 是服务端授权数据，**不在安装包内**。安装器强制包内不得携带到期
  信息（`PackageContents.xml` 的 `LicenseExpiresUtc` 必须为空），到期只来自
  pro.key；`release.ps1` 同步校验 `LicenseMode=Online`。
- 服务端发布：

```powershell
$env:UNCAD_ADMIN_PASSWORD = '<管理员密码>'
.\tools\publish-release.ps1 -Version 3.0.0.17 `
    -ZipPath artifacts\Pro\UNCAD-Pro-v3.0.0.17.zip `
    -NotesPath docs\RELEASE-3.0.0.md
```

  脚本会登录 → 上传（服务端回传 SHA-256/字节数与本地核对）→ 更新版本记录
  （`isLatest` 排他，旧版自动降级；`-NoLatest` 只上传不切最新）→ 回读公开
  下载链路校验内容一致。

## 7. 版本规则速记（D-032，四段式）

| 场景 | 动什么 | 命令 |
| --- | --- | --- |
| 加功能 | 前三段 `X.Y.Z` +1、第四段归 0；新 tag `v<X.Y.Z>`、新 `docs/RELEASE-X.Y.Z.md`、VersionChangeLog 条目 | `.\scripts\Bump-Version.ps1 -Kind Feature -Label X.Y.Z` |
| 非功能（修 bug/测试/文档） | 只动第四段 +1；不新开 tag、不新建 RELEASE 文档 | `.\scripts\Bump-Version.ps1 -Kind Patch` |

两种 Kind 都会同步 `UNCAD.csproj`（AssemblyVersion/FileVersion）与
`PackageContents.xml`（AppVersion）；`installer.ps1` 要求 DLL FileVersion 与
AppVersion 完全相等（`-dev` 版按前三段比对），所以版本号永远用脚本改，不手改。
