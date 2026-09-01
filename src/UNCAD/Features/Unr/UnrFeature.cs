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

        // 规范命令：U1R；统一设置使用 U1SET
        [CommandMethod(CommandIds.Arch)]
        public void UncadArch() => Run();

        // 旧名兼容（后续版本可删除）
        [CommandMethod(CommandIds.LegacyArch)]
        public void Unr() => UncadArch();

        public void OpUnr() => UncadArchSet();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Arch);
            var ed = ctx.Ed;

            double diameter0 = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
            ConfigPrinter.Print(ctx, CommandIds.Arch, ("直径", TextFormatter.FormatNum(diameter0)));

            ed.WriteMessage("\n>> 进入连续开洞模式 (按 ESC 或空格键退出)...");
            while (true)
            {
                var eopts = new PromptEntityOptions("\n点击直线或已合并线路的直线段开洞: ")
                {
                    AllowNone = true
                };
                eopts.SetRejectMessage("\n[警告] 仅支持 LINE 或二维 Polyline，已跳过。");
                eopts.AddAllowedClass(typeof(Line), false);
                eopts.AddAllowedClass(typeof(Polyline), false);

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
                    var entity = tr.GetObject(sel.ObjectId, OpenMode.ForRead)
                        as Entity;
                    if (entity == null)
                    {
                        ed.WriteMessage("\n[警告] 无法读取选中的线路，已跳过。");
                        tr.Commit();
                        continue;
                    }

                    double diameter = Settings.GetDouble(ConfigKeys.UnrDiameter, 300.0);
                    bool updated;
                    string warning;
                    if (entity is Line line)
                    {
                        updated = TryReplaceLine(ctx, tr, line, sel.PickedPoint,
                            diameter, out warning);
                    }
                    else if (entity is Polyline polyline)
                    {
                        updated = TryInsertIntoPolyline(polyline, sel.PickedPoint,
                            diameter, out warning);
                    }
                    else
                    {
                        updated = false;
                        warning = "仅支持 LINE 或二维 Polyline，已跳过。";
                    }
                    if (!updated)
                    {
                        ed.WriteMessage("\n[警告] " + warning);
                        tr.Commit();
                        continue;
                    }
                    tr.Commit();
                }
            }
            ed.WriteMessage("\n已退出连续开洞模式。");
        }

        private static bool TryReplaceLine(CadContext ctx, Transaction tr,
            Line line, Point3d pickedPoint, double diameter, out string warning)
        {
            warning = string.Empty;
            if (!TryPlanOpening(line.StartPoint, line.EndPoint, pickedPoint,
                    diameter, out OpeningPlan plan, out warning)) return false;

            var merged = new Polyline(4);
            merged.SetDatabaseDefaults(ctx.Db);
            merged.SetPropertiesFrom(line);
            merged.Normal = Vector3d.ZAxis;
            merged.Elevation = line.StartPoint.Z;
            merged.Thickness = line.Thickness;
            merged.AddVertexAt(0, ToPlanPoint(line.StartPoint), 0.0, 0.0, 0.0);
            merged.AddVertexAt(1, ToPlanPoint(plan.First), plan.Bulge,
                0.0, 0.0);
            merged.AddVertexAt(2, ToPlanPoint(plan.Second), 0.0, 0.0, 0.0);
            merged.AddVertexAt(3, ToPlanPoint(line.EndPoint), 0.0, 0.0, 0.0);

            var owner = tr.GetObject(line.OwnerId, OpenMode.ForWrite)
                as BlockTableRecord;
            if (owner == null)
            {
                merged.Dispose();
                warning = "无法写入直线所在空间。";
                return false;
            }
            owner.AppendEntity(merged);
            tr.AddNewlyCreatedDBObject(merged, true);
            line.UpgradeOpen();
            line.Erase();
            return true;
        }

        private static bool TryInsertIntoPolyline(Polyline polyline,
            Point3d pickedPoint, double diameter, out string warning)
        {
            warning = string.Empty;
            const double tolerance = 1e-7;
            if (polyline.Closed || polyline.NumberOfVertices < 2
                || polyline.Normal.DotProduct(Vector3d.ZAxis) < 1.0 - tolerance)
            {
                warning = "仅支持位于 XY 平面的开放二维 Polyline。";
                return false;
            }

            Point3d nearest;
            double parameter;
            try
            {
                nearest = polyline.GetClosestPointTo(pickedPoint, false);
                parameter = polyline.GetParameterAtPoint(nearest);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                warning = "无法定位 Polyline 上被点击的线段。";
                return false;
            }

            int lastSegment = polyline.NumberOfVertices - 2;
            int segmentIndex = Math.Max(0,
                Math.Min(lastSegment, (int)Math.Floor(parameter)));
            if (polyline.GetSegmentType(segmentIndex) != SegmentType.Line)
            {
                warning = "请点击 Polyline 的直线段，不要点击已有拱桥圆弧。";
                return false;
            }

            Point3d start = polyline.GetPoint3dAt(segmentIndex);
            Point3d end = polyline.GetPoint3dAt(segmentIndex + 1);
            if (!TryPlanOpening(start, end, pickedPoint, diameter,
                    out OpeningPlan plan, out warning)) return false;

            double startWidth = polyline.GetStartWidthAt(segmentIndex);
            double endWidth = polyline.GetEndWidthAt(segmentIndex);
            double firstWidth = Interpolate(startWidth, endWidth,
                plan.FirstFraction);
            double secondWidth = Interpolate(startWidth, endWidth,
                plan.SecondFraction);
            Matrix3d worldToPlane = Matrix3d.WorldToPlane(polyline.Normal);
            Point3d first = plan.First.TransformBy(worldToPlane);
            Point3d second = plan.Second.TransformBy(worldToPlane);

            polyline.UpgradeOpen();
            polyline.SetEndWidthAt(segmentIndex, firstWidth);
            polyline.AddVertexAt(segmentIndex + 1,
                new Point2d(first.X, first.Y), plan.Bulge,
                firstWidth, secondWidth);
            polyline.AddVertexAt(segmentIndex + 2,
                new Point2d(second.X, second.Y), 0.0,
                secondWidth, endWidth);
            return true;
        }

        private static bool TryPlanOpening(Point3d start, Point3d end,
            Point3d pickedPoint, double diameter, out OpeningPlan plan,
            out string warning)
        {
            plan = default;
            warning = string.Empty;
            const double tolerance = 1e-7;
            if (diameter <= tolerance || double.IsNaN(diameter)
                || double.IsInfinity(diameter))
            {
                warning = "拱桥直径无效。";
                return false;
            }
            if (Math.Abs(start.Z - end.Z) > tolerance)
            {
                warning = "仅支持位于同一 XY 平面的二维直线。";
                return false;
            }

            Vector3d path = end - start;
            double totalLength = path.Length;
            if (totalLength <= diameter + tolerance)
            {
                warning = "直线段长度必须大于洞口直径，已跳过。";
                return false;
            }
            double fraction = start.GetVectorTo(pickedPoint).DotProduct(path)
                / (totalLength * totalLength);
            fraction = Math.Max(0.0, Math.Min(1.0, fraction));
            double centerDistance = totalLength * fraction;
            if (!SemicircleGeometry.CanSplit(totalLength, diameter,
                    centerDistance, tolerance))
            {
                warning = "位置太靠近直线段端点，已跳过。";
                return false;
            }

            double radius = diameter / 2.0;
            Point3d center = start + path * fraction;
            double firstFraction = (centerDistance - radius) / totalLength;
            double secondFraction = (centerDistance + radius) / totalLength;
            Point3d first = start + path * firstFraction;
            Point3d second = start + path * secondFraction;
            double bulge = SemicircleGeometry.Bulge(center.X, center.Y,
                first.X, first.Y, second.X, second.Y, radius);
            plan = new OpeningPlan(first, second, bulge,
                firstFraction, secondFraction);
            return true;
        }

        private static Point2d ToPlanPoint(Point3d point)
            => new Point2d(point.X, point.Y);

        private static double Interpolate(double start, double end, double fraction)
            => start + (end - start) * fraction;

        private readonly struct OpeningPlan
        {
            public OpeningPlan(Point3d first, Point3d second, double bulge,
                double firstFraction, double secondFraction)
            {
                First = first;
                Second = second;
                Bulge = bulge;
                FirstFraction = firstFraction;
                SecondFraction = secondFraction;
            }

            public Point3d First { get; }
            public Point3d Second { get; }
            public double Bulge { get; }
            public double FirstFraction { get; }
            public double SecondFraction { get; }
        }
    }
}
