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
            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string fill = Source("src", "UNCAD", "Features", "Fill", "FillFeature.cs");

            // 读取范围必须包含图框块（旧内嵌 JSON 在帧块上）。
            Assert.Contains("FrameBlockIds", writer);
            Assert.Contains("ReadSources(", writer);

            // 批量：预检（独立方法，早于写事务执行）读取 previous，写入阶段直接复用
            // plan.PreviousRecord，因此文件内的先后顺序不适用；改为锁定"读-存-用"三点。
            Assert.Contains("FrameInfoJsonBlockWriter.ReadSources(selection)", batch);
            Assert.Contains("PreviousRecord = previousRecord", batch);
            Assert.Contains("XFrameMigrationService.Migrate(", batch);

            // 单框：U1F/U1U 共用同一处读取，也必须早于迁移。
            int fillRead = fill.IndexOf("FrameInfoJsonBlockWriter.ReadSources(selection)",
                System.StringComparison.Ordinal);
            int fillMigrate = fill.IndexOf("XFrameMigrationService.Migrate(",
                System.StringComparison.Ordinal);
            Assert.True(fillRead >= 0 && fillMigrate > fillRead,
                "单框 U1F/U1U 必须在迁移前读取旧内嵌 frameinfo_json");
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
