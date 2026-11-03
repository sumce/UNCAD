using System.IO;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// frameinfo_json 框句柄绑定（2026-11-02 幽灵机台修复）：
    /// 图框移动后遗留的 JSON 块被几何错误归属时，读取方按 meta.frameHandle
    /// 拒绝外块；老 JSON 无句柄保持兼容放行。
    /// </summary>
    public sealed class FrameInfoJsonHandleBindingTests
    {
        [Fact]
        public void Codec_RoundTripsFrameHandle()
        {
            var record = new FrameInfoJsonRecord
            {
                MachineId = "M-2F-01",
                DeviceName = "注塑机",
                FrameHandle = "3A5F"
            };
            string json = FrameInfoJsonCodec.Serialize(record);

            Assert.Contains("\"frameHandle\"", json);
            FrameInfoJsonRecord parsed = FrameInfoJsonCodec.Parse(json);
            Assert.Equal("3A5F", parsed.FrameHandle);
        }

        [Fact]
        public void Codec_LegacyJsonWithoutHandle_ParsesEmpty()
        {
            // 老版 JSON：meta 没有 frameHandle 键，必须照常解析且句柄为空。
            var record = new FrameInfoJsonRecord { MachineId = "M-2F-01" };
            string json = FrameInfoJsonCodec.Serialize(record);
            string legacy = System.Text.RegularExpressions.Regex.Replace(
                json, ",\"frameHandle\":\"[^\"]*\"", "");
            Assert.DoesNotContain("frameHandle", legacy);

            FrameInfoJsonRecord parsed = FrameInfoJsonCodec.Parse(legacy);
            Assert.Equal("M-2F-01", parsed.MachineId);
            Assert.Equal("", parsed.FrameHandle);
        }

        [Theory]
        [InlineData("3A5F", "3A5F", false)]
        [InlineData("3A5F", "3a5f", false)]
        [InlineData("3A5F", "9D44", true)]
        [InlineData("3A5F", "", false)]
        [InlineData("", "9D44", false)]
        [InlineData("", "", false)]
        public void IsForeignTo_RequiresBothHandlesToDiffer(
            string stamped, string expected, bool foreign)
        {
            var record = new FrameInfoJsonRecord { FrameHandle = stamped };
            Assert.Equal(foreign, record.IsForeignTo(expected));
        }

        [Fact]
        public void Updater_PreservesFrameHandleAcrossUpdates()
        {
            var previous = new FrameInfoJsonRecord
            {
                MachineId = "M-2F-01",
                FrameHandle = "3A5F"
            };
            var machine = new UNCAD.Core.Excel.MachineRow
            {
                MachineId = "M-2F-01",
                CircuitName = "注塑机"
            };
            FrameInfoJsonRecord next = FrameInfoJsonRecordUpdater.Update(
                previous, machine, null, "U1U", System.DateTime.UtcNow, null);

            Assert.Equal("3A5F", next.FrameHandle);
        }

        [Theory]
        [InlineData("MDEGN04", "MDEGN04", true)]
        [InlineData("mdegn04", "MDEGN04", true)]
        [InlineData("", "MDEGN04", true)]
        [InlineData("MTTSA01", "MDEGN04", false)]
        [InlineData("MTTSA01", "", false)]
        public void BelongsToMachine_GuardsInheritance(
            string recordMachine, string targetMachine, bool expected)
        {
            var record = new FrameInfoJsonRecord { MachineId = recordMachine };
            Assert.Equal(expected, record.BelongsToMachine(targetMachine));
        }

        [Fact]
        public void Writer_InheritanceGuard_IsWired()
        {
            // 源码契约：继承路径（块内嵌入属性、遗留独立块）都必须走
            // BelongsToMachine 守卫，否则他机遗留数据仍会污染新记录。
            string writer = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FrameInfoJsonBlockWriter.cs"));
            Assert.Contains("丢弃他机遗留 frameinfo_json", writer);
            // 三处守卫：入参 previousRecord、块内嵌入属性读取、遗留独立块读取。
            Assert.Equal(3, CountOccurrences(writer, "BelongsToMachine(machine.MachineId)"));
            // D-040：JSON 主存储 = 图框块内 FRAMEINFO 属性；不再新建独立块。
            Assert.Contains("\"FRAMEINFO\"", writer);
            Assert.Contains("WriteFrameJsonAttribute", writer);
            Assert.Contains("HasLegacyJsonAttribute", writer);
            Assert.DoesNotContain("CreateMetadataBlock", writer);
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(token, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        [Fact]
        public void IdentityPath_ThreadsFrameHandleIntoJsonRead()
        {
            // 源码契约：统计身份链必须把图框句柄传进 JSON 校验，否则幽灵机台修复失效。
            string reader = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Submit", "FrameIdentityReader.cs"));
            string collector = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FillSelectionCollector.cs"));
            string writer = File.ReadAllText(RepoFile(
                "src", "UNCAD", "Features", "Fill", "FrameInfoJsonBlockWriter.cs"));

            Assert.Contains("region.Handle, out ExistingFillIdentity", reader);
            Assert.Contains("string expectedFrameHandle", collector);
            Assert.Contains("out string foreignJsonNote", collector);
            Assert.Contains("IsForeignTo(expectedFrameHandle)", writer);
        }

        private static string RepoFile(params string[] parts)
        {
            string root = Path.GetFullPath(System.AppContext.BaseDirectory
                + "\\..\\..\\..\\..\\..");
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
