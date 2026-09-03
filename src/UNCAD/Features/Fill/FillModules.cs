using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    internal static class FillStatisticsModule
    {
        public static SummationOutput Execute(CadContext ctx, ObjectId[] textIds,
            double mmPerGrid, bool statisticsScopeComplete)
        {
            StatisticsSettingsSnapshot settings = StatisticsSettings.Current();
            List<string> lines = ModuleRunner.Run(SummationModule.Descriptor,
                "读取图中文字", () => FillSelectionCollector.ReadStatisticsLines(
                    ctx, textIds, settings.IncludeText, settings.IncludeMText));
            settings.Calculation.MmPerGrid = mmPerGrid;
            SummationOutput output = ModuleRunner.Run(SummationModule.Descriptor,
                "解析并求和", () => SummationModule.Execute(
                    new SummationRequest(lines, settings.Calculation)));
            output.Statistics.ApplySourceCoverage(statisticsScopeComplete);
            Log.Info("MODULE " + SummationModule.Descriptor.Label + " result: sources="
                + output.SourceLineCount + ", cable=" + output.CableMatchCount
                + ", bridge=" + output.BridgeMatchCount + ", conduit="
                + output.ConduitMatchCount);
            return output;
        }
    }

    internal static class FillTableModule
    {
        public static TableGenerationOutput Plan(MachineRow machine,
            BoqCatalogIndex catalog, CableStatResult statistics,
            FillPlanningOptions options)
        {
            return ModuleRunner.Run(TableGenerationModule.Descriptor,
                "规划清单行", () => TableGenerationModule.Plan(
                    new TableGenerationRequest(machine, catalog, statistics, options)));
        }

        public static int Write(CadContext ctx, Transaction transaction,
            ObjectId[] tableIds, int startRow, int clearRowCount,
            List<TableFillRow> rows, double textHeight)
        {
            return ModuleRunner.Run(TableGenerationModule.Descriptor,
                "写入CAD表格", () => CadTableFillWriter.Fill(ctx, transaction,
                    tableIds, startRow, clearRowCount, rows, textHeight));
        }
    }
}
