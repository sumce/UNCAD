using System;
using System.Collections.Generic;
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
using UNCAD.Core.Text;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_HOSE_SELFTEST（3.3.3，U1B 绘制软管真机自检）：非交互直调
    /// HoseCadService 生产几何服务，在临时图纸上断言——
    /// 轮廓=闭合 LWPolyline 落 U_HOSE（色号 3）、顶点含两个半圆端帽（|bulge|=1）
    /// 与外拐角圆弧（|bulge|=tan(π/8)）、内拐角 miter 截断；引线起点=路径长度
    /// 中点；文字=软管链路既有格式；三个实体全带 KIND=U1B 元数据；
    /// QuickLineCadService.Scan 不把 U1B 实体当电缆路由（TryRead 命中即跳过）。
    /// </summary>
    public sealed class HoseSelfTestCommand
    {
        private const double BaseX = 90006000;
        private const double BaseY = 90006000;

        [CommandMethod("UNCAD_HOSE_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            CadContext context = new CadContext(document);
            var created = new List<ObjectId>();
            try
            {
                // L 形路径 (0,0)→(1000,0)→(1000,1000)，直径 100（偏移到远端坐标，
                // 不碰夹具图纸真实内容）。
                var path = new List<QuickLinePoint>
                {
                    new QuickLinePoint(BaseX, BaseY),
                    new QuickLinePoint(BaseX + 1000, BaseY),
                    new QuickLinePoint(BaseX + 1000, BaseY + 1000)
                };
                HoseDrawResult result;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    result = HoseCadService.DrawOutline(context, transaction, path, 100.0);
                    Require(result != null, "outline planning");
                    string defaultText = HoseAnnotationDefaults.BuildDefaultText(
                        100.0, result.CenterlineLength);
                    Require(result.CenterlineLength == 2000.0,
                        "centerline length (got " + result.CenterlineLength + ")");
                    Require(defaultText == "100mm软管:2000mm",
                        "default annotation text (got " + defaultText + ")");
                    HoseCadService.DrawAnnotation(context, transaction, result, path,
                        new Point3d(BaseX + 1500, BaseY + 500, 0), defaultText, 180.0);
                    transaction.Commit();
                }
                created.Add(result.OutlineId);
                created.Add(result.LeaderId);
                created.Add(result.TextId);
                Require(!result.OutlineId.IsNull && !result.LeaderId.IsNull
                    && !result.TextId.IsNull, "all three entities created");

                Require(VerifyLayer(context, document.Database),
                    "U_HOSE layer exists with ACI 3");

                Require(VerifyOutline(context, document.Database, result.OutlineId),
                    "closed outline geometry (caps/arc/miter/colour)");

                Require(VerifyAnnotation(context, document.Database, result),
                    "leader from length midpoint + label text");

                Require(VerifyMetadata(context, document.Database, result),
                    "KIND=U1B metadata on outline/leader/text");

                Require(VerifyScanExclusion(context, result),
                    "scan must not treat U1B entities as cable routes");

                Require(VerifyDegenerateInputs(context),
                    "degenerate paths are rejected politely");

                Pass(document);
            }
            finally
            {
                Cleanup(document.Database, created);
            }
        }

        private static bool VerifyLayer(CadContext context, Database database)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                var table = (LayerTable)transaction.GetObject(database.LayerTableId,
                    OpenMode.ForRead);
                if (!table.Has(UncadLayerStandard.HoseLayerName)) return false;
                var record = (LayerTableRecord)transaction.GetObject(
                    table[UncadLayerStandard.HoseLayerName], OpenMode.ForRead);
                bool colorOk = record.Color.ColorMethod
                        == Autodesk.AutoCAD.Colors.ColorMethod.ByAci
                    && record.Color.ColorIndex == UncadLayerStandard.HoseColorIndex;
                transaction.Commit();
                return colorOk;
            }
        }

        private static bool VerifyOutline(CadContext context, Database database,
            ObjectId outlineId)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                var outline = (Polyline)transaction.GetObject(outlineId,
                    OpenMode.ForRead);
                if (!outline.Closed) return false;
                if (outline.Layer != UncadLayerStandard.HoseLayerName) return false;
                if (outline.ColorIndex != HoseCadService.HoseColorIndex) return false;
                // L 形（d=100）期望 7 顶点（逐值单测锁死）：两帽 |bulge|=1、
                // 一个外拐角弧 |bulge|=tan(π/8)≈0.4142、内 miter 截断直连。
                if (outline.NumberOfVertices != 7) return false;
                int caps = 0, arcs = 0;
                for (int i = 0; i < outline.NumberOfVertices; i++)
                {
                    double bulge = outline.GetBulgeAt(i);
                    if (Math.Abs(Math.Abs(bulge) - 1.0) < 1e-9) caps++;
                    else if (Math.Abs(bulge) > 0.1) arcs++;
                }
                transaction.Commit();
                return caps == 2 && arcs == 1;
            }
        }

        private static bool VerifyAnnotation(CadContext context, Database database,
            HoseDrawResult result)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                var leader = (Line)transaction.GetObject(result.LeaderId,
                    OpenMode.ForRead);
                // 长度中点=L 形拐角 (BaseX+1000, BaseY)。
                bool leaderOk = leader.StartPoint.DistanceTo(
                        new Point3d(BaseX + 1000, BaseY, 0)) < 1e-6
                    && leader.EndPoint.DistanceTo(
                        new Point3d(BaseX + 1500, BaseY + 500, 0)) < 1e-6
                    && leader.Layer == UncadLayerStandard.HoseLayerName;
                var text = (DBText)transaction.GetObject(result.TextId,
                    OpenMode.ForRead);
                bool textOk = text.TextString == "100mm软管:2000mm"
                    && text.Layer == UncadLayerStandard.HoseLayerName
                    && Math.Abs(text.Height - 180.0) < 1e-6
                    && Math.Abs(text.Rotation) < 1e-9;
                transaction.Commit();
                return leaderOk && textOk;
            }
        }

        private static bool VerifyMetadata(CadContext context, Database database,
            HoseDrawResult result)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                bool all = true;
                foreach (ObjectId id in new[] { result.OutlineId, result.LeaderId,
                    result.TextId })
                {
                    Entity entity = (Entity)transaction.GetObject(id,
                        OpenMode.ForRead, true);
                    if (!ParallelAnnotationMetadata.TryRead(entity,
                            out string kind, out string source))
                        return false;
                    all &= string.Equals(kind,
                        UncadLayerStandard.HoseAnnotationKind,
                        StringComparison.OrdinalIgnoreCase)
                        && source == result.OutlineId.Handle.ToString();
                }
                // 归属链认得 U1B：KIND → U_HOSE。
                all &= UncadLayerStandard.TryResolveAnnotationLayer(
                    UncadLayerStandard.HoseAnnotationKind)
                    == UncadLayerStandard.HoseLayerName;
                transaction.Commit();
                return all;
            }
        }

        private static bool VerifyScanExclusion(CadContext context,
            HoseDrawResult result)
        {
            IReadOnlyList<QuickLineCadSegment> segments = QuickLineCadService.Scan(
                context, new QuickLineScanOptions { IncludeUnlabelled = true });
            var ids = new HashSet<ObjectId>(new[] { result.OutlineId,
                result.LeaderId, result.TextId });
            return segments.All(segment => segment == null
                || !ids.Contains(segment.LineId));
        }

        private static bool VerifyDegenerateInputs(CadContext context)
        {
            // 单点/退化路径：DrawOutline 拒绝（返回 null），不开事务不画图。
            using (Transaction transaction = context.Db.TransactionManager
                .StartTransaction())
            {
                HoseDrawResult one = HoseCadService.DrawOutline(context, transaction,
                    new List<QuickLinePoint> { new QuickLinePoint(1, 1) }, 100.0);
                HoseDrawResult spike = HoseCadService.DrawOutline(context, transaction,
                    new List<QuickLinePoint> { new QuickLinePoint(1, 1),
                        new QuickLinePoint(1, 1) }, 100.0);
                HoseDrawResult badDiameter = HoseCadService.DrawOutline(context,
                    transaction, new List<QuickLinePoint> { new QuickLinePoint(0, 0),
                        new QuickLinePoint(1000, 0) }, 0.0);
                transaction.Abort();
                return one == null && spike == null && badDiameter == null;
            }
        }

        private static void Cleanup(Database database, List<ObjectId> ids)
        {
            using (Transaction transaction = database.TransactionManager
                .StartTransaction())
            {
                foreach (ObjectId id in ids ?? new List<ObjectId>())
                {
                    try
                    {
                        Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                            as Entity;
                        if (entity != null && !entity.IsErased) entity.Erase();
                    }
                    catch { }
                }
                transaction.Commit();
            }
        }

        private static void Require(bool condition, string message)
        {
            if (condition) return;
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                System.IO.File.WriteAllText(resultPath, "FAIL: " + message);
            throw new InvalidOperationException("Hose self-test: " + message);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_HOSE_SELFTEST:PASS\n");
            string resultPath = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(resultPath))
                System.IO.File.WriteAllText(resultPath, "PASS");
        }
    }
}
