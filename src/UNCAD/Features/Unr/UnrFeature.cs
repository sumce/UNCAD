using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Unr
{
    /// <summary>UNR / OPUNR：连续在直线上开半圆拱桥洞。</summary>
    [Feature("unr", "拱桥开洞",
        RibbonPanel = "绘制",
        Commands = "UNC_ARCH;UNC_ARCH_SET",
        Description = "在直线上连续开半圆拱桥洞")]
    public class UnrFeature : CommandBase
    {
        private const string DiameterKey = "UNR_DIAMETER";

        [CommandMethod("UNC_ARCH_SET")]
        public void UncadArchSet()
        {
            Guard(() =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var ed = doc.Editor;

                double oldDia = Settings.GetDouble(DiameterKey, 300.0);
                var opts = new PromptDistanceOptions(
                    "\n请输入新的拱桥直径 <" + TextFormatter.FormatNum(oldDia) + ">: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = oldDia,
                    UseDefaultValue = true
                };
                var res = ed.GetDistance(opts);
                if (res.Status == PromptStatus.OK && res.Value > 0)
                {
                    Settings.Set(DiameterKey,
                        res.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    ed.WriteMessage("\n拱桥直径已设置为: " + TextFormatter.FormatNum(res.Value));
                }
                else
                {
                    ed.WriteMessage("\n保持当前直径。");
                }
            });
        }

        // 规范命令：UNC_ARCH / UNC_ARCH_SET
        [CommandMethod("UNC_ARCH")]
        public void UncadArch() => Run();

        // 旧名兼容（后续版本可删除）
        [CommandMethod("UNR")]
        public void Unr() => UncadArch();

        [CommandMethod("OPUNR")]
        public void OpUnr() => UncadArchSet();

        protected override void Execute(CadContext ctx)
        {
            var ed = ctx.Ed;

            double diameter0 = Settings.GetDouble(DiameterKey, 300.0);
            ConfigPrinter.Print(ctx, "UNC_ARCH", ("直径", TextFormatter.FormatNum(diameter0)));

            object oldCmdEcho = null, oldOsmode = null;
            try { oldCmdEcho = Application.GetSystemVariable("CMDECHO"); }
            catch (System.Exception ex) { Log.Warn("读取 CMDECHO 失败: " + ex.Message); }
            try { oldOsmode = Application.GetSystemVariable("OSMODE"); }
            catch (System.Exception ex) { Log.Warn("读取 OSMODE 失败: " + ex.Message); }
            try { Application.SetSystemVariable("CMDECHO", (short)0); }
            catch (System.Exception ex) { Log.Warn("设置 CMDECHO 失败: " + ex.Message); }

            ed.WriteMessage("\n>> 进入连续开洞模式 (按 ESC 或空格键退出)...");
            try
            {
                while (true)
                {
                    var eopts = new PromptEntityOptions("\n点击直线直接开洞: ")
                    {
                        AllowNone = true
                    };
                    eopts.SetRejectMessage("\n[警告] 仅支持 LINE 直线，已跳过。");
                    eopts.AddAllowedClass(typeof(Line), false);

                    PromptEntityResult sel;
                    try
                    {
                        sel = ed.GetEntity(eopts);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak)
                    {
                        break;
                    }
                    if (sel.Status == PromptStatus.None) break;
                    if (sel.Status != PromptStatus.OK) break;

                    using (var tr = ctx.Db.TransactionManager.StartTransaction())
                    {
                        var line = tr.GetObject(sel.ObjectId, OpenMode.ForRead) as Line;
                        if (line == null)
                        {
                            ed.WriteMessage("\n[警告] 仅支持 LINE 直线，已跳过。");
                            tr.Commit();
                            continue;
                        }

                        double diameter = Settings.GetDouble(DiameterKey, 300.0);
                        double R = diameter / 2.0;
                        double totalLen = line.Length;
                        Point3d closest = line.GetClosestPointTo(sel.PickedPoint, false);
                        double distC = line.GetDistAtPoint(closest);

                        // 边界安全检查
                        if (distC < R || (totalLen - distC) < R)
                        {
                            ed.WriteMessage("\n[警告] 位置太靠近端点，已跳过。");
                            tr.Commit();
                            continue;
                        }

                        Point3d bp1 = line.GetPointAtDist(Math.Max(0.0, distC - R));
                        Point3d bp2 = line.GetPointAtDist(Math.Min(totalLen, distC + R));

                        // 智能判断上半边（合并到 GeoMath，与 LISP 启发式一致）
                        bool reverse = GeoMath.ArcNeedReverse(closest, bp1, bp2, R);

                        tr.Commit();

                        // 临时关闭捕捉，执行 ARC + BREAK（命令级，与 LISP 行为一致）
                        try { Application.SetSystemVariable("OSMODE", (short)0); }
                        catch (System.Exception ex) { Log.Warn("关闭 OSMODE 失败: " + ex.Message); }
                        try
                        {
                            var c2d = new Point3d(closest.X, closest.Y, 0);
                            Point3d start = reverse ? bp2 : bp1;
                            Point3d end = reverse ? bp1 : bp2;
                            ed.Command("\\_.ARC", "\\_C", c2d, start, end);
                            ed.Command("\\_.BREAK", sel.ObjectId, bp1, bp2);
                        }
                        finally
                        {
                            if (oldOsmode != null)
                            {
                                try { Application.SetSystemVariable("OSMODE", oldOsmode); }
                                catch (System.Exception ex) { Log.Warn("恢复 OSMODE 失败: " + ex.Message); }
                            }
                        }
                    }
                }
            }
            finally
            {
                if (oldCmdEcho != null)
                {
                    try { Application.SetSystemVariable("CMDECHO", oldCmdEcho); }
                    catch (System.Exception ex) { Log.Warn("恢复 CMDECHO 失败: " + ex.Message); }
                }
                if (oldOsmode != null)
                {
                    try { Application.SetSystemVariable("OSMODE", oldOsmode); }
                    catch (System.Exception ex) { Log.Warn("恢复 OSMODE 失败: " + ex.Message); }
                }
            }
            ed.WriteMessage("\n已退出连续开洞模式。");
        }
    }
}
