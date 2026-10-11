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
    /// U1PDF 契约：**一个图框 = 一个回路 = 一份 PDF**（与按机台的 DWG 不同）。
    /// 锁住：文件命名与 %PDF- 校验、上传请求构造（kind=pdf / circuits 恰好一个 /
    /// .pdf 结尾）、回路名推导与 U1F/U1U 同源、以及"某一份出图失败不影响其余份上传"。
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
        [InlineData("MDIDA03-1", "MDIDA03-1.pdf")]
        [InlineData("  MDIDA03-1  ", "MDIDA03-1.pdf")]
        [InlineData("MDIDA03-1.pdf", "MDIDA03-1.pdf")]
        [InlineData("A/B:C*D?", "A_B_C_D_.pdf")]
        [InlineData("", "circuit.pdf")]
        [InlineData("   ", "circuit.pdf")]
        public void BuildFileName_AlwaysEndsWithPdfAndIsSafe(string circuitName, string expected)
            => Assert.Equal(expected, PdfExportNaming.BuildFileName(circuitName));

        [Fact]
        public void BuildFileName_KeepsChineseCircuitNamesWithinLengthLimit()
        {
            string name = PdfExportNaming.BuildFileName("一号厂房-3F-1号回路");
            Assert.Equal("一号厂房-3F-1号回路.pdf", name);

            string longName = PdfExportNaming.BuildFileName(new string('机', 300));
            Assert.True(longName.Length <= 104, "文件名必须截断到安全长度：" + longName.Length);
            Assert.True(PdfExportNaming.HasPdfExtension(longName));
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

        // ---- 上传请求构造（kind=pdf、恰好一个回路、.pdf 结尾）----

        [Fact]
        public void UploadQuery_UsesPdfKindExactlyOneCircuitAndPdfFileName()
        {
            var credentials = new ClientSubmissionCredentials
            {
                Account = "devuser",
                DeviceId = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a",
                Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe"
            };

            string query = ClientSubmissionUploader.BuildUploadQuery(credentials, Machine,
                PdfExportNaming.BuildFileName(Circuit), new List<string> { Circuit },
                ClientSubmissionUploader.KindPdf);

            Assert.Contains("kind=pdf", query);
            Assert.Contains("fileName=" + Circuit + ".pdf", query);
            Assert.Contains("circuits=" + Circuit, query);
            Assert.DoesNotContain(",", query.Substring(query.IndexOf("circuits=",
                StringComparison.Ordinal)));
            Assert.Equal(".pdf", ClientSubmissionUploader.PdfExtension);
        }

        [Fact]
        public void UploadPdf_PostsRawStreamWithPdfKind()
        {
            // 临时目录里放一份"真正的" <回路>.pdf，模拟命令的出图产物命名。
            string directory = Path.Combine(Path.GetTempPath(),
                "uncad-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, Circuit + ".pdf");
            File.WriteAllBytes(file, Encoding.ASCII.GetBytes("%PDF-1.7 body"));
            try
            {
                ClientSubmissionRequest captured = null;
                var uploader = new ClientSubmissionUploader(
                    () => Credentials(),
                    request => { captured = request; return new ClientSubmissionHttpResult(200, "{\"ok\":true}"); });

                ClientSubmissionOutcome outcome = uploader.UploadPdf(Machine, file, Circuit);

                Assert.Equal(ClientSubmissionStatus.Accepted, outcome.Status);
                Assert.Equal("/api/client/submissions/file", captured.Path);
                Assert.Equal(file, captured.FilePath);       // 原始字节流，不是 multipart/JSON
                Assert.Null(captured.JsonBody);
                Assert.Contains("kind=pdf", captured.Query);
                Assert.Contains("circuits=" + Circuit, captured.Query);
                Assert.Contains("fileName=" + Circuit + ".pdf", captured.Query);
            }
            finally
            {
                TryDelete(file);
                try { Directory.Delete(directory, true); } catch (System.Exception) { }
            }
        }

        [Fact]
        public void UploadPdf_RejectsMissingOrMultipleCircuitsLocally()
        {
            string file = WriteTemp(Circuit + ".pdf", Encoding.ASCII.GetBytes("%PDF-1.7"));
            try
            {
                bool sent = false;
                var uploader = new ClientSubmissionUploader(() => Credentials(),
                    request => { sent = true; return new ClientSubmissionHttpResult(200, ""); });

                ClientSubmissionOutcome none = uploader.UploadPdf(Machine, file, "");
                Assert.Equal(ClientSubmissionStatus.Rejected, none.Status);
                Assert.Contains("只能指定一个回路", none.Message);

                // 回路名里的逗号会被服务端当作多个回路 → 客户端同样拒绝。
                ClientSubmissionOutcome two = uploader.UploadPdf(Machine, file, "A,B");
                Assert.Equal(ClientSubmissionStatus.Rejected, two.Status);
                Assert.False(sent, "不满足服务端硬规则时不得发请求");
            }
            finally { TryDelete(file); }
        }

        [Fact]
        public void UploadPdf_RejectsNonPdfFileNameLocally()
        {
            string file = WriteTemp("drawing.dwg", Encoding.ASCII.GetBytes("AC1032"));
            try
            {
                bool sent = false;
                var uploader = new ClientSubmissionUploader(() => Credentials(),
                    request => { sent = true; return new ClientSubmissionHttpResult(200, ""); });

                ClientSubmissionOutcome outcome = uploader.UploadPdf(Machine, file, Circuit);

                Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
                Assert.Contains(".pdf", outcome.Message);
                Assert.False(sent);
            }
            finally { TryDelete(file); }
        }

        [Fact]
        public void UploadPdf_SkipsSilentlyWhenNotSignedInAndSurfaces409()
        {
            string file = WriteTemp(Circuit + ".pdf", Encoding.ASCII.GetBytes("%PDF-1.7"));
            try
            {
                var anonymous = new ClientSubmissionUploader(
                    () => new ClientSubmissionCredentials(),
                    request => new ClientSubmissionHttpResult(200, ""));
                Assert.Equal(ClientSubmissionStatus.SkippedNotSignedIn,
                    anonymous.UploadPdf(Machine, file, Circuit).Status);

                var kicked = new ClientSubmissionUploader(() => Credentials(),
                    request => new ClientSubmissionHttpResult(409,
                        "{\"error\":\"本机授权已停止（账号已在其他设备登录）。\"}"));
                ClientSubmissionOutcome outcome = kicked.UploadPdf(Machine, file, Circuit);
                Assert.Equal(ClientSubmissionStatus.Rejected, outcome.Status);
                Assert.True(outcome.RequiresRelogin);
                Assert.Contains("UNC_LOGIN", outcome.Message);
            }
            finally { TryDelete(file); }
        }

        // ---- 出图编排：逐份进度、失败隔离、汇总 ----

        [Fact]
        public void Coordinator_RendersAndQueuesEveryFrameWithPerFrameProgress()
        {
            var messages = new List<string>();
            var queued = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target => PdfRenderResult.Success(target.CircuitName + ".pdf"),
                (target, file) => queued.Add(target.CircuitName),
                messages.Add);

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target(Circuit),
                Target("MDIDA03-2")
            });

            Assert.Equal(2, run.Total);
            Assert.Equal(2, run.Rendered);
            Assert.Equal(2, run.Queued);
            Assert.Equal(0, run.Failed);
            Assert.Equal(new[] { Circuit, "MDIDA03-2" }, queued);
            Assert.Contains(messages, message => message.Contains("正在出图 1/2"));
            Assert.Contains(messages, message => message.Contains("正在出图 2/2"));
            Assert.Contains("成功 2 份 / 失败 0 份", run.BuildSummary());
        }

        [Fact]
        public void Coordinator_SkipsFailedFrameAndStillQueuesTheOthers()
        {
            // 第二份出图失败：不得上传它，其余两份照常上传（用户需求：一份失败继续处理其余份）。
            var queued = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target => target.CircuitName == "MDIDA03-2"
                    ? PdfRenderResult.Fail("打印引擎异常")
                    : PdfRenderResult.Success(target.CircuitName + ".pdf"),
                (target, file) => queued.Add(target.CircuitName),
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target(Circuit),
                Target("MDIDA03-2"),
                Target("MDIDA03-3")
            });

            Assert.Equal(3, run.Total);
            Assert.Equal(2, run.Rendered);
            Assert.Equal(2, run.Queued);
            Assert.Equal(1, run.Failed);
            Assert.Equal(new[] { Circuit, "MDIDA03-3" }, queued);
            Assert.DoesNotContain("MDIDA03-2", queued);
            Assert.Contains(run.Failures, item => item.Contains("MDIDA03-2")
                && item.Contains("打印引擎异常"));
            string summary = run.BuildSummary();
            Assert.Contains("成功 2 份 / 失败 1 份", summary);
            Assert.Contains("打印引擎异常", summary);
        }

        [Fact]
        public void Coordinator_TreatsRenderExceptionAsFailureForThatFrameOnly()
        {
            var queued = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target =>
                {
                    if (target.CircuitName == "BOOM") throw new InvalidOperationException("渲染炸了");
                    return PdfRenderResult.Success(target.CircuitName + ".pdf");
                },
                (target, file) => queued.Add(target.CircuitName),
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                Target("BOOM"),
                Target(Circuit)
            });

            Assert.Equal(1, run.Failed);
            Assert.Equal(1, run.Queued);
            Assert.Equal(new[] { Circuit }, queued);
            Assert.Contains(run.Failures, item => item.Contains("渲染炸了"));
        }

        [Fact]
        public void Coordinator_RejectsFramesWithoutCircuitNameOrMachine()
        {
            var queued = new List<string>();
            var coordinator = new PdfExportCoordinator(
                target => PdfRenderResult.Success("x.pdf"),
                (target, file) => queued.Add(target.CircuitName),
                _ => { });

            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>
            {
                new PdfExportTarget { MachineId = "", CircuitName = Circuit, FrameHandle = "1" },
                new PdfExportTarget { MachineId = Machine, CircuitName = "", FrameHandle = "2" }
            });

            Assert.Equal(2, run.Failed);
            Assert.Empty(queued);
            Assert.Equal(0, run.Rendered);
        }

        [Fact]
        public void Coordinator_HandlesEmptySelection()
        {
            var coordinator = new PdfExportCoordinator(
                target => PdfRenderResult.Success("x.pdf"), (target, file) => { }, _ => { });
            PdfExportRun run = coordinator.Run(new List<PdfExportTarget>());
            Assert.Equal(0, run.Total);
            Assert.Contains("成功 0 份 / 失败 0 份", run.BuildSummary());
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
            // 图框身份 (机台 MDIDA03 / 设备 MDIDA03-2) 必须解析到 MDIDA03-2 那一行，
            // 不能因为"同机台只有一个回路"就回退到别的回路。
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

        // ---- 命令实现要点 ----

        [Fact]
        public void Feature_UsesFrameCollectorPlotWindowAndBackgroundUpload()
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
            // 未选中/无 pc3 都要有中文提示后返回
            Assert.Contains("未选择图框，未生成 PDF", feature);
            Assert.Contains("TryGetDeviceError", feature);
            // 上传在后台，失败不影响图纸
            Assert.Contains("ThreadPool.QueueUserWorkItem", feature);
            Assert.Contains("UploadPdf(", feature);
            // 出图：窗口 + Fit + 彩色（不套打印样式表）+ 完成后校验 PDF 产物
            Assert.Contains("SetPlotWindowArea", session);
            Assert.Contains("StdScaleType.ScaleToFit", session);
            Assert.Contains("PlotPlotStyles = false", session);
            Assert.Contains("IsUsablePdf", service);
            Assert.Contains("PlotFactory.CreatePublishEngine()", session);
            Assert.Contains("PdfExportNaming.DeviceName", pipeline);

            // 自研内核：不许调用 SendCommand（注释里提"不 SendCommand"是允许的，锁的是调用），
            // 也不许依赖第三方批量打印插件（用户明确要求：第三方插件会接管界面、影响正在画图的人）。
            foreach (string source in new[] { feature, service, session, pipeline })
            {
                Assert.DoesNotContain("SendCommand(", source);
                Assert.DoesNotContain("M批打印", source);
                Assert.DoesNotContain("M_PDF", source);
            }

            // 真机结论：必须把设备和介质设在**布局自身的**打印设置上（用 OverrideSettings
            // 时 PlotInfoValidator 会把请求的 A3 重新匹配成布局自带的 A4），
            // 并在出图后把用户设置原样还原。
            Assert.Contains("OpenMode.ForWrite", session);
            Assert.Contains("PdfPlotSettingsSnapshot.Capture", session);
            Assert.Contains("_original.Restore(_layout)", session);
        }

        [Fact]
        public void PlotWindowMustBeSetBeforeTheWindowPlotType()
        {
            // 真机根因锁：先 SetPlotType(Window) 再 SetPlotWindowArea，AutoCAD 抛 eInvalidInput
            // （2.6.0 真机出图失败就是这个）。这里在源码接线层面再锁一次顺序。
            string pipeline = File.ReadAllText(Repo("src", "UNCAD", "Features", "PdfExport",
                "PdfPlotSession.cs"));
            int window = pipeline.IndexOf("session.SetWindowArea(window)",
                StringComparison.Ordinal);
            int type = pipeline.IndexOf("session.SetPlotTypeWindow()", StringComparison.Ordinal);
            Assert.True(window >= 0 && type >= 0, "打印窗口/打印范围必须都设置有");
            Assert.True(window < type,
                "PdfPlotPipeline 里 SetWindowArea 必须先于 SetPlotTypeWindow");
        }

        // ---- 辅助 ----

        private static PdfExportTarget Target(string circuit) => new PdfExportTarget
        {
            MachineId = Machine,
            CircuitName = circuit,
            FrameHandle = "1A" + circuit.Length
        };

        private static ClientSubmissionCredentials Credentials()
            => new ClientSubmissionCredentials
            {
                Account = "devuser",
                DeviceId = "8f14e45f-ceea-467a-9575-1b0e0f0d9c3a",
                Token = "qN3fZk9Wx2LpRt7YvBc4MdE6GhJ1Ks8UoA5TiPnQrZe"
            };

        private static string WriteTemp(string name, byte[] content)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "uncad-pdf-" + Guid.NewGuid().ToString("N") + "-" + name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception) { }
        }

        private static string Repo(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return Path.Combine(new string[] { root }.Concat(parts).ToArray());
        }
    }
}
