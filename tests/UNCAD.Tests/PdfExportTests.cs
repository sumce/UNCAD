using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Features.PdfExport;
using UNCAD.Infra;
using Xunit;

namespace UNCAD.Tests
{
    /// <summary>
    /// U1PDF 契约：**一个图框 = 一个回路 = 一份 PDF**，直接输出到
    /// <c>&lt;root&gt;\&lt;图纸&gt;\PDF\&lt;机台&gt;_&lt;回路&gt;.pdf</c>，不做任何上传。
    /// 锁住：文件命名与 PDF 有效性校验、同批同名冲突后缀、失败隔离与中文提示、
    /// 回路名推导与 U1F/U1U 同源。
    /// </summary>
    public sealed class PdfExportTests
    {
        private const string Machine = "MDIDA03";
        private const string Circuit = "MDIDA03-1";

        // ---- 命令注册 ----

        [Fact]
        public void U1Pdf_IsRegisteredOnceAndDeclaredInTheCommandSurface()
        {
            Assert.Equal("U1PDF", CommandIds.PdfExport);
            Assert.Equal("U1PDF", CommandIds.PdfExportFeatureCommands);
            Assert.Contains(CommandIds.PdfExport, CommandIds.Registered);

            string source = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs"));
            Assert.Contains("[CommandMethod(CommandIds.PdfExport, CommandFlags.UsePickSet)]",
                source);
            Assert.Contains("[Feature(\"pdf-export\"", source);
            Assert.Contains("Commands = CommandIds.PdfExportFeatureCommands", source);
            Assert.Contains("ProductMetadata.EnsureCommandAllowed(CommandIds.PdfExport)", source);
        }

        [Fact]
        public void U1Pdf_RibbonAndHelpAreInSync()
        {
            string ribbon = File.ReadAllText(Repo("src", "UNCAD", "UI", "RibbonDefinition.cs"));
            Assert.Contains("CommandIds.PdfExport", ribbon);
            Assert.Equal(CommandIds.Registered.Count, CommandHelpCatalog.All.Count);
            Assert.NotNull(CommandHelpCatalog.Find(CommandIds.PdfExport));
        }

        // ---- 文件命名与 PDF 校验 ----

        [Theory]
        [InlineData("MDIDA03", "MDIDA03-1", "MDIDA03_MDIDA03-1.pdf")]
        [InlineData("  MDIDA03  ", "  MDIDA03-1  ", "MDIDA03_MDIDA03-1.pdf")]
        [InlineData("MDIDA03", "MDIDA03-1.pdf", "MDIDA03_MDIDA03-1.pdf")]
        [InlineData("MDIDA03", "A/B:C*D?", "MDIDA03_A_B_C_D_.pdf")]
        [InlineData("", "MDIDA03-1", "MDIDA03-1.pdf")]
        [InlineData("MDIDA03", "", "MDIDA03.pdf")]
        [InlineData("", "", "circuit.pdf")]
        [InlineData("   ", "   ", "circuit.pdf")]
        public void BuildFileName_CombinesMachineAndCircuitSafely(string machineId,
            string circuitName, string expected)
            => Assert.Equal(expected, PdfExportNaming.BuildFileName(machineId, circuitName));

        [Fact]
        public void BuildFileName_KeepsChineseCircuitNamesWithinLengthLimit()
        {
            string name = PdfExportNaming.BuildFileName("", "一号厂房-3F-1号回路");
            Assert.Equal("一号厂房-3F-1号回路.pdf", name);

            string longName = PdfExportNaming.BuildFileName("", new string('机', 300));
            Assert.True(longName.Length <= 104, "文件名必须截断到安全长度：" + longName.Length);
            Assert.True(PdfExportNaming.HasPdfExtension(longName));
        }

        [Fact]
        public void AllocateFileName_AddsCollisionSuffixForDuplicateMachineAndCircuit()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string first = PdfExportNaming.AllocateFileName(Machine, Circuit, used);
            string second = PdfExportNaming.AllocateFileName(Machine, Circuit, used);
            string third = PdfExportNaming.AllocateFileName(Machine, Circuit, used);

