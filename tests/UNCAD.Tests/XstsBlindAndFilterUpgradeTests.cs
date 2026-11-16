using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.Dwg;
using UNCAD.Core.Excel;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 3.2.0.7 现场反馈三件的回归锁：
    /// ① 近名判定（MSRTL04 ↔ MPSTL04 仅一字母之差——图上/档案笔误的报警基础）；
    /// ② XSTS 盲区名单（档案有、全图无的机台 + 盲区近名提示 + 图上未入档近名
    ///    issue；现场此前靠手工 VLOOKUP 拼名单）；
    /// ③ XLAYOUT 筛选表达力（区间词 M603-M612 / 前缀词 MS* / 排除词 !MS*，
    ///    旧"子串包含 OR"行为全部保持）；
    /// ④ U1U 复制框两跑修复（首跑把残留毫米标值预填为设备内预留建议值——
    ///    用户报告"同一图框要跑两次 U1U 才能得到正确结果"）。
    /// </summary>
    public sealed class XstsBlindAndFilterUpgradeTests
    {
        // ---------------- ① 近名判定 ----------------

        [Fact]
        public void Levenshtein_OneLetterSwap_IsWithinDistanceOne()
        {
            // 现场实例：清单 MSRTL04 ↔ 图上 MPSTL04——逐位是两处替换（S↔P、
            // R↔S），编辑距离恰为 2（"就差一个字母"的视觉口语）。
            Assert.True(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MPSTL04", 2));
            Assert.False(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MPSTL04", 1));
            // 一处替换 / 一处删除 / 一处插入都在距离 1 内。
            Assert.True(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MSRTL05", 1));
            Assert.True(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MSRTL4", 1));
            Assert.True(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MSRTL045", 1));
            // 三处差异不在距离 2 内；长度差先决。
            Assert.False(LevenshteinNeighbour.IsWithinDistance("MSRTL04", "MPSTL05", 2));
            Assert.False(LevenshteinNeighbour.IsWithinDistance("ABC", "ABCDEF", 2));
            // 与全库身份口径一致：忽略空白与大小写。
            Assert.True(LevenshteinNeighbour.IsWithinDistance("msrtl 04", "MSRTL04", 0));
        }

        [Fact]
        public void FindNeighbours_SkipsExactMatch_AndCapsResults()
        {
            var candidates = new[] { "MSRTL04", "MPSTL04", "MSRTL14", "OTHER99" };
            List<string> neighbours = LevenshteinNeighbour.FindNeighbours(
                "MSRTL04", candidates, 2, 2);
            // 精确相等不算近名（那是命中）；MPSTL04（距离2）与 MSRTL14（距离1）
            // 都收，上限 2——OTHER99 距离远，不收。
            Assert.DoesNotContain("MSRTL04", neighbours);
            Assert.Contains("MPSTL04", neighbours);
            Assert.Contains("MSRTL14", neighbours);
            Assert.Equal(2, neighbours.Count);
        }

        // ---------------- ② 盲区名单 ----------------

        [Fact]
        public void BlindAnalyzer_ListsSnapshotMachinesMissingFromDrawing()
        {
            var snapshot = new List<MachineRow>
            {
                new MachineRow { MachineId = "MSRTL04", CircuitName = "A", Batch = "1", Seq = "7" },
                new MachineRow { MachineId = "MSRTL04", CircuitName = "B", Batch = "1", Seq = "7" },
                new MachineRow { MachineId = "MPSTL04", CircuitName = "A", Batch = "1", Seq = "8" },
                new MachineRow { MachineId = "PSP_M603", CircuitName = "A", Batch = "2", Seq = "1" }
            };
            var issues = new List<string>();
            List<XstsBlindMachine> blind = XstsBlindAnalyzer.Analyze(
                snapshot,
                new[] { "MPSTL04", "AMHS01" },   // 图上有的
                new[] { "AMHS01" },              // 本次选中
                issues);
            // 同机台多回路只算一台；图上有的不算盲区。
            Assert.Equal(2, blind.Count);
            Assert.Contains(blind, item => item.MachineId == "MSRTL04");
            Assert.Contains(blind, item => item.MachineId == "PSP_M603");
            // 盲区近名：MSRTL04 与图上 MPSTL04 仅一字母之差，提示点名。
            XstsBlindMachine near = blind.First(item => item.MachineId == "MSRTL04");
            Assert.Contains("MPSTL04", near.NearNameNote);
            Assert.Contains("1~2 字母之差", near.NearNameNote);
            Assert.Equal("", blind.First(item => item.MachineId == "PSP_M603").NearNameNote);
            // 图上未入档近名只对"选中"机台报：AMHS01 不在档案，但无近名 → 无 issue。
            Assert.Empty(issues);
        }

        [Fact]
        public void BlindAnalyzer_ReportsSelectedMachineNearSnapshotAsIssue()
        {
            var snapshot = new List<MachineRow>
            {
                new MachineRow { MachineId = "MSRTL04", CircuitName = "A" }
            };
            var issues = new List<string>();
            XstsBlindAnalyzer.Analyze(snapshot,
                new[] { "MSRTL05" },            // 图上（未入档）机台
                new[] { "MSRTL05" },            // 且被本次选中
                issues);
            Assert.Single(issues);
            Assert.Contains("MSRTL05", issues[0]);
            Assert.Contains("MSRTL04", issues[0]);
            Assert.Contains("1~2 字母之差", issues[0]);
        }

        [Fact]
        public void BlindAnalyzer_GroupSummary_CountsByLetterPrefix()
        {
            var snapshot = Enumerable.Range(603, 10)
                .Select(number => new MachineRow { MachineId = "M" + number })
                .Concat(Enumerable.Range(603, 10)
                    .Select(number => new MachineRow { MachineId = "PSP" + number }))
                .ToList();
            List<XstsBlindMachine> blind = XstsBlindAnalyzer.Analyze(
                snapshot, new[] { "OTHER" }, Array.Empty<string>(), new List<string>());
            Assert.Equal(20, blind.Count);
            string summary = XstsBlindAnalyzer.GroupSummary(blind);
            Assert.Contains("M×10", summary);
            Assert.Contains("PSP×10", summary);
        }

        // ---------------- ③ 筛选表达力 ----------------

        [Fact]
        public void Filter_PlainContainsOr_AndEmptyTerms_StillWork()
        {
            // 旧语义回归：子串包含 + OR + 空关键词全命中。
            Assert.True(XLayoutSelectionFilter.MatchesCircuit("MSRTL04", "照明",
                "MSRTL 8.2"));
            Assert.False(XLayoutSelectionFilter.MatchesCircuit("MSRTL04", "照明",
                "PSP OHVC"));
            Assert.True(XLayoutSelectionFilter.MatchesCircuit("MSRTL04", "照明",
                (string)null));
        }

        [Fact]
        public void Filter_RangeTerm_MatchesExactNumbersOnly()
        {
            // 区间词命中区间内编号；区间外不命中。
            Assert.True(XLayoutSelectionFilter.MatchesMachine("PSP_M605",
                XLayoutSelectionFilter.SplitTerms("M603-M612")));
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M612",
                XLayoutSelectionFilter.SplitTerms("M603-M612")));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("M602",
                XLayoutSelectionFilter.SplitTerms("M603-M612")));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("M613",
                XLayoutSelectionFilter.SplitTerms("M603-M612")));
            // 区间词按编号精确比对——子串时代的 M6030 误命中被根治。
            Assert.False(XLayoutSelectionFilter.MatchesMachine("M6030",
                XLayoutSelectionFilter.SplitTerms("M603-M612")));
            // 前缀不一致不是区间词（当普通子串处理）。
            Assert.False(XLayoutSelectionFilter.MatchesMachine("M605",
                XLayoutSelectionFilter.SplitTerms("A603-B612")));
            // 波浪线分隔同样支持。
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M604",
                XLayoutSelectionFilter.SplitTerms("M603~M612")));
        }

        [Fact]
        public void Filter_PrefixTerm_AnchorsToStart()
        {
            Assert.True(XLayoutSelectionFilter.MatchesMachine("MSRTL04",
                XLayoutSelectionFilter.SplitTerms("MS*")));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("PMS01",
                XLayoutSelectionFilter.SplitTerms("MS*")));
            // 大小写不敏感。
            Assert.True(XLayoutSelectionFilter.MatchesMachine("msrtl04",
                XLayoutSelectionFilter.SplitTerms("MS*")));
        }

        [Fact]
        public void Filter_ExclusionTerm_HidesMatches_AndCombinesWithPositive()
        {
            IReadOnlyList<string> terms = XLayoutSelectionFilter.SplitTerms("!MS*");
            Assert.False(XLayoutSelectionFilter.MatchesMachine("MSRTL04", terms));
            Assert.True(XLayoutSelectionFilter.MatchesMachine("AMHS01", terms));
            // 组合：AMHS 里去掉 MS 开头。
            IReadOnlyList<string> combo = XLayoutSelectionFilter.SplitTerms("AMHS !MS*");
            Assert.True(XLayoutSelectionFilter.MatchesMachine("AMHS02", combo));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("MSAMHS", combo));
            // 全角叹号同义。
            IReadOnlyList<string> fullWidth = XLayoutSelectionFilter.SplitTerms("！MS*");
            Assert.False(XLayoutSelectionFilter.MatchesMachine("MSRTL04", fullWidth));
        }

        [Fact]
        public void Filter_KnownMachineIdTerm_MatchesExactly_NotBySubstring()
        {
            // 3.2.0.9 现场：粘贴 AMHS 机台清单（含 M651）排版出了 56 台而非 49 台
            // ——多余的 7 台全是 "PSPM651"/"OHVC-M651" 这类同号异组机台（子串
            // 包含把双胞胎一起捞进来）。传入已知机台号全集后，完整机台号词按
            // 精确匹配。
            string[] known = { "M651", "PSPM651", "OHVC-M651", "M623" };
            IReadOnlyList<string> terms = XLayoutSelectionFilter.SplitTerms("M651 M623");
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M651", terms, known));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("PSPM651", terms, known));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("OHVC-M651", terms, known));
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M623", terms, known));
            // 精确命中的机台，其全部回路可见（与机台节点口径一致）。
            Assert.True(XLayoutSelectionFilter.MatchesCircuit("M651", "RCM", terms, known));
            Assert.False(XLayoutSelectionFilter.MatchesCircuit("PSPM651", "RCM", terms, known));
            // 片段词不是完整机台号，仍走包含（旧交互不变）。
            IReadOnlyList<string> fragment = XLayoutSelectionFilter.SplitTerms("651");
            Assert.True(XLayoutSelectionFilter.MatchesMachine("PSPM651", fragment, known));
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M651", fragment, known));
            // 混合：精确词 + 片段词 OR 共存。
            IReadOnlyList<string> mixed = XLayoutSelectionFilter.SplitTerms("M651 OHVC");
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M651", mixed, known));
            Assert.True(XLayoutSelectionFilter.MatchesMachine("OHVC-M651", mixed, known));
            Assert.False(XLayoutSelectionFilter.MatchesMachine("PSPM651", mixed, known));
            // 不传已知全集 = 旧版纯包含（兼容旧调用方）。
            Assert.True(XLayoutSelectionFilter.MatchesMachine("PSPM651", terms));
            // 带通配的词不按精确处理（M651* 是前缀语义）。
            IReadOnlyList<string> wildcard = XLayoutSelectionFilter.SplitTerms("M651*");
            Assert.True(XLayoutSelectionFilter.MatchesMachine("M6519", wildcard, known));
        }

        // ---------------- ④ U1U 复制框两跑修复（源码契约） ----------------

        [Fact]
        public void CopiedMarker_PrefillsReserveHint_OnFirstRun()
        {
            // 单框链：复制框残留毫米标在首跑（未吸收分支）就作为设备内预留建议值
            // 带出——首跑即得正确预留，第二跑才吸收的两跑问题就此消除。
            string stage = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillReserveStage.cs"));
            int prefill = stage.IndexOf(
                "deviceReserveHintMm = reserveMarkers.Hits[0].Millimeters",
                StringComparison.Ordinal);
            int message = stage.IndexOf("复制残留毫米标", StringComparison.Ordinal);
            Assert.True(prefill >= 0 && message > prefill,
                "复制残留毫米标必须先写入建议值再提示核对");

            // 批量链：同一修复在批量预检侧。
            string preflight = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs"));
            Assert.Contains(
                "plan_DeviceReserveHintMm = reserveMarkers.Hits[0].Millimeters",
                preflight, StringComparison.Ordinal);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new[] { root }.Concat(parts).ToArray());
        }
    }
}
