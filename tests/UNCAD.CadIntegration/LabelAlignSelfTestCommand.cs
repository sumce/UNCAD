using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Geometry;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_LABEL_ALIGN_SELFTEST（3.7.0，U1UL 毫米标归位真机自检）：真事务里验证
    /// 归位结果——只改离线距离和角度，毫米标所在侧、沿线位置、文字、字高和
    /// 关联元数据都不变；没有毫米标的线段和只靠位置匹配的毫米标不动并计数；
    /// 重跑幂等（已在位的不动）。非交互直调服务，几何画在远离图纸内容的位置。
    /// </summary>
    public sealed class LabelAlignSelfTestCommand
    {
        [CommandMethod("UNCAD_LABEL_ALIGN_SELFTEST")]
        public void Run()
        {
            string result = "PASS";
            try { RunCore(); }
            catch (System.Exception ex) { result = ex.ToString(); }
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, result);
            Application.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n" + result + "\n");
        }

        private static void RunCore()
        {
            var ctx = new CadContext(Application.DocumentManager.MdiActiveDocument);
            const double o = 500000000;
            const double height = 180;
            const double offset = 50;
            ObjectId lineA, labelA, lineB, labelB, lineC, labelC, lineD, lineE, labelE,
                lineG, labelG, lineF, labelF;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                // A：水平线，毫米标在线上方。
                lineA = AddLine(ctx, tr, o, o, o + 4000, o);
                labelA = AddLabel(ctx, tr, "2000mm", height, o + 2000, o + 600, 0.0,
                    AttachmentPoint.BottomCenter);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineA, labelA),
                    "link label A");
                // B：水平线，毫米标被挪到线下方，且被转歪了 0.1 弧度。
                lineB = AddLine(ctx, tr, o, o + 5000, o + 4000, o + 5000);
                labelB = AddLabel(ctx, tr, "3000mm", height, o + 2000, o + 5000 - 600, 0.1,
                    AttachmentPoint.TopCenter);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineB, labelB),
                    "link label B");
                // C：竖直线，毫米标在左侧、竖排。
                lineC = AddLine(ctx, tr, o + 10000, o, o + 10000, o + 4000);
                labelC = AddLabel(ctx, tr, "2500mm", height, o + 10000 - 600, o + 2000,
                    Math.PI / 2, AttachmentPoint.BottomCenter);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineC, labelC),
                    "link label C");
                // D：没有毫米标的线段。
                lineD = AddLine(ctx, tr, o + 20000, o, o + 24000, o);
                // E：毫米标只靠位置匹配，没有关联元数据（旧图纸）：贴在线旁，
                // 3.8.0（D-059）起也归位（位置认领）。
                lineE = AddLine(ctx, tr, o + 20000, o + 3000, o + 24000, o + 3000);
                labelE = AddLabel(ctx, tr, "1500mm", height, o + 22000, o + 3100, 0.0,
                    AttachmentPoint.BaseLeft);
                // G：只靠位置匹配、但离线太远（>4 倍字高）——拿不准，不动。
                lineG = AddLine(ctx, tr, o + 20000, o + 6000, o + 24000, o + 6000);
                labelG = AddLabel(ctx, tr, "900mm", height, o + 22000, o + 6000 + 2000, 0.0,
                    AttachmentPoint.BaseLeft);
                // F：从右往左画的线，毫米标倒着读（180°）放在线下方。
                lineF = AddLine(ctx, tr, o + 4000, o + 8000, o, o + 8000);
                labelF = AddLabel(ctx, tr, "4000mm", height, o + 2000, o + 8000 - 600, Math.PI,
                    AttachmentPoint.TopCenter);
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, lineF, labelF),
                    "link label F");
                tr.Commit();
            }

            var boundary = new FrameRectangle("align", o - 5000, o - 5000, o + 30000, o + 16000);
            var scanned = QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                IncludeUnlabelled = true,
                FrameBoundaries = new[] { boundary }
            });
            Require(scanned.Count == 7, "seven lines in scope, got " + scanned.Count);

            QuickLineLabelAlignResult result = QuickLineLabelAlignService.Apply(
                ctx, scanned, offset, false, true);
            Require(result != null, "apply returns a result");
            // A/B/C/F 有关联元数据；E 只靠位置（贴在线旁）也算一条，共 5 个。
            Require(result.Moved == 5, "five labels moved, got " + result.Moved);
            Require(result.MovedByPosition == 1, "one label claimed by position");
            Require(result.Unchanged == 0, "nothing unchanged on the first run");
            Require(result.WithoutLabel == 1, "one line without a label");
            Require(result.PositionRejected == 1, "one too-far position match rejected");
            Require(result.SkippedHandles.Count == 1, "one line skipped");

            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                // A：上方 → 仍在上方，距离 = 偏移值，角度归零，底中对齐。
                DBText a = (DBText)tr.GetObject(labelA, OpenMode.ForRead);
                Near(a.AlignmentPoint.X, o + 2000, "A stays at its along-line position");
                Near(a.AlignmentPoint.Y, o + offset, "A keeps the upper side at the offset");
                Near(a.Rotation, 0.0, "A angle follows the line");
                Require(a.Justify == AttachmentPoint.BottomCenter, "A attachment");
                Require(a.TextString == "2000mm" && a.Height == height, "A text and height untouched");
                Require(string.Equals(QuickLineMetadataStore.ReadAssociatedLineHandle(a),
                    lineA.Handle.ToString(), StringComparison.OrdinalIgnoreCase),
                    "A association kept");

                // B：被挪到下方、转歪 → 仍在下方，角度归零，顶中对齐。
                DBText b = (DBText)tr.GetObject(labelB, OpenMode.ForRead);
                Near(b.AlignmentPoint.X, o + 2000, "B along-line position");
                Near(b.AlignmentPoint.Y, o + 5000 - offset, "B keeps the lower side");
                Near(b.Rotation, 0.0, "B angle follows the line");
                Require(b.Justify == AttachmentPoint.TopCenter, "B attachment");
                Require(b.TextString == "3000mm", "B text untouched");

                // C：竖线左侧 → 仍在左侧，保持竖排。
                DBText c = (DBText)tr.GetObject(labelC, OpenMode.ForRead);
                Near(c.AlignmentPoint.X, o + 10000 - offset, "C keeps the left side");
                Near(c.AlignmentPoint.Y, o + 2000, "C along-line position");
                Near(c.Rotation, Math.PI / 2, "C keeps reading bottom-to-top");
                Require(c.Justify == AttachmentPoint.BottomCenter, "C attachment");

                // E：只靠位置匹配（贴在线旁）也归位：离线距离 50、角度归零。
                // 对齐方式已改成底中，位置读 AlignmentPoint（Position 会被 AutoCAD
                // 反算成文字框左侧点）。
                DBText e = (DBText)tr.GetObject(labelE, OpenMode.ForRead);
                Require(e.Justify == AttachmentPoint.BottomCenter, "E attachment");
                Near(e.AlignmentPoint.X, o + 22000, "E along-line position kept");
                Near(e.AlignmentPoint.Y, o + 3000 + offset, "E aligned by position");
                Near(e.Rotation, 0.0, "E angle follows the line");
                Require(QuickLineMetadataStore.ReadAssociatedLineHandle(e).Length == 0,
                    "E must not gain an association");
                // G：离线太远，拿不准，原样不动。
                DBText g = (DBText)tr.GetObject(labelG, OpenMode.ForRead);
                Near(g.Position.X, o + 22000, "G untouched (too far)");
                Near(g.Position.Y, o + 6000 + 2000, "G untouched (too far)");
                Require(g.Justify == AttachmentPoint.BaseLeft, "G justification untouched");

                // F：倒读的文字保持倒读，留在下方。倒读文字自身坐标系的「底边」
                // 在世界里朝上，底中对齐才会让文字背离线段展开（顶中会压到线上）。
                DBText f = (DBText)tr.GetObject(labelF, OpenMode.ForRead);
                Near(f.AlignmentPoint.Y, o + 8000 - offset, "F keeps the lower side");
                Near(Math.Abs(f.Rotation), Math.PI, "F keeps reading upside down");
                Require(f.Justify == AttachmentPoint.BottomCenter, "F attachment");
                Require(f.TextString == "4000mm", "F text untouched");
                tr.Commit();
            }

            // 重跑幂等：5 个都已在位，不再改动；G 仍然不动。
            scanned = QuickLineCadService.Scan(ctx, new QuickLineScanOptions
            {
                IncludeUnlabelled = true,
                FrameBoundaries = new[] { boundary }
            });
            QuickLineLabelAlignResult again = QuickLineLabelAlignService.Apply(
                ctx, scanned, offset, false, true);
            Require(again != null && again.Moved == 0 && again.Unchanged == 5,
                "second run moves nothing");
        }

        private static ObjectId AddLine(CadContext ctx, Transaction tr, double x1, double y1,
            double x2, double y2)
            => ctx.AddToCurrentSpace(tr, new Line(new Point3d(x1, y1, 0), new Point3d(x2, y2, 0)));

        private static ObjectId AddLabel(CadContext ctx, Transaction tr, string text,
            double height, double x, double y, double rotation, AttachmentPoint justify)
        {
            var label = new DBText { TextString = text, Height = height, Rotation = rotation };
            if (justify == AttachmentPoint.BaseLeft) label.Position = new Point3d(x, y, 0);
            else
            {
                label.Justify = justify;
                label.AlignmentPoint = new Point3d(x, y, 0);
            }
            return ctx.AddToCurrentSpace(tr, label);
        }

        private static void Near(double actual, double expected, string what)
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > 1e-4)
                throw new InvalidOperationException(what + ": expected " + expected
                    + ", got " + actual);
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}
