using System;
using System.Collections.Generic;
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
            string cable = Unique(source, FrameBlockFiller.TagCable);
            int separator = cable.IndexOfAny(new[] { ':', '：' });
            if (separator >= 0) cable = cable.Substring(0, separator);
            cable = CableSuffix.Replace(cable.Trim(), "");

            return new SubmissionRecord
            {
                MachineId = machineId,
                DeviceName = deviceName,
                PanelType = InferPanelType(source.DynamicValues)
                    ?? InferPanelType(source.TableValues) ?? "未指定",
                Cable = cable,
                Fr = infoLines.Count > 0 ? infoLines[0] : "",
                Detail = infoLines.Count > 1 ? string.Join("\n", infoLines.Skip(1)) : "",
                Diameter = ExtractDiameter(Unique(source, FrameBlockFiller.TagConduit)),
                DownstreamAxis = Unique(source, ConnectionBlockFiller.TagDownstreamAxis),
                UpstreamAxis = Unique(source, ConnectionBlockFiller.TagUpstreamAxis)
            };
        }

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
