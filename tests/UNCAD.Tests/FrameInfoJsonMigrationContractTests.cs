using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 旧图框 frameinfo_json 的迁移顺序契约：
    /// XFrameMigrationService 在写入事务里会从图框块参照上移除旧属性
    /// （FRAMEINFO_JSON/BRIDGE_FRAME_INFO/CONDUIT_INFO），所以 previous 记录必须
    /// **在迁移之前**读出（预检/写入事务之前），并且要包含图框块本身。
    /// </summary>
    public sealed class FrameInfoJsonMigrationContractTests
    {
        [Fact]
        public void Previous_record_is_read_from_frames_before_migration_runs()
        {
            string writer = Source("src", "UNCAD", "Features", "Fill",
                "FrameInfoJsonBlockWriter.cs");
            string reader = Source("src", "UNCAD", "Cad", "Frame",
                "FrameInfoJsonBlockReader.cs");
            string batchPreflight = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string fill = Source("src", "UNCAD", "Features", "Fill", "FillFeature.cs");

            // 读取范围必须包含图框块（旧内嵌 JSON 在帧块上）。
            Assert.Contains("FrameBlockIds", reader);
            Assert.Contains("ReadSources(", writer);

            // 批量：预检（2026-11-09 拆到 BatchFillUpdatePreflight.Build，早于写事务
            // 执行）读取 previous，写入阶段直接复用 plan.PreviousRecord，因此文件内
            // 的先后顺序不适用；改为锁定"读-存-用"三点。
            Assert.Contains("FrameInfoJsonBlockWriter.ReadSources(selection)", batchPreflight);
            Assert.Contains("PreviousRecord = previousRecord", batchPreflight);
            Assert.Contains("XFrameMigrationService.Migrate(", batch);

            // 单框：读取在 FillReserveStage（2026-11-05 前置到统计之前，自然早于
            // 写事务里的迁移），编排类先跑预留阶段再进写事务。
            int fillRead = fill.IndexOf("FillReserveStage.Resolve(ctx",
                System.StringComparison.Ordinal);
            int fillMigrate = fill.IndexOf("XFrameMigrationService.Migrate(",
                System.StringComparison.Ordinal);
            Assert.True(fillRead >= 0 && fillMigrate > fillRead,
                "单框 U1F/U1U 必须在迁移前读取旧内嵌 frameinfo_json");
            Assert.Contains("FrameInfoJsonBlockWriter.ReadSources(selection)",
                Source("src", "UNCAD", "Features", "Fill", "FillReserveStage.cs"));
        }

        [Fact]
        public void D040_json_reads_at_every_read_site_include_the_frame_block()
        {
            // D-040（2026-11-02）后 JSON 主存储 = 图框块内 FRAMEINFO 属性，独立
            // frameinfo_json 块仅是遗留读源。任何把 FrameInfoJsonBlockIds 直接
            // 传给 Read 的只读调用点都会漏读新模板（身份仲裁、U1U 对比窗、
            // 电缆替代型号、母线插接箱选择）。统一走 ReadSources(selection)。
            // 2026-11-04：还必须带本图框句柄校验（OwnFrameHandle）——复制图框
            // 会把源框 JSON 一起带过来，不带校验的读取会静默继承别的图框的
            // 设备内预留/替代型号/更新历史（用户报告）。
            string[] files =
            {
                "src/UNCAD/Cad/Frame/FillSelectionCollector.cs",
                "src/UNCAD/Features/Fill/FillFeature.cs",
                "src/UNCAD/Features/Fill/FillMachinePickStage.cs",
                "src/UNCAD/Features/Fill/FillPlanningStage.cs",
                "src/UNCAD/Features/Fill/BatchFillUpdatePreflight.cs"
            };
            foreach (string file in files)
            {
                string source = Source(file.Split('/'));
                // 读取必须经网关：ReadSources（含图框块）或 ReadOwn（封装
                // ReadSources + 句柄校验）。
                Assert.True(
                    source.Contains("FrameInfoJsonBlockWriter.ReadSources(")
                    || source.Contains("FrameInfoJsonBlockReader.ReadSources(")
                    || source.Contains("FrameInfoJsonBlockWriter.ReadOwn(")
                    || source.Contains("FrameInfoJsonBlockReader.ReadOwn("),
                    file + " 必须经 ReadSources/ReadOwn 读取 JSON（含图框块）");
                // 且必须带本图框句柄校验（复制图框防继承）；ReadOwn 内部已带。
                Assert.True(
                    source.Contains("FrameInfoJsonBlockWriter.OwnFrameHandle(")
                    || source.Contains("FrameInfoJsonBlockReader.OwnFrameHandle(")
                    || source.Contains("FrameInfoJsonBlockWriter.ReadOwn(")
                    || source.Contains("FrameInfoJsonBlockReader.ReadOwn("),
                    file + " 的 JSON 读取必须带本图框句柄校验（复制图框防继承）");
                Assert.DoesNotMatch(
                    @"FrameInfoJsonBlock(?:Writer|Reader)\.Read\(\w+,\s*(?:[\w\.]+\??\.)?FrameInfoJsonBlockIds\b",
                    source);
                // 裸 ctx 两参读取（不带句柄）不得回流。
                Assert.DoesNotMatch(
                    @"FrameInfoJsonBlock(?:Writer|Reader)\.Read\(ctx,\s*FrameInfoJsonBlock(?:Writer|Reader)\.ReadSources",
                    source);
            }
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            var all = new string[parts.Length + 1];
            all[0] = root;
            System.Array.Copy(parts, 0, all, 1, parts.Length);
            return File.ReadAllText(Path.Combine(all));
        }
    }
}
