using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Dwg;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_DEAD_ASSOCIATION_SELFTEST（3.8.0.2）：现场「300mm 怎么都识别不到」的
    /// 同款形态——标注带着 U1L 关联元数据，但关联句柄是 0（复制图框/标注时 AutoCAD
    /// 把没一起复制的 XData 句柄置空），文字就压在短线的中点上。断言：这种「关联
    /// 已死」的标注回到几何配对、归到旁边那根线上（不再线和标各报一次）；关联仍
    /// 指向活线的标注不会被别的线抢走；关联指向别的活实体（不是线）的照旧不猜；
    /// 「4*10mm²: …」这种截面文字不再被当成毫米标。只读（只加临时实体）。
    /// </summary>
    public sealed class DeadAssociationSelfTestCommand
    {
        [CommandMethod("UNCAD_DEAD_ASSOCIATION_SELFTEST")]
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
            const double o = 800000000;
            ObjectId stub, owned, liveNonLine, bare,
                hoseAttribute = ObjectId.Null;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                Require(QuickLineMetadataStore.EnsureApplication(ctx.Db, tr), "U1LX metadata app");

                // ① 短线 + 压在中点上的「300mm」，关联句柄 = 0（已死）。
                stub = Line(ctx, tr, o, o, o + 300, o);
                DBText dead = Text(ctx, tr, "300mm", o + 150, o + 300);
                dead.XData = QuickLineMetadataStore.Build(dead, "0", false);

                // ② 活关联的线：自己的标注在，旁边再放一个「关联已死」的 500mm——
                //    活关联优先，死关联的只能去几何配对（附近没有别的线 → 不猜）。
                owned = Line(ctx, tr, o, o + 20000, o + 6000, o + 20000);
                ObjectId ownedLabel = Text(ctx, tr, "6000mm", o + 3000, o + 20300)
                    .ObjectId;
                Require(QuickLineCadService.TryLinkLineAndLabel(ctx, tr, owned,
                    ownedLabel), "link the owned label");
                DBText stray = Text(ctx, tr, "500mm", o + 4000, o + 19600);
                stray.XData = QuickLineMetadataStore.Build(stray, "0", false);

                // ③ 关联指向一个活着的非线实体：说不清归属，照旧不猜。
                liveNonLine = Text(ctx, tr, "1500mm", o + 50000, o + 50000)
                    .ObjectId;
                DBText pointed = Text(ctx, tr, "800mm", o + 52000, o + 50000);
                pointed.XData = QuickLineMetadataStore.Build(pointed,
                    liveNonLine.Handle.ToString(), false);

                // ④ 截面文字放在一根没有毫米标的线的中点上：不是毫米标。
                bare = Line(ctx, tr, o, o + 40000, o + 8000, o + 40000);
                Text(ctx, tr, "4*10mm²: 3.4+0.5+3+5=52.5M", o + 4000, o + 40300);
                Text(ctx, tr, "电缆ZB-YJV-3*(2*70)+1*70mm²: 1500+1000（箱内）=14800mm",
                    o + 4000, o + 40700);

                // ⑤ 软管长度：Ruanguan 块的「软管长度」属性和自由文字「软管1500mm」
                //    是软管长度，不是电缆毫米标——都放在短线旁边也不能抢走短线。
                var table = (BlockTable)tr.GetObject(ctx.Db.BlockTableId,
                    OpenMode.ForRead);
                var definition = new BlockTableRecord { Name = "UNCAD_HOSE_SELFTEST" };
                table.UpgradeOpen();
                ObjectId definitionId = table.Add(definition);
                tr.AddNewlyCreatedDBObject(definition, true);
                var definitionTag = new AttributeDefinition
                {
                    Position = new Point3d(o + 70000, o + 50000, 0),
                    Tag = "软管长度",
                    Prompt = "软管长度",
                    TextString = "1500mm",
                    Height = 180
                };
                definition.AppendEntity(definitionTag);
                tr.AddNewlyCreatedDBObject(definitionTag, true);
                var hoseReference = new BlockReference(
                    new Point3d(o + 70000, o + 50000, 0), definitionId);
                ctx.AddToCurrentSpace(tr, hoseReference);
                // 控制台里不会自动生成属性参照：照 AutoCAD 的做法手工挂上。
                var hoseAttributeReference = new AttributeReference();
                hoseAttributeReference.SetAttributeFromBlock(definitionTag,
                    hoseReference.BlockTransform);
                hoseAttributeReference.TextString = "1500mm";
                hoseReference.AttributeCollection.AppendAttribute(
                    hoseAttributeReference);
                tr.AddNewlyCreatedDBObject(hoseAttributeReference, true);
                hoseAttribute = hoseAttributeReference.ObjectId;
                Text(ctx, tr, "软管1500mm", o + 150, o - 300);
                tr.Commit();
            }

            var boundary = new FrameRectangle("dead-association", o - 1000, o - 1000,
                o + 100000, o + 100000);
            QuickLineScanResult scan = QuickLineCadService.ScanDetailed(ctx,
                new QuickLineScanOptions
                {
                    IncludeUnlabelled = true,
                    FrameBoundaries = new[] { boundary }
                });

            // ① 死关联的 300mm 归到旁边那根短线上。
            QuickLineCadSegment stubSegment = scan.Segments.Single(item =>
                item.LineId == stub);
            Require(stubSegment.HasMillimetreLabel && stubSegment.LabelText == "300mm",
                "dead-association label binds to the line beside it, got ["
                + stubSegment.LabelText + "]");
            Require(!scan.UnboundLabels.Any(item =>
                    item.Text.IndexOf("300mm", StringComparison.Ordinal) >= 0),
                "300mm is not reported as an unbound label");

            // ② 活关联的线保住自己的标注；死关联的 500mm 没有线可归 → 点名。
            QuickLineCadSegment ownedSegment = scan.Segments.Single(item =>
                item.LineId == owned);
            Require(ownedSegment.LabelText == "6000mm",
                "the live association keeps its own label, got ["
                + ownedSegment.LabelText + "]");
            Require(scan.UnboundLabels.Any(item =>
                    item.Text == "500mm" && item.AssociationDangling),
                "the stray dead label is still reported");

            // ③ 关联指向活着的非线实体：不猜，照旧点名。
            Require(scan.UnboundLabels.Any(item => item.Text == "800mm"
                    && item.AssociationDangling),
                "a label pointing at a live non-line entity stays unresolved");

            // ④ 截面文字不是毫米标：那根线照旧算「没有毫米标」，文字也不点名。
            Require(!scan.UnboundLabels.Any(item =>
                    item.Text.IndexOf("mm²", StringComparison.Ordinal) >= 0),
                "cross-section text is not a millimetre label");
            Require(scan.Segments.Single(item => item.LineId == bare)
                    .HasMillimetreLabel == false,
                "the line under the cross-section text stays unlabelled");
            Require(!scan.UnboundLabels.Any(item =>
                    item.Text.IndexOf("14800mm", StringComparison.Ordinal) >= 0),
                "long-form cable info text is not a millimetre label");

            // ⑤ 软管长度属性与自由软管文字都不是电缆毫米标；短线仍归 300mm。
            Require(hoseAttribute != ObjectId.Null,
                "the hose block's length attribute reference exists");
            Require(!scan.UnboundLabels.Any(item =>
                    item.EntityId == hoseAttribute),
                "the hose block's length attribute is not a cable label");
            Require(!scan.UnboundLabels.Any(item =>
                    item.Text.IndexOf("软管", StringComparison.Ordinal) >= 0),
                "free hose text is not a cable label");
            Require(stubSegment.LabelText == "300mm",
                "the hose text beside the stub does not steal it");

            FillRouteInspection inspection;
            using (Transaction tr = ctx.Db.TransactionManager.StartTransaction())
            {
                inspection = FillRouteInspectionStage.Inspect(ctx, tr,
                    new ObjectId[0], scan.Segments, boundary, null, null,
                    scan.UnboundLabels, new FillSelection());
                tr.Commit();
            }
            string stubHandle = stub.Handle.ToString();
            Require(!inspection.UnlabelledSegmentHandles.Contains(stubHandle),
                "the 300mm line is not reported as unlabelled");
            Require(!inspection.RouteIssues.Any(issue =>
                    issue.IndexOf("300mm", StringComparison.Ordinal) >= 0),
                "no issue mentions 300mm: " + string.Join(" | ",
                    inspection.RouteIssues));
        }

        private static ObjectId Line(CadContext ctx, Transaction tr, double x1,
            double y1, double x2, double y2)
            => ctx.AddToCurrentSpace(tr, new Line(new Point3d(x1, y1, 0),
                new Point3d(x2, y2, 0)));

        private static DBText Text(CadContext ctx, Transaction tr, string text,
            double x, double y)
        {
            var entity = new DBText
            {
                TextString = text,
                Height = 180,
                Position = new Point3d(x, y, 0)
            };
            ctx.AddToCurrentSpace(tr, entity);
            return entity;
        }

        private static void Require(bool condition, string what)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + what);
        }
    }
}
