using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Text;

namespace UNCAD.Features.Fill
{
    /// <summary>Migrates selected legacy U1Q grid labels inside the caller's transaction.</summary>
    internal static class BridgeLabelMigrationWriter
    {
        public static int Migrate(Transaction transaction,
            IEnumerable<ObjectId> textIds, double mmPerGrid)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (textIds == null) return 0;

            int changed = 0;
            foreach (ObjectId id in textIds.Distinct())
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (entity is DBText text
                    && BridgeLabelFormatter.TryMigrateLegacyGrid(text.TextString,
                        mmPerGrid, out string dbTextValue))
                {
                    text.UpgradeOpen();
                    text.TextString = dbTextValue;
                    changed++;
                }
                else if (entity is MText mtext
                    && BridgeLabelFormatter.TryMigrateLegacyGrid(
                        TextParser.CleanMText(mtext.Contents), mmPerGrid,
                        out string mTextValue))
                {
                    mtext.UpgradeOpen();
                    mtext.Contents = mTextValue;
                    changed++;
                }
            }
            return changed;
        }
    }
}
