using Autodesk.AutoCAD.Runtime;
using UNCAD.Infra;

namespace UNCAD.Features.Unl
{
    /// <summary>Retains UNLX as a keyboard alias for the canonical U1LX command.</summary>
    public sealed class QuickLineAliasCommand
    {
        [CommandMethod(CommandIds.LegacyLineQuick)]
        public void QuickLineAlias() => new QuickLineFeature().QuickLine();
    }
}
