using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class BatchFillUpdateContractTests
    {
        [Fact]
        public void BatchUpdate_ShowsAtMostOneCompareDialog()
        {
            string source = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Equal(1, Count(source, "new FillUpdateCompareWindow("));
        }

        [Fact]
        public void BatchUpdate_CatalogPickerCoversEveryUnmatchedItemOfAnyCategory()
        {
            // D-052-A（2026-11-10，现场报告“选完了还警告未匹配、整批跳过”）：
            // 批量预检必须为**每一个**未匹配固定清单的清单项建替代选择请求
            // （不再只收集电缆/母线插接箱各一条——桥架/线管/软管/第二条电缆行
            // 以前无处可选，确认后落入“未匹配固定清单”整批取消）。
            string preflight = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            Assert.Contains("foreach (FillReviewItem item in review.Items.Where(item =>",
                preflight);
            Assert.Contains("item.RequiresCatalogConfirmation))", preflight);
            Assert.Contains("FillReviewData.CategoryName(item.Category)", preflight);
            Assert.Contains("workbook.Catalog.SelectableItems", preflight);
            Assert.DoesNotContain("FirstOrDefault(item =>", preflight
                .Substring(preflight.IndexOf("D-052-A", StringComparison.Ordinal)));
            Assert.Contains("固定清单没有可选择的\" + category + \"规格", preflight);

            string coordinator = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            // 确认后逐项应用回清单；软管联动只在电缆被替换时触发。
            Assert.Contains("foreach (BatchCatalogAssignment assignment in plan.CatalogAssignments)",
                coordinator);
            Assert.Contains("picker.Selections.TryGetValue(assignment.RequestKey", coordinator);
            Assert.Contains("plan.Review.ReplaceWithCatalogItem(assignment.Item, selected,",
                coordinator);
            Assert.Contains("assignment.Item.Category == Core.Fill.TableFillCategory.Cable",
                coordinator);
        }

        [Fact]
        public void BatchUpdate_CopiedFrameReserveIsPrefilledNotRestartedAtZero()
        {
            // D-052-B（2026-11-10，现场报告“图上明明有预留、弹窗却默认 0”）：
            // 复制图框的预留弹窗行用源框 CABLE_INFO 解析值预填并标注来源；
            // 恢复本身仍被守卫跳过（3.0.0.8 不静默继承的语义不变）。
            string preflight = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdatePreflight.cs");
            Assert.Contains("plan_DeviceReserveHintMm = copiedReserveHint;", preflight);
            Assert.Contains("DeviceReserveHintMm = plan_DeviceReserveHintMm", preflight);
            Assert.Contains("（复制源，请在弹窗核对后确认）", preflight);

            string coordinator = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Contains("new DeviceReserveRow(plan.Machine?.MachineId ?? \"\",",
                coordinator);
            Assert.Contains("plan.DeviceReserveHintMm.HasValue", coordinator);
            Assert.Contains("\"复制源预填，请核对\"", coordinator);
        }

        [Fact]
        public void BatchUpdate_UnmatchedDefaultsIdentifyAffectedMachineAndImpact()
        {
            string source = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            int start = source.IndexOf("List<Tuple<BatchFillUpdatePlan, FillReviewItem>> unresolvedDefaults",
                StringComparison.Ordinal);
            int end = source.IndexOf("foreach (BatchFillUpdatePlan plan in plans)", start,
                StringComparison.Ordinal);

            Assert.True(start >= 0 && end > start);
            string prompt = source.Substring(start, end - start);
            Assert.Contains("entry.Item1.Machine.MachineId", prompt);
            Assert.Contains("entry.Item1.Machine.CircuitName", prompt);
            Assert.Contains("entry.Item1.Region.Handle", prompt);
            Assert.Contains("entry.Item1.Machine.Detail", prompt);
            Assert.Contains("批量更新已取消", prompt);
        }

        [Fact]
        public void SingleUpdate_CompareFailureStopsInsteadOfWritingBlindly()
        {
            string source = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            int marker = source.IndexOf("U1U 更新对比读取失败，已停止更新",
                StringComparison.Ordinal);

            Assert.True(marker >= 0);
            Assert.Contains("return false;", source.Substring(marker, 240));
        }

        [Fact]
        public void SingleUpdate_CompareShowsFinalRowsAfterReviewEditing()
        {
            // 设备内预留/电缆米数在清单确认窗里编辑：对比窗必须在确认窗之后、
            // 用确认后的最终行构建，否则用户改完预留，先弹的变更记录就是假数据。
            string source = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            int review = source.IndexOf("FillReviewStage.ShowReview(",
                StringComparison.Ordinal);
            int compareCall = source.IndexOf("ShowUpdateCompare(ctx",
                StringComparison.Ordinal);
            int metersOverride = source.IndexOf("ApplyCableLengthOverride(statistics",
                StringComparison.Ordinal);
            int write = source.IndexOf("new FileBatchRollback(",
                StringComparison.Ordinal);
            Assert.True(review >= 0 && compareCall > review,
                "对比窗必须在清单确认窗之后弹出");
            Assert.True(metersOverride >= 0 && compareCall > metersOverride,
                "对比窗必须在米数覆盖之后构建");
            Assert.True(compareCall >= 0 && compareCall < write,
                "对比窗必须在写表之前");
            Assert.Contains("ShowUpdateCompare(ctx, selection, picked, tableRows,",
                source);
        }

        [Fact]
        public void UpdateFlows_RecheckBoqAfterConfirmationBeforeCadWrite()
        {
            string single = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            int singleCapture = single.IndexOf("CaptureTargetRevisions(",
                StringComparison.Ordinal);
            int singleCompare = single.IndexOf("ShowUpdateCompare(",
                StringComparison.Ordinal);
            int singleValidate = single.IndexOf("ValidateTargetRevisions(",
                StringComparison.Ordinal);
            int singleWrite = single.IndexOf("new FileBatchRollback(",
                StringComparison.Ordinal);
            int singleFinalValidate = single.LastIndexOf("ValidateTargetRevisions(",
                StringComparison.Ordinal);
            Assert.True(singleCapture >= 0 && singleCapture < singleCompare);
            Assert.True(singleValidate > singleCompare && singleValidate < singleWrite);
            Assert.True(singleFinalValidate > singleWrite);

            string batch = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            int batchCapture = batch.IndexOf("CaptureTargetRevisions(",
                StringComparison.Ordinal);
            int batchCompare = batch.IndexOf("new FillUpdateCompareWindow(",
                StringComparison.Ordinal);
            int batchValidate = batch.IndexOf("ValidateTargetRevisions(",
                StringComparison.Ordinal);
            int batchWrite = batch.IndexOf("new FileBatchRollback(",
                StringComparison.Ordinal);
            int batchFinalValidate = batch.LastIndexOf("ValidateTargetRevisions(",
                StringComparison.Ordinal);
            Assert.True(batchCapture >= 0 && batchCapture < batchCompare);
            Assert.True(batchValidate > batchCompare && batchValidate < batchWrite);
            Assert.True(batchFinalValidate > batchWrite);
        }

        [Fact]
        public void FillFlows_SurfaceBatchRollbackFailures()
        {
            string single = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            Assert.Contains("outputBatch.Rollback();", single);
            Assert.Contains("图纸和 BOQ 修改均已回滚", single);
            Assert.Contains("new AggregateException(", single);
            Assert.Contains("BOQ 文件回滚不完整", single);

            string batch = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            Assert.Contains("outputBatch.Rollback();", batch);
            Assert.Contains("所有图框和 BOQ 修改均已回滚", batch);
            Assert.Contains("new AggregateException(", batch);
            Assert.Contains("部分 BOQ 文件无法安全恢复", batch);
        }

        private static string Read(params string[] parts)
            => File.ReadAllText(PathOf(parts));

        private static int Count(string value, string token)
        {
            int count = 0;
            int offset = 0;
            while ((offset = value.IndexOf(token, offset,
                StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += token.Length;
            }
            return count;
        }

        private static string PathOf(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(root, Path.Combine(parts));
        }
    }
}
