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

        /// <summary>
        /// D-056 回路身份键（frameinfo_json identity.circuitKey）。老图框为空串，
        /// 匹配回退到「按名匹配 + 同名歧义交还调用方」。
        /// </summary>
        public string CircuitKey { get; set; } = "";

        /// <summary>
        /// D-057：图框记录的上游编号明文（frameinfo_json identity.fr）。只在 JSON
        /// 身份胜出时才有值；物理标注身份没有上游，保持空串。统计/排版用它给
        /// 同名回路标注“（上游 …）”，不参与匹配（匹配走 CircuitKey）。
        /// </summary>
        public string UpstreamId { get; set; } = "";
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
            return MatchMachine(identity, candidates,
                out IReadOnlyList<MachineRow> _, out error);
        }

        /// <summary>
        /// D-056 匹配次序：① CircuitKey 精确匹配（唯一命中；多行同键=表格数据错误）；
        /// ② 按回路名匹配（唯一命中放行——含“上游改挂导致帧键过期”的更新场景）；
        /// ③ 同名多行 → 不猜，把候选行交还调用方（弹窗让用户选）；
        /// ④ 空设备名单行兜底保留。行序/实时刷新不再影响结果。
        /// </summary>
        public static MachineRow MatchMachine(ExistingFillIdentity identity,
            IEnumerable<MachineRow> candidates,
            out IReadOnlyList<MachineRow> ambiguousCandidates, out string error)
        {
            error = "";
            ambiguousCandidates = new List<MachineRow>();
            identity = identity ?? new ExistingFillIdentity();
            List<MachineRow> machines = (candidates ?? Enumerable.Empty<MachineRow>())
                .Where(row => IdentityTextNormalizer.Equals(row.MachineId,
                    identity.MachineId)).ToList();
            if (machines.Count == 0)
            {
                error = "Excel中未找到机台ID “" + identity.MachineId + "”。";
                return null;
            }

            if (!string.IsNullOrWhiteSpace(identity.CircuitKey))
            {
                List<MachineRow> keyed = machines
                    .Where(row => CircuitIdentityKey.Of(row) == identity.CircuitKey)
                    .ToList();
                if (keyed.Count == 1) return Adopt(keyed[0], identity);
                if (keyed.Count > 1)
                {
                    error = "机台 “" + identity.MachineId + "” 的Excel中存在 "
                        + keyed.Count + " 行相同的回路身份键（同名且同上游编号），"
                        + "请先修正表格中的重复行。";
                    return null;
                }
                // 键未命中：上游可能已改挂，落到按名匹配（唯一同名仍放行）。
            }

            List<MachineRow> byName = machines
                .Where(row => IdentityTextNormalizer.Equals(row.CircuitName,
                    identity.DeviceName))
                .ToList();
            if (byName.Count == 1) return Adopt(byName[0], identity);
            if (byName.Count > 1)
            {
                // 同名回路（上游不同）是不同回路；按行序静默取第一条会把 U1U
                // 的量更新到另一条回路上。候选交还调用方，由用户选择。
                ambiguousCandidates = byName;
                error = "机台 “" + identity.MachineId + "” 存在 " + byName.Count
                    + " 条同名回路 “" + identity.DeviceName + "”（上游编号不同），"
                    + "请选择本图框对应的回路。";
                return null;
            }

            // U1U is an update operation, so an existing device name is an identity
            // key, not a hint.  Falling back to the only row would let a renamed or
            // stale frame redirect its quantities to an unrelated Excel circuit.
            // A blank device name is the only safe case for the legacy single-row
            // fallback (old frames may not have carried the device attribute).
            MachineRow match = null;
            if (string.IsNullOrWhiteSpace(identity.DeviceName) && machines.Count == 1)
                match = machines[0];
            if (match == null)
            {
                error = "机台 “" + identity.MachineId + "” 在Excel中有多个回路，"
                    + "但未找到设备 “" + identity.DeviceName + "”。请使用普通清单填充重新选择。";
                return null;
            }
            return Adopt(match, identity);
        }

        private static MachineRow Adopt(MachineRow match, ExistingFillIdentity identity)
        {
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
