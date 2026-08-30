using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>内嵌电缆规格与包塑金属软管直径对照表，直径单位为毫米。</summary>
    public static class FlexibleConduitCableMap
    {
        private static readonly Dictionary<string, string> Diameters =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "3*2.5", "20" }, { "4*2.5", "20" }, { "5*2.5", "20" },
                { "3*4", "20" }, { "4*4", "20" }, { "5*4", "20" },
                { "3*10", "25" }, { "4*10", "25" }, { "5*10", "25" },
                { "3*16", "25" }, { "4*16", "25" }, { "5*16", "25" },
                { "2*25+1*16", "38" }, { "3*25+1*16", "38" }, { "4*25+1*16", "38" },
                { "2*35+1*16", "38" }, { "3*35+1*16", "38" }, { "4*35+1*16", "38" },
                { "3*50+1*25", "51" }, { "4*50+1*25", "51" },
                { "3*70+1*35", "51" }, { "4*70+1*35", "51" },
                { "3*(2*70)+1*70", "75" }, { "4*(2*70)+1*70", "75" },
                { "3*95+1*50", "75" }, { "4*95+1*50", "75" },
                { "3*(2*95)+1*50", "75" }, { "3*120+1*70", "75" },
                { "3*(2*120)+1*120", "100" }
            };

        public static int Count => Diameters.Count;

        public static bool TryGetDiameter(string cableSpec, out string diameter)
        {
            string normalized = Normalize(cableSpec);
            if (Diameters.TryGetValue(normalized, out diameter)) return true;

            // Excel commonly stores the complete cable model (for example,
            // ZB-YJVR-3*2.5); the embedded table is keyed by its core specification.
            string core = BoqCatalogIndex.NormalizeCable(cableSpec);
            if (core != normalized && Diameters.TryGetValue(core, out diameter)) return true;

            diameter = null;
            return false;
        }

        public static bool ApplyTo(MachineRow machine)
        {
            if (machine == null) return false;
            if (!TryGetDiameter(machine.Cable, out string diameter))
            {
                // An old/blank workbook DIA value must never survive this boundary.
                machine.Dia = "";
                return false;
            }
            machine.Dia = diameter;
            return true;
        }

        private static string Normalize(string value)
        {
            return new string((value ?? "")
                .Where(character => !char.IsWhiteSpace(character)).ToArray())
                .Replace('×', '*').Replace('x', '*').Replace('X', '*')
                .ToUpperInvariant();
        }
    }
}
