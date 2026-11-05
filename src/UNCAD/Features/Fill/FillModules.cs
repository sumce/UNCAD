using System.Collections.Generic;
using System.Linq;
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
            double mmPerGrid, bool statisticsScopeComplete,
            IReadOnlyCollection<ObjectId> excludeTextIds = null)
        {
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                SummationOutput output = Execute(ctx, transaction, textIds, mmPerGrid,
                    statisticsScopeComplete, null, excludeTextIds);
                transaction.Commit();
                return output;
            }
        }

        internal static SummationOutput Execute(CadContext ctx, Transaction transaction,
            ObjectId[] textIds, double mmPerGrid, bool statisticsScopeComplete,
            StatisticsSettingsSnapshot settings = null,
            IReadOnlyCollection<ObjectId> excludeTextIds = null)
        {
            settings = settings ?? StatisticsSettings.Current();
            // 2026-11-05：排除被 D-043 吸收为设备内预留的横排毫米标——它同时
            // 命中电缆长度判据（^数字0mm$），不排除会双算总长。
            if (excludeTextIds != null && excludeTextIds.Count > 0)
            {
                var excluded = new HashSet<ObjectId>(excludeTextIds);
                textIds = textIds.Where(id => !excluded.Contains(id)).ToArray();
            }
            List<string> lines = ModuleRunner.Run(SummationModule.Descriptor,
                "读取图中文字", () => FillSelectionCollector.ReadStatisticsLines(
                    transaction, textIds, settings.IncludeText, settings.IncludeMText,
                    settings.IncludeDimension));
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
            FillPlanningOptions options, FillAutoFillOptions autoFill = null)
        {
            TableGenerationOutput output = ModuleRunner.Run(TableGenerationModule.Descriptor,
                "规划清单行", () => TableGenerationModule.Plan(
                    new TableGenerationRequest(machine, catalog, statistics, options,
                        autoFill)));
            TableFillPlanner.ApplyBridgeCatalogModels(catalog, statistics);
            return output;
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
