using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Unadd
{
    /// <summary>
    /// UNADD：框选文字，统计电缆长度、桥架格数与线管长度，并生成汇总文字。
    /// </summary>
    [Feature("unadd", "文字统计汇总",
        Commands = CommandIds.StatisticsFeatureCommands,
        Description = "框选 TEXT/MTEXT，统计电缆、桥架和线管长度并输出图纸汇总")]
    public class UnaddFeature : CommandBase
    {
        // UNADD is intentionally the only public statistics command retained for compatibility.
        [CommandMethod(CommandIds.LegacyStatistics, CommandFlags.UsePickSet)]
        public void Unadd() => Run();

        protected override void Execute(CadContext ctx)
        {
            CableStatResult stat = CollectAndCalculate(ctx);
            if (stat == null) return;

            IReadOnlyList<string> report = StatCalculator.BuildReport(stat);
            var pointOptions = new PromptPointOptions("\n请点击指定统计结果放置位置: ");
            var pointResult = ctx.Ed.GetPoint(pointOptions);
            if (pointResult.Status != PromptStatus.OK) return;

            double height = Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0);
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                // Output remains individual DBText rows so legacy drawings can edit every line.
                ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);
                double lineSpacing = height * 1.5;
                for (int i = 0; i < report.Count; i++)
                {
                    var point = new Point3d(pointResult.Value.X,
                        pointResult.Value.Y - i * lineSpacing, pointResult.Value.Z);
                    var text = EntityFactory.DBText(ctx, report[i], point, height, 0.0,
                        AttachmentPoint.TopLeft, textStyleId: styleId);
                    ctx.AddToCurrentSpace(transaction, text);
                }
                transaction.Commit();
            }
            ctx.Write("\n[UNADD] 汇总结果已成功生成！");
        }

        /// <summary>选择文字并按当前统计设置汇总；用户取消时返回 null。</summary>
        private CableStatResult CollectAndCalculate(CadContext ctx)
        {
            StatisticsSettingsSnapshot settings = StatisticsSettings.Current();
            if (settings.SelectionFilter.Length == 0)
            {
                ctx.Write("\n[UNADD] TEXT和MTEXT来源均已关闭，请先在配置中心开启。");
                return null;
            }
            if (!settings.Calculation.IncludeCable && !settings.Calculation.IncludeBridge
                && !settings.Calculation.IncludeConduit)
            {
                ctx.Write("\n[UNADD] 电缆、桥架和线管统计均已关闭，请先在配置中心开启。");
                return null;
            }
            ConfigPrinter.Print(ctx, CommandIds.LegacyStatistics,
                ("文字来源", SourceLabel(settings)),
                ("统计类别", CategoryLabel(settings.Calculation)),
                ("桥架每格", TextFormatter.FormatNum(settings.Calculation.MmPerGrid) + " mm"));
            var ids = SelectionService.PickFirstOrPrompt(ctx, "请框选需要统计的文字: ",
                new TypedValue(0, settings.SelectionFilter));
            if (ids == null) return null;

            List<string> rawLines = ModuleRunner.Run(SummationModule.Descriptor,
                "读取图中文字", () =>
                {
                    var lines = new List<string>();
                    using (var transaction = ctx.Db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in ids)
                        {
                            var entity = transaction.GetObject(id, OpenMode.ForRead) as Entity;
                            if (settings.IncludeText && entity is DBText text)
                                lines.Add(text.TextString);
                            else if (settings.IncludeMText && entity is MText mtext)
                                lines.AddRange(TextParser.SplitMTextLines(mtext.Contents));
                        }
                    }
                    return lines;
                });

            List<string> cleaned = rawLines.Select(TextParser.CleanMText).ToList();
            SummationOutput output = ModuleRunner.Run(SummationModule.Descriptor,
                "解析并求和", () => SummationModule.Execute(
                    new SummationRequest(cleaned, settings.Calculation)));
            ctx.Write("\n[SUM-STAT/求和统计] 来源 " + output.SourceLineCount
                + " 行，命中 " + output.TotalMatchCount + " 行。");
            return output.Statistics;
        }

        private static string SourceLabel(StatisticsSettingsSnapshot settings)
        {
            if (settings.IncludeText && settings.IncludeMText) return "TEXT + MTEXT";
            return settings.IncludeText ? "TEXT" : "MTEXT";
        }

        private static string CategoryLabel(StatCalculationOptions options)
        {
            var values = new List<string>();
            if (options.IncludeCable) values.Add("电缆");
            if (options.IncludeBridge) values.Add("桥架");
            if (options.IncludeConduit) values.Add("线管");
            return string.Join(" + ", values);
        }

    }
}
