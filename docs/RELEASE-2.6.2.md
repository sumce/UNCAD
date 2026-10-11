# UNCAD Pro 2.6.2

发布日期：2026-10-11

本版是审计修复版：不新增命令、不改变业务规则，只修正确性、可观测性与一致性。

## 修复

**1. U1PDF 出图失败会把临时打印设置永久写进用户图纸（本次审计的 P1）。**

`PdfExportService` 的失败分支此前写着 `transaction.Commit()` 再 `return`：C# 会先执行
已完成的提交语句，再展开 `using` 触发 `AutoCadPlotSession.Dispose()`，而"还原用户
布局打印设置"恰恰在 `Dispose()` 里。于是"PDF 设备／纸张／旋转／窗口／不套打印样式"
这套临时设置被提交进 dwg，随后的还原要么被 AutoCAD 拒绝、要么不落盘——用户图纸的
页面设置被静默改写。现在三条失败分支一律不提交，整个事务回滚，提交点只剩成功路径
的一个。

**2. 批次输出回滚不幂等，会把完好现场误报成"回滚不完整"。**

批次收尾（`FileBatchRollback.Complete`）会删除备份并释放文件锁，但 `Rollback()`
没有检查收尾状态：一旦在收尾之后再次进入失败分支，它会拿**已删除的备份**去恢复
输出文件，然后抛 `AggregateException`。用户看到的是
"CAD 修改已回滚，但 BOQ 文件回滚不完整，请先检查错误再继续更新"——而实际上
BOQ 文件根本没被改动。现在 `Rollback()` 幂等：批次一旦收尾就不再触碰任何文件。

**3. BOQ 工作簿输出阶段没有模块标识，出问题无法定位。**

按 `docs/DEVELOPMENT.md`，"有外部副作用或可能失败的阶段"必须经 `ModuleRunner`
执行，日志要能按模块ID定位。此前"读取图框记录"与"写入 BOQ 工作簿"两个阶段没有
模块标识：日志里查不到，用户看到的失败也没有模块上下文。现在两个阶段都带
`BOQ-XLSX/清单输出`，日志新增 `MODULE BOQ-XLSX/清单输出 [阶段] started/completed/failed`
与耗时，失败提示统一为
`模块[BOQ-XLSX/清单输出]在[写入 BOQ 工作簿]阶段失败: <原始原因>`。

**4. BOQ 异常提示是英文。**

工程量填成负数、机台ID缺失、一个工作簿混入多个机台时，抛出的提示是英文
（如 `BOQ material quantity cannot be negative: 1.1 = -3`），直接显示给用户。
现已全部改为中文，并说明下一步动作（例如"请先在图框清单中改正数量"）。

**5. 固定名 `.bak` 备份既吃掉用户文件，又让降级替换永久失败。**

BOQ 与 DWG 输出曾用 `target + ".bak"` 做替换备份：`File.Replace` 会覆盖同名文件、
随后代码又把它删掉，因此用户自己的 `[BOQ]机台ID.xlsx.bak`／`机台ID.dwg.bak` 会被
静默销毁；而网络盘/重定向路径走的 `File.Move` 降级分支在 `.bak` 已存在时**必然**
抛 `IOException`（.NET Framework 的 `File.Move` 不覆盖目标），导出直接失败。
现在备份名带 GUID，只删自己建的备份。

**6. 文件锁的中文提示永远到不了用户眼前。**

`FileUpdateLock` 用 `catch (IOException) when (attempt < 19)` 重试，第 20 次的异常
直接穿出方法，循环之后那句 `throw new IOException(busyMessage)` 不可达。于是 4 个
调用点的中文提示（"BOQ 正在被另一个 UNCAD 用户更新，请稍后重试。"等）全部失效，
用户等约 2.8 秒后看到原始系统异常。现在最后一次也进入 catch，统一抛调用方文案。

**7. 加载横幅漏列 5 条兼容命令。**

