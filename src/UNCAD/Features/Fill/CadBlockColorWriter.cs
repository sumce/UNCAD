using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace UNCAD.Features.Fill
{
    internal static class CadBlockColorWriter
    {
        internal static int Apply(Transaction transaction,
            IEnumerable<ObjectId> blockIds, short colorIndex)
        {
            if (transaction == null || blockIds == null) return 0;
            var seen = new HashSet<ObjectId>();
            int changedBlocks = 0;

            foreach (ObjectId id in blockIds)
            {
                if (id.IsNull || !seen.Add(id)) continue;
                BlockReference block = transaction.GetObject(id, OpenMode.ForRead, true)
                    as BlockReference;
                if (block == null) continue;

                bool changed = SetColor(block, colorIndex);
                foreach (ObjectId attributeId in block.AttributeCollection)
                {
                    AttributeReference attribute = transaction.GetObject(attributeId,
                        OpenMode.ForRead, true) as AttributeReference;
                    if (attribute != null && SetColor(attribute, colorIndex)) changed = true;
                }

                if (!changed) continue;
                if (!block.IsWriteEnabled) block.UpgradeOpen();
                block.RecordGraphicsModified(true);
                changedBlocks++;
            }

            return changedBlocks;
        }

        private static bool SetColor(Entity entity, short colorIndex)
        {
            if (entity.ColorIndex == colorIndex) return false;
            if (!entity.IsWriteEnabled) entity.UpgradeOpen();
            entity.ColorIndex = colorIndex;
            return true;
        }
    }
}
