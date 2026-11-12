using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Geometry;
using UNCAD.Core.Stat;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
#pragma warning disable 618 // AutoCAD 2022 direct Table setters avoid stale Cell wrappers.
    /// <summary>
    /// 图框迁移自检（UNCAD_MIGRATION_SELFTEST），发布门槛用（release.ps1 默认执行，
    /// -SkipMigration 可跳过）。背景：AGENTS.md 硬约束「Old frames and xframe frames
    /// remain readable. Migration must not resize a user's existing table, row, or
    /// column」长期只有口头承诺与纯字符串单测；五代图框（frame / frame_20260812 /
    /// xframe / xframe_v2 / FRAME_20261102）在真机 AutoCAD 里的迁移行为没有命令级断言。
    ///
    /// D-040（2026-11-02，用户更换图框模板）：最新块名 = FRAME_20261102，JSON 主存储
    /// = 图框块内 FRAMEINFO 文字属性；xframe_v2 降级为"上一代"（可读、可迁移），
    /// 独立 frameinfo_json 块与旧内嵌 FRAMEINFO_JSON 属性只作遗留读源（读照常、
    /// 写入后清空属于本框的副本）。
    ///
    /// 在临时图纸副本（必须是新版 XFrameTemplate.dwg——夹具要用其中真实的
    /// FRAME_20261102 定义造第五代实例）里程序化构造"老图"：
    ///   1) 四代旧图框定义按各自年代的边框样式合成（frame=闭合矩形多段线，
    ///      frame_20260812/xframe/xframe_v2=四条外边 Line），并挂旧式属性定义
    ///      （MACHINEID-*/CABLE_INFO/FRAMEINFO_JSON/BRIDGE_FRAME_INFO/CONDUIT_INFO）；
    ///      若模板自带同名定义则直接复用，只补缺失的属性定义；
    ///   2) 第五代直接插入图纸中真实的 FRAME_20261102 定义；
    ///   3) frame_20260812 与 xframe 的实例属性里写入旧版 v1 扁平
    ///      FRAMEINFO_JSON（含替代型号与历史条目，样例键名与解码器 ParseV1 对齐）；
    ///   4) frame 框内放一个遗留独立 frameinfo_json 块（JSON 属性挂 v1 扁平档案），
    ///      验证"遗留独立块读取照常、写入后清空、迁移不新建"；
    ///   5) FRAME_20261102 框内放一张最小 BOQ 表（数据行高用 D-027 的 5847.8848）。
    ///
    /// 然后按 U1F/U1U 首次写入的同一条迁移链执行（Features 层服务直调，与
    /// BatchFillUpdateCoordinator 写事务同序，不含 UI/Excel 输出）：
    ///   迁移前 FrameInfoJsonBlockWriter.Read(ReadSources(selection))
    ///   → XFrameMigrationService.Migrate(全部选集一次)
    ///   → FrameInfoJsonBlockWriter.FillOrMigrate(逐框)。
    ///
    /// 断言（任一失败即 FAIL，写 %UNCAD_CAD_INTEGRATION_RESULT% + 明细）：
    ///   a) 五代图框全部被 FrameRegionCollector 识别，迁移前后各一遍；迁移后
    ///      全部升级为 FRAME_20261102（IsLegacyFrame 全 false）；
    ///   b) 旧内嵌 FRAMEINFO_JSON 与遗留独立块 JSON 迁移到图框块的 FRAMEINFO
    ///      属性（meta.frameHandle 与图框一致），机台ID/设备名/替代型号/实测长度
    ///      与 v1 历史条目不丢；
    ///   c) 迁移不改既有表格几何：BOQ 表行数/列数/行高/列宽逐值相等；
    ///   d) 新定义里不存在的旧属性（FRAMEINFO_JSON/BRIDGE_FRAME_INFO/
    ///      CONDUIT_INFO）从块参照上移除，MACHINEID-* 值保留；FRAMEINFO 是主存储，
    ///      不得被清空且必须含合法 JSON；
    ///   e) 迁移不新建、不删除任何独立 frameinfo_json 块；frame 框内的遗留独立块
    ///      在写入后被清空（JSON 属性变空串）。
    ///
    /// 2026-11-09 增补阶段 5（复制图框三重防线，回应用户 2026-11-06 报告
    /// “从别的机台复制图框后 U1F 自动带出 3000 设备内预留”）：克隆已迁移的
    /// FRAME_20261102 图框（FRAMEINFO 带源框 meta.frameHandle、CABLE_INFO 带
    /// 「+3000（设备内）」残留文本），断言
    ///   f) ①句柄校验拒绝外来 JSON（不复读，源框自身读取不受影响）
    ///      ②FillReserveStage 在外来 JSON 时跳过 CABLE_INFO 预留恢复（不继承）
    ///      ③ClearStaleInfo 显式清空复制框旧属性且不波及源框（显式清理）
    ///      ④清理→FillFrame 后新写的 CABLE_INFO 存活（3.0.0.14 顺序锁：旧顺序
    ///        先写后清，复制框电缆信息永远为空——用户报告“左下角电缆信息直接
    ///        不出现”的根因）
    ///      ⑤复制框携带的横排毫米标不被 D-043 自动吸收（3.0.0.16：源框残留
    ///        不算本机台预留；不吸收、不删除，但仍从统计排除防 3000mm 双算）。
    ///
    /// 2026-11-10 增补阶段 6（整机台搬场重绑，D-052-C，回应用户报告
    /// “图纸跨文件复制后 U1U 提示输入预留、默认 0”）：同文件克隆后删除源框 =
    /// 跨文件搬场的等价物（原句柄消失、身份未变），断言
    ///   g) ①身份一致 + 原句柄已不在本图 → 判搬场收下记录，FillReserveStage
    ///      不再标记复制框、预留恢复通道重新打开；
    ///      ②身份不符（复制后改号/幽灵）→ 外来拒绝照旧（3.0.0.6 语义不回归）；
    ///      ③句柄撞号免疫（3.0.0.14）——记录句柄指向无关实体（直线）不得误判
    ///        “源框还在”，身份一致仍判搬场收下。
    ///
    /// FAIL 路径验证：环境变量 %UNCAD_MIGRATION_SELFTEST_SABOTAGE% 支持两种人为破坏
    ///   RowHeight   —— 把期望行高改错（断言 c 必须报警）；
    ///   DropPreRead —— 跳过迁移前的 previous 读取（断言 b 必须报警，证明
    ///                  "先读后迁"的顺序契约在 CAD 层有守门）。
    /// 报告写控制台与 %UNCAD_MIGRATION_SELFTEST_REPORT%（缺省为临时目录）。
    /// </summary>
    public sealed class MigrationSelfTestCommand
    {
        private const string CommandName = "UNCAD_MIGRATION_SELFTEST";
        private const string ResultVariable = "UNCAD_CAD_INTEGRATION_RESULT";
        private const string ReportVariable = "UNCAD_MIGRATION_SELFTEST_REPORT";
        private const string SabotageVariable = "UNCAD_MIGRATION_SELFTEST_SABOTAGE";

        private const string LegacyJsonTag = "FRAMEINFO_JSON";
        private const string LegacyBridgeTag = "BRIDGE_FRAME_INFO";
        private const string ConduitTag = FrameBlockFiller.TagConduit;
        private const string ProjectNameTag = XFrameMigrationService.ProjectNameTag;
        private const string StandaloneJsonDefinitionName = "frameinfo_json";
        private const string StandaloneJsonTag = "JSON";

        // 夹具放在远离模板既有内容的坐标带，避免模板自带图框/表格把分区搞混。
        private const double BaseX = 5000000;
        private const double BaseY = 5000000;
        private const double LegacyFrameWidth = 84000;
        private const double LegacyFrameHeight = 59400;
        private const double FrameGapX = 260000;

        /// <summary>D-027：生成的 BOQ 数据行高冻结值，这里用作"不许被迁移改写"的基准。</summary>
        private const double BoqDataRowHeight = 5847.8848;

        private static readonly List<string> Report = new List<string>();
        private static string _stage = "init";

        [CommandMethod(CommandName)]
        public void Run()
        {
            try
            {
                WriteResult(RunCore());
            }
            catch (System.Exception ex)
            {
                Info("异常[" + _stage + "]: " + ex);
                WriteResult("FAIL\r\n" + CommandName + " [" + _stage + "] " + ex);
            }
            finally
            {
                Dump();
            }
        }

        private static string RunCore()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            var ctx = new CadContext(document);
            string sabotage = (Environment.GetEnvironmentVariable(SabotageVariable) ?? "")
                .Trim();
            if (sabotage.Length > 0 && sabotage != "RowHeight" && sabotage != "DropPreRead")
                throw new InvalidOperationException("未识别的破坏模式 " + sabotage
                    + "（可用: RowHeight / DropPreRead）");

            Info("=== " + CommandName + " 图框迁移自检 ===");
            Info("图纸: " + document.Name);
            Info("破坏模式: " + (sabotage.Length == 0 ? "无（正常判定）" : sabotage));
            Info("覆盖层级: Features 层迁移服务直调（XFrameMigrationService.Migrate + "
                + "FrameInfoJsonBlockWriter.Read/FillOrMigrate），与 U1F/U1U 首次写入同一"
                + "事务顺序；不含 FillTableModule/FillFrame/BOQ Excel 输出与任何 UI。");

            // ---------------- 阶段 1：构造五代"老图"夹具 ----------------
            _stage = "build fixture";
            Fixture fixture;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                fixture = BuildFixture(ctx, tx);
                tx.Commit();
            }
            Info("");
            Info("=== 夹具 ===");
            foreach (FrameSlot slot in fixture.Slots)
                Info("  " + slot.Generation + " → " + slot.FrameId.Handle
                    + "（" + (slot.Legacy ? "合成旧定义" : "真实模板定义") + "，"
                    + slot.Attributes.Count + " 个实例属性）");
            Info("  遗留独立 JSON 块 → " + fixture.LegacyStandaloneJsonBlockId.Handle
                + "（frame 框内，v1 扁平档案）");
            Info("  BOQ 表 → " + fixture.BoqTableId.Handle + "（"
                + FrameRegionCollector.CurrentFrameName + " 框内，数据行高 "
                + BoqDataRowHeight + "）");

            TableSnapshot before = ReadTableSnapshot(ctx, fixture.BoqTableId);
            Info("  迁移前表格几何: " + before.Rows + " 行 × " + before.Columns
                + " 列，行高 [" + string.Join(", ", before.RowHeights.Select(v => v
                .ToString("0.####"))) + "]，列宽 [" + string.Join(", ",
                before.ColumnWidths.Select(v => v.ToString("0.####"))) + "]");

            // ---------------- 阶段 2：迁移前的识别与读取（与 U1U preflight 同序） ----------------
            _stage = "recognize frames";
            Dictionary<FrameSlot, FillSelection> selections;
            Dictionary<FrameSlot, FrameInfoJsonRecord> previous;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(tx);
                selections = CollectAndSplit(ctx, tx, fixture, definitions);

                // 独立 frameinfo_json 块的迁移前快照：断言 e 要证明"迁移不新建"。
                fixture.StandaloneJsonHandlesBefore = ScanStandaloneJsonBlocks(ctx, tx);
                Info("  迁移前独立 frameinfo_json 块: "
                    + fixture.StandaloneJsonHandlesBefore.Count + " 个（"
                    + string.Join(", ", fixture.StandaloneJsonHandlesBefore) + "）");

                // 迁移会从旧图框块参照上移除内嵌 FRAMEINFO_JSON，previous 必须先读。
                previous = new Dictionary<FrameSlot, FrameInfoJsonRecord>();
                foreach (FrameSlot slot in fixture.Slots)
                {
                    FrameInfoJsonRecord record = null;
                    if (sabotage != "DropPreRead")
                    {
                        record = FrameInfoJsonBlockWriter.Read(tx,
                            FrameInfoJsonBlockWriter.ReadSources(selections[slot]));
                        if (slot.V1Json != null)
                        {
                            if (record == null)
                                throw new InvalidOperationException("[" + CommandName
                                    + "] 迁移前未从 " + slot.Generation
                                    + " 图框读出遗留 JSON（内嵌属性或独立块）；旧图身份读取链退化。");
                            Check(record.MachineId == slot.Machine.MachineId,
                                slot.Generation + " 迁移前读出的机台ID应为 "
                                + slot.Machine.MachineId + "，实际 " + record.MachineId);
                            Check(record.BoqCableModel.Length > 0,
                                slot.Generation + " 迁移前应读出 v1 替代型号 boqCableModel");
                        }
                    }
                    else if (slot.V1Json != null)
                    {
                        Info("  [破坏] DropPreRead: 跳过 " + slot.Generation
                            + " 的迁移前 previous 读取");
                    }
                    previous[slot] = record;
                }
                tx.Commit();
            }

            // ---------------- 阶段 3：执行迁移链（单写事务，与 U1U 批量写入同构） ----------------
            _stage = "run migration";
            XFrameMigrationResult migration;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                migration = XFrameMigrationService.Migrate(ctx, tx,
                    fixture.Slots.Select(slot => selections[slot]));
                Info("");
                Info("=== 迁移 ===");
                Info("  XFrameMigrationService.Migrate: 图框 " + migration.Frames
                    + " 个，BOQ 表替换 " + migration.BoqTables
                    + " 个，制图信息表替换 " + migration.DrawingInfoTables + " 个");
                Check(migration.Frames == 4 && migration.BoqTables == 4
                    && migration.DrawingInfoTables == 5,
                    "迁移计数应为 图框4/BOQ表4/制图信息表5（4 个旧图框 + 1 个无信息表的 "
                    + FrameRegionCollector.CurrentFrameName + " 框），实际 "
                    + migration.Frames + "/" + migration.BoqTables + "/"
                    + migration.DrawingInfoTables);

                FrameInfoJsonBlockWriter.MetadataBlockIndex index =
                    FrameInfoJsonBlockWriter.MetadataBlockIndex.Scan(ctx, tx);
                foreach (FrameSlot slot in fixture.Slots)
                {
                    FillWriteResult result = FrameInfoJsonBlockWriter.FillOrMigrate(
                        ctx, tx, selections[slot].FrameBlockIds,
                        selections[slot].FrameInfoJsonBlockIds, slot.Machine, null,
                        "U1U", index, null, previous[slot]);
                    Info("  FillOrMigrate " + slot.Generation + ": 写入图框 "
                        + result.Blocks + " 个，值 " + result.Values + " 项");
                    Check(result.Blocks >= 1,
                        slot.Generation + " 应把 JSON 写入图框块 FRAMEINFO 属性（实际 "
                        + result.Blocks + " 个图框写入成功）");
                }
                tx.Commit();
            }

            // ---------------- 阶段 4：读回断言 a/b/c/d/e ----------------
            _stage = "assert after migration";
            AssertAfterMigration(ctx, fixture, selections, sabotage, before);

            // ---------------- 阶段 5：复制图框三重防线（断言 f） ----------------
            _stage = "copied frame defense";
            CopiedFrameDefense(ctx, fixture, selections);

            // ---------------- 阶段 6：整机台搬场重绑（D-052-C，断言 g） ----------
            _stage = "moved frame rebind";
            MovedFrameRebindDefense(ctx, fixture, selections);

            Info("");
            Info("结论: PASS");
            return "PASS";
        }

        // ================================================================== fixture

        private sealed class FrameSlot
        {
            public string Generation = "";
            public bool Legacy;
            public ObjectId FrameId;
            public Dictionary<string, string> Attributes =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>遗留 JSON（内嵌 v1 扁平或独立块上的 v1 扁平）；null 表示该年代没有。</summary>
            public string V1Json;
            public MachineRow Machine = new MachineRow();
        }

        private sealed class Fixture
        {
            public readonly List<FrameSlot> Slots = new List<FrameSlot>();
            public ObjectId BoqTableId;
            public ObjectId LegacyStandaloneJsonBlockId;
            public HashSet<string> StandaloneJsonHandlesBefore =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public string[] V2DefinitionTags = new string[0];
        }

        /// <summary>构造五代图框 + 遗留独立 JSON 块 + 最小 BOQ 表。使用后提交，不留半成品。</summary>
        private static Fixture BuildFixture(CadContext ctx, Transaction tx)
        {
            Database db = ctx.Db;
            var blocks = (BlockTable)tx.GetObject(db.BlockTableId, OpenMode.ForWrite);
            if (!blocks.Has(FrameRegionCollector.CurrentFrameName))
                throw new InvalidOperationException("输入图纸中没有真实的 "
                    + FrameRegionCollector.CurrentFrameName
                    + " 定义；请用 run.ps1 -InputDrawing src/UNCAD/bin/Release/net48/"
                    + "Resources/XFrameTemplate.dwg 运行本自检。");

            var fixture = new Fixture();

            // 第五代：真实模板定义（探针即正式实例，避免多插一个多余的图框）。
            ObjectId v2Definition = blocks[FrameRegionCollector.CurrentFrameName];
            FrameRectangle v2Boundary;
            {
                var probe = new BlockReference(new Point3d(BaseX + 4 * FrameGapX
                    + 4 * LegacyFrameWidth, BaseY, 0), v2Definition);
                ctx.AddToCurrentSpace(tx, probe);
                v2Boundary = FrameRegionCollector.ReadBoundary(tx, probe);
                var v2Slot = new FrameSlot
                {
                    Generation = FrameRegionCollector.CurrentFrameName,
                    Legacy = false,
                    FrameId = probe.ObjectId,
                    Machine = NewMachine("MIG-F5", "泵5", "E区", "ZB-YJV-4*16", "7.5",
                        "详图戊", "12", "50", "母线插接箱", "H9", "J3", "6F", "5F", "QF5")
                };
                fixture.Slots.Add(v2Slot);
            }
            fixture.V2DefinitionTags = DefinitionTags(tx, v2Definition);
            Info("真实 " + FrameRegionCollector.CurrentFrameName + " 定义属性标签: "
                + string.Join(", ", fixture.V2DefinitionTags));

            // 四代旧定义：frame 用闭合矩形多段线（最老模板样式），
            // frame_20260812 / xframe / xframe_v2 用四条外边 Line（上一代模板样式）。
            // xframe_v2 从 D-040 起是"上一代"（可读、可迁移），模板已不再自带，
            // 与三代更老的图框一样按旧样式合成；若图纸里恰好已有同名定义则复用。
            // frame 不挂内嵌 JSON（withEmbeddedJson=false）：它的 v1 档案由框内的
            // 遗留独立 frameinfo_json 块携带，覆盖"独立块也是合法遗留读源"这条链。
            InsertLegacyFrame(ctx, tx, blocks, fixture, 0,
                FrameRegionCollector.LegacyFrameName, true, false,
                NewMachine("MIG-F1", "泵1", "A区", "ZB-YJV-4*2.5", "1.5", "详图一", "01",
                    "20", "配电盘", "A1", "D4", "2F", "1F", "QF2"), V1JsonF1);
            InsertLegacyFrame(ctx, tx, blocks, fixture, 1,
                FrameRegionCollector.SupportedFrameName, false, true,
                NewMachine("MIG-F2", "泵2", "B区", "ZB-YJV-5*4", "2.2", "详图甲", "03",
                    "25", "I-Line", "B3", "C7", "3F", "2F", "QF1"), V1JsonF2);
            InsertLegacyFrame(ctx, tx, blocks, fixture, 2,
                FrameRegionCollector.XFrameName, false, true,
                NewMachine("MIG-F3", "泵3", "C区", "ZB-YJV-4*2.5", "3.7", "详图乙", "07",
                    "32", "插座盘", "E2", "F5", "4F", "3F", "QF3"), V1JsonF3);
            InsertLegacyFrame(ctx, tx, blocks, fixture, 3,
                FrameRegionCollector.XFrameV2Name, false, false,
                NewMachine("MIG-F4", "泵4", "D区", "ZB-YJV-6*10", "5.5", "详图丁", "11",
                    "40", "母线插接箱", "G8", "H2", "5F", "4F", "QF4"), null);

            // frame 框内的遗留独立 frameinfo_json 块：D-040 前的旧图纸用它存 JSON，
            // 现在必须"读取照常、写入后清空、迁移永不新建"。
            // 注意 Slots[0] 是先插入的第五代探针，这里必须按定义名取 frame 槽位。
            fixture.LegacyStandaloneJsonBlockId = InsertStandaloneJsonBlock(ctx, tx,
                blocks, fixture.Slots.First(slot => slot.Generation.Equals(
                    FrameRegionCollector.LegacyFrameName,
                    StringComparison.OrdinalIgnoreCase)), V1JsonF1);

            // 最小 BOQ 表放在第五代（当前代）图框内：迁移不会替换它的表，
            // 正好用它守住"不改用户既有表格尺寸"的承诺。
            double width = v2Boundary.Width;
            double height = v2Boundary.Height;
            var table = new Table();
            table.SetDatabaseDefaults(db);
            table.SetSize(4, 6);
            double[] widths = { 2800, 8000, 20000, 2800, 3200, 5000 };
            for (int column = 0; column < widths.Length; column++)
                table.Columns[column].Width = widths[column];
            table.Rows[0].Height = 3000;
            for (int row = 1; row < 4; row++)
                table.Rows[row].Height = BoqDataRowHeight;
            table.Position = new Point3d(v2Boundary.MinX + width * 0.08,
                v2Boundary.MaxY - height * 0.35, 0);
            table.Cells[0, 0].TextString = "NO.";
            table.Cells[0, 1].TextString = "项目名称";
            table.Cells[0, 2].TextString = "项目特征描述";
            table.Cells[0, 3].TextString = "单位";
            table.Cells[0, 4].TextString = "数量";
            table.Cells[0, 5].TextString = "项次编码";
            table.GenerateLayout();
            fixture.BoqTableId = ctx.AddToCurrentSpace(tx, table);
            return fixture;
        }

        private static MachineRow NewMachine(string machineId, string device,
            string region, string cable, string fr, string detail, string seq,
            string dia, string next, string upstreamAxis, string downstreamAxis,
            string deviceFloor, string panelFloor, string facilitySwitch)
            => new MachineRow
            {
                MachineId = machineId,
                CircuitName = device,
                Region = region,
                Cable = cable,
                Fr = fr,
                Detail = detail,
                Seq = seq,
                Dia = dia,
                Next = next,
                UpstreamAxis = upstreamAxis,
                DownstreamAxis = downstreamAxis,
                DeviceFloor = deviceFloor,
                PanelFloor = panelFloor,
                FacilitySwitch = facilitySwitch
            };

        // 旧版 v1 扁平 FRAMEINFO_JSON（键名与 FrameInfoJsonCodec.ParseV1 对齐，
        // changes 用 v1 的 timestampUtc/command/before/after/note 旧键）。
        // F1 挂在 frame 框内的遗留独立 frameinfo_json 块上；F2/F3 内嵌在图框块上。
        private const string V1JsonF1 =
            "{\"machineId\":\"MIG-F1\",\"deviceName\":\"泵1\",\"region\":\"A区\","
            + "\"originalCableModel\":\"ZB-YJV-4*2.5\",\"boqCableModel\":\"ZB-YJVR-4*2.5\","
            + "\"boqBusPlugBoxCode\":\"BPX-01\",\"fr\":\"1.5\",\"detail\":\"详图一\","
            + "\"seq\":\"01\",\"hoseDiameter\":\"20\",\"next\":\"配电盘\","
            + "\"upstreamAxis\":\"A1\",\"downstreamAxis\":\"D4\",\"deviceFloor\":\"2F\","
            + "\"panelFloor\":\"1F\",\"facilitySwitch\":\"QF2\","
            + "\"lastModifiedUtc\":\"2026-06-20T07:00:00.000Z\",\"lastModifiedUser\":\"旧用户丙\","
            + "\"measuredCableM\":\"31.4\",\"panelReserveMm\":\"3400\","
            + "\"deviceReserveMm\":\"2000\",\"totalM\":\"36.8\","
            + "\"changes\":[{\"timestampUtc\":\"2026-06-01T00:00:00.000Z\","
            + "\"command\":\"U1F\",\"field\":\"BoqCableModel\","
            + "\"before\":\"ZB-YJV-4*2.5\",\"after\":\"ZB-YJVR-4*2.5\","
            + "\"note\":\"旧版历史样例丙\"}]}";

        private const string V1JsonF2 =
            "{\"machineId\":\"MIG-F2\",\"deviceName\":\"泵2\",\"region\":\"B区\","
            + "\"originalCableModel\":\"ZB-YJV-5*4\",\"boqCableModel\":\"ZB-YJVR-5*4\","
            + "\"boqBusPlugBoxCode\":\"BPX-02\",\"fr\":\"2.2\",\"detail\":\"详图甲\","
            + "\"seq\":\"03\",\"hoseDiameter\":\"25\",\"next\":\"I-Line\","
            + "\"upstreamAxis\":\"B3\",\"downstreamAxis\":\"C7\",\"deviceFloor\":\"3F\","
            + "\"panelFloor\":\"2F\",\"facilitySwitch\":\"QF1\","
            + "\"lastModifiedUtc\":\"2026-07-15T08:00:00.000Z\",\"lastModifiedUser\":\"旧用户甲\","
            + "\"measuredCableM\":\"26.2\",\"panelReserveMm\":\"3400\","
            + "\"deviceReserveMm\":\"2000\",\"totalM\":\"34.6\","
            + "\"changes\":[{\"timestampUtc\":\"2026-07-01T00:00:00.000Z\","
            + "\"command\":\"U1F\",\"field\":\"BoqCableModel\","
            + "\"before\":\"ZB-YJV-5*4\",\"after\":\"ZB-YJVR-5*4\","
            + "\"note\":\"旧版历史样例甲\"}]}";

        private const string V1JsonF3 =
            "{\"machineId\":\"MIG-F3\",\"deviceName\":\"泵3\",\"region\":\"C区\","
            + "\"originalCableModel\":\"ZB-YJV-4*2.5\",\"boqCableModel\":\"ZB-YJVR-4*2.5\","
            + "\"boqBusPlugBoxCode\":\"\",\"fr\":\"3.7\",\"detail\":\"详图乙\","
            + "\"seq\":\"07\",\"hoseDiameter\":\"32\",\"next\":\"插座盘\","
            + "\"upstreamAxis\":\"E2\",\"downstreamAxis\":\"F5\",\"deviceFloor\":\"4F\","
            + "\"panelFloor\":\"3F\",\"facilitySwitch\":\"QF3\","
            + "\"lastModifiedUtc\":\"2026-08-02T09:30:00.000Z\",\"lastModifiedUser\":\"旧用户乙\","
            + "\"measuredCableM\":\"18.9\",\"panelReserveMm\":\"1000\","
            + "\"deviceReserveMm\":\"0\",\"totalM\":\"22.9\","
            + "\"changes\":[{\"timestampUtc\":\"2026-08-01T00:00:00.000Z\","
            + "\"command\":\"U1U\",\"field\":\"DeviceReserveMm\","
            + "\"before\":\"\",\"after\":\"0\",\"note\":\"旧版历史样例乙\"}]}";

        private static void InsertLegacyFrame(CadContext ctx, Transaction tx,
            BlockTable blocks, Fixture fixture, int index, string definitionName,
            bool polylineBorder, bool withEmbeddedJson, MachineRow machine, string v1Json)
        {
            Database db = ctx.Db;
            ObjectId definitionId;
            if (blocks.Has(definitionName))
            {
                // 图纸已带同名定义（如模板遗留）：复用，只补缺失的属性定义。
                definitionId = blocks[definitionName];
            }
            else
            {
                var definition = new BlockTableRecord { Name = definitionName };
                definitionId = blocks.Add(definition);
                tx.AddNewlyCreatedDBObject(definition, true);

                if (polylineBorder)
                {
                    var border = new Polyline();
                    border.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                    border.AddVertexAt(1, new Point2d(LegacyFrameWidth, 0), 0, 0, 0);
                    border.AddVertexAt(2, new Point2d(LegacyFrameWidth, LegacyFrameHeight), 0, 0, 0);
                    border.AddVertexAt(3, new Point2d(0, LegacyFrameHeight), 0, 0, 0);
                    border.Closed = true;
                    definition.AppendEntity(border);
                    tx.AddNewlyCreatedDBObject(border, true);
                }
                else
                {
                    AddBorderLine(tx, definition, 0, 0, LegacyFrameWidth, 0);
                    AddBorderLine(tx, definition, LegacyFrameWidth, 0, LegacyFrameWidth,
                        LegacyFrameHeight);
                    AddBorderLine(tx, definition, LegacyFrameWidth, LegacyFrameHeight, 0,
                        LegacyFrameHeight);
                    AddBorderLine(tx, definition, 0, LegacyFrameHeight, 0, 0);
                }
            }

            var tags = new List<string>
            {
                FrameBlockFiller.TagDevice,
                FrameBlockFiller.TagPower,
                FrameBlockFiller.TagCable,
                LegacyBridgeTag,
                ConduitTag
            };
            if (withEmbeddedJson) tags.Add(LegacyJsonTag);
            string[] existingTags = DefinitionTags(tx, definitionId);
            foreach (string tag in tags)
                if (!existingTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    AddAttributeDefinition(db, tx, (BlockTableRecord)tx.GetObject(
                        definitionId, OpenMode.ForWrite), tag);

            var frame = new BlockReference(new Point3d(BaseX + index * FrameGapX
                + index * LegacyFrameWidth, BaseY, 0), definitionId);
            ctx.AddToCurrentSpace(tx, frame);

            var slot = new FrameSlot
            {
                Generation = definitionName,
                Legacy = true,
                FrameId = frame.ObjectId,
                V1Json = v1Json,
                Machine = machine
            };
            AttachAttribute(ctx, tx, frame, FrameBlockFiller.TagDevice,
                FrameBlockFiller.DeviceText(machine));
            AttachAttribute(ctx, tx, frame, FrameBlockFiller.TagPower,
                FrameBlockFiller.PowerText(machine));
            AttachAttribute(ctx, tx, frame, FrameBlockFiller.TagCable, "电缆"
                + machine.Cable + "mm²: 2600mm");
            AttachAttribute(ctx, tx, frame, LegacyBridgeTag, "桥架200Wx100H 4.2M");
            AttachAttribute(ctx, tx, frame, ConduitTag, "⌀20线管 12M");
            if (v1Json != null && withEmbeddedJson)
                AttachAttribute(ctx, tx, frame, LegacyJsonTag, v1Json);
            foreach (KeyValuePair<string, string> pair in Attributes(tx, frame.ObjectId))
                slot.Attributes[pair.Key] = pair.Value;
            fixture.Slots.Add(slot);
        }

        /// <summary>在 frame 图框中心放一个遗留独立 frameinfo_json 块（JSON 属性=v1 档案）。</summary>
        private static ObjectId InsertStandaloneJsonBlock(CadContext ctx, Transaction tx,
            BlockTable blocks, FrameSlot owner, string v1Json)
        {
            Database db = ctx.Db;
            ObjectId definitionId;
            if (blocks.Has(StandaloneJsonDefinitionName))
            {
                definitionId = blocks[StandaloneJsonDefinitionName];
            }
            else
            {
                var definition = new BlockTableRecord
                {
                    Name = StandaloneJsonDefinitionName
                };
                definitionId = blocks.Add(definition);
                tx.AddNewlyCreatedDBObject(definition, true);
                var attribute = new AttributeDefinition
                {
                    Tag = StandaloneJsonTag,
                    Prompt = StandaloneJsonDefinitionName,
                    TextString = "",
                    Position = Point3d.Origin,
                    Invisible = true,
                    Constant = false,
                    Preset = true
                };
                attribute.SetDatabaseDefaults(db);
                definition.AppendEntity(attribute);
                tx.AddNewlyCreatedDBObject(attribute, true);
            }

            BlockReference ownerFrame = (BlockReference)tx.GetObject(owner.FrameId,
                OpenMode.ForRead, true);
            var block = new BlockReference(new Point3d(
                ownerFrame.Position.X + LegacyFrameWidth * 0.5,
                ownerFrame.Position.Y + LegacyFrameHeight * 0.5, 0), definitionId);
            ctx.AddToCurrentSpace(tx, block);

            var definitionRecord = (BlockTableRecord)tx.GetObject(definitionId,
                OpenMode.ForRead);
            foreach (ObjectId entityId in definitionRecord)
            {
                if (!(tx.GetObject(entityId, OpenMode.ForRead, true) is AttributeDefinition
                    attributeDefinition) || attributeDefinition.Constant) continue;
                var reference = new AttributeReference();
                reference.SetDatabaseDefaults(db);
                reference.SetAttributeFromBlock(attributeDefinition, block.BlockTransform);
                reference.TextString = v1Json;
                block.AttributeCollection.AppendAttribute(reference);
                tx.AddNewlyCreatedDBObject(reference, true);
            }
            return block.ObjectId;
        }

        private static void AddBorderLine(Transaction tx, BlockTableRecord definition,
            double x1, double y1, double x2, double y2)
        {
            var line = new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0));
            definition.AppendEntity(line);
            tx.AddNewlyCreatedDBObject(line, true);
        }

        private static void AddAttributeDefinition(Database db, Transaction tx,
            BlockTableRecord definition, string tag)
        {
            var attribute = new AttributeDefinition
            {
                Tag = tag,
                Prompt = tag,
                TextString = "",
                Position = new Point3d(LegacyFrameWidth * 0.05,
                    LegacyFrameHeight * 0.9, 0),
                Invisible = true,
                Constant = false,
                Preset = true
            };
            attribute.SetDatabaseDefaults(db);
            definition.AppendEntity(attribute);
            tx.AddNewlyCreatedDBObject(attribute, true);
        }

        private static void AttachAttribute(CadContext ctx, Transaction tx,
            BlockReference frame, string tag, string value)
        {
            AttributeDefinition definition = null;
            foreach (ObjectId definitionId in FrameRegionCollector.DefinitionIds(frame))
            {
                var record = (BlockTableRecord)tx.GetObject(definitionId,
                    OpenMode.ForRead, true);
                foreach (ObjectId entityId in record)
                {
                    if (tx.GetObject(entityId, OpenMode.ForRead, true)
                        is AttributeDefinition candidate
                        && string.Equals(candidate.Tag, tag,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        definition = candidate;
                        break;
                    }
                }
                if (definition != null) break;
            }
            if (definition == null)
                throw new InvalidOperationException("夹具属性定义缺失: " + tag);
            var reference = new AttributeReference();
            reference.SetDatabaseDefaults(ctx.Db);
            reference.SetAttributeFromBlock(definition, frame.BlockTransform);
            reference.TextString = value ?? "";
            frame.AttributeCollection.AppendAttribute(reference);
            tx.AddNewlyCreatedDBObject(reference, true);
        }

        // ================================================================== phase 2

        private static Dictionary<FrameSlot, FillSelection> CollectAndSplit(
            CadContext ctx, Transaction tx, Fixture fixture,
            CadBlockDefinitionReader definitions)
        {
            ObjectId[] frameIds = fixture.Slots.Select(slot => slot.FrameId).ToArray();
            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, frameIds);
            if (regions.Errors.Count > 0)
                throw new InvalidOperationException("图框分区报错: "
                    + string.Join("; ", regions.Errors));
            Check(regions.SelectedFrameCount == fixture.Slots.Count
                && regions.Groups.Count == fixture.Slots.Count,
                "五代图框应全部被识别（分组 " + regions.Groups.Count + "/选中 "
                + regions.SelectedFrameCount + "，期望 "
                + fixture.Slots.Count + "）；FrameRegionCollector.IsSupportedFrameName "
                + "语义退化。");

            var result = new Dictionary<FrameSlot, FillSelection>();
            foreach (FrameRegionGroup region in regions.Groups)
            {
                FillSelection selection = FillSelectionCollector.Split(tx,
                    region.EntityIds.ToArray(), true, definitions);
                Check(selection.FrameBlockIds.Length == 1,
                    "图框 " + region.Handle + " 应识别出唯一图框块，实际 "
                    + selection.FrameBlockIds.Length + " 个");
                FrameSlot slot = fixture.Slots.FirstOrDefault(item =>
                    item.FrameId == selection.FrameBlockIds[0]);
                Check(slot != null, "分区结果 " + region.Handle + " 不属于任何夹具图框。");
                Check(DefinitionName(tx, slot.FrameId).Equals(slot.Generation,
                    StringComparison.OrdinalIgnoreCase),
                    slot.Generation + " 迁移前定义名应为 " + slot.Generation + "，实际 "
                    + DefinitionName(tx, slot.FrameId));
                result[slot] = selection;
            }
            Check(result.Count == fixture.Slots.Count, "不是每个夹具图框都有对应分区。");

            // frame 框内的遗留独立 JSON 块应进入该选集的 FrameInfoJsonBlockIds。
            FrameSlot oldest = fixture.Slots.First(slot =>
                slot.Generation.Equals(FrameRegionCollector.LegacyFrameName,
                    StringComparison.OrdinalIgnoreCase));
            Check(result[oldest].FrameInfoJsonBlockIds.Length == 1
                && result[oldest].FrameInfoJsonBlockIds[0]
                    == fixture.LegacyStandaloneJsonBlockId,
                oldest.Generation + " 选集应包含框内的遗留独立 frameinfo_json 块（实际 "
                + result[oldest].FrameInfoJsonBlockIds.Length + " 个）。");

            FrameSlot current = fixture.Slots.First(slot => !slot.Legacy);
            Check(result[current].TableIds.Length == 1
                && result[current].TableIds[0] == fixture.BoqTableId,
                "BOQ 表应归属 " + FrameRegionCollector.CurrentFrameName
                + " 图框选集（实际 TableIds=" + result[current].TableIds.Length
                + "）");
            foreach (FrameSlot slot in fixture.Slots.Where(item => item.Legacy))
                Check(result[slot].TableIds.Length == 0,
                    slot.Generation + " 选集不应包含清单表（老图框原本没有表）。");
            return result;
        }

        /// <summary>当前空间里所有独立 frameinfo_json 块的句柄集合（断言 e 的快照工具）。</summary>
        private static HashSet<string> ScanStandaloneJsonBlocks(CadContext ctx,
            Transaction tx)
        {
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var space = (BlockTableRecord)tx.GetObject(ctx.Db.CurrentSpaceId,
                OpenMode.ForRead);
            foreach (ObjectId id in space)
            {
                if (!(tx.GetObject(id, OpenMode.ForRead, true) is BlockReference block)
                    || block is Table)
                    continue;
                if (FrameInfoJsonBlockWriter.IsFrameInfoJsonBlock(tx, block))
                    handles.Add(block.Handle.ToString());
            }
            return handles;
        }

        // ================================================================== phase 4

        private static void AssertAfterMigration(CadContext ctx, Fixture fixture,
            Dictionary<FrameSlot, FillSelection> selections, string sabotage,
            TableSnapshot before)
        {
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                Info("");
                Info("=== 迁移后断言 ===");

                // a) 五代全部迁移到当前代且仍可被识别。
                foreach (FrameSlot slot in fixture.Slots)
                {
                    BlockReference frame = (BlockReference)tx.GetObject(slot.FrameId,
                        OpenMode.ForRead, true);
                    Check(!FrameRegionCollector.IsLegacyFrame(tx, frame),
                        slot.Generation + " 迁移后仍被判定为旧图框；未升级到 "
                        + FrameRegionCollector.CurrentFrameName + "。");
                    Check(DefinitionName(tx, slot.FrameId).Equals(
                            FrameRegionCollector.CurrentFrameName,
                            StringComparison.OrdinalIgnoreCase),
                        slot.Generation + " 迁移后定义名应为 "
                        + FrameRegionCollector.CurrentFrameName + "，实际 "
                        + DefinitionName(tx, slot.FrameId));
                }
                FrameRegionCollection after = FrameRegionCollector.Collect(ctx,
                    fixture.Slots.Select(slot => slot.FrameId).ToArray());
                Check(after.Errors.Count == 0 && after.SelectedFrameCount == 5
                    && after.Groups.Count == 5,
                    "迁移后应仍识别出 5 个图框（实际 " + after.Groups.Count
                    + "），分区错误: " + string.Join("; ", after.Errors));
                Info("  a) 五代图框全部识别并升级为 "
                    + FrameRegionCollector.CurrentFrameName + ": 通过");

                // b) 遗留 JSON（内嵌属性 + 独立块）→ 图框块 FRAMEINFO 属性，身份不丢。
                foreach (FrameSlot slot in fixture.Slots)
                {
                    BlockReference frame = (BlockReference)tx.GetObject(slot.FrameId,
                        OpenMode.ForRead, true);
                    FrameInfoJsonRecord record = FrameInfoJsonBlockWriter.Read(tx,
                        new[] { slot.FrameId });
                    Check(record != null,
                        slot.Generation + " 图框块 FRAMEINFO 属性读不出 JSON；主存储写入链退化。");
                    Check(record.SchemaVersion == FrameInfoJsonCodec.CurrentSchemaVersion,
                        slot.Generation + " FRAMEINFO 应为 v2 结构，实际 schemaVersion="
                        + record.SchemaVersion);
                    Check(record.MachineId == slot.Machine.MachineId,
                        slot.Generation + " 机台ID丢失: 期望 "
                        + slot.Machine.MachineId + "，实际 " + record.MachineId);
                    Check(record.DeviceName == slot.Machine.CircuitName,
                        slot.Generation + " 设备名丢失: 期望 "
                        + slot.Machine.CircuitName + "，实际 " + record.DeviceName);
                    // meta.frameHandle 句柄绑定：JSON 跟着图框实体走。
                    Check(string.Equals(record.FrameHandle, frame.Handle.ToString(),
                            StringComparison.OrdinalIgnoreCase),
                        slot.Generation + " meta.frameHandle 应绑定本图框（期望 "
                        + frame.Handle + "，实际 " + record.FrameHandle + "）。");
                    if (slot.V1Json != null)
                    {
                        // 旧 JSON 的替代型号/实测长度只能来自迁移前的 previous——
                        // 工作簿里特意不放同值，丢了这里一定报警。
                        Check(record.BoqCableModel ==
                                V1Value(slot.V1Json, "boqCableModel"),
                            slot.Generation + " 替代型号 boqCableModel 丢失: 期望 "
                            + V1Value(slot.V1Json, "boqCableModel") + "，实际 "
                            + record.BoqCableModel);
                        Check(record.OriginalCableModel ==
                                V1Value(slot.V1Json, "originalCableModel"),
                            slot.Generation + " 原始型号丢失: 期望 "
                            + V1Value(slot.V1Json, "originalCableModel") + "，实际 "
                            + record.OriginalCableModel);
                        Check(record.MeasuredCableM ==
                                V1Value(slot.V1Json, "measuredCableM"),
                            slot.Generation + " 实测长度丢失: 期望 "
                            + V1Value(slot.V1Json, "measuredCableM") + "，实际 "
                            + record.MeasuredCableM);
                        Check(record.UpstreamAxis == slot.Machine.UpstreamAxis
                            && record.DownstreamAxis == slot.Machine.DownstreamAxis
                            && record.DeviceFloor == slot.Machine.DeviceFloor
                            && record.PanelFloor == slot.Machine.PanelFloor,
                            slot.Generation + " 上下游轴位/楼层丢失。");
                        // v2 的 changes 只持久化 ts/cmd/field/from/to 五个键
                        // （v1 的 note 在解码后即丢弃），这里按持久化键对齐比较。
                        FrameInfoJsonChange v1Change = FrameInfoJsonCodec
                            .Parse(slot.V1Json).Changes.FirstOrDefault();
                        Check(v1Change != null && record.Changes.Any(change =>
                                change.Field == v1Change.Field
                                && change.Command == v1Change.Command
                                && change.Before == v1Change.Before
                                && change.After == v1Change.After
                                && change.TimestampUtc == v1Change.TimestampUtc),
                            slot.Generation + " v1 历史条目未迁移到 changes。实际 payload: "
                            + FrameInfoJsonCodec.Serialize(record));
                    }
                    Info("  b) " + slot.Generation + " → 图框块 FRAMEINFO（"
                        + frame.Handle + "，机台 " + record.MachineId
                        + (slot.V1Json == null ? "" : "，替代型号 "
                            + record.BoqCableModel + "，v1 历史 "
                            + record.Changes.Count + " 条") + "）: 通过");
                }

                // c) 迁移不改既有表格几何。
                TableSnapshot snapshot = ReadTableSnapshot(ctx, fixture.BoqTableId);
                Table boq = (Table)tx.GetObject(fixture.BoqTableId, OpenMode.ForRead, true);
                Check(!boq.IsErased, "BOQ 表被删除或替换；当前代图框的既有表必须保留原实体。");
                Check(snapshot.Rows == before.Rows && snapshot.Columns == before.Columns,
                    "BOQ 表行列数被改变: " + before.Rows + "x" + before.Columns + " → "
                    + snapshot.Rows + "x" + snapshot.Columns);
                double[] expectedHeights = (double[])before.RowHeights.Clone();
                if (sabotage == "RowHeight")
                {
                    Info("  [破坏] RowHeight: 期望行高被改错（用于验证 FAIL 路径）");
                    for (int row = 1; row < expectedHeights.Length; row++)
                        expectedHeights[row] += 0.5;
                }
                for (int row = 0; row < before.RowHeights.Length; row++)
                    Check(snapshot.RowHeights[row] == expectedHeights[row],
                        "BOQ 表第 " + row + " 行高与期望不符: 期望 "
                        + expectedHeights[row] + "，迁移前 " + before.RowHeights[row]
                        + "，迁移后 " + snapshot.RowHeights[row]
                        + "（D-027 契约: 迁移不得改用户表格行高）");
                for (int column = 0; column < before.ColumnWidths.Length; column++)
                    Check(snapshot.ColumnWidths[column] == before.ColumnWidths[column],
                        "BOQ 表第 " + column + " 列宽被改变: 迁移前 "
                        + before.ColumnWidths[column] + "，迁移后 "
                        + snapshot.ColumnWidths[column]);
                Check(snapshot.Position.DistanceTo(before.Position) < 0.000001,
                    "BOQ 表位置被移动。");
                Info("  c) BOQ 表几何逐值相等（" + snapshot.Rows + "x" + snapshot.Columns
                    + "，数据行高 " + BoqDataRowHeight + "）: 通过");

                // d) 旧属性移除 + 保留属性值不丢 + FRAMEINFO 主存储不被清空。
                string[] allowed = fixture.V2DefinitionTags
                    .Concat(new[] { ProjectNameTag })
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (FrameSlot slot in fixture.Slots)
                {
                    Dictionary<string, string> attributes = Attributes(tx,
                        slot.FrameId);
                    // FRAMEINFO 是 D-040 主存储：必须存在、非空且是合法 JSON。
                    Check(attributes.TryGetValue(
                            FrameInfoJsonBlockWriter.FrameInfoTag, out string frameInfo)
                        && frameInfo.TrimStart().StartsWith("{", StringComparison.Ordinal)
                        && FrameInfoJsonCodec.Parse(frameInfo) != null,
                        slot.Generation + " FRAMEINFO 属性缺失/为空/非法（主存储被清空）。");
                    if (!slot.Legacy) continue;
                    foreach (string legacyTag in new[]
                        { LegacyJsonTag, LegacyBridgeTag, ConduitTag })
                        Check(!attributes.ContainsKey(legacyTag),
                            slot.Generation + " 迁移后仍残留旧属性 " + legacyTag
                            + "；新定义没有的属性必须从块参照移除。");
                    foreach (string tag in attributes.Keys)
                        if (!string.Equals(tag, FrameInfoJsonBlockWriter.FrameInfoTag,
                                StringComparison.OrdinalIgnoreCase))
                            Check(allowed.Contains(tag, StringComparer.OrdinalIgnoreCase),
                                slot.Generation + " 迁移后出现意外属性 " + tag
                                + "（不在 " + FrameRegionCollector.CurrentFrameName
                                + " 定义里）。");
                    foreach (string tag in new[]
                        { FrameBlockFiller.TagDevice, FrameBlockFiller.TagPower })
                        Check(attributes.TryGetValue(tag, out string value)
                            && value == slot.Attributes[tag],
                            slot.Generation + " 属性 " + tag + " 的值在迁移中丢失: 期望 "
                            + slot.Attributes[tag] + "，实际 "
                            + (attributes.TryGetValue(tag, out string after2)
                                ? after2 : "<缺失>"));
                    // 迁移为旧图框克隆的新版清单表必须真实存在。
                    FillSelection selection = selections[slot];
                    Check(selection.TableIds.Length == 1,
                        slot.Generation + " 迁移后选集应有克隆的 BOQ 表。");
                    Table cloned = (Table)tx.GetObject(selection.TableIds[0],
                        OpenMode.ForRead, true);
                    Check(!cloned.IsErased && cloned.Columns.Count >= 6
                        && !CadDrawingInfoTableWriter.IsDrawingInfoTable(cloned),
                        slot.Generation + " 克隆的 BOQ 表布局不对（列 "
                        + cloned.Columns.Count + "）。");
                    Info("  d) " + slot.Generation + " 旧属性已移除（"
                        + LegacyJsonTag + "/" + LegacyBridgeTag + "/" + ConduitTag
                        + "），FRAMEINFO 主存储完好，MACHINEID-* 值保留，克隆 BOQ 表 "
                        + cloned.Handle + ": 通过");
                }

                // e) 独立 frameinfo_json 块：不新建、不删除；属于本框的遗留块被清空。
                HashSet<string> afterStandalone = ScanStandaloneJsonBlocks(ctx, tx);
                Check(afterStandalone.SetEquals(fixture.StandaloneJsonHandlesBefore),
                    "迁移不得新建或删除独立 frameinfo_json 块（迁移前 "
                    + fixture.StandaloneJsonHandlesBefore.Count + " 个，迁移后 "
                    + afterStandalone.Count + " 个；D-040: JSON 主存储在图框块 FRAMEINFO"
                    + " 属性，写入链不应再创建独立块）。");
                BlockReference legacyBlock = (BlockReference)tx.GetObject(
                    fixture.LegacyStandaloneJsonBlockId, OpenMode.ForRead, true);
                Check(!legacyBlock.IsErased,
                    "遗留独立 frameinfo_json 块不应被删除（只清空 JSON 属性）。");
                Dictionary<string, string> legacyBlockAttributes = Attributes(tx,
                    fixture.LegacyStandaloneJsonBlockId);
                Check(legacyBlockAttributes.TryGetValue(StandaloneJsonTag,
                        out string legacyJson) && legacyJson.Trim().Length == 0,
                    "frame 框内的遗留独立块 JSON 应在写入后被清空（实际 "
                    + (legacyJson == null ? "<缺失>" : "长度 " + legacyJson.Length)
                    + "）——否则同一图框两份 JSON 会互相覆盖。");
                Info("  e) 独立 frameinfo_json 块零新建零删除（迁移前后都是 "
                    + afterStandalone.Count + " 个），frame 框内遗留块 JSON 已清空: 通过");
                tx.Commit();
            }
        }

        // ================================================================== phase 5

        /// <summary>
        /// 复制图框三重防线自检：在真机 AutoCAD 里构造“整框复制”（克隆已迁移的
        /// 当前代图框，FRAMEINFO 携带源框句柄的 JSON、CABLE_INFO 携带
        /// 「+3000（设备内）」源框残留），逐层断言 3.0.0.6/3.0.0.8/3.0.0.9 的防线：
        /// ①不复读（句柄校验拒外来 JSON）②不继承（外来 JSON 时跳过 CABLE_INFO
        /// 预留恢复）③显式清理（ClearStaleInfo 清空旧属性、不波及源框）
        /// ④清后新写存活（3.0.0.14：清理→FillFrame 之后新电缆公式必须在，
        /// 锁死“清空先于写入”的顺序——旧顺序把刚写的 CABLE_INFO 抹空）
        /// ⑤复制框毫米标不吸收（3.0.0.16：随框带过来的横排毫米标是源框残留，
        /// D-043 自动吸收被外来 JSON 守卫拦住——不吸收、不删除、只从统计排除）。
        /// </summary>
        private static void CopiedFrameDefense(CadContext ctx, Fixture fixture,
            Dictionary<FrameSlot, FillSelection> selections)
        {
            Info("");
            Info("=== 复制图框三重防线 ===");
            FrameSlot source = fixture.Slots.First(slot => !slot.Legacy);
            // 用户 bug 报告里的形态：源框 CABLE_INFO 残留文本带 3000 设备内预留。
            const string foreignCableInfo =
                "电缆ZB-YJV-4*16mm²: 2600+3400（盘内）+3000（设备内）=9000mm";

            ObjectId copyId;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                BlockReference sourceFrame = (BlockReference)tx.GetObject(source.FrameId,
                    OpenMode.ForRead, true);
                ObjectId definitionId = FrameRegionCollector.DefinitionIds(sourceFrame)
                    .First();
                var copy = new BlockReference(new Point3d(sourceFrame.Position.X
                    + FrameGapX, sourceFrame.Position.Y - LegacyFrameHeight * 2, 0),
                    definitionId);
                ctx.AddToCurrentSpace(tx, copy);
                // 整框复制的等价物：逐属性克隆（FRAMEINFO = 源框 meta.frameHandle
                // 的 JSON，即外来记录；CABLE_INFO 覆写为带设备内预留的残留文本）。
                bool cableInfoAttached = false;
                foreach (KeyValuePair<string, string> pair in Attributes(tx,
                    source.FrameId))
                {
                    string value = pair.Key.Equals(FrameBlockFiller.TagCable,
                        StringComparison.OrdinalIgnoreCase) ? foreignCableInfo
                        : pair.Value;
                    if (pair.Key.Equals(FrameBlockFiller.TagCable,
                        StringComparison.OrdinalIgnoreCase))
                        cableInfoAttached = true;
                    AttachAttribute(ctx, tx, copy, pair.Key, value);
                }
                if (!cableInfoAttached)
                    AttachAttribute(ctx, tx, copy, FrameBlockFiller.TagCable,
                        foreignCableInfo);
                copyId = copy.ObjectId;
                tx.Commit();
            }
            Info("  已克隆源框 → " + copyId.Handle + "（FRAMEINFO=源框 JSON，"
                + "CABLE_INFO=+3000（设备内））");

            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx,
                    new[] { copyId });
                Check(regions.Errors.Count == 0 && regions.Groups.Count == 1,
                    "复制图框应被分区识别（分组 " + regions.Groups.Count
                    + "，错误: " + string.Join("; ", regions.Errors) + "）。");
                var definitions = new CadBlockDefinitionReader(tx);
                FillSelection copySelection = FillSelectionCollector.Split(tx,
                    regions.Groups[0].EntityIds.ToArray(), true, definitions);
                Check(copySelection.FrameBlockIds.Length == 1
                    && copySelection.FrameBlockIds[0] == copyId,
                    "复制图框选集应识别出唯一图框块（实际 "
                    + copySelection.FrameBlockIds.Length + " 个）。");

                // ① 不复读：句柄校验必须拒绝复制框携带的源框 JSON。
                FrameInfoJsonBlockWriter.Read(tx,
                    FrameInfoJsonBlockWriter.ReadSources(copySelection),
                    FrameInfoJsonBlockWriter.OwnFrameHandle(tx,
                        copySelection.FrameBlockIds),
                    out string foreignJsonNote);
                Check(foreignJsonNote.Length > 0,
                    "复制图框携带的源框 JSON 应被句柄校验识别为外来记录"
                    + "（foreignJsonNote 为空 = ①防线失效，复制框会复读源框数据）。");
                Info("  ① 不复读: 外来 JSON 已被句柄校验拒绝（note 长度 "
                    + foreignJsonNote.Length + "）: 通过");

                // 对照组：源框自身读取照常（句柄一致、无外来提示）。
                FrameInfoJsonRecord own = FrameInfoJsonBlockWriter.Read(tx,
                    FrameInfoJsonBlockWriter.ReadSources(selections[source]),
                    FrameInfoJsonBlockWriter.OwnFrameHandle(tx,
                        selections[source].FrameBlockIds),
                    out string ownJsonNote);
                Check(ownJsonNote.Length == 0 && own != null
                    && own.MachineId == source.Machine.MachineId,
                    "源框自身 JSON 读取应照常通过（实际 note='" + ownJsonNote
                    + "'，机台 " + (own == null ? "<null>" : own.MachineId)
                    + "）——句柄校验不得误伤本框记录。");
                Info("  ① 对照: 源框自身读取照常（机台 " + own.MachineId + "）: 通过");

                // 前置证明：残留 CABLE_INFO 本身能解析出 3000——守卫是唯一闸门。
                string copiedCableInfo = CadBlockAttributeWriter.ReadFirstTagged(tx,
                    copySelection.FrameBlockIds, FrameBlockFiller.TagCable);
                Check(CableReserveReview.TryParseDeviceReserveMillimeters(
                        copiedCableInfo, out double parsedReserve)
                    && Math.Abs(parsedReserve - 3000.0) < 0.001,
                    "夹具自检：复制框 CABLE_INFO 应解析出 3000 设备内预留"
                    + "（实际 " + parsedReserve + "），否则②的守卫断言无意义。");

                // ② 不继承：生产链路 FillReserveStage.Resolve 必须跳过该恢复。
                FrameInfoJsonRecord resolved = FillReserveStage.Resolve(ctx,
                    copySelection, FillSettings.Current(), true,
                    out List<ObjectId> eraseIds, out List<ObjectId> excludedIds,
                    out bool copiedFrameDetected, out double? reserveHint);
                Check(copiedFrameDetected,
                    "FillReserveStage 应检测到复制图框（copiedFrameDetected=false）。");
                Check(resolved == null
                    || string.IsNullOrWhiteSpace(resolved.DeviceReserveMm),
                    "外来 JSON 时 CABLE_INFO 恢复必须被守卫跳过——设备内预留不得"
                    + "继承 3000（实际 DeviceReserveMm='"
                    + (resolved == null ? "" : resolved.DeviceReserveMm)
                    + "'，②防线失效）。");
                Check(reserveHint.HasValue && Math.Abs(reserveHint.Value - 3000.0) < 0.001,
                    "D-052-B：复制框应把 CABLE_INFO 的 3000 作为建议值带出（预留弹窗"
                    + "预填用），实际 " + (reserveHint?.ToString() ?? "<null>") + "。");
                Check(eraseIds.Count == 0,
                    "复制图框区域内没有横排毫米标，不应有待删除标注。");
                Info("  ② 不继承: FillReserveStage 跳过 CABLE_INFO 恢复，"
                    + "设备内预留为空（copiedFrameDetected=true）: 通过");

                // ③ 显式清理：ClearStaleInfo 清空复制框旧属性，源框无损，事务回滚。
                int cleared;
                {
                    using (Transaction clearTx = ctx.Db.TransactionManager
                        .StartTransaction())
                    {
                        cleared = CadBlockAttributeWriter.ClearStaleInfo(clearTx,
                            copySelection.FrameBlockIds);
                        Check(cleared >= 2,
                            "ClearStaleInfo 应清空 CABLE_INFO 与 FRAMEINFO 至少 2 个"
                            + "旧属性（实际 " + cleared + " 个）。");
                        Dictionary<string, string> copyAttributes = Attributes(
                            clearTx, copyId);
                        Check(copyAttributes.TryGetValue(FrameBlockFiller.TagCable,
                                out string clearedCable)
                            && clearedCable.Trim().Length == 0,
                            "清理后复制框 CABLE_INFO 应为空（实际 '"
                            + clearedCable + "'）。");
                        Check(copyAttributes.TryGetValue(
                                FrameInfoJsonBlockWriter.FrameInfoTag,
                                out string clearedJson)
                            && clearedJson.Trim().Length == 0,
                            "清理后复制框 FRAMEINFO 应为空（实际长度 "
                            + (clearedJson == null ? -1 : clearedJson.Length)
                            + "）——否则源框 JSON 副本残留，保存后会被当成两框记录。");
                        Dictionary<string, string> sourceAttributes = Attributes(
                            clearTx, source.FrameId);
                        Check(sourceAttributes.TryGetValue(
                                FrameInfoJsonBlockWriter.FrameInfoTag,
                                out string sourceJson)
                            && sourceJson.TrimStart().StartsWith("{",
                                StringComparison.Ordinal),
                            "ClearStaleInfo 只清复制框，源框 FRAMEINFO 必须完好。");
                        // ④ 清后新写存活（3.0.0.14 顺序修正的直接回归）：生产顺序是
                        // ClearStaleInfo → FillFrame——清理之后写入的新机台
                        // CABLE_INFO 必须存活；旧顺序（FillFrame 在前、清空在后）
                        // 会把刚写的公式当源框残留抹掉，这里应读到空串。
                        var freshStat = new CableStatResult
                        {
                            CableSum = 14.8,
                            MeasuredCableSum = 6.4,
                            CableReserveApplied = true,
                            PanelReserveMillimeters = 3400,
                            DeviceReserveMillimeters = 2000,
                            OwnerReserveMillimeters = 3000
                        };
                        freshStat.CableFormatted.Add("1.0");
                        freshStat.CableFormatted.Add("5.4");
                        CadBlockAttributeWriter.FillFrame(ctx, clearTx,
                            copySelection.FrameBlockIds, source.Machine, freshStat,
                            false, false);
                        string afterRewrite = CadBlockAttributeWriter.ReadFirstTagged(
                            clearTx, copySelection.FrameBlockIds,
                            FrameBlockFiller.TagCable);
                        Check(afterRewrite != null && afterRewrite.IndexOf(
                                "1000 +5400 +3400（盘内） +2000（设备内） +3000（预留）=14800mm",
                                StringComparison.Ordinal) >= 0,
                            "复制框清理后写入的新 CABLE_INFO 必须存活（实际 '"
                            + afterRewrite + "'）——3.0.0.14 前的清空顺序会把它抹空。");
                        clearTx.Abort(); // 自检不改图。
                    }
                }
                Info("  ③ 显式清理: ClearStaleInfo 清空 " + cleared
                    + " 个旧属性（CABLE_INFO/FRAMEINFO），源框无损: 通过");
                Info("  ④ 清后新写: 清理→写入的新电缆公式存活（…=14800mm）: 通过");

                // ⑤ 复制框毫米标不吸收（3.0.0.16 审计修复）：随框带过来的横排
                // 毫米标是源框残留——D-043 自动吸收必须被外来 JSON 守卫拦住，
                // 否则源机台的设备内预留从这条暗道静默继承回来（设备内预留
                // 直接变 3000），图上文字还被删掉。守卫语义：不吸收、不删除、
                // 只从统计排除（"3000mm"同时命中电缆长度判据，不排除会双算）。
                {
                    ObjectId markerId;
                    using (Transaction markerTx = ctx.Db.TransactionManager
                        .StartTransaction())
                    {
                        BlockReference copyFrame = (BlockReference)markerTx.GetObject(
                            copyId, OpenMode.ForRead);
                        var marker = new DBText();
                        marker.SetDatabaseDefaults();
                        marker.Position = new Point3d(copyFrame.Position.X + 2000,
                            copyFrame.Position.Y - 3000, 0);
                        marker.Rotation = 0d; // 横排是 D-043 的候选判据。
                        marker.Height = 300d;
                        marker.TextString = "3000mm";
                        markerId = ctx.AddToCurrentSpace(markerTx, marker);
                        markerTx.Commit();
                    }
                    // 夹具自检：这段文字同时是 D-043 候选和电缆长度——排除是
                    // 守卫后唯一防双算的闸门。
                    Check(DeviceReserveMarker.TryParseMillimeters("3000mm",
                            out double markerMm) && Math.Abs(markerMm - 3000d) < 0.001,
                        "夹具自检：3000mm 应是 D-043 预留标记候选。");
                    Check(UNCAD.Core.Text.TextParser.ExtractCableLength("3000mm")
                            .HasValue,
                        "夹具自检：3000mm 同时命中电缆长度判据——不排除会双算。");
                    var markerSelection = new FillSelection
                    {
                        SourceIds = copySelection.SourceIds,
                        FrameBlockIds = copySelection.FrameBlockIds,
                        FrameInfoJsonBlockIds = copySelection.FrameInfoJsonBlockIds,
                        TextIds = new[] { markerId }
                    };
                    FrameInfoJsonRecord markerResolved = FillReserveStage.Resolve(ctx,
                        markerSelection, FillSettings.Current(), true,
                        out List<ObjectId> markerEraseIds,
                        out List<ObjectId> markerExcludedIds,
                        out bool markerCopied, out double? markerHint);
                    Check(markerCopied,
                        "携带毫米标的复制框仍应被判定为复制图框。");
                    Check(markerResolved == null
                        || string.IsNullOrWhiteSpace(markerResolved.DeviceReserveMm),
                        "复制框携带的源框毫米标不得被自动吸收为设备内预留"
                        + "（实际 DeviceReserveMm='"
                        + (markerResolved == null ? "" : markerResolved.DeviceReserveMm)
                        + "'——旧代码会把源机台的 3000 从这条暗道继承回来）。");
                    Check(markerEraseIds.Count == 0,
                        "未吸收的毫米标不得登记删除（图上文字保留给用户对照）。");
                    Check(markerExcludedIds.Count == 1 && markerExcludedIds[0] == markerId,
                        "复制框毫米标必须从米数统计排除（实际排除 "
                        + markerExcludedIds.Count + " 个），否则 3000mm 双算。");
                    Info("  ⑤ 复制框毫米标: 不吸收、不删除、只从统计排除（防暗道"
                        + "继承+防双算）: 通过");
                    using (Transaction cleanupTx = ctx.Db.TransactionManager
                        .StartTransaction())
                    {
                        cleanupTx.GetObject(markerId, OpenMode.ForWrite).Erase();
                        cleanupTx.Commit();
                    }
                }
                tx.Commit();
            }
        }

        // ================================================================== phase 6

        /// <summary>
        /// 整机台搬场重绑自检（D-052-C，2026-11-10）：「同文件复制后删除源框」是
        /// 「跨文件复制/搬场」的等价物（原句柄已不在本图，身份未变）。断言：
        /// ①搬场收下——句柄失配但身份一致且原句柄已消失 → Read 判搬场收下记录
        ///   （非外来），FillReserveStage 不再标记复制框、预留恢复通道重新打开；
        /// ②改号仍拒——同一克隆用别的机台身份读取 → 外来拒绝照旧（复制去定义
        ///   另一台设备的语义不回归 3.0.0.6 之前）；
        /// ③撞号免疫（3.0.0.14）——记录句柄指向无关实体（直线）时不得误判
        ///   “源框还在”，身份一致仍判搬场收下。
        /// </summary>
        private static void MovedFrameRebindDefense(CadContext ctx, Fixture fixture,
            Dictionary<FrameSlot, FillSelection> selections)
        {
            Info("");
            Info("=== 整机台搬场重绑（D-052-C） ===");
            FrameSlot source = fixture.Slots.First(slot => !slot.Legacy);
            const string foreignCableInfo =
                "电缆ZB-YJV-4*16mm²: 2600+3400（盘内）+3000（设备内）=9000mm";

            ObjectId movedId;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                BlockReference sourceFrame = (BlockReference)tx.GetObject(source.FrameId,
                    OpenMode.ForRead, true);
                ObjectId definitionId = FrameRegionCollector.DefinitionIds(sourceFrame)
                    .First();
                var moved = new BlockReference(new Point3d(sourceFrame.Position.X
                    + FrameGapX * 2, sourceFrame.Position.Y - LegacyFrameHeight * 3, 0),
                    definitionId);
                ctx.AddToCurrentSpace(tx, moved);
                bool cableInfoAttached = false;
                foreach (KeyValuePair<string, string> pair in Attributes(tx, source.FrameId))
                {
                    if (pair.Key.Equals(FrameBlockFiller.TagCable,
                            StringComparison.OrdinalIgnoreCase))
                        cableInfoAttached = true;
                    AttachAttribute(ctx, tx, moved, pair.Key,
                        pair.Key.Equals(FrameBlockFiller.TagCable,
                            StringComparison.OrdinalIgnoreCase)
                            ? foreignCableInfo : pair.Value);
                }
                // F5 模板框可能没有 CABLE_INFO 实例属性（阶段 5 同款兜底）——
                // 没有就补挂，保证恢复通道有源可读。
                if (!cableInfoAttached)
                    AttachAttribute(ctx, tx, moved, FrameBlockFiller.TagCable,
                        foreignCableInfo);
                // 模板 F5 框的 MACHINEID-* 实例属性不存在（物理身份读取落到定义
                // 占位值 “MachineID/Device”）——真实场景里搬场框的物理身份与
                // JSON 记录一致才会触发重绑。给克隆框挂上真实机台身份属性。
                AttachAttribute(ctx, tx, moved, FrameBlockFiller.TagPower,
                    FrameBlockFiller.PowerText(source.Machine));
                AttachAttribute(ctx, tx, moved, FrameBlockFiller.TagDevice,
                    FrameBlockFiller.DeviceText(source.Machine));
                movedId = moved.ObjectId;
                // 搬场等价物：源框从本图消失（原句柄不复存在）。
                ((BlockReference)tx.GetObject(source.FrameId, OpenMode.ForWrite, true))
                    .Erase();
                tx.Commit();
            }
            Info("  已克隆源框并删除源框 → 搬场框 " + movedId.Handle);

            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx,
                    new[] { movedId });
                Check(regions.Errors.Count == 0 && regions.Groups.Count == 1,
                    "搬场图框应被分区识别（分组 " + regions.Groups.Count
                    + "，错误: " + string.Join("; ", regions.Errors) + "）。");
                var definitions = new CadBlockDefinitionReader(tx);
                FillSelection movedSelection = FillSelectionCollector.Split(tx,
                    regions.Groups[0].EntityIds.ToArray(), true, definitions);
                Check(movedSelection.FrameBlockIds.Length == 1,
                    "搬场图框选集应识别出唯一图框块（实际 "
                    + movedSelection.FrameBlockIds.Length + " 个）。");

                // ① 搬场收下：身份一致 + 原句柄已不在本图 → 判搬场，收下记录。
                string ownHandle = FrameInfoJsonBlockWriter.OwnFrameHandle(tx,
                    movedSelection.FrameBlockIds);
                FrameInfoJsonRecord record = FrameInfoJsonBlockWriter.Read(tx,
                    FrameInfoJsonBlockWriter.ReadSources(movedSelection), ownHandle,
                    new ExistingFillIdentity
                    {
                        MachineId = source.Machine.MachineId,
                        DeviceName = source.Machine.CircuitName
                    },
                    out string foreignNote, out string movedNote);
                Check(foreignNote.Length == 0 && movedNote.Length > 0 && record != null
                    && record.MachineId == source.Machine.MachineId,
                    "搬场图框（身份未变、原句柄已消失）应被判为整机台搬场并收下记录"
                    + "（foreign='" + foreignNote + "'，moved 长度 " + movedNote.Length
                    + "，记录 " + (record == null ? "<null>" : record.MachineId)
                    + "）——否则跨文件复制会丢失全部预留/替代/历史。");
                Info("  ① 搬场收下: 记录已重绑（机台 " + record.MachineId + "）: 通过");

                // 生产身份读取（物理标注）必须与 JSON 记录一致——占位/改过号的
                // 物理身份不得触发重绑（下面的②就是这个反例）。
                FillSelectionCollector.TryReadExistingIdentity(tx, movedSelection,
                    out ExistingFillIdentity physicalIdentity, out string physicalError);
                Check(physicalIdentity != null
                    && physicalIdentity.MachineId == source.Machine.MachineId
                    && physicalIdentity.DeviceName == source.Machine.CircuitName,
                    "搬场框的物理身份应与 JSON 记录一致（实际 "
                    + (physicalIdentity == null
                        ? "<null>：" + physicalError
                        : physicalIdentity.MachineId + "/" + physicalIdentity.DeviceName)
                    + "）——夹具或生产身份读取回归。");

                // 生产链路：不再按复制框处理，预留恢复通道重新打开。
                FrameInfoJsonRecord resolved = FillReserveStage.Resolve(ctx,
                    movedSelection, FillSettings.Current(), true,
                    out List<ObjectId> eraseIds, out List<ObjectId> excludedIds,
                    out bool copiedFrameDetected, out double? reserveHint);
                Check(!copiedFrameDetected,
                    "搬场图框不应再被标记为复制框（copiedFrameDetected=true 会误清"
                    + "CABLE_INFO 并跳过预留恢复）。");
                Check(resolved != null
                    && !string.IsNullOrWhiteSpace(resolved.DeviceReserveMm)
                    && !reserveHint.HasValue,
                    "搬场图框应恢复设备内预留（实际 DeviceReserveMm='"
                    + (resolved == null ? "" : resolved.DeviceReserveMm)
                    + "'，hint=" + (reserveHint?.ToString() ?? "<null>")
                    + "）——CABLE_INFO 恢复通道应重新打开。");
                Check(eraseIds.Count == 0, "搬场图框区域内无横排毫米标，不应有删除项。");
                Info("  ① 生产链路: 非复制框，预留恢复通道打开（DeviceReserveMm="
                    + resolved.DeviceReserveMm + "）: 通过");

                // ② 改号仍拒：同一图框按别的机台身份读取 → 外来拒绝照旧。
                FrameInfoJsonBlockWriter.Read(tx,
                    FrameInfoJsonBlockWriter.ReadSources(movedSelection), ownHandle,
                    new ExistingFillIdentity
                    {
                        MachineId = "OTHER-MACHINE",
                        DeviceName = source.Machine.CircuitName
                    },
                    out string foreignOther, out string movedOther);
                Check(foreignOther.Length > 0 && movedOther.Length == 0,
                    "身份不符（复制后改号/幽灵记录）必须维持外来拒绝"
                    + "（foreign 长度 " + foreignOther.Length + "，moved 长度 "
                    + movedOther.Length + "）——3.0.0.6 语义不得回归。");
                Info("  ② 改号仍拒: 身份不符时外来拒绝照旧: 通过");
                tx.Commit();
            }

            // ③ 撞号免疫（3.0.0.14）：跨文件复制后，记录里的原句柄号可能与目标图
            // 的无关实体（图层/线型等表记录、直线、文字）撞号——句柄存在 ≠ 源框
            // 还在。构造：记录句柄指向一根普通直线、身份一致 → 必须仍判搬场收下。
            ObjectId colliderLineId, colliderFrameId;
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                var colliderLine = new Line(new Point3d(0, -400000, 0),
                    new Point3d(1000, -400000, 0));
                ctx.AddToCurrentSpace(tx, colliderLine);
                BlockReference movedFrame = (BlockReference)tx.GetObject(movedId,
                    OpenMode.ForRead, true);
                ObjectId definitionId = FrameRegionCollector.DefinitionIds(movedFrame)
                    .First();
                var collider = new BlockReference(new Point3d(
                    movedFrame.Position.X + FrameGapX,
                    movedFrame.Position.Y - LegacyFrameHeight, 0), definitionId);
                ctx.AddToCurrentSpace(tx, collider);
                var collisionRecord = new FrameInfoJsonRecord
                {
                    MachineId = source.Machine.MachineId,
                    DeviceName = source.Machine.CircuitName,
                    FrameHandle = colliderLine.ObjectId.Handle.ToString()
                };
                AttachAttribute(ctx, tx, collider,
                    FrameInfoJsonBlockWriter.FrameInfoTag,
                    FrameInfoJsonCodec.Serialize(collisionRecord));
                AttachAttribute(ctx, tx, collider, FrameBlockFiller.TagPower,
                    FrameBlockFiller.PowerText(source.Machine));
                AttachAttribute(ctx, tx, collider, FrameBlockFiller.TagDevice,
                    FrameBlockFiller.DeviceText(source.Machine));
                colliderLineId = colliderLine.ObjectId;
                colliderFrameId = collider.ObjectId;
                tx.Commit();
            }
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx,
                    new[] { colliderFrameId });
                Check(regions.Errors.Count == 0 && regions.Groups.Count == 1,
                    "撞号图框应被分区识别（分组 " + regions.Groups.Count
                    + "，错误: " + string.Join("; ", regions.Errors) + "）。");
                var definitions = new CadBlockDefinitionReader(tx);
                FillSelection colliderSelection = FillSelectionCollector.Split(tx,
                    regions.Groups[0].EntityIds.ToArray(), true, definitions);
                FrameInfoJsonBlockWriter.Read(tx,
                    FrameInfoJsonBlockWriter.ReadSources(colliderSelection),
                    FrameInfoJsonBlockWriter.OwnFrameHandle(tx,
                        colliderSelection.FrameBlockIds),
                    new ExistingFillIdentity
                    {
                        MachineId = source.Machine.MachineId,
                        DeviceName = source.Machine.CircuitName
                    },
                    out string foreignCollider, out string movedCollider);
                Check(foreignCollider.Length == 0 && movedCollider.Length > 0,
                    "记录句柄撞上无关实体（直线）时不得误判同文件复制——身份一致"
                    + "必须仍判搬场收下（foreign 长度 " + foreignCollider.Length
                    + "，moved 长度 " + movedCollider.Length
                    + "）——否则跨文件搬场在实体较多的图纸上基本必被误拒。");
                Info("  ③ 撞号免疫: 句柄指向直线（非图框块）仍判搬场收下: 通过");
                // 清理自测实体（先断言后清理：擦除后句柄失效，断言就失真了）。
                tx.GetObject(colliderFrameId, OpenMode.ForWrite, true).Erase();
                tx.GetObject(colliderLineId, OpenMode.ForWrite, true).Erase();
                tx.Commit();
            }
        }

        // ================================================================== helpers

        private sealed class TableSnapshot
        {
            public int Rows;
            public int Columns;
            public double[] RowHeights = new double[0];
            public double[] ColumnWidths = new double[0];
            public Point3d Position;
        }

        private static TableSnapshot ReadTableSnapshot(CadContext ctx, ObjectId tableId)
        {
            using (Transaction tx = ctx.Db.TransactionManager.StartTransaction())
            {
                var table = (Table)tx.GetObject(tableId, OpenMode.ForRead, true);
                var snapshot = new TableSnapshot
                {
                    Rows = table.Rows.Count,
                    Columns = table.Columns.Count,
                    RowHeights = Enumerable.Range(0, table.Rows.Count)
                        .Select(row => table.Rows[row].Height).ToArray(),
                    ColumnWidths = Enumerable.Range(0, table.Columns.Count)
                        .Select(column => table.Columns[column].Width).ToArray(),
                    Position = table.Position
                };
                tx.Commit();
                return snapshot;
            }
        }

        private static string V1Value(string json, string key)
        {
            FrameInfoJsonRecord record = FrameInfoJsonCodec.Parse(json);
            if (record == null)
                throw new InvalidOperationException("夹具 v1 JSON 无法被解码器解析: " + key);
            switch (key)
            {
                case "boqCableModel": return record.BoqCableModel;
                case "originalCableModel": return record.OriginalCableModel;
                case "measuredCableM": return record.MeasuredCableM;
                default: throw new ArgumentOutOfRangeException(nameof(key));
            }
        }

        private static Dictionary<string, string> Attributes(Transaction tx,
            ObjectId frameId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            BlockReference frame = (BlockReference)tx.GetObject(frameId,
                OpenMode.ForRead, true);
            foreach (ObjectId id in frame.AttributeCollection)
            {
                if (tx.GetObject(id, OpenMode.ForRead, true) is AttributeReference
                    attribute)
                    result[attribute.Tag ?? ""] = attribute.TextString ?? "";
            }
            return result;
        }

        private static string DefinitionName(Transaction tx, ObjectId frameId)
        {
            BlockReference frame = (BlockReference)tx.GetObject(frameId,
                OpenMode.ForRead, true);
            ObjectId definitionId = FrameRegionCollector.DefinitionIds(frame)
                .FirstOrDefault();
            if (definitionId.IsNull) return "";
            var definition = (BlockTableRecord)tx.GetObject(definitionId,
                OpenMode.ForRead, true);
            return definition?.Name ?? "";
        }

        private static string[] DefinitionTags(Transaction tx, ObjectId definitionId)
        {
            var definition = (BlockTableRecord)tx.GetObject(definitionId,
                OpenMode.ForRead, true);
            var tags = new List<string>();
            foreach (ObjectId entityId in definition)
            {
                if (tx.GetObject(entityId, OpenMode.ForRead, true) is
                    AttributeDefinition attribute && !attribute.Constant)
                    tags.Add(attribute.Tag ?? "");
            }
            return tags.ToArray();
        }

        private static void Check(bool condition, string message)
        {
            if (condition) return;
            throw new InvalidOperationException("[" + CommandName + " 断言失败] "
                + message);
        }

        private static void Info(string line)
        {
            Report.Add(line);
            try
            {
                Application.DocumentManager.MdiActiveDocument.Editor
                    .WriteMessage("\n" + line + "\n");
            }
            catch
            {
                // 控制台不可用时报告文件仍然完整。
            }
        }

        private static void Dump()
        {
            string path = Environment.GetEnvironmentVariable(ReportVariable);
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "uncad-migration-selftest.txt");
            try
            {
                File.WriteAllText(path, string.Join("\r\n", Report.ToArray()),
                    new UTF8Encoding(true));
                Application.DocumentManager.MdiActiveDocument.Editor
                    .WriteMessage("\n" + CommandName + " report: " + path + "\n");
            }
            catch
            {
                // 报告文件写不进去时，结果文件与控制台仍然可用。
            }
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable(ResultVariable);
            if (string.IsNullOrWhiteSpace(path)) return;
            // 带 BOM：run.ps1 用 Get-Content -Raw 读取，FAIL 明细里的中文不能变乱码。
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
    }
}
