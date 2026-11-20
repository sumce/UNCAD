using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UNCAD.Core.Dwg;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// XLAYOUT 筛选表达力（区间词 M603-M612 / 前缀词 MS* / 排除词 !MS*，
    ///    旧"子串包含 OR"行为全部保持）；
    /// U1U 复制框两跑修复（首跑把残留毫米标值预填为设备内预留建议值——
    ///    用户报告"同一图框要跑两次 U1U 才能得到正确结果"）。
    /// </summary>
    public sealed class XLayoutFilterUpgradeTests
    {
        // ---------------- 筛选表达力 ----------------

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

        // ---------------- U1U 复制框两跑修复（源码契约） ----------------

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
