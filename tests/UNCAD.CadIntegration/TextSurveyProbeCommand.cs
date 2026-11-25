using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Cad.QuickLine;
using UNCAD.Core.Text;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_TEXT_SURVEY（诊断，只读）：把当前空间（含块属性）里所有带桥架/线管
    /// 字样的文字按「实体类型 + 文字原文」去重计数导出，只记事实（出现次数、
    /// 旋转角、有没有 U1L 关联 / 归属元数据），不做任何判定。给「这种标注文字
    /// 为什么不认」取证用——先把真实图纸上的写法收齐，再改识别规则。
    /// 环境变量：UNCAD_SURVEY_OUT=导出文本路径。始终报 PASS。
    /// </summary>
    public sealed class TextSurveyProbeCommand
    {
        private static readonly Regex Keyword = new Regex(
            "桥架|线管|穿线管|梯形|梯式|槽式|托盘|[Ww][Xx]",
            RegexOptions.Compiled);

        [CommandMethod("UNCAD_TEXT_SURVEY")]
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
            string output = Environment.GetEnvironmentVariable("UNCAD_SURVEY_OUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException(
                "FAILED: UNCAD_SURVEY_OUT is not set");
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database db = document.Database;
            var rows = new Dictionary<string, SurveyRow>(StringComparer.Ordinal);
            int scanned = 0;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId,
                    OpenMode.ForRead, false);
                foreach (ObjectId id in space)
                {
                    if (id.IsNull || !id.IsValid || id.IsErased) continue;
                    Entity entity;
                    try { entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity; }
                    catch { continue; }
                    if (entity == null || entity.IsErased) continue;
                    var block = entity as BlockReference;
                    if (block != null)
                    {
                        foreach (ObjectId attributeId in block.AttributeCollection)
                        {
                            var attribute = tr.GetObject(attributeId, OpenMode.ForRead, false)
                                as AttributeReference;
                            if (attribute != null)
                                scanned += Add(rows, tr, attributeId, attribute.TextString,
                                    "Attribute", attribute.Rotation);
                        }
                        continue;
                    }
                    var dbText = entity as DBText;
                    string text;
                    string kind;
                    double rotation;
                    if (dbText != null)
                    {
                        text = dbText.TextString;
                        kind = "DBText";
                        rotation = dbText.Rotation;
                    }
                    else if (entity is MText mText)
                    {
                        text = TextParser.CleanMText(mText.Contents);
                        if (string.IsNullOrWhiteSpace(text)) text = mText.Text;
                        kind = "MText";
                        rotation = mText.Rotation;
                    }
                    else continue;
                    scanned += Add(rows, tr, id, text, kind, rotation);
                }
                tr.Commit();
            }
            var builder = new StringBuilder();
            builder.AppendLine("Drawing: " + document.Name);
            builder.AppendLine("keyword texts found " + scanned + ", distinct "
                + rows.Count);
            foreach (SurveyRow row in rows.Values
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Text, StringComparer.Ordinal))
            {
                builder.AppendLine(row.Count + "\t" + row.Kind + "\t["
                    + row.Text.Replace("\r", "").Replace("\n", "⏎") + "]\trot="
                    + string.Join("/", row.Rotations.OrderBy(item => item))
                    + "\tU1L=" + row.Associated + "\townership=" + row.Owned
                    + "\tfirst=" + row.FirstHandle);
            }
            File.WriteAllText(output, builder.ToString(), Encoding.UTF8);
        }

        private static int Add(Dictionary<string, SurveyRow> rows, Transaction tr,
            ObjectId id, string text, string kind, double rotation)
        {
            string content = (text ?? string.Empty).Replace("\r", "").Trim();
            if (!Keyword.IsMatch(content)) return 0;
            string key = kind + "\u0001" + content;
            if (!rows.TryGetValue(key, out SurveyRow row))
            {
                row = new SurveyRow(kind, content, id.Handle.ToString());
                rows[key] = row;
            }
            row.Count++;
            row.Rotations.Add(Math.Round(rotation * 180.0 / Math.PI));
            try
            {
                if (entityOf(tr, id) is Entity entity)
                {
                    if (!string.IsNullOrEmpty(
                        QuickLineMetadataStore.ReadAssociatedLineHandle(entity)))
                        row.Associated++;
                    if (ParallelAnnotationMetadata.TryRead(entity, out _, out _))
                        row.Owned++;
                }
            }
            catch
            {
                // 元数据读不出来不影响文字清单本身。
            }
            return 1;
        }

        private static object entityOf(Transaction tr, ObjectId id)
            => tr.GetObject(id, OpenMode.ForRead, false);

        private sealed class SurveyRow
        {
            internal SurveyRow(string kind, string text, string firstHandle)
            {
                Kind = kind;
                Text = text;
                FirstHandle = firstHandle;
            }

            internal string Kind { get; }
            internal string Text { get; }
            internal string FirstHandle { get; }
            internal int Count { get; set; }
            internal int Associated { get; set; }
            internal int Owned { get; set; }
            internal List<double> Rotations { get; } = new List<double>();
        }
    }
}
