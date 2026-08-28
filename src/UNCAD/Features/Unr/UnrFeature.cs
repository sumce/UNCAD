using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Geometry;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Unr
{
    /// <summary>U1R / UNR：连续在直线上开半圆拱桥洞。</summary>
    [Feature("unr", "拱桥开洞",
        Commands = CommandIds.ArchFeatureCommands,
        Description = "在直线上连续开半圆拱桥洞")]
    public class UnrFeature : CommandBase
    {
        public void UncadArchSet()
        {
            Guard(() =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var ed = doc.Editor;

                double oldDia = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
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
                    Settings.Set(ConfigKeys.UnrDiameter,
                        res.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    ed.WriteMessage("\n拱桥直径已设置为: " + TextFormatter.FormatNum(res.Value));
                }
                else
                {
                    ed.WriteMessage("\n保持当前直径。");
                }
            });
        }

        // 规范命令：U1R / U1S
        [CommandMethod(CommandIds.Arch)]
        public void UncadArch() => Run();

        // 旧名兼容（后续版本可删除）
        [CommandMethod(CommandIds.LegacyArch)]
        public void Unr() => UncadArch();

        public void OpUnr() => UncadArchSet();

        protected override void Execute(CadContext ctx)
        {
            var ed = ctx.Ed;

            double diameter0 = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
            ConfigPrinter.Print(ctx, CommandIds.Arch, ("直径", TextFormatter.FormatNum(diameter0)));

            ed.WriteMessage("\n>> 进入连续开洞模式 (按 ESC 或空格键退出)...");
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

                    double diameter = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
                    double R = diameter / 2.0;
                    double totalLen = line.Length;
                    const double geometryTolerance = 1e-7;
                    if (diameter <= geometryTolerance || double.IsNaN(diameter)
                        || double.IsInfinity(diameter)
                        || totalLen <= diameter + geometryTolerance)
                    {
                        ed.WriteMessage("\n[警告] 直线长度必须大于洞口直径，已跳过。");
                        tr.Commit();
                        continue;
                    }
                    Point3d closest = line.GetClosestPointTo(sel.PickedPoint, false);
                    double distC = line.GetDistAtPoint(closest);

                    // 边界安全检查
                    if (!SemicircleGeometry.CanSplit(
                        totalLen, diameter, distC, geometryTolerance))
                    {
                        ed.WriteMessage("\n[警告] 位置太靠近端点，已跳过。");
                        tr.Commit();
                        continue;
                    }

                    Point3d bp1 = line.GetPointAtDist(Math.Max(0.0, distC - R));
                    Point3d bp2 = line.GetPointAtDist(Math.Min(totalLen, distC + R));

                    if (Math.Abs(line.StartPoint.Z - line.EndPoint.Z) > 1e-7)
                    {
                        ed.WriteMessage("\n[警告] 仅支持位于同一XY平面的二维直线，已跳过。");
                        tr.Commit();
                        continue;
                    }

                    GeoMath.SemicircleAngles(closest, bp1, bp2, R,
                        out double startAngle, out double endAngle);
                    Point3d originalEnd = line.EndPoint;
                    var second = line.Clone() as Line;
                    if (second == null)
                    {
                        ed.WriteMessage("\n[警告] 无法复制目标直线，已跳过。");
                        tr.Commit();
                        continue;
                    }

                    line.UpgradeOpen();
                    line.EndPoint = bp1;
                    second.StartPoint = bp2;
                    second.EndPoint = originalEnd;
                    var arc = new Arc(closest, Vector3d.ZAxis, R, startAngle, endAngle);
                    arc.SetPropertiesFrom(line);

                    var owner = tr.GetObject(line.OwnerId, OpenMode.ForWrite)
                        as BlockTableRecord;
                    if (owner == null)
                    {
                        second.Dispose();
                        arc.Dispose();
                        throw new InvalidOperationException("无法写入直线所在空间。");
                    }
                    owner.AppendEntity(second);
                    tr.AddNewlyCreatedDBObject(second, true);
                    owner.AppendEntity(arc);
                    tr.AddNewlyCreatedDBObject(arc, true);
                    tr.Commit();
                }
            }
            ed.WriteMessage("\n已退出连续开洞模式。");
        }
    }
}
