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
            Assert.Contains("OutputLayout.ValidateNotLocked(targets)", submit);
            Assert.Contains("OutputLayout.ValidateNotLocked(new[] { target })", submit);
            // D-042：机台文件夹布局，无图纸/类别分层，也不再有两个布局开关。
            Assert.DoesNotContain("ConfigKeys.OutputByDrawing", submit);
            Assert.DoesNotContain("ConfigKeys.OutputSeparateKinds", submit);

            string dwg = File.ReadAllText(Repo("src", "UNCAD", "Features", "DwgExport",
                "DwgFrameExportService.cs"));
            Assert.Contains("OutputLayout.ComposeDwgPath(", dwg);
            Assert.Contains("OutputLayout.ValidateNotLocked(paths)", dwg);
            Assert.DoesNotContain("ConfigKeys.OutputByDrawing", dwg);
            Assert.DoesNotContain("ConfigKeys.OutputSeparateKinds", dwg);

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
        public void RemoteWorkbookDownloadIsExplicitAndLedgerStaysDeleted()
        {
            // D-041：恢复只下载的网络机台数据源；上传/台账/启动自动刷新保持移除。
            string source = File.ReadAllText(Repo("src", "UNCAD", "Core", "Excel",
                "MachineWorkbookSource.cs"));
            Assert.Contains("WebRequest.CreateHttp", source);
            Assert.Contains("request.Method = \"GET\"", source);
            Assert.DoesNotContain("UploadValues", source);
            Assert.DoesNotContain("PostAsync", source);
            Assert.DoesNotContain("MachineLedgerReporter", source);

            string coordinator = File.ReadAllText(Repo("src", "UNCAD", "Infra",
                "MachineDataRefreshCoordinator.cs"));
            Assert.DoesNotContain("TryStartStartupRefresh", coordinator);
            Assert.DoesNotContain("MachineLedgerReporter", coordinator);

            string bootstrap = File.ReadAllText(Repo("src", "UNCAD", "Bootstrap.cs"));
            Assert.DoesNotContain("MachineDataRefreshCoordinator", bootstrap);
            Assert.DoesNotContain("MachineWorkbookSource.Refresh", bootstrap);
        }

        [Fact]
        public void U1U_PartialCoverage_UpdatesDrawingAndSkipsOnlyBoq()
        {
            // D-033 修订（2026-11-01）：U1U 缺回路不再拦整条命令——图框照常更新，
            // 只跳过该机 BOQ（部分回路的 BOQ 会丢未选回路的工程量）。
            // D-044（2026-11-03）：U1F 与 U1U 同口径——缺回路照常填写图框、只跳
            // BOQ（边画边填；每框数据只依赖自己的统计与机台行）。
            // U1S/U1DWG 仍是硬拦截（缺回路不出清单/图）。
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            Assert.Contains("boqBlockedByCoverage", fill);
            Assert.Contains("本次照常填写图框数据、", fill);
            Assert.Contains("不生成 BOQ；全部回路画完后", fill);
            Assert.Contains("EvaluateCircuitCoverage", fill);
            Assert.Contains("CoverageIncompleteMachines", fill);
            // 旧的 U1F 硬拦截文案不得回流。
            Assert.DoesNotContain("回路不全不生成 BOQ/DWG，", fill);
            // U1F 的 BOQ 路径准备同样按门禁跳过（ !updateMode && !boqBlockedByCoverage ）。
            Assert.Contains("!updateMode && !boqBlockedByCoverage", fill);

            string batch = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs"));
            Assert.Contains("CoverageIncompleteMachines", batch);
            Assert.Contains("boqPlans", batch);
            Assert.Contains("不生成该机 BOQ", batch);
            // 旧的"整批取消"路径不得回流。
            Assert.DoesNotContain("本次批量更新已取消", batch);
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}
