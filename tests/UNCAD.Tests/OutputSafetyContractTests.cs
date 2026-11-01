using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// 2.10.0 输出安全契约：BOQ/DWG/PDF 统一输出布局、写盘前占用预检、
    /// 整机回路校验、去掉云端上传；U1S/U1DWG/U1PDF 失败路径有明确中文提示。
    /// 这些是源码接线断言（行为细节由 OutputLayoutTests/PdfExportTests 覆盖）。
    /// </summary>
    public sealed class OutputSafetyContractTests
    {
        [Fact]
        public void UploadInfrastructure_IsGone()
        {
            Assert.False(File.Exists(Repo("src", "UNCAD", "Infra",
                "BackgroundUploadQueue.cs")));
            Assert.False(File.Exists(Repo("src", "UNCAD", "Infra",
                "ClientSubmissionUploader.cs")));
            Assert.False(File.Exists(Repo("src", "UNCAD", "Infra",
                "MachineLedgerReporter.cs")));
        }

        [Theory]
        [InlineData("src", "UNCAD", "Features", "DwgExport", "DwgExportFeature.cs")]
        [InlineData("src", "UNCAD", "Features", "DwgExport", "DwgFrameExportService.cs")]
        [InlineData("src", "UNCAD", "Features", "PdfExport", "PdfExportFeature.cs")]
        [InlineData("src", "UNCAD", "Features", "PdfExport", "PdfExportService.cs")]
        [InlineData("src", "UNCAD", "Features", "Submit", "SubmitFeature.cs")]
        [InlineData("src", "UNCAD", "Features", "Submit", "AutomaticSubmissionService.cs")]
        public void ExportAndSubmitPaths_ContainNoUploadCode(params string[] parts)
        {
            string source = File.ReadAllText(Repo(parts));
            Assert.DoesNotContain("ClientSubmissionUploader", source);
            Assert.DoesNotContain("BackgroundUploadQueue", source);
            Assert.DoesNotContain("MachineLedgerReporter", source);
            Assert.DoesNotContain("ThreadPool", source);
            Assert.DoesNotContain("UploadPdf", source);
            Assert.DoesNotContain("UploadDwgFile", source);
            Assert.DoesNotContain("ReportDwgExport", source);
        }

        [Fact]
        public void BoqDwgPdf_AllUseOutputLayoutAndPrecheckBeforeWriting()
        {
            string submit = File.ReadAllText(Repo("src", "UNCAD", "Features", "Submit",
                "AutomaticSubmissionService.cs"));
            Assert.Contains("OutputLayout.ResolveBoqDirectory(root, drawingName,", submit);
            Assert.Contains("OutputLayout.ValidateNotLocked(targets)", submit);
            Assert.Contains("OutputLayout.ValidateNotLocked(new[] { target })", submit);
            Assert.Contains("string drawingName = null", submit);
            Assert.Contains("ConfigKeys.OutputByDrawing", submit);
            Assert.Contains("ConfigKeys.OutputSeparateKinds", submit);

            string dwg = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgFrameExportService.cs"));
            Assert.Contains("OutputLayout.ComposeDwgPath(", dwg);
            Assert.Contains("OutputLayout.ValidateNotLocked(paths)", dwg);
            Assert.Contains("ConfigKeys.OutputByDrawing", dwg);
            Assert.Contains("ConfigKeys.OutputSeparateKinds", dwg);

            string pdf = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs"));
            Assert.Contains("OutputLayout.ComposePdfPath(", pdf);
            Assert.Contains("OutputLayout.ValidateNotLocked(", pdf);
        }

        [Fact]
        public void U1S_RequiresCompleteMachineCoverageBeforeWritingBoq()
        {
            string submit = File.ReadAllText(Repo("src", "UNCAD", "Features", "Submit",
                "SubmitFeature.cs"));
            int coverage = submit.IndexOf("CircuitCoverageGuard.Ensure(", StringComparison.Ordinal);
            int prepare = submit.IndexOf("AutomaticSubmissionService.PrepareTargetPath(",
                StringComparison.Ordinal);
            Assert.True(coverage >= 0, "U1S 必须做整机回路校验");
            Assert.True(prepare > coverage, "整机回路校验必须在 BOQ 写入之前");
            Assert.Contains("提交已中止", submit);

            string guard = File.ReadAllText(Repo("src", "UNCAD", "Features", "Submit",
                "CircuitCoverageGuard.cs"));
            Assert.Contains("FillFeature.EnsureCircuitCoverage(", guard);
            Assert.Contains("MachineWorkbookSource.TryGetSnapshot(", guard);
            Assert.Contains("FillWorkbookSnapshot.Load(source)", guard);
            // 只读快照，绝不刷新/下载源 Excel。
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", guard);
        }

        [Fact]
        public void U1Dwg_RequiresCompleteMachineCoverageBeforeWritingFiles()
        {
            string dwg = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgExportFeature.cs"));
            int coverage = dwg.IndexOf("CircuitCoverageGuard.Ensure(", StringComparison.Ordinal);
            int export = dwg.IndexOf("DwgFrameExportService.Export(", StringComparison.Ordinal);
            Assert.True(coverage >= 0, "U1DWG 必须做整机回路校验");
            Assert.True(export > coverage, "整机回路校验必须在 DWG 写盘之前");
            Assert.Contains("导出已中止", dwg);
        }

        [Fact]
        public void OutputsAreRegisteredInTheOperationLog()
        {
            string submit = File.ReadAllText(Repo("src", "UNCAD", "Features", "Submit",
                "SubmitFeature.cs"));
            Assert.Contains("Operation?.Machine(identity.Key, identity.Value)", submit);
            Assert.Contains("Operation?.Output(\"BOQ\"", submit);

            string dwg = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgExportFeature.cs"));
            Assert.Contains("Operation?.Machine(machine.MachineId)", dwg);
            Assert.Contains("Operation?.Output(\"DWG\"", dwg);

            string pdf = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs"));
            Assert.Contains("Operation?.Output(\"PDF\"", pdf);
        }

        [Fact]
        public void NoRemoteWorkbookDownloadOrStartupRefreshRemains()
        {
            string source = File.ReadAllText(Repo("src", "UNCAD", "Core", "Excel",
                "MachineWorkbookSource.cs"));
            Assert.DoesNotContain("HttpWebRequest", source);
            Assert.DoesNotContain("WebRequest.CreateHttp", source);
            Assert.DoesNotContain("TryGetCachedPath", source);
            Assert.Contains("不再支持网络机台数据源", source);

            string coordinator = File.ReadAllText(Repo("src", "UNCAD", "Infra",
                "MachineDataRefreshCoordinator.cs"));
            Assert.DoesNotContain("TryStartStartupRefresh", coordinator);
            Assert.DoesNotContain("MachineLedgerReporter", coordinator);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}
