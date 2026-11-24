using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;

namespace UNCAD.Core.Submission
{
    /// <summary>
    /// D-057：清单（BOQ）里同机台同名回路的区分。
    ///
    /// 3.5.0 之前 BOQ 的设备列按回路名定位：一个机台两条同名回路（上游编号
    /// 不同）会并成一列——数量相加，只跑其中一条时另一条的数量被清掉；
    /// DATA 页也只留最后一行。现在设备列按回路身份（有键用键，无键退回
    /// 回路名）分组，列名在机台内有同名回路时写成“回路名（上游 X）”。
    /// 是否重名取自台账（而不是本批图框）：先后两次各出一条同名回路时，
    /// 列名稳定，不会先落一列“AC Rack”再另起一列。
    /// </summary>
    public static class BoqDeviceLabels
    {
        /// <summary>
        /// 给记录补上 <see cref="SubmissionRecord.DeviceLabel"/> 与
        /// <see cref="SubmissionRecord.UpstreamId"/>。rows = 这些机台的台账行
        /// （可空：快照不可用时退回按回路名，行为同旧版）。
        /// </summary>
        public static void Apply(IEnumerable<SubmissionRecord> records,
            IEnumerable<MachineRow> rows)
        {
            List<SubmissionRecord> list = (records ?? Enumerable.Empty<SubmissionRecord>())
                .Where(record => record != null).ToList();
            if (list.Count == 0) return;
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var rowByKey = new Dictionary<string, MachineRow>(StringComparer.OrdinalIgnoreCase);
            foreach (MachineRow row in rows ?? Enumerable.Empty<MachineRow>())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.CircuitName)) continue;
                string nameKey = NameKey(row.MachineId, row.CircuitName);
                nameCounts[nameKey] = nameCounts.TryGetValue(nameKey, out int count)
                    ? count + 1 : 1;
                string key = CircuitIdentityKey.Of(row);
                if (!rowByKey.ContainsKey(key)) rowByKey[key] = row;
            }
            foreach (SubmissionRecord record in list)
            {
                string name = (record.DeviceName ?? "").Trim();
                string circuitKey = (record.CircuitKey ?? "").Trim();
                string upstream = circuitKey.Length > 0
                    && rowByKey.TryGetValue(circuitKey, out MachineRow row)
                        ? CircuitIdentityKey.UpstreamDisplay(row.Fr) : "";
                record.UpstreamId = upstream;
                bool duplicated = nameCounts.TryGetValue(NameKey(record.MachineId, name),
                    out int sameName) && sameName > 1;
                record.DeviceLabel = !duplicated ? name
                    : name + (upstream.Length > 0
                        ? "（上游 " + upstream + "）" : "（上游未记录）");
            }
        }

        /// <summary>设备列的分组键：有回路身份键用键，没有退回回路名。</summary>
        public static string ColumnKey(SubmissionRecord record)
        {
            string circuitKey = (record?.CircuitKey ?? "").Trim();
            return circuitKey.Length > 0
                ? "k:" + circuitKey
                : "n:" + IdentityTextNormalizer.Key(record?.DeviceName ?? "");
        }

        /// <summary>设备列列名：补过的用 DeviceLabel，没补的用回路名。</summary>
        public static string LabelOf(SubmissionRecord record)
            => (record.DeviceLabel ?? "").Trim().Length > 0
                ? record.DeviceLabel.Trim()
                : (record.DeviceName ?? "").Trim();

        private static string NameKey(string machineId, string circuitName)
            => IdentityTextNormalizer.Key(machineId ?? "") + "\u001f"
                + IdentityTextNormalizer.Key(circuitName ?? "");
    }
}
