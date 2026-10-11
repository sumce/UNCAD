using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// 事务语义探针：验证 <c>Transaction.Dispose()</c>（没有 Commit）到底是回滚还是提交。
    ///
    /// 为什么必须验：U1PDF 的失败路径改成"不提交、靠回滚丢弃临时打印设置"，
    /// 这条修复的**全部正确性**都建立在"不 Commit 就等于回滚"这个前提上。
    /// 如果 Dispose 其实是提交，那失败路径仍会把临时设备/纸张/旋转写进用户图纸。
    /// </summary>
    public sealed class TransactionSemanticsProbeCommand
    {
        [CommandMethod("UNCAD_TXPROBE")]
        public void Run()
        {
            var log = new List<string>();
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            try
            {
                ObjectId layoutId = ModelLayoutId(database);
                bool original = ReadFlag(database, layoutId, out string device);
                log.Add("初始：PlotPlotStyles=" + original + " 设备=" + device);

                // A) 改了之后直接 Dispose（不 Commit）——这正是 U1PDF 失败路径依赖的行为。
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
                    layout.PlotPlotStyles = !original;
                }
                bool afterDispose = ReadFlag(database, layoutId, out _);
                log.Add("A 不 Commit 直接 Dispose → PlotPlotStyles=" + afterDispose
                    + " 结论：" + (afterDispose == original
                        ? "回滚（Dispose 未提交，修复前提成立）"
                        : "已提交（Dispose 提交了！失败路径的修复不成立）"));
                if (afterDispose != original) Restore(database, layoutId, original);
                log.Add("   已恢复现场：PlotPlotStyles=" + ReadFlag(database, layoutId, out _));

                // B) 显式 Abort 作为对照。
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
                    layout.PlotPlotStyles = !original;
                    transaction.Abort();
                }
                bool afterAbort = ReadFlag(database, layoutId, out _);
                log.Add("B 显式 Abort → PlotPlotStyles=" + afterAbort
                    + " 结论：" + (afterAbort == original ? "回滚" : "已提交（异常！）"));
                if (afterAbort != original) Restore(database, layoutId, original);

                // C) Commit 作为对照，确认改动确实能被写进图纸。
                using (Transaction transaction = database.TransactionManager.StartTransaction())
                {
                    var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
                    layout.PlotPlotStyles = !original;
                    transaction.Commit();
                }
                bool afterCommit = ReadFlag(database, layoutId, out _);
                log.Add("C 显式 Commit → PlotPlotStyles=" + afterCommit
                    + " 结论：" + (afterCommit != original ? "已提交（符合预期）" : "未提交（异常）"));
                Restore(database, layoutId, original);
                log.Add("收尾：PlotPlotStyles=" + ReadFlag(database, layoutId, out _)
                    + "（应等于初始 " + original + "）");
            }
            catch (System.Exception ex)
            {
                log.Add("探针异常：" + ex);
            }
            finally
            {
                string path = Environment.GetEnvironmentVariable("UNCAD_PLOT_PROBE_DUMP");
                if (!string.IsNullOrWhiteSpace(path))
                    try { File.WriteAllText(path, string.Join("\r\n", log.ToArray()), Encoding.UTF8); }
                    catch { }
                document.Editor.WriteMessage("\nUNCAD_TXPROBE:DONE\n");
            }
        }

        private static void Restore(Database database, ObjectId layoutId, bool value)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForWrite);
                layout.PlotPlotStyles = value;
                transaction.Commit();
            }
        }

        private static bool ReadFlag(Database database, ObjectId layoutId, out string device)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layout = (Layout)transaction.GetObject(layoutId, OpenMode.ForRead);
                device = layout.PlotConfigurationName ?? "";
                bool value = layout.PlotPlotStyles;
                transaction.Commit();
                return value;
            }
        }

        private static ObjectId ModelLayoutId(Database database)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                var layouts = (DBDictionary)transaction.GetObject(database.LayoutDictionaryId,
                    OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in layouts)
                {
                    var candidate = transaction.GetObject(entry.Value, OpenMode.ForRead) as Layout;
                    if (candidate != null && candidate.ModelType)
                    {
                        transaction.Commit();
                        return entry.Value;
                    }
                }
                transaction.Commit();
            }
            return ObjectId.Null;
        }
    }
}