`FeatureRegistry` 用各 `[Feature]` 的 `Commands` 拼接启动横幅，而
`LineFeatureCommands`/`TrayFeatureCommands`/`ArchFeatureCommands` 不含兼容旧名，
于是横幅只列 30/35 条命令，漏掉 `UNL`/`UNR`/`UNQ1`/`UNQ2`/`UNQ4`。
现在三个清单都带上各自的兼容名。

**8. 空选择静默退出。**

`U1DT`、`U1L3D`、`U13D` 在用户没有选中任何对象时直接返回，命令行没有任何提示，
用户无法区分"没选中"和"已经执行完"。`Xmerge` 在选择列表为空时同样静默返回。
现在都会给出明确中文提示。

**9. U1PDF 全部失败时留下临时目录。**

出图临时目录无条件创建，唯一的递归删除却在"后台上传"协程里，而它只在有产物
排队时才被调用。所有图框出图失败（或全部被跳过）时，`%TEMP%\UNCAD\pdf-<guid>`
会永久残留，失败一次留一个。现在没有产物排队时立即清理。

## 验证

- 全量测试 `dotnet test UNCAD.slnx`：1001 项，全部通过（本版新增 7 项回归测试：
  回滚幂等 2 项、负数工程量中文提示 1 项、用户 .bak 不被触碰 1 项、
  BOQ 输出模块标识 1 项、Feature 命令清单覆盖兼容名 1 项、文件锁忙提示 1 项，
  以及一条加强后的命令注册集合对账测试）。
- 命令注册对账测试已加强：从"只扫 `Features/` 且只认 `CommandIds.X` 写法"改为
  "扫描整个 `src/UNCAD`、同时接受字面量注册、并断言注册集合与
  `CommandIds.Registered` 逐一相等"，这样少注册和多注册都会让测试变红。
- 真机验收：`tests\UNCAD.CadIntegration\run.ps1 -Command UNCAD_U1PDF_SELFTEST`
  （accoreconsole 无头 AutoCAD 2022）通过——出图页数/介质/颜色正确、布局打印设置
  未被改动、打印引擎回到 NotPlotting。
- 关于第 1 条（P1）：曾尝试在自检里加"强制出图失败后再比对布局打印设置"的场景，
  实测该探针会让 accoreconsole 偶发原生崩溃（4 次里 3 次拿不到结果），因此**没有纳入**
  自检，改由代码结构保证（失败分支不提交，提交点只有成功路径一个）。这是本版唯一
  一处"修复有把握、但没能用真机断言长期锁住"的地方。

## 本版不含（需要产品决策，未擅自改动）

- **`U1SET`/`U1DATA` 没有授权门禁。** 审计发现 `EnsureCommandAllowed` 覆盖 30/35
  条命令，`U1SET`（配置中心，写注册表）与 `U1DATA`（刷新机台数据，写 SQLite 快照并
  上报台账）不调用它，而 `ProductMetadata` 的注释写的是"只有拿到服务器明确结论且为
  active 才允许执行命令"，`CommandIds` 又把 `UNC_LOGIN` 标为唯一豁免。
  这两种状态不可能同时是对的：要么补门禁，要么把"配置/帮助/关于类命令豁免"写成
  正式决策。**它涉及授权策略，本版只报告不修改。**
- `CommandIds.Canonical` 只含 21 条，`U1C20/U1C25/U1C38/U1C51/U1PDF/UNC_LOGIN`
  不属于 Canonical/Aliases/Legacy 任何一类，而 `RibbonCatalogTests` 用 Canonical
  驱动"每条命令都在 Ribbon"的断言，这 6 条因此不在该断言覆盖内。
- `U1C20/U1C25/U1C38/U1C51` 与 `U1D` 在授权被停用时，提示文案里显示的是族命令名
  （`U1C`/`U1DWG`）而不是用户实际输入的命令名。
