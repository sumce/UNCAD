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
            Assert.Equal(1, Count(source, "new FillUpdateCompareForm("));
        }

        [Fact]
        public void BatchUpdate_UnmatchedDefaultsIdentifyAffectedMachineAndImpact()
        {
            string source = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            int start = source.IndexOf("List<Tuple<Plan, FillReviewItem>> unresolvedDefaults",
                StringComparison.Ordinal);
            int end = source.IndexOf("foreach (Plan plan in plans)", start,
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
            int batchCompare = batch.IndexOf("new FillUpdateCompareForm(",
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
