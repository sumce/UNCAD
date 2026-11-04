using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1U 更新对比窗契约（2026-11-04 用户要求"确保×4"）：
    /// 1. **最后一轮**——单框对比必须用清单确认窗之后、插座/容量/米数同步
    ///    之后的最终行构建；批量对比必须在预留/目录确认窗之后、最终确认与
    ///    写事务之前构建。展示 = 实际写入。
    /// 2. **无变化不弹窗**——门控是 HasChanges（行新增/移除/改量），不是
    ///    HasHistory；内容没变时单框直接更新并提示，批量跳过弹窗。
    /// 3. **电缆长度卡片**——BuildCompareItem 必须接收 CableReserveInfo，
    ///    卡片展示 实测/盘内（箱内）/设备内/业主 分解与上次总长对比。
    /// 行为细节由 FillUpdateCompareWindowTests 覆盖；这里锁定源码接线。
    /// </summary>
    public sealed class FillUpdateCompareContractTests
    {
        [Fact]
        public void SingleFrame_CompareIsBuiltFromTheFinalRound()
        {
            string fill = Source("src", "UNCAD", "Features", "Fill", "FillFeature.cs");

            // 顺序：插座最终裁定 → 容量确认 → 米数覆盖同步 → 对比窗。
            // 对比若提前弹，用户在确认窗里改的预留/米数就是假数据（3.0.0 修过）。
            int outlets = fill.IndexOf("ApplyReviewedOutlets(", StringComparison.Ordinal);
            int capacity = fill.IndexOf("ConfirmRowsAndCapacity(ctx, tableRows",
                StringComparison.Ordinal);
            int sync = fill.IndexOf("SyncCableReserveTotal(", StringComparison.Ordinal);
            int compare = fill.IndexOf(
                "ShowUpdateCompare(ctx, selection, picked, tableRows",
                StringComparison.Ordinal);
            Assert.True(outlets >= 0, "缺少插座最终裁定步骤");
            Assert.True(capacity > outlets, "容量确认必须在插座裁定之后");
            Assert.True(sync > capacity, "米数同步必须在容量确认之后");
            Assert.True(compare > sync,
                "U1U 对比窗必须在最终轮（预留/米数同步）之后构建——展示必须等于写入");
        }

        [Fact]
        public void SingleFrame_NoChangeSkipsTheCompareWindow()
        {
            string fill = Source("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            Assert.Contains("item.HasChanges", fill);
            Assert.Contains("本次更新内容与上次一致（清单无变化），直接更新", fill);
            // 旧的 HasHistory 门控不得回流（有历史但无变化也弹窗 = 打扰）。
            Assert.DoesNotContain("if (!item.HasHistory) return true;", fill);
        }

        [Fact]
        public void Batch_CompareSitsBetweenReserveConfirmAndFinalConfirm()
        {
            string batch = Source("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");

            int reserve = batch.IndexOf("new DeviceReserveWindow(reserveRows)",
                StringComparison.Ordinal);
            int compare = batch.IndexOf("FillFeature.BuildCompareItem(",
                StringComparison.Ordinal);
            int finalConfirm = batch.IndexOf("new BatchFillConfirmationWindow(",
                StringComparison.Ordinal);
            Assert.True(reserve >= 0, "缺少设备预留确认窗");
            Assert.True(compare > reserve,
                "批量对比必须在设备预留确认之后构建——plan.Rows 才是最终行");
            Assert.True(finalConfirm > compare,
                "最终确认窗必须在对比窗之后");

            // 卡片数据来自 plan.Reserve；门控是 HasChanges；全部无变化直接更新。
            Assert.Contains("plan.Reserve))", batch);
            Assert.Contains(".Where(item => item.HasChanges)", batch);
            Assert.Contains("所选图框内容均无变化，跳过对比，直接更新", batch);

            // 对比窗之后、本次写事务开始之前，不得再改 plan.Rows。
            int windowOpen = batch.IndexOf("new FillUpdateCompareWindow(compareItems)",
                StringComparison.Ordinal);
            Assert.True(windowOpen > 0, "缺少批量对比窗");
            int nextTransaction = batch.IndexOf("StartTransaction()",
                windowOpen, StringComparison.Ordinal);
            Assert.True(nextTransaction > windowOpen,
                "对比窗与写事务之间不得插入其他事务");
        }

        [Fact]
        public void BuildCompareItem_ReceivesReserveForTheCableCard()
        {
            string review = Source("src", "UNCAD", "Features", "Fill",
                "FillReviewStage.cs");
            Assert.Contains("CableReserveInfo reserve = null", review);
            Assert.Contains("BuildCableCard(previous, reserve)", review);
            // 上次总长只取本框自己的记录（复制框记录已被句柄校验拒掉）。
            Assert.Contains("TotalCableM", review);
        }

        private static string Source(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(
                new string[] { root }.Concat(parts).ToArray()));
        }
    }
}
