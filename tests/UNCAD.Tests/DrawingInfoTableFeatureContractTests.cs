using System;
using System.IO;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class DrawingInfoTableFeatureContractTests
    {
        [Fact]
        public void XframeAndDrawingInfoTable_AreConnectedToBothFillCommands()
        {
            string collector = Read("src", "UNCAD", "Cad", "FrameRegionCollector.cs");
            string selection = Read("src", "UNCAD", "Cad", "Frame",
                "FillSelectionCollector.cs");
            string fill = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            string update = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string migration = Read("src", "UNCAD", "Features", "Fill",
                "XFrameMigrationService.cs");

            Assert.Contains("XFrameName = \"xframe\"", collector);
            Assert.Contains("CadTableLayoutClassifier.IsDrawingInfoTable", selection);
            Assert.Contains("selection.DrawingInfoTableIds", fill);
            Assert.Contains("plan.Selection.DrawingInfoTableIds", update);
            Assert.Contains("XFrameMigrationService.Migrate", fill);
            Assert.Contains("XFrameMigrationService.Migrate", update);
            Assert.Contains("FrameRegionCollector.IsLegacyFrame(transaction, frame)", migration);
            Assert.Contains("frame.BlockTableRecord = targetDefinitionId", migration);
            Assert.DoesNotContain("EraseTables(", migration);
            Assert.Contains("selection.TableIds = new[] { boqId }", migration);
            Assert.Contains("selection.DrawingInfoTableIds =", migration);
            Assert.Contains("RepairSplitDrawingInfoTables", migration);
            Assert.Contains("HasDrawingInfoTable", migration);
            Assert.Contains("ReplaceSourceIds(selection, Array.Empty<ObjectId>(), boqId)",
                migration);
            Assert.Contains("ReplaceSourceIds(selection, Array.Empty<ObjectId>(), tableId)",
                migration);
            Assert.Contains("DefaultProjectName = \"新项目二次配电力工程(Power)\"",
                migration);
            Assert.Contains("EnsureProjectNameDefaults", migration);
            Assert.Contains("FrameRegionCollector.CurrentFrameName", migration);
            string writer = Read("src", "UNCAD", "Features", "Fill",
                "CadDrawingInfoTableWriter.cs");
            // D-057：六列内容按 U1SET 模板写；模板留空的列不动图框里的内容。
            Assert.Contains("DrawingInfoTemplates templates = null", writer);
            Assert.Contains("if (!effective.IsSet(column)) continue;", writer);
            Assert.Contains("table.SetTextString(headerRow + 1, column, values[column])", writer);
            Assert.Contains("DrawingInfoContext.ForRun(machine, previous,", writer);
            // 单框与批量都把模板和上一条记录（时间来源）传给写入方。
            Assert.Contains("options.DrawingInfo, frameInfoPrevious", fill);
            Assert.Contains("options.DrawingInfo, plan.PreviousRecord", update);
            string submission = Read("src", "UNCAD", "Cad", "Frame",
                "CadSubmissionReader.cs");
            Assert.Contains("CadTableLayoutClassifier.IsDrawingInfoTable(table)", submission);
        }

        [Fact]
        public void XframeTemplate_IsPackagedAndRequiredByInstaller()
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            string template = Path.Combine(root, "src", "UNCAD", "Resources",
                "XFrameTemplate.dwg");
            Assert.True(File.Exists(template));
            Assert.Contains("Resources\\XFrameTemplate.dwg",
                File.ReadAllText(Path.Combine(root, "build.ps1")));
            Assert.Contains("Resources\\XFrameTemplate.dwg",
                File.ReadAllText(Path.Combine(root, "installer.ps1")));
            Assert.Contains("Resources\\XFrameTemplate.dwg",
                File.ReadAllText(Path.Combine(root, "release.ps1")));
        }

        private static string Read(params string[] parts)
        {
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", ".."));
            return File.ReadAllText(Path.Combine(root, Path.Combine(parts)));
        }
    }
}
