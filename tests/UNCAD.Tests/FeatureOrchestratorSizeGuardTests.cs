using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// D-032/D-037 编排类清晰线扩面（2026-11-09）：FillFeatureSizeGuardTests 只锁
    /// FillFeature.cs，BatchFillUpdateCoordinator 一路涨到 954 行才被发现——本轮
    /// 复制图框三连修（3.0.0.6/8/9）两次都改它。本守卫覆盖 Features 层全部 .cs：
    /// 超过 800 行必须显式进白名单（带拆分说明）；白名单条目一旦降到线下必须
    /// 移除（防止白名单腐化成永久豁免）。拆分手段照 FillFeature 先例：阶段逻辑
    /// 下沉到独立 Stage/服务类（如本轮拆出的 BatchFillUpdatePreflight）。
    /// </summary>
    public sealed class FeatureOrchestratorSizeGuardTests
    {
        private const int MaxLines = 800;

        /// <summary>已知超线的遗留大文件（文件名 → 拆分说明）。
        /// 新增条目必须写清楚原因与拆分方向，不许空挂。</summary>
        private static readonly Dictionary<string, string> OversizeWhitelist =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // U1L3D/U13D：单/批两套 payload 序列化高度重复，待合并后拆分。
                { "QuickLine3dPreviewFeature.cs",
                    "3D 预览装配：单/批 payload 序列化重复，待合并拆分" },
                // XLAYOUT 合并服务：多排布模式 + 机台标签，待按模式拆分。
                { "XmergeService.cs", "XLAYOUT 合并服务，待按排布模式拆分" }
            };

        [Fact]
        public void AllFeatureFiles_StayWithinOrchestratorCap()
        {
            string[] files = Directory.GetFiles(FeaturesRoot(), "*.cs",
                SearchOption.AllDirectories);
            Assert.True(files.Length > 50,
                "Features 层文件扫描异常：只找到 " + files.Length
                + " 个 .cs（守卫自身失效，检查路径）。");
            var offenders = new List<string>();
            foreach (string file in files)
            {
                int lines = File.ReadAllLines(file).Length;
                if (lines <= MaxLines) continue;
                string name = Path.GetFileName(file);
                if (OversizeWhitelist.ContainsKey(name)) continue;
                offenders.Add(name + " (" + lines + " 行)");
            }
            Assert.True(offenders.Count == 0,
                "Features 层文件超过 " + MaxLines + " 行编排上限（拆阶段类，或经评估后"
                + "带说明加入白名单）: " + string.Join(", ", offenders));
        }

        [Fact]
        public void WhitelistEntries_AreStillOversize()
        {
            foreach (KeyValuePair<string, string> entry in OversizeWhitelist)
            {
                string[] matches = Directory.GetFiles(FeaturesRoot(), entry.Key,
                    SearchOption.AllDirectories);
                if (matches.Length == 0)
                    continue; // 文件已删除/改名：上面的全量扫描不再放行它。
                int lines = File.ReadAllLines(matches[0]).Length;
                Assert.True(lines > MaxLines,
                    entry.Key + " 已降到 " + lines + " 行（≤" + MaxLines
                    + "），请从白名单移除——豁免条目必须随拆分收敛，不许空挂。");
            }
        }

        private static string FeaturesRoot()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, "src", "UNCAD", "Features");
        }
    }
}
