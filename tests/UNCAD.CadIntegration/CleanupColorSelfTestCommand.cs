using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

namespace UNCAD.CadIntegration
{
    public sealed class CleanupColorSelfTestCommand
    {
        private const string Prefix = "UNCAD_COLOR_SELFTEST_";

        [CommandMethod("UNCAD_CLEAN_COLOR_TEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            Database database = document.Database;
            int erasedReferences = 0;
            int erasedDefinitions = 0;
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTable table = (BlockTable)transaction.GetObject(database.BlockTableId,
                    OpenMode.ForRead);
                BlockTableRecord model = (BlockTableRecord)transaction.GetObject(
                    database.CurrentSpaceId, OpenMode.ForWrite);
                foreach (ObjectId id in model)
                {
                    BlockReference reference = transaction.GetObject(id, OpenMode.ForRead, true)
                        as BlockReference;
                    BlockTableRecord definition = reference == null ? null
                        : transaction.GetObject(reference.BlockTableRecord,
                            OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition == null || !definition.Name.StartsWith(Prefix,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    reference.UpgradeOpen();
                    reference.Erase();
                    erasedReferences++;
                }

                var definitions = new List<ObjectId>();
                foreach (ObjectId id in table)
                {
                    BlockTableRecord definition = transaction.GetObject(id,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition != null && definition.Name.StartsWith(Prefix,
                        StringComparison.OrdinalIgnoreCase)) definitions.Add(id);
                }
                foreach (ObjectId id in definitions)
                {
                    BlockTableRecord definition = transaction.GetObject(id,
                        OpenMode.ForRead, true) as BlockTableRecord;
                    if (definition == null || definition.IsErased) continue;
                    try
                    {
                        definition.UpgradeOpen();
                        definition.Erase(true);
                        erasedDefinitions++;
                    }
                    catch { }
                }
                transaction.Commit();
            }
            document.Editor.WriteMessage("\nUNCAD_CLEAN_COLOR_TEST: "
                + erasedReferences + " refs, " + erasedDefinitions + " defs\n");
        }
    }
}
