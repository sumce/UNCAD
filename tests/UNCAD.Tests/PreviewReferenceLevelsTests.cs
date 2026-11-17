using System;
using System.Linq;
using UNCAD.Core.QuickLine;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.3.6（D-054 Amendment）：U1U 3D 预览**参照层**清单解析（用户提供的
    /// 建筑标高链 + 设备高度；楼层链按累计标高折算进默认表）与信封携带锁死。
    /// 清单是 U1SET 配置（UNC_PREVIEW_REF_LEVELS）——不同项目标高不同，
    /// 页面不写死；解析纯函数、非法条目跳过不炸。
    /// </summary>
    public sealed class PreviewReferenceLevelsTests
    {
        [Fact]
        public void DefaultList_ParsesAllTwelveLevelsWithCumulativeFloors()
        {
            var levels = PreviewReferenceLevels.Parse(
                PreviewReferenceLevels.DefaultList);
            Assert.Equal(12, levels.Count);
            // 楼层链累计：2F工字钢顶面=7800+3200、2F葡萄架=7800+5500、
            // 3F地面=13300+1700（=11000+4000 自洽）、3F高架地板=15000+600。
            Assert.Equal(11000.0, levels.First(l => l.Label == "2F工字钢顶面").ZMillimetres);
            Assert.Equal(13300.0, levels.First(l => l.Label == "2F葡萄架顶面").ZMillimetres);
            Assert.Equal(15000.0, levels.First(l => l.Label == "3F地面").ZMillimetres);
            Assert.Equal(15600.0, levels.First(l => l.Label == "3F高架地板面").ZMillimetres);
            // 设备高度原值。
            Assert.Equal(3660.0, levels.First(l => l.Label == "第一层母线").ZMillimetres);
            Assert.Equal(4900.0, levels.First(l => l.Label == "第三层母线").ZMillimetres);
            Assert.Equal(2200.0, levels.First(l => l.Label == "I-L盘顶面").ZMillimetres);
        }

        [Fact]
        public void Parse_SeparatorsAndWhitespace_Accepted()
        {
            var levels = PreviewReferenceLevels.Parse(
                " 2F地面 = 7800 ; \r\n第一层母线=3660\n\n插座盘安装=1000 ");
            Assert.Equal(3, levels.Count);
            Assert.Equal("2F地面", levels[0].Label);
            Assert.Equal(7800.0, levels[0].ZMillimetres);
            Assert.Equal(3660.0, levels[1].ZMillimetres);
            Assert.Equal(1000.0, levels[2].ZMillimetres);
        }

        [Fact]
        public void Parse_InvalidEntries_SkippedNotFatal()
        {
            var levels = PreviewReferenceLevels.Parse(
                "缺等号;=7800;标签=;标签=abc;标签=-5;标签=0;标签=1e999;好的=3660;;  ");
            Assert.Single(levels);
            Assert.Equal("好的", levels[0].Label);
            Assert.Equal(3660.0, levels[0].ZMillimetres);
        }

        [Fact]
        public void Parse_NullOrEmpty_ReturnsEmptyList()
        {
            Assert.Empty(PreviewReferenceLevels.Parse(null));
            Assert.Empty(PreviewReferenceLevels.Parse(""));
            Assert.Empty(PreviewReferenceLevels.Parse("  \r\n "));
        }

        [Fact]
        public void Envelope_CarriesReferenceLevels_AndLabelsJoinHeight()
        {
            var model = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true)
            }, "", null);
            var levels = PreviewReferenceLevels.Parse("2F地面=7800;第一层母线=3660");
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model, levels);
            Assert.NotNull(envelope);
            Assert.Equal(2, envelope.ReferenceLevels.Count);
            Assert.Equal("2F地面", envelope.ReferenceLevels[0].Label);
            Assert.Equal(7800.0, envelope.ReferenceLevels[0].Z);
            // null 清单 → 空参照层（页面不画）。
            QuickLineOrbitPreviewEnvelope plain =
                QuickLineOrbitPreviewEnvelope.Build(model, null);
            Assert.Empty(plain.ReferenceLevels);
        }

        [Fact]
        public void Serialize_ReferenceLevelsKey_IsTheJsContract()
        {
            var model = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(4000, 0, 0),
                    QuickLineSpatialAxis.X, 4000, true)
            }, "", null);
            var envelope = QuickLineOrbitPreviewEnvelope.Build(model,
                PreviewReferenceLevels.Parse("2F地面=7800"));
            string json = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Serialize(envelope);
            Assert.Contains("\"ReferenceLevels\"", json, StringComparison.Ordinal);
            Assert.Contains("2F地面", json, StringComparison.Ordinal);
        }
    }
}
