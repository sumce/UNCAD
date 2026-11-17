using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Features.Fill;
using UNCAD.Features.Unr;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_UNR_SELFTEST（3.3.3.2，U1R 开洞元数据重挂真机自检）：非交互直调
    /// UnrFeature.TryReplaceLine 真实换线路径，断言——
    /// ①换线前两张倒排表（CollectLabelsByLineHandle / CollectBySourceHandle）
    ///   都能把旧线句柄映射到标注与 U1Q 输出；
    /// ②换线后毫米标的 UNCAD_U1LX 关联重挂到新 Polyline（活体），
    ///   U1Q 输出的 UNCAD_PARALLEL SOURCE 同步改挂；
    /// ③DeviceReserveMarkerReader 对该毫米标零候选（D-043 排除口按活性判：
    /// 关联活=排除）——修复前关联悬空会被判"不在役"而误当设备预留吸收。
    /// </summary>
    public sealed class UnrRelinkSelfTestCommand
    {
        private const double BaseX = 91006000;
        private const double BaseY = 91006000;

        [CommandMethod("UNCAD_UNR_SELFTEST")]
        public void Run()
        {
            // accoreconsole 对命令内未处理异常是直接断进程不打印——所有失败
            // 必须先落结果文件（run.ps1 会带出），再抛/退出。
            Document document = Application.DocumentManager.MdiActiveDocument;
            CadContext context = new CadContext(document);
            var created = new List<ObjectId>();
            try
            {
                ObjectId lineId, textId, outputId;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    // 夹具：横线 3000 + 横排毫米标（真 U1L 标注）+ 一条挂
                    // UNCAD_PARALLEL KIND=U1Q 的偏移线（模拟 U1Q 输出引用源线）。
                    lineId = context.AddToCurrentSpace(transaction, new Line(
                        new Point3d(BaseX, BaseY, 0),
                        new Point3d(BaseX + 3000, BaseY, 0)));
                    textId = context.AddToCurrentSpace(transaction, new DBText
                    {
                        TextString = "3000mm",
                        Position = new Point3d(BaseX + 1350, BaseY + 260, 0),
                        Height = 180.0,
                        Rotation = 0.0
                    });
                    outputId = context.AddToCurrentSpace(transaction, new Line(
                        new Point3d(BaseX, BaseY + 80, 0),
                        new Point3d(BaseX + 3000, BaseY + 80, 0)));
                    Require(QuickLineCadService.TryLinkLineAndLabel(context,
                            transaction, lineId, textId),
                        "U1LX association must be writable on fixture");
                    // Set 对未注册 RegApp 的 XData 赋值是静默吞错（生产调用方
                    // 先 EnsureApplication——ParallelCurveAnnotator:77/:333）。
                    Require(ParallelAnnotationMetadata.EnsureApplication(
                            document.Database, transaction),
                        "UNCAD_PARALLEL RegApp must be ensured");
                    var output = (Entity)transaction.GetObject(outputId,
                        OpenMode.ForWrite);
                    ParallelAnnotationMetadata.Set(output, "U1Q",
                        lineId.Handle.ToString());
                    // Set 吞错兜底：写完读回验证夹具元数据真的落上了。
                    Require(ParallelAnnotationMetadata.TryRead(output,
                            out string writtenKind, out string writtenSource)
                        && writtenKind == "U1Q"
                        && string.Equals(writtenSource, lineId.Handle.ToString(),
                            StringComparison.OrdinalIgnoreCase),
                        "fixture U1Q metadata must be readable after Set");
                    transaction.Commit();
                }
                created.Add(lineId);
                created.Add(textId);
                created.Add(outputId);

                // ①换线前：两张倒排表按旧句柄命中；毫米标被 D-043 排除口
                // 排除（关联指向活线）。
                Dictionary<string, List<ObjectId>> parallelBySource;
                Dictionary<string, List<ObjectId>> labelsByLine;
                string oldHandle = lineId.Handle.ToString();
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    parallelBySource = ParallelCurveAnnotator.CollectBySourceHandle(
                        context, transaction);
                    labelsByLine = QuickLineCadService.CollectLabelsByLineHandle(
                        context, transaction);
                    Require(parallelBySource.TryGetValue(oldHandle,
                            out List<ObjectId> outputs) && outputs.Contains(outputId),
                        "CollectBySourceHandle must map the old line handle to the U1Q output");
                    Require(labelsByLine.TryGetValue(oldHandle,
                            out List<ObjectId> labels) && labels.Contains(textId),
                        "CollectLabelsByLineHandle must map the old line handle to the mm label");
                    Require(DeviceReserveMarkerReader.Collect(transaction,
                            new[] { textId }).Hits.Count == 0,
                        "live association must exclude the label from reserve candidates");
                    transaction.Commit();
                }

                // ②真实换线路径（UnrFeature.TryReplaceLine，直径 300，洞心=中点）。
                string newHandle;
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    var line = (Line)transaction.GetObject(lineId, OpenMode.ForRead);
                    Require(UnrFeature.TryReplaceLine(context, transaction, line,
                            new Point3d(BaseX + 1500, BaseY, 0), 300.0,
                            parallelBySource, labelsByLine, out string warning),
                        "TryReplaceLine must succeed (warning: " + warning + ")");
                    transaction.Commit();
                }

                // ③换线后：旧线已删、关联/SOURCE 都改挂新 Polyline、毫米标
                // 仍被排除（非空关联且解析到活体 Polyline）。
                using (Transaction transaction = document.Database.TransactionManager
                    .StartTransaction())
                {
                    DBObject erased = transaction.GetObject(lineId,
                        OpenMode.ForRead, true);
                    Require(erased.IsErased, "the original line must be erased");

                    var label = (Entity)transaction.GetObject(textId, OpenMode.ForRead);
                    string associated = QuickLineMetadataStore
                        .ReadAssociatedLineHandle(label);
                    newHandle = associated;
                    Require(!string.IsNullOrWhiteSpace(associated)
                        && !string.Equals(associated, oldHandle,
                            StringComparison.OrdinalIgnoreCase),
                        "label association must be retargeted off the erased line");
                    ObjectId resolvedId = document.Database.GetObjectId(false,
                        new Handle(Convert.ToInt64(associated, 16)), 0);
                    Require(!resolvedId.IsNull && resolvedId.IsValid,
                        "retargeted handle must resolve in this drawing");
                    var resolved = transaction.GetObject(resolvedId, OpenMode.ForRead);
                    Require(!resolved.IsErased && resolved is Polyline,
                        "retargeted association must point at the live arch polyline");

                    var output = (Entity)transaction.GetObject(outputId,
                        OpenMode.ForRead);
                    Require(ParallelAnnotationMetadata.TryRead(output,
                            out string kind, out string sourceHandle)
                        && kind == "U1Q"
                        && string.Equals(sourceHandle, associated,
                            StringComparison.OrdinalIgnoreCase),
                        "U1Q output SOURCE must follow the new polyline handle");

                    Require(DeviceReserveMarkerReader.Collect(transaction,
                            new[] { textId }).Hits.Count == 0,
                        "after U1R relink the mm label must stay excluded "
                        + "(dangling association would be absorbed as a reserve marker)");
                    transaction.Commit();
                }
                created.Add(document.Database.GetObjectId(false,
                    new Handle(Convert.ToInt64(newHandle, 16)), 0));

                Pass(document);
            }
            catch (System.Exception ex)
            {
                WriteResult("FAIL: " + ex.Message);
                try
                {
                    document.Editor.WriteMessage("\nUNCAD_UNR_SELFTEST:FAIL "
                        + ex.Message + "\n");
                }
                catch { /* 输出尽力而为 */ }
            }
            finally
            {
                Cleanup(document.Database, created);
            }
        }

        private static void Require(bool condition, string message)
        {
            if (condition) return;
            WriteResult("FAIL: " + message);
            throw new InvalidOperationException("U1R 重挂自检: " + message);
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable(
                "UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, text);
        }

        private static void Pass(Document document)
        {
            document.Editor.WriteMessage("\nUNCAD_UNR_SELFTEST:PASS\n");
            WriteResult("PASS");
        }

        private static void Cleanup(Database database, IEnumerable<ObjectId> ids)
        {
            try
            {
                using (Transaction transaction = database.TransactionManager
                    .StartTransaction())
                {
                    foreach (ObjectId id in ids)
                    {
                        try
                        {
                            DBObject entity = transaction.GetObject(id,
                                OpenMode.ForWrite, true);
                            if (entity != null && !entity.IsErased) entity.Erase();
                        }
                        catch { /* 清理尽力而为 */ }
                    }
                    transaction.Commit();
                }
            }
            catch { /* 清理尽力而为 */ }
        }
    }
}
