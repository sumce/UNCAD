using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Core.Submission
{
    public static class SubmissionRecordExtractor
    {
        private static readonly Regex CableSuffix = new Regex(@"mm(?:²|2)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Diameter = new Regex(
            @"[⌀ØΦφ]\s*([0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);
        private static readonly Regex MillimeterValue = new Regex(
            @"([0-9]+(?:\.[0-9]+)?)\s*mm", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CableModelInDescription = new Regex(
            @"名称\s*[:：]\s*(.+?)mm(?:²|2)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EqualsMeters = new Regex(
            @"=\s*([0-9]+(?:\.[0-9]+)?)\s*[mM](?:\b|$)", RegexOptions.Compiled);
        private static readonly Regex MeterValue = new Regex(
            @"([0-9]+(?:\.[0-9]+)?)\s*[mM](?:\b|$)", RegexOptions.Compiled);

        public static SubmissionRecord Extract(SubmissionSourceData source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            string power = Unique(source, FrameBlockFiller.TagPower);
            string deviceComposite = Unique(source, FrameBlockFiller.TagDevice);
            string machineId = MachineFromPower(power);
            string deviceName = Unique(source, DeviceBlockFiller.TagDeviceName);

            if (machineId.Length == 0 && deviceComposite.Length > 0)
                machineId = MachineFromComposite(deviceComposite);
            if (deviceName.Length == 0 && deviceComposite.Length > 0)
                deviceName = DeviceFromComposite(deviceComposite, machineId);

            if (machineId.Length == 0)
                throw new InvalidDataException("框选内容中未读取到机台ID（MACHINEID-POWER/MACHINEID-DEVICE）。");
            if (deviceName.Length == 0)
                throw new InvalidDataException("框选内容中未读取到设备名称（DEVICENAME/MACHINEID-DEVICE）。");

            string upstreamInfo = Unique(source, ConnectionBlockFiller.TagUpstreamInfo);
            List<string> infoLines = TextParser.SplitMTextLines(upstreamInfo)
                .Select(TextParser.CleanMText).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            string cableInfo = Unique(source, FrameBlockFiller.TagCable);
            string bridgeInfo = Unique(source, FrameBlockFiller.TagBridge);
            string conduitInfo = Unique(source, FrameBlockFiller.TagConduit);
            bool hasCableRows = HasTableRows(source, IsCableRow);
            bool hasBridgeRows = HasTableRows(source, IsBridgeRow);
            bool hasConduitRows = HasTableRows(source, IsRigidConduitRow);
            string tableCable = ExtractTableCableModel(source);
            string originalCable = ExtractCableModel(cableInfo);
            // The edited table is authoritative for procurement, while the frame attribute
            // preserves the device design value for traceability after a BOQ substitution.
            string cable = tableCable.Length > 0 ? tableCable : originalCable;

            string flexibleDiameter = ExtractTableDiameter(source, IsFlexibleConduitRow);
            string flexibleMeters = TableMeters(source, IsFlexibleConduitRow);
            string cableMeters = hasCableRows ? TableMeters(source, IsCableRow)
                : FormatMeters(ParseInfoMeters(cableInfo));
            string bridgeMeters = hasBridgeRows ? TableMeters(source, IsBridgeRow)
                : FormatMeters(ParseInfoMeters(bridgeInfo));
            string conduitMeters = hasConduitRows ? TableMeters(source, IsRigidConduitRow)
                : FormatMeters(ParseInfoMeters(conduitInfo));
            string currentBridgeInfo = hasBridgeRows
                ? BuildTableInfo(source, IsBridgeRow) : bridgeInfo;
            string currentConduitInfo = hasConduitRows
                ? BuildTableInfo(source, IsRigidConduitRow) : conduitInfo;

            return new SubmissionRecord
            {
                MachineId = machineId,
                DeviceName = deviceName,
                PanelType = InferPanelType(source.DynamicValues)
                    ?? InferPanelType(source.TableValues) ?? "未指定",
                OriginalCable = originalCable,
                Cable = cable,
                CableMeters = cableMeters,
                Fr = infoLines.Count > 0 ? infoLines[0] : "",
                Detail = infoLines.Count > 1 ? string.Join("\n", infoLines.Skip(1)) : "",
                Diameter = flexibleDiameter.Length > 0
                    ? flexibleDiameter : ExtractDiameter(conduitInfo),
                FlexibleConduitMeters = flexibleMeters,
                BridgeInfo = currentBridgeInfo,
                BridgeMeters = bridgeMeters,
                ConduitInfo = currentConduitInfo,
                ConduitMeters = conduitMeters,
                DownstreamAxis = Unique(source, ConnectionBlockFiller.TagDownstreamAxis),
                UpstreamAxis = Unique(source, ConnectionBlockFiller.TagUpstreamAxis),
                Materials = ExtractMaterials(source),
                TableRowsRead = source.TableRows.Count,
                TextEntityCount = source.TextEntityCount
            };
        }

        private static string ExtractCableModel(string cableInfo)
        {
            string cable = cableInfo ?? "";
            int separator = cable.IndexOfAny(new[] { ':', '：' });
            if (separator >= 0) cable = cable.Substring(0, separator);
            return CableSuffix.Replace(cable.Trim(), "");
        }

        private static string ExtractTableCableModel(SubmissionSourceData source)
        {
            foreach (List<string> row in source.TableRows.Where(IsCableRow))
            {
                string description = Cell(row, 2);
                Match match = CableModelInDescription.Match(description);
                if (match.Success) return match.Groups[1].Value.Trim();
                if (LooksLikeDirectCableModel(description))
                    return NormalizeTableText(description);
                string name = Cell(row, 1);
                if (CodeStarts(row, "1.") && LooksLikeDirectCableModel(name)) return name;
            }
            return "";
        }

        private static bool LooksLikeDirectCableModel(string value)
        {
            string text = (value ?? "").Trim();
            return text.Length > 0 && text.Length <= 80
                && text.IndexOf("电缆", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf(@"\P", StringComparison.OrdinalIgnoreCase) < 0
                && (Regex.IsMatch(text, "[A-Za-z].*[0-9]|[0-9].*[A-Za-z]")
                    || text.IndexOf('*') >= 0
                    || (text.IndexOf('-') >= 0 && Regex.IsMatch(text, "[A-Za-z]")));
        }

        private static string ExtractTableDiameter(SubmissionSourceData source,
            Func<List<string>, bool> predicate)
        {
            foreach (List<string> row in source.TableRows.Where(predicate))
            {
                string text = string.Join(" ", row);
                Match symbol = Diameter.Match(text);
                if (symbol.Success) return symbol.Groups[1].Value;
                Match millimeter = MillimeterValue.Match(text);
                if (millimeter.Success) return millimeter.Groups[1].Value;
            }
            return "";
        }

        private static bool HasTableRows(SubmissionSourceData source,
            Func<List<string>, bool> predicate)
            => source.TableRows.Any(predicate);

        private static string BuildTableInfo(SubmissionSourceData source,
            Func<List<string>, bool> predicate)
        {
            var values = new List<string>();
            foreach (List<string> row in source.TableRows.Where(predicate))
            {
                string name = Cell(row, 1);
                if (name.Length == 0) name = NormalizeTableText(Cell(row, 2));
                string quantity = Cell(row, 4);
                string unit = Cell(row, 3);
                values.Add((name + (quantity.Length > 0
                    ? " " + quantity + (unit.Length > 0 ? unit : "M") : "")).Trim());
            }
            return string.Join("; ", values);
        }

        private static string TableMeters(SubmissionSourceData source,
            Func<List<string>, bool> predicate)
        {
            double total = 0;
            foreach (List<string> row in source.TableRows.Where(predicate))
            {
                if (row.Count <= 4) continue;
                total += ParseMeters(row[4]);
            }
            return FormatMeters(total);
        }

        private static double ParseInfoMeters(string value)
        {
            double total = 0;
            string[] parts = (value ?? "").Split(new[] { ';', '；', '，', ',', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                Match equals = EqualsMeters.Match(part);
                if (equals.Success)
                {
                    total += ParseMeters(equals.Groups[1].Value);
                    continue;
                }
                MatchCollection meters = MeterValue.Matches(part);
                if (meters.Count > 0)
                    total += ParseMeters(meters[meters.Count - 1].Groups[1].Value);
            }
            return total;
        }

        private static double ParseMeters(string value)
        {
            string text = (value ?? "").Trim().TrimEnd('M', 'm').Trim();
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out double invariant)) return invariant;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture,
                out double current) ? current : 0;
        }

        private static string FormatMeters(double value)
            => value > 0 ? TextFormatter.FormatNum(value) : "";

        private static List<SubmissionMaterial> ExtractMaterials(SubmissionSourceData source)
        {
            var materials = new List<SubmissionMaterial>();
            foreach (List<string> row in source.TableRows)
            {
                if (row.Count < 5) continue;
                string number = Cell(row, 0);
                string name = Cell(row, 1);
                string description = Cell(row, 2);
                string unit = Cell(row, 3);
                string quantity = Cell(row, 4);
                string code = Cell(row, 5);
                if (TableLayoutClassifier.IsHeaderLike(number, name, code)) continue;
                if (!TableLayoutClassifier.IsNumberedDataRow(number)
                    && quantity.Length == 0) continue;
                if (name.Length == 0 && description.Length == 0
                    && unit.Length == 0 && quantity.Length == 0 && code.Length == 0) continue;
                materials.Add(new SubmissionMaterial
                {
                    Number = number,
                    Name = name,
                    Description = NormalizeTableText(description),
                    Unit = unit,
                    Quantity = quantity,
                    Code = code
                });
            }
            return materials;
        }

        private static string Cell(List<string> row, int index)
            => index >= 0 && index < row.Count ? (row[index] ?? "").Trim() : "";

        private static string NormalizeTableText(string value)
        {
            return string.Join("\n", TextParser.SplitMTextLines(value ?? "")
                .Select(TextParser.CleanMText).Where(line => !string.IsNullOrWhiteSpace(line)));
        }

        private static bool IsCableRow(List<string> row)
            => CodeStarts(row, "1.") || RowContains(row, "电缆");

        private static bool IsBridgeRow(List<string> row)
            => CodeStarts(row, "2.") || RowContains(row, "桥架");

        private static bool IsFlexibleConduitRow(List<string> row)
            => CodeStarts(row, "3.8") || ((CodeStarts(row, "3.") || RowContains(row, "管"))
                && (RowContains(row, "软管") || RowContains(row, "波纹管")));

        private static bool IsRigidConduitRow(List<string> row)
            => (CodeStarts(row, "3.") || RowContains(row, "线管")
                || RowContains(row, "穿线管")) && !IsFlexibleConduitRow(row);

        private static bool CodeStarts(List<string> row, string prefix)
            => Cell(row, 5).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static bool RowContains(List<string> row, string value)
            => row.Any(cell => Contains(cell, value));

        private static string Unique(SubmissionSourceData source, string tag)
        {
            if (!source.Attributes.TryGetValue(tag, out List<string> values)) return "";
            List<string> distinct = values.Select(v => (v ?? "").Trim())
                .Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinct.Count > 1)
                throw new InvalidDataException("框选内容中存在多个不同的 " + tag + " 值，请每次只提交一台设备。");
            return distinct.Count == 1 ? distinct[0] : "";
        }

        private static string MachineFromPower(string value)
        {
            string text = (value ?? "").Trim();
            return text.EndsWith("-POWER", StringComparison.OrdinalIgnoreCase)
                ? text.Substring(0, text.Length - 6).TrimEnd('-').Trim() : "";
        }

        private static string MachineFromComposite(string value)
        {
            string text = (value ?? "").Trim();
            int dash = text.IndexOf('-');
            return dash > 0 ? text.Substring(0, dash).Trim() : "";
        }

        private static string DeviceFromComposite(string value, string machineId)
        {
            string text = (value ?? "").Trim();
            string prefix = (machineId ?? "").Trim() + "-";
            return prefix.Length > 1 && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text.Substring(prefix.Length).Trim() : "";
        }

        private static string ExtractDiameter(string conduitInfo)
        {
            Match match = Diameter.Match(conduitInfo ?? "");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static string InferPanelType(IEnumerable<string> values)
        {
            string[] source = (values ?? Enumerable.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
            if (source.Any(v => Contains(v, "I-Line") || Contains(v, "I_line")
                || Contains(v, "断路器") || IsCode(v, "6."))) return "I-Line盘";
            if (source.Any(v => Contains(v, "母线插接") || Contains(v, "PLUG-IN")
                || IsCode(v, "5."))) return "母线插接口";
            if (source.Any(v => Contains(v, "插座") || Contains(v, "socket")
                || IsCode(v, "8."))) return "插座盘";
            return null;
        }

        private static bool Contains(string value, string fragment)
            => (value ?? "").IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsCode(string value, string prefix)
            => (value ?? "").Trim().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
