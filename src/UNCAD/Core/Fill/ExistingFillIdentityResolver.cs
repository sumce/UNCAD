using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public sealed class ExistingFillIdentity
    {
        public string MachineId { get; set; } = "";
        public string DeviceName { get; set; } = "";
    }

    public static class ExistingFillIdentityResolver
    {
        public static bool TryResolve(IEnumerable<string> powerValues,
            IEnumerable<string> compositeValues, IEnumerable<string> deviceValues,
            out ExistingFillIdentity identity, out string error)
        {
            identity = null;
            error = "";
            if (!TryUnique(powerValues, "机台ID", out string power, out error)
                || !TryUnique(compositeValues, "机台/设备信息", out string composite, out error)
                || !TryUnique(deviceValues, "设备名称", out string device, out error))
                return false;

            string machine = MachineFromPower(power);
            if (machine.Length == 0) machine = MachineFromComposite(composite, device);
            if (device.Length == 0) device = DeviceFromComposite(composite, machine);
            if (machine.Length == 0 || device.Length == 0)
            {
                error = "未从已填充图框读取到完整的机台ID和设备名称。请同时框选图框与设备块。";
                return false;
            }
            identity = new ExistingFillIdentity { MachineId = machine, DeviceName = device };
            return true;
        }

        public static MachineRow MatchMachine(ExistingFillIdentity identity,
            IEnumerable<MachineRow> candidates, out string error)
        {
            error = "";
            identity = identity ?? new ExistingFillIdentity();
            List<MachineRow> machines = (candidates ?? Enumerable.Empty<MachineRow>())
                .Where(row => string.Equals((row.MachineId ?? "").Trim(), identity.MachineId,
                    StringComparison.OrdinalIgnoreCase)).ToList();
            if (machines.Count == 0)
            {
                error = "Excel中未找到机台ID “" + identity.MachineId + "”。";
                return null;
            }
            MachineRow match = machines.FirstOrDefault(row => string.Equals(
                (row.CircuitName ?? "").Trim(), identity.DeviceName,
                StringComparison.OrdinalIgnoreCase));
            if (match == null && machines.Count == 1) match = machines[0];
            if (match == null)
            {
                error = "机台 “" + identity.MachineId + "” 在Excel中有多个回路，"
                    + "但未找到设备 “" + identity.DeviceName + "”。请使用普通清单填充重新选择。";
                return null;
            }
            MachineRow result = Clone(match);
            result.MachineId = identity.MachineId;
            result.CircuitName = identity.DeviceName;
            return result;
        }

        private static bool TryUnique(IEnumerable<string> source, string label,
            out string value, out string error)
        {
            List<string> values = (source ?? Enumerable.Empty<string>())
                .Select(item => (item ?? "").Trim()).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            value = values.Count == 1 ? values[0] : "";
            error = values.Count > 1 ? "框选内容包含多个不同的" + label + "，请每次只更新一台设备。" : "";
            return values.Count <= 1;
        }

        private static string MachineFromPower(string value)
        {
            string text = (value ?? "").Trim();
            return text.EndsWith("-POWER", StringComparison.OrdinalIgnoreCase)
                ? text.Substring(0, text.Length - 6).TrimEnd('-').Trim() : "";
        }

        private static string MachineFromComposite(string composite, string device)
        {
            string text = (composite ?? "").Trim();
            string suffix = "-" + (device ?? "").Trim();
            if (suffix.Length > 1 && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text.Substring(0, text.Length - suffix.Length).Trim();
            int separator = text.IndexOf('-');
            return separator > 0 ? text.Substring(0, separator).Trim() : "";
        }

        private static string DeviceFromComposite(string composite, string machine)
        {
            string text = (composite ?? "").Trim();
            string prefix = (machine ?? "").Trim() + "-";
            return prefix.Length > 1 && text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text.Substring(prefix.Length).Trim() : "";
        }

        private static MachineRow Clone(MachineRow source)
        {
            return new MachineRow
            {
                Region = source.Region,
                MachineId = source.MachineId,
                CircuitName = source.CircuitName,
                Cable = source.Cable,
                Fr = source.Fr,
                Detail = source.Detail,
                Seq = source.Seq,
                Dia = source.Dia,
                Next = source.Next,
                DownstreamAxis = source.DownstreamAxis,
                UpstreamAxis = source.UpstreamAxis
            };
        }
    }
}
