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
        public void DefaultList_ParsesNineLevelsWithoutDemoCabinets()
        {
            var levels = PreviewReferenceLevels.Parse(
                PreviewReferenceLevels.DefaultList);
            Assert.Equal(9, levels.Count);
            // 楼层链累计：2F工字钢顶面=7800+3200、2F葡萄架=7800+5500、
            // 3F地面=13300+1700（=11000+4000 自洽）、3F高架地板=15000+600。
            Assert.Equal(11000.0, levels.First(l => l.Label == "2F工字钢顶面").ZMillimetres);
            Assert.Equal(13300.0, levels.First(l => l.Label == "2F葡萄架顶面").ZMillimetres);
            Assert.Equal(15000.0, levels.First(l => l.Label == "3F地面").ZMillimetres);
            Assert.Equal(15600.0, levels.First(l => l.Label == "3F高架地板面").ZMillimetres);
            Assert.Equal(3660.0, levels.First(l => l.Label == "第一层母线").ZMillimetres);
            Assert.Equal(4900.0, levels.First(l => l.Label == "第三层母线").ZMillimetres);
            Assert.DoesNotContain(levels, level =>
                level.Label == "I-L盘高度" || level.Label == "插座盘安装");
            // 3.3.7：默认清单带类型（第二轮用户口径：2F=环氧地坪、3F=华夫
            // 圆孔板、母线=半透明玻璃层；页面按类型画实体几何）。
            Assert.Equal("steel", levels.First(l => l.Label == "1F工字钢顶面").Kind);
            Assert.Equal("epoxy", levels.First(l => l.Label == "2F地面").Kind);
            Assert.Equal("grating", levels.First(l => l.Label == "2F葡萄架顶面").Kind);
            Assert.Equal("waffle", levels.First(l => l.Label == "3F地面").Kind);
            Assert.Equal("raised", levels.First(l => l.Label == "3F高架地板面").Kind);
            Assert.Equal("bus", levels.First(l => l.Label == "第二层母线").Kind);
            // 三轮：默认清单带「本层=2F地面」指令（不进清单）+ 设备/母线锚定
            // 2F地面（高度相对本层地面起算）。
            Assert.DoesNotContain(levels, l => l.Label == "本层");
            Assert.True(PreviewReferenceLevels.TryParseBaseFloor(
                PreviewReferenceLevels.DefaultList, out string baseFloor));
            Assert.Equal("2F地面", baseFloor);
            Assert.Equal("2F地面",
                levels.First(l => l.Label == "第一层母线").AnchorLabel);
            Assert.Equal("",
                levels.First(l => l.Label == "2F地面").AnchorLabel);
        }

        [Fact]
        public void Envelope_ResolvesAnchorsAndBaseFloor()
        {
            // 三轮用户口径：母线/设备高度相对本层地面（「第一/2/3层母线放到
            // 二层」）——信封按锚定条目折绝对标高（7800+3660=11460），并把
            // 「本层」解析成 BaseFloorZ 供页面整栋楼重定位（本层地面=场景 0）。
            var model = new QuickLineOrbitPreviewModel(new[]
            {
                new QuickLineOrbitPreviewSegment("s1",
                    new QuickLineSpatialPoint(0, 0, 0),
                    new QuickLineSpatialPoint(9600, 0, 0),
                    QuickLineSpatialAxis.X, 9600, true)
            }, "", null);
            string config = "本层=2F地面;2F地面=7800=环氧地坪"
                + ";第一层母线=3660=母线=2F地面;孤锚=500=母线=不存在的层";
            PreviewReferenceLevels.TryParseBaseFloor(config, out string baseFloor);
            QuickLineOrbitPreviewEnvelope envelope =
                QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse(config), baseFloor);
            Assert.Equal(3, envelope.ReferenceLevels.Count);
            Assert.Equal(11460.0, envelope.ReferenceLevels
                .First(l => l.Label == "第一层母线").Z);
            // 锚定找不到保持原值不猜。
            Assert.Equal(500.0, envelope.ReferenceLevels
                .First(l => l.Label == "孤锚").Z);
            Assert.Equal(7800.0, envelope.BaseFloorZ);
            // 无指令 → BaseFloorZ=0（1F 绝对标高口径，3.3.6 兼容）。
            QuickLineOrbitPreviewEnvelope plain =
                QuickLineOrbitPreviewEnvelope.Build(model,
                    PreviewReferenceLevels.Parse("2F地面=7800=环氧地坪"), null);
            Assert.Equal(0.0, plain.BaseFloorZ);
            // 序列化契约：BaseFloorZ 进 JS。
            string json = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Serialize(envelope);
            Assert.Contains("\"BaseFloorZ\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Parse_OptionalKindThirdSegment_NormalizedOrFrame()
        {
            // 三段式「标签=毫米=类型」；中英关键词归一化；缺省/空/认不出
            // 一律 frame（3.3.6 旧清单两段式兼容）。
            var levels = PreviewReferenceLevels.Parse(
                "楼板=3000=地面;梁顶=2850=STEEL;架空=600=高架地板"
                + ";华夫顶=15000=华夫板;树脂面=7800=环氧树脂;旧条目=7800"
                + ";空类型=900=;乱写=500=不明物;配电箱=1800=柜");
            Assert.Equal(9, levels.Count);
            Assert.Equal("floor", levels[0].Kind);
            Assert.Equal("steel", levels[1].Kind);
            Assert.Equal("raised", levels[2].Kind);
            Assert.Equal("waffle", levels[3].Kind);
            Assert.Equal("epoxy", levels[4].Kind);
            Assert.Equal("frame", levels[5].Kind);
            Assert.Equal("frame", levels[6].Kind);
            Assert.Equal("frame", levels[7].Kind);
            Assert.Equal("device", levels[8].Kind);
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
                "缺等号;=7800;标签=;标签=abc;标签=-5;标签=0;标签=1e999"
                + ";五段=1=2=3=4;类型非法值不救数值=abc=母线;好的=3660;;  ");
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
                PreviewReferenceLevels.Parse("2F地面=7800=地面"));
            string json = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Serialize(envelope);
            Assert.Contains("\"ReferenceLevels\"", json, StringComparison.Ordinal);
            Assert.Contains("2F地面", json, StringComparison.Ordinal);
            // 3.3.7：Kind 进 PascalCase 序列化契约（页面按它分派几何）。
            Assert.Contains("\"Kind\"", json, StringComparison.Ordinal);
            Assert.Contains("\"floor\"", json, StringComparison.Ordinal);
        }
    }
}
