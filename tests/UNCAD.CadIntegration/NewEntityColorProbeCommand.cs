using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// UNCAD_NEW_ENTITY_COLOR_PROBE（诊断，只读口径）：验证「把当前颜色设为真彩白后，
    /// U1L 这类不显式设色的新建实体（new Line/DBText + 加进模型空间）会不会继承真彩白」。
    /// 现场口径：图里 U_CABLE 上成对出现真彩白的 Line+DBText，U1PDF 白纸上丢线。
    /// 只在传入图纸的临时副本上跑；不改任何生产图纸。
    /// </summary>
    public sealed class NewEntityColorProbeCommand
    {
        [CommandMethod("UNCAD_NEW_ENTITY_COLOR_PROBE")]
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
            string output = Environment.GetEnvironmentVariable("UNCAD_PROBE_OUT");
            if (string.IsNullOrEmpty(output)) throw new InvalidOperationException(
                "FAILED: UNCAD_PROBE_OUT is not set");
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database db = document.Database;
            var builder = new StringBuilder();
            Color previous = db.Cecolor;
            try
            {
                db.Cecolor = Color.FromRgb(255, 255, 255);
                builder.AppendLine("CECOLOR set to " + Describe(db.Cecolor));

                var detachedLine = new Line(new Point3d(0, 0, 0), new Point3d(1000, 0, 0));
                builder.AppendLine("detached Line: " + Describe(detachedLine.Color));

                var detachedText = new DBText
                {
                    TextString = "1000mm",
                    Height = 100,
                    Position = new Point3d(0, 500, 0)
                };
                builder.AppendLine("detached DBText: " + Describe(detachedText.Color));

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId,
                        OpenMode.ForWrite, false);
                    var line = new Line(new Point3d(0, 0, 0), new Point3d(1000, 0, 0));
                    var text = new DBText
                    {
                        TextString = "1000mm",
                        Height = 100,
                        Position = new Point3d(0, 500, 0)
                    };
                    space.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                    space.AppendEntity(text);
                    tr.AddNewlyCreatedDBObject(text, true);
                    builder.AppendLine("added Line: " + Describe(line.Color)
                        + ", ColorIndex=" + line.ColorIndex);
                    builder.AppendLine("added DBText: " + Describe(text.Color)
                        + ", ColorIndex=" + text.ColorIndex);
                    tr.Commit();
                }
            }
            finally
            {
                db.Cecolor = previous;
            }
            File.WriteAllText(output, builder.ToString(), Encoding.UTF8);
        }

        private static string Describe(Color color)
        {
            if (color == null) return "null";
            try
            {
                return color.ColorMethod + "/" + color.ColorIndex
                    + "/#" + ((color.Red << 16) | (color.Green << 8) | color.Blue)
                        .ToString("X6");
            }
            catch
            {
                return color.ColorMethod.ToString();
            }
        }
    }
}
