using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Contracts;
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
        // 规范命令：U1R；统一设置使用 U1SET
        [CommandMethod(CommandIds.Arch)]
        public void UncadArch() => Run();

        // 旧名兼容（后续版本可删除）
        [CommandMethod(CommandIds.LegacyArch)]
        public void Unr() => UncadArch();

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
            if (!ArchBridgePolyline.TryPlanOpening(line.StartPoint, line.EndPoint,
                    ProjectCenterDistance(line.StartPoint, line.EndPoint,
                        pickedPoint), diameter,
                    out ArchBridgeOpening plan, out warning))
                return false;

            var vertices = new List<KeyValuePair<Point2d, double>>
            {
                new KeyValuePair<Point2d, double>(
                    new Point2d(line.StartPoint.X, line.StartPoint.Y), 0.0),
                new KeyValuePair<Point2d, double>(
                    new Point2d(plan.First.X, plan.First.Y), plan.Bulge),
                new KeyValuePair<Point2d, double>(
                    new Point2d(plan.Second.X, plan.Second.Y), 0.0),
                new KeyValuePair<Point2d, double>(
                    new Point2d(line.EndPoint.X, line.EndPoint.Y), 0.0)
            };
            Polyline merged = ArchBridgePolyline.BuildMerged(ctx.Db, line,
                vertices);

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
            if (!ArchBridgePolyline.TryPlanOpening(start, end,
                    ProjectCenterDistance(start, end, pickedPoint), diameter,
                    out ArchBridgeOpening plan, out warning)) return false;

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

        /// <summary>洞心 = 拾取点在线段上的投影，钳在 [0, 线段长]。</summary>
        private static double ProjectCenterDistance(Point3d start, Point3d end,
            Point3d pickedPoint)
        {
            Vector3d path = end - start;
            double totalLength = path.Length;
            if (totalLength <= 0.0) return 0.0;
            double fraction = start.GetVectorTo(pickedPoint).DotProduct(path)
                / (totalLength * totalLength);
            fraction = Math.Max(0.0, Math.Min(1.0, fraction));
            return totalLength * fraction;
        }

        private static double Interpolate(double start, double end, double fraction)
            => start + (end - start) * fraction;
    }
}
