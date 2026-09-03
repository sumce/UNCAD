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
            string selection = Read("src", "UNCAD", "Features", "Fill",
                "FillSelectionCollector.cs");
            string fill = Read("src", "UNCAD", "Features", "Fill", "FillFeature.cs");
            string update = Read("src", "UNCAD", "Features", "Fill",
                "BatchFillUpdateCoordinator.cs");
            string migration = Read("src", "UNCAD", "Features", "Fill",
                "XFrameMigrationService.cs");

            Assert.Contains("XFrameName = \"xframe\"", collector);
            Assert.Contains("CadDrawingInfoTableWriter.IsDrawingInfoTable", selection);
            Assert.Contains("selection.DrawingInfoTableIds", fill);
            Assert.Contains("plan.Selection.DrawingInfoTableIds", update);
            Assert.Contains("XFrameMigrationService.Migrate", fill);
            Assert.Contains("XFrameMigrationService.Migrate", update);
            Assert.Contains("FrameRegionCollector.SupportedFrameName", migration);
            Assert.Contains("frame.BlockTableRecord = targetDefinitionId", migration);
            Assert.Contains("EraseTables(transaction, oldTables)", migration);
            Assert.Contains("selection.TableIds = new[] { boqId }", migration);
            Assert.Contains("selection.DrawingInfoTableIds = new[] { tableId }", migration);
            Assert.Contains("ReplaceSourceIds(selection, oldTables, boqId, infoId)", migration);
            Assert.Contains("DefaultProjectName = \"新项目二次配电力工程(Power)\"",
                migration);
            Assert.Contains("EnsureProjectNameDefaults", migration);
            Assert.Contains("HasCurrentDrawingInfoTable", migration);
            string writer = Read("src", "UNCAD", "Features", "Fill",
                "CadDrawingInfoTableWriter.cs");
            Assert.Contains("table.Cells[headerRow + 1, 1].TextString = floor", writer);
            Assert.Contains("table.Cells[headerRow + 1, 4].TextString = date", writer);
            string submission = Read("src", "UNCAD", "Features", "Submit",
                "AutomaticSubmissionService.cs");
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
