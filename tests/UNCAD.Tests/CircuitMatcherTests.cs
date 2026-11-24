using System;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-057 回路配对内核：同机台同名回路（上游编号不同）的台账行 ↔ 图框配对。
    /// 规则：身份键优先一一消耗；没配掉的图框只在回路名唯一时按名配对；
    /// 同名回路不按名猜（记为待确认）；同键/唯一同名多出的图框是重复。
    /// </summary>
    public sealed class CircuitMatcherTests
    {
        private const string Machine = "MEDPC01";
        private const string Circuit = "AC Rack";

        private static CircuitSelection Row(string upstream, string name = Circuit)
            => CircuitSelection.Of(new MachineRow
                { MachineId = Machine, CircuitName = name, Fr = upstream });

        private static CircuitSelection Frame(string upstream, string handle,
            string key = null, string name = Circuit)
            => new CircuitSelection
            {
                MachineId = Machine,
                CircuitName = name,
                UpstreamId = upstream ?? "",
                CircuitKey = key ?? (upstream == null
                    ? "" : CircuitIdentityKey.Of(new MachineRow
                        { MachineId = Machine, CircuitName = name, Fr = upstream })),
                Handle = handle
            };

        private static CircuitMatchResult Pair(
            CircuitSelection[] expected, CircuitSelection[] drawn, bool baseline = true)
            => CircuitMatcher.Pair(Machine, expected, drawn, baseline);

        [Fact]
        public void SameNameDifferentUpstream_BothKeyed_PairsEachByItsOwnKey()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame("3N-31D2A-PP-03", "h2"), Frame("3N-11B1A-PP-02", "h1") });

            Assert.True(result.Complete);
            Assert.Equal(2, result.Pairs.Count);
            Assert.All(result.Pairs, pair => Assert.True(pair.ByKey));
            Assert.Empty(result.Missing);
            Assert.Empty(result.Extra);
            Assert.Empty(result.Duplicates);
            Assert.Empty(result.Unresolved);
        }

        [Fact]
        public void SameNameOnlyOneKeyedFrame_ReportsTheOtherWithItsUpstream()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame("3N-11B1A-PP-02", "h1") });

            Assert.False(result.Complete);
            CircuitSelection missing = Assert.Single(result.CertainMissing);
            Assert.Equal("3N-31D2A-PP-03", missing.UpstreamId);
            Assert.Equal(1, CircuitMatchText.MissingCount(result));
            // 缺失行带上游编号后缀，用户能分清缺的是哪一条。
            Assert.Equal("AC Rack（上游 3N-31D2A-PP-03）",
                Assert.Single(CircuitMatchText.Missing(result)));
            Assert.Empty(result.Unresolved);
        }

        [Fact]
        public void SameNameKeylessFrame_IsUnresolvedAndNeverGuessed()
        {
            // 老图框/复制来的图框没有上游编号：不能按名替它认领其中一条。
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame(null, "h1", key: "") });

            Assert.False(result.Complete);
            CircuitSelection pending = Assert.Single(result.Unresolved);
            Assert.Equal("h1", pending.Handle);
            Assert.Empty(result.CertainMissing);
            // 能确定“缺一张”，不能确定缺的是哪一条。
            Assert.Equal(1, CircuitMatchText.MissingCount(result));
            string text = Assert.Single(CircuitMatchText.Missing(result));
            Assert.Contains("AC Rack", text, StringComparison.Ordinal);
            Assert.Contains("2 条同名回路", text, StringComparison.Ordinal);
            Assert.Contains("h1", text, StringComparison.Ordinal);
            Assert.Contains("其中缺 1 张", text, StringComparison.Ordinal);
            Assert.DoesNotContain("上游 3N-", text, StringComparison.Ordinal);
            Assert.False(result.IsUniqueName(Circuit));
        }

        [Fact]
        public void SameNameTwoKeylessFrames_TwoPendingAndNoMissing()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame(null, "h1", key: ""), Frame(null, "h2", key: "") });

            // 数量对得上（不缺），但对应关系没核实——整机校验不放行。
            Assert.Equal(0, CircuitMatchText.MissingCount(result));
            Assert.Equal(2, result.Unresolved.Count);
            Assert.Equal(2, result.DistinctDrawnCount);
            Assert.False(result.Complete);
            Assert.Single(CircuitMatchText.Unresolved(result));
        }

        [Fact]
        public void SecondFrameWithTheSameKey_IsACertainDuplicateOfTheFirst()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame("3N-11B1A-PP-02", "h1"), Frame("3N-11B1A-PP-02", "h2") });

            Assert.Single(result.Pairs);
            CircuitDuplicate duplicate = Assert.Single(result.Duplicates);
            Assert.True(duplicate.Certain);
            Assert.Equal("h2", duplicate.Drawn.Handle);
            Assert.Equal("h1", duplicate.Original.Handle);
            // 重复的那张不能再去顶替另一条同名回路。
            Assert.Equal("3N-31D2A-PP-03", Assert.Single(result.CertainMissing).UpstreamId);
            Assert.False(result.Complete);
        }

        [Fact]
        public void SurplusKeylessFrame_AfterAllSameNameRowsPaired_IsADuplicate()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[]
                {
                    Frame("3N-11B1A-PP-02", "h1"), Frame("3N-31D2A-PP-03", "h2"),
                    Frame(null, "h3", key: "")
                });

            Assert.Equal(2, result.Pairs.Count);
            CircuitDuplicate duplicate = Assert.Single(result.Duplicates);
            Assert.True(duplicate.Certain);
            Assert.Null(duplicate.Original);
            Assert.Empty(result.Unresolved);
            Assert.Empty(result.Missing);
        }

        [Fact]
        public void StaleKeyOnDuplicateName_IsUnresolvedNotNameMatched()
        {
            // 上游改挂后帧键过期：名字又有重名——说不出它现在是哪一条。
            CircuitMatchResult result = Pair(
                new[] { Row("3N-11B1A-PP-02"), Row("3N-31D2A-PP-03") },
                new[] { Frame("3N-OLD-PP-09", "h1") });

            CircuitSelection pending = Assert.Single(result.Unresolved);
            Assert.Equal("3N-OLD-PP-09", pending.UpstreamId);
            Assert.Empty(result.Pairs);
            Assert.Equal(1, CircuitMatchText.MissingCount(result));
        }

        [Fact]
        public void StaleKeyOnUniqueName_StillMatchesByName()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-NEW-PP-01", "LP") },
                new[] { Frame("3N-OLD-PP-01", "h1", key: CircuitIdentityKey.Of(
                    Machine, "LP", "3N-OLD-PP-01"), name: "LP") });

            CircuitPair pair = Assert.Single(result.Pairs);
            Assert.False(pair.ByKey);
            Assert.True(result.Complete);
        }

        [Fact]
        public void UniqueNameDrawnTwice_SecondFrameIsADuplicate()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-A-PP-01", "LP") },
                new[] { Frame("3N-A-PP-01", "h1", name: "LP"), Frame("3N-A-PP-01", "h2", name: "LP") });

            Assert.Single(result.Pairs);
            Assert.True(Assert.Single(result.Duplicates).Certain);
            Assert.True(result.Complete);
        }

        [Fact]
        public void UnknownCircuitName_IsExtraNotDuplicate()
        {
            CircuitMatchResult result = Pair(
                new[] { Row("3N-A-PP-01", "LP") },
                new[] { Frame("3N-A-PP-01", "h1", name: "LP"), Frame("3N-Z-PP-09", "h2") });

            CircuitSelection extra = Assert.Single(result.Extra);
            Assert.Equal("AC Rack", extra.CircuitName);
            Assert.Empty(result.Duplicates);
            Assert.True(result.Complete);
        }

        [Fact]
        public void WithoutBaseline_OnlyKeyEqualityProvesDuplication()
        {
            CircuitMatchResult certain = Pair(null,
                new[] { Frame("3N-A-PP-01", "h1"), Frame("3N-A-PP-01", "h2") },
                baseline: false);
            Assert.True(Assert.Single(certain.Duplicates).Certain);
            Assert.Equal(1, certain.DistinctDrawnCount);

            CircuitMatchResult uncertain = Pair(null,
                new[] { Frame(null, "h1", key: ""), Frame(null, "h2", key: "") },
                baseline: false);
            CircuitDuplicate duplicate = Assert.Single(uncertain.Duplicates);
            Assert.False(duplicate.Certain);
            // 同名但键不同（上游不同）的合法回路：无基准时也不是重复。
            CircuitMatchResult distinct = Pair(null,
                new[] { Frame("3N-A-PP-01", "h1"), Frame("3N-B-PP-02", "h2") },
                baseline: false);
            Assert.Empty(distinct.Duplicates);
            Assert.Equal(2, distinct.DistinctDrawnCount);
            Assert.Equal(2, CircuitMatchText.Selected(distinct).Count);
        }

        [Fact]
        public void UpstreamDisplayAndKey_StripSlotAndAxisAnnotations()
        {
            Assert.Equal("3U-UPS10D1-PP-03",
                CircuitIdentityKey.UpstreamDisplay("3U-UPS10D1-PP-03 36(43/AC)"));
            Assert.Equal("2u-ups5d1-pp-02",
                CircuitIdentityKey.UpstreamKey("2U-UPS5D1-PP-02 40-42(41/Ab)"));
            Assert.Equal(CircuitIdentityKey.UpstreamKey("ＰＰ-０２"),
                CircuitIdentityKey.UpstreamKey(" pp-02 "));
            Assert.Equal("", CircuitIdentityKey.UpstreamDisplay("  "));
        }
    }
}
