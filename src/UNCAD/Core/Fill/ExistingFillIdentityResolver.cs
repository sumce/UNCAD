using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Text;

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

            // MACHINEID-DEVICE and DEVICENAME describe the same identity.  A
            // stale device block must not silently redirect U1U to another
            // Excel circuit; require the two sources to agree before matching.
            if (composite.Length > 0 && device.Length > 0
                && !IdentityTextNormalizer.EndsWith(composite, "-" + device))
            {
                error = "图框属性“" + composite + "”与设备块 DEVICENAME“"
                    + device + "”不一致，请先统一设备名称后再更新。";
                return false;
            }

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
                .Where(row => IdentityTextNormalizer.Equals(row.MachineId,
                    identity.MachineId)).ToList();
            if (machines.Count == 0)
            {
                error = "Excel中未找到机台ID “" + identity.MachineId + "”。";
                return null;
            }
            MachineRow match = machines.FirstOrDefault(row =>
                IdentityTextNormalizer.Equals(row.CircuitName, identity.DeviceName));
            // U1U is an update operation, so an existing device name is an identity
            // key, not a hint.  Falling back to the only row would let a renamed or
            // stale frame redirect its quantities to an unrelated Excel circuit.
            // A blank device name is the only safe case for the legacy single-row
            // fallback (old frames may not have carried the device attribute).
            if (match == null && string.IsNullOrWhiteSpace(identity.DeviceName)
                && machines.Count == 1) match = machines[0];
            if (match == null)
            {
                error = "机台 “" + identity.MachineId + "” 在Excel中有多个回路，"
                    + "但未找到设备 “" + identity.DeviceName + "”。请使用普通清单填充重新选择。";
                return null;
            }
            MachineRow result = Clone(match);
            result.MachineId = identity.MachineId;
            result.CircuitName = string.IsNullOrWhiteSpace(identity.DeviceName)
                ? match.CircuitName : identity.DeviceName;
            return result;
        }

        private static bool TryUnique(IEnumerable<string> source, string label,
            out string value, out string error)
        {
            List<string> values = (source ?? Enumerable.Empty<string>())
                .Select(item => (item ?? "").Trim()).Where(item => item.Length > 0)
                .GroupBy(IdentityTextNormalizer.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
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
            if (IdentityTextNormalizer.TryGetPrefixBeforeSuffix(text, device,
                out string prefix)) return prefix;
            int separator = text.IndexOf('-');
            return separator > 0 ? text.Substring(0, separator).Trim() : "";
        }

        private static string DeviceFromComposite(string composite, string machine)
        {
            string text = (composite ?? "").Trim();
            return IdentityTextNormalizer.TryGetSuffixAfterPrefix(text, machine,
                out string suffix) ? suffix : "";
        }

        private static MachineRow Clone(MachineRow source)
        {
            return new MachineRow
            {
                Region = source.Region,
                MachineId = source.MachineId,
                CircuitName = source.CircuitName,
                Batch = source.Batch,
                Cable = source.Cable,
                Fr = source.Fr,
                Detail = source.Detail,
                Seq = source.Seq,
                Dia = source.Dia,
                Next = source.Next,
                DownstreamAxis = source.DownstreamAxis,
                UpstreamAxis = source.UpstreamAxis,
                DeviceFloor = source.DeviceFloor,
                PanelFloor = source.PanelFloor,
                FacilitySwitch = source.FacilitySwitch
            };
        }
    }
}
