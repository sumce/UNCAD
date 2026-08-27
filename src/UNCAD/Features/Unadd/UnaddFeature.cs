using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Report;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Unadd
{
    /// <summary>
    /// UNADD：框选文字，统计电缆长度、桥架格数与线管长度，生成汇总文字；
    /// UNADDX：同统计，导出 Excel 报表（NPOI）。
    /// </summary>
    [Feature("unadd", "文字统计汇总",
        Commands = CommandIds.StatisticsFeatureCommands,
        Description = "框选 TEXT/MTEXT，统计电缆、桥架和线管长度（UNADD 输出图纸，UNADDX 导出 Excel）")]
    public class UnaddFeature : CommandBase
    {
        // 规范命令：UNC_STAT（图纸汇总）/ UNC_STAT_EX（Excel 导出）
        // 通过 Run(state) 传参，避免命令类共享可变实例字段
        [CommandMethod(CommandIds.Statistics, CommandFlags.UsePickSet)]
        public void UncadStat() => Run(false);

        [CommandMethod(CommandIds.StatisticsExcel, CommandFlags.UsePickSet)]
        public void UncadStatEx() => Run(true);

        // 旧名兼容（后续版本可删除）
        [CommandMethod(CommandIds.LegacyStatistics, CommandFlags.UsePickSet)]
        public void Unadd() => UncadStat();

        [CommandMethod(CommandIds.LegacyStatisticsExcel, CommandFlags.UsePickSet)]
        public void UnaddX() => UncadStatEx();

        protected override void Execute(CadContext ctx) => Execute(ctx, false);

        protected override void Execute(CadContext ctx, object state)
        {
            var stat = CollectAndCalculate(ctx);
            if (stat == null) return;

            if (state is bool b && b)
            {
                ExportExcel(ctx, stat);
            }
            else
            {
                var report = StatCalculator.BuildReport(stat);
                var pp = new PromptPointOptions("\n请点击指定统计结果放置位置: ");
                var pr = ctx.Ed.GetPoint(pp);
                if (pr.Status != PromptStatus.OK) return;

                double hgt = Settings.GetDouble(ConfigKeys.UnaddHeight, 180.0);

                using (var tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    // 电缆长度和各规格桥架长度都跟随图纸 Standard 样式
                    ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, tr);
                    // 单行文字逐行输出（从点击点向下排）：无 MTEXT 控制码、可单独编辑，
                    // 与旧 UNADD 兼容（TEXT 读取干净，不会夹带 \\P 等控制内容）
                    double lineSpacing = hgt * 1.5;
                    for (int i = 0; i < report.Count; i++)
                    {
                        var pt = new Point3d(pr.Value.X, pr.Value.Y - i * lineSpacing, pr.Value.Z);
                        var t = EntityFactory.DBText(ctx, report[i], pt, hgt, 0.0,
                            AttachmentPoint.TopLeft, textStyleId: styleId);
                        ctx.AddToCurrentSpace(tr, t);
                    }
                    tr.Commit();
                }
                ctx.Write("\n[UNADD] 汇总结果已成功生成！");
            }
        }

        /// <summary>选择文字并统计（UNADD/UNADDX 共用；取消返回 null）。</summary>
        private CableStatResult CollectAndCalculate(CadContext ctx)
        {
            var ids = SelectionService.PickFirstOrPrompt(ctx, "请框选需要统计的单行文字/多行文字: ",
                new TypedValue(0, "TEXT,MTEXT"));
            if (ids == null) return null;

            var rawLines = new List<string>();
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent is DBText t) rawLines.Add(t.TextString);
                    else if (ent is MText mt) rawLines.AddRange(TextParser.SplitMTextLines(mt.Contents));
                }
                tr.Commit();
            }

            var cleaned = rawLines.Select(TextParser.CleanMText).ToList();
            double mmPerGrid = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0);
            return StatCalculator.Calculate(cleaned, mmPerGrid);
        }

        private void ExportExcel(CadContext ctx, CableStatResult stat)
        {
            using (var dlg = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                FileName = "UNCAD统计_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".xlsx",
                Title = "导出统计报表"
            })
            {
                if (dlg.ShowDialog(new UI.WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle)) !=
                    System.Windows.Forms.DialogResult.OK)
                    return;

                try
                {
                    StatExcelReporter.WriteToFile(stat, dlg.FileName);
                    Log.Info("UNADDX exported: " + dlg.FileName);
                    ctx.Write("\n[UNADDX] 已导出 Excel: " + dlg.FileName);
                }
                catch (System.Exception ex)
                {
                    ctx.Write("\n[UNADDX] 导出失败: " + ex.Message);
                    Log.Error("UNADDX export failed", ex);
                }
            }
        }
    }
}