            Assert.Equal("MDIDA03_MDIDA03-1.pdf", first);
            Assert.Equal("MDIDA03_MDIDA03-1-2.pdf", second);
            Assert.Equal("MDIDA03_MDIDA03-1-3.pdf", third);
            Assert.Equal(3, used.Count);
        }

        [Fact]
        public void AllocateFileName_ComparesCaseInsensitivelyAndAllowsNullSet()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Assert.Equal("MDIDA03_MDIDA03-1.pdf",
                PdfExportNaming.AllocateFileName(Machine, Circuit, used));
            // 冲突后缀保留这次调用的原始写法（Windows 文件名不区分大小写，集合已忽略大小写）。
            Assert.Equal("mdida03_mdida03-1-2.pdf",
                PdfExportNaming.AllocateFileName("mdida03", "mdida03-1", used));
            Assert.Equal("MDIDA03_MDIDA03-1.pdf",
                PdfExportNaming.AllocateFileName(Machine, Circuit, null));
        }

        [Fact]
        public void AllocateFileName_KeepsNameWithinLengthLimitWhenAddingSuffix()
        {
            string longCircuit = new string('回', 120);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string first = PdfExportNaming.AllocateFileName(Machine, longCircuit, used);
            string second = PdfExportNaming.AllocateFileName(Machine, longCircuit, used);

            Assert.NotEqual(first, second);
            Assert.True(second.Length <= 104, "加冲突后缀后仍必须截断：" + second.Length);
            Assert.True(PdfExportNaming.HasPdfExtension(second));
            Assert.Contains("-2.pdf", second);
        }

        [Fact]
        public void HasPdfMagic_DetectsRealPdfHeaderOnly()
        {
            string pdf = WriteTemp("real.pdf", Encoding.ASCII.GetBytes("%PDF-1.7\n..."));
            string fake = WriteTemp("fake.pdf", Encoding.ASCII.GetBytes("not a pdf at all"));
            string empty = WriteTemp("empty.pdf", new byte[0]);
            try
            {
                Assert.True(PdfExportNaming.HasPdfMagic(pdf));
                Assert.False(PdfExportNaming.HasPdfMagic(fake));
                Assert.False(PdfExportNaming.HasPdfMagic(empty));
                Assert.False(PdfExportNaming.HasPdfMagic(Path.Combine(Path.GetTempPath(),
                    "missing-" + Guid.NewGuid().ToString("N") + ".pdf")));
            }
            finally
            {
                foreach (string path in new[] { pdf, fake, empty })
                    try { File.Delete(path); } catch (System.Exception) { }
            }
        }

        [Fact]
        public void DeviceName_IsTheBuiltInPdfPlotter()
            => Assert.Equal("DWG To PDF.pc3", PdfExportNaming.DeviceName);

        // ---- 出图编排：逐份进度、失败隔离、汇总 ----

        [Fact]
        public void Coordinator_RendersEveryFrameWithPerFrameProgress()
        {
            var messages = new List<string>();
            var rendered = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target =>
                {
                    rendered.Add(target.CircuitName);
                    return PdfRenderResult.Success(target.FilePath);
                },
                messages.Add);

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target(Circuit),
                Target("MDIDA03-2")
            });

            Assert.Equal(2, run.Total);
            Assert.Equal(2, run.Rendered);
            Assert.Equal(0, run.Failed);
            Assert.Equal(new[] { Circuit, "MDIDA03-2" }, rendered);
            Assert.Contains(messages, message => message.Contains("正在出图 1/2"));
            Assert.Contains(messages, message => message.Contains("正在出图 2/2"));
            Assert.Contains("成功 2 份 / 失败 0 份", run.BuildSummary());
            Assert.Contains("PDF 已保存到输出目录", run.BuildSummary());
        }

        [Fact]
        public void Coordinator_SkipsFailedFrameAndStillRendersTheOthers()
        {
            // 第二份出图失败：只跳过该份，其余两份照常输出（用户需求：一份失败继续处理其余份）。
            var coordinator = new PdfExportCoordinator(
                target => target.CircuitName == "MDIDA03-2"
                    ? PdfRenderResult.Fail("打印引擎异常")
                    : PdfRenderResult.Success(target.FilePath),
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target(Circuit),
                Target("MDIDA03-2"),
                Target("MDIDA03-3")
            });

            Assert.Equal(3, run.Total);
            Assert.Equal(2, run.Rendered);
            Assert.Equal(1, run.Failed);
            Assert.Contains(run.Failures, item => item.Contains("MDIDA03-2")
                && item.Contains("打印引擎异常"));
            string summary = run.BuildSummary();
            Assert.Contains("成功 2 份 / 失败 1 份", summary);
            Assert.Contains("打印引擎异常", summary);
        }

        [Fact]
        public void Coordinator_TreatsRenderExceptionAsFailureForThatFrameOnly()
        {
            var rendered = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target =>
                {
                    if (target.CircuitName == "BOOM") throw new InvalidOperationException("渲染炸了");
                    rendered.Add(target.CircuitName);
                    return PdfRenderResult.Success(target.FilePath);
                },
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target("BOOM"),
                Target(Circuit)
            });

            Assert.Equal(1, run.Failed);
            Assert.Equal(1, run.Rendered);
            Assert.Equal(new[] { Circuit }, rendered);
            Assert.Contains(run.Failures, item => item.Contains("渲染炸了"));
        }

        [Fact]
        public void Coordinator_RejectsFramesWithoutCircuitNameOrMachine()
        {
            var rendered = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target => { rendered.Add(target.CircuitName); return PdfRenderResult.Success(target.FilePath); },
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                new PdfExportTarget { MachineId = "", CircuitName = Circuit, FrameHandle = "1" },
                new PdfExportTarget { MachineId = Machine, CircuitName = "", FrameHandle = "2" }
            });

            Assert.Equal(2, run.Failed);
            Assert.Empty(rendered);
            Assert.Equal(0, run.Rendered);
        }

        [Fact]
        public void Coordinator_HandlesEmptySelection()
        {
            var coordinator = new PdfExportCoordinator(
                target => PdfRenderResult.Success(target.FilePath), _ => { });
            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>());
            Assert.Equal(0, run.Total);
            Assert.Contains("成功 0 份 / 失败 0 份", run.BuildSummary());
        }

        // ---- 命令实现要点 ----

        [Fact]
        public void Feature_WritesToOutputLayoutWithoutAnyUpload()
        {
            string feature = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs"));
            string service = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportService.cs"));
            string session = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "AutoCadPlotSession.cs"));
            string pipeline = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfPlotSession.cs"));

            // 与 U1DWG 同一套图框识别
            Assert.Contains("FrameRegionCollector.CollectForExport(ctx, selected)", feature);
            // 未选中/无 pc3/输出目录不可用都要有中文提示后返回
            Assert.Contains("未选择图框，未生成 PDF", feature);
            Assert.Contains("TryGetDeviceError", feature);
            // 输出目录统一走 OutputLayout，文件名由 PdfExportNaming 分配
            Assert.Contains("OutputLayout.ComposePdfPath(", feature);
            Assert.Contains("PdfExportNaming.AllocateFileName(", feature);
            // 写盘前预检占用，占用时中文中止
            Assert.Contains("OutputLayout.ValidateNotLocked(", feature);
            Assert.Contains("输出文件被占用或被锁定", feature);
            // 2.10.0：不再上传 PDF（ThreadPool/ClientSubmissionUploader/queue 全部移除）
            Assert.DoesNotContain("ThreadPool", feature);
            Assert.DoesNotContain("UploadPdf", feature);
            Assert.DoesNotContain("ClientSubmissionUploader", feature);
            Assert.DoesNotContain("QueueUploads", feature);
            // 操作记录：成功回路与 PDF 路径要登记
            Assert.Contains("Operation?.Machine(target.MachineId, target.CircuitName)", feature);
            Assert.Contains("Operation?.Output(\"PDF\", target.FilePath)", feature);
            // 出图：窗口 + Fit + 彩色（不套打印样式表）+ 完成后校验 PDF 产物
            Assert.Contains("SetPlotWindowArea", session);
            Assert.Contains("StdScaleType.ScaleToFit", session);
            Assert.Contains("PlotPlotStyles = false", session);
            Assert.Contains("IsUsablePdf", service);
            Assert.Contains("PlotFactory.CreatePublishEngine()", session);
            Assert.Contains("PdfExportNaming.DeviceName", pipeline);

            // 自研内核：不许调用 SendCommand，也不许依赖第三方批量打印插件。
            foreach (string source in new[] { feature, service, session, pipeline })
            {
                Assert.DoesNotContain("SendCommand(", source);
                Assert.DoesNotContain("M批打印", source);
                Assert.DoesNotContain("M_PDF", source);
            }

            // 真机结论：必须把设备和介质设在布局自身的打印设置上，并在出图后原样还原。
            Assert.Contains("OpenMode.ForWrite", session);
            Assert.Contains("PdfPlotSettingsSnapshot.Capture", session);
            Assert.Contains("_original.Restore(_layout)", session);
        }

        [Fact]
        public void PlotWindowMustBeSetBeforeTheWindowPlotType()
        {
            // 真机根因锁：先 SetPlotType(Window) 再 SetPlotWindowArea，AutoCAD 抛 eInvalidInput。
            string pipeline = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfPlotSession.cs"));
            int window = pipeline.IndexOf("session.SetWindowArea(window)",
                StringComparison.Ordinal);
            int type = pipeline.IndexOf("session.SetPlotTypeWindow()", StringComparison.Ordinal);
            Assert.True(window >= 0 && type >= 0, "打印窗口/打印范围必须都设置有");
            Assert.True(window < type,
                "PdfPlotPipeline 里 SetWindowArea 必须先于 SetPlotTypeWindow");
        }

        // ---- 回路名推导必须与 U1F/U1U 同源 ----

        [Fact]
        public void FrameCircuit_IsResolvedByTheSameResolverAsU1FAndU1U()
        {
            // 与 FillFeature 的 U1U 路径同一套：FrameIdentityReader 读持久身份 +
            // ExistingFillIdentityResolver.MatchMachine 匹配快照行；回路名取匹配行的 CircuitName。
            string fill = File.ReadAllText(Repo("src", "UNCAD", "Features", "Fill",
                "FillFeature.cs"));
            Assert.Contains("ExistingFillIdentityResolver.MatchMachine(identity,", fill);
            Assert.Contains("internal static bool TryResolveFrameCircuit(", fill);
            Assert.Contains("workbook.FindRows(identity.MachineId)", fill);

            string pdf = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfExportFeature.cs"));
            Assert.Contains("FillFeature.TryResolveFrameCircuit(", pdf);
            // PDF 功能不得自己另写一套回路名推导。
            Assert.DoesNotContain("ExistingFillIdentityResolver.MatchMachine(", pdf);
            Assert.DoesNotContain("DeviceName =", pdf);
        }

        [Fact]
        public void MatchMachine_ReturnsTheFramesOwnCircuitName()
        {
            var identity = new ExistingFillIdentity
            {
                MachineId = "MDIDA03",
                DeviceName = "MDIDA03-2"
            };
            var candidates = new List<MachineRow>
            {
                new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-1" },
                new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-2" }
            };

            MachineRow matched = ExistingFillIdentityResolver.MatchMachine(identity,
                candidates, out string error);

            Assert.Equal("", error);
            Assert.NotNull(matched);
            Assert.Equal("MDIDA03", matched.MachineId);
            Assert.Equal("MDIDA03-2", matched.CircuitName);
        }

        [Fact]
        public void MatchMachine_RefusesToGuessBetweenMultipleCircuits()
        {
            var identity = new ExistingFillIdentity
            {
                MachineId = "MDIDA03",
                DeviceName = "MDIDA03-9"
            };
            var candidates = new List<MachineRow>
            {
                new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-1" },
                new MachineRow { MachineId = "MDIDA03", CircuitName = "MDIDA03-2" }
            };

            MachineRow matched = ExistingFillIdentityResolver.MatchMachine(identity,
                candidates, out string error);

            Assert.Null(matched);
            Assert.Contains("未找到设备", error);
        }

        // ---- 辅助 ----

        private static PdfExportTarget Target(string circuit) => new PdfExportTarget
        {
            MachineId = Machine,
            CircuitName = circuit,
            FrameHandle = "1A" + circuit.Length,
            FilePath = Path.Combine(Path.GetTempPath(), circuit + ".pdf")
        };

        private static string WriteTemp(string name, byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad-pdf-" + Guid.NewGuid().ToString("N") + "-" + name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}
