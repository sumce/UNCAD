using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// Circuit identity key (D-056): a stable hash over the fields that make a
    /// circuit THIS circuit — machine ID, circuit name, and the upstream feeder
    /// number. Volatile construction attributes (facility switch, cable model,
    /// floors/axes) are deliberately excluded: they flow through the existing
    /// per-field change history instead of reassigning identity. Row order in
    /// the live workbook never participates, so real-time refreshes cannot
    /// redirect U1U to a different circuit with the same name.
    /// </summary>
    public static class CircuitIdentityKey
    {
        internal const string Prefix = "ck1:";

        /// <summary>Builds the identity key for a workbook row. Never empty.</summary>
        public static string Of(MachineRow row)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            return Of(row.MachineId, row.CircuitName, row.Fr);
        }

        /// <summary>
        /// Builds the identity key from the plaintext identity triple. Used both
        /// for workbook rows and for the persisted frameinfo_json identity
        /// (D-056 amendment: the key is derived on demand from the stored
        /// plaintext — machineId, deviceName, fr — and never persisted itself,
        /// so the JSON stays human-readable and reversible).
        /// </summary>
        public static string Of(string machineId, string circuitName, string upstreamId)
        {
            string machine = NormalizeIdentityText(machineId);
            string circuit = NormalizeCircuitName(circuitName);
            string upstream = NormalizeUpstream(upstreamId);
            // '\n' cannot survive whitespace stripping, so it is a safe separator.
            string payload = machine + "\n" + circuit + "\n" + upstream;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return Prefix + BitConverter.ToString(hash, 0, 12)
                    .Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// D-056 刷新诊断：找出同机台内「同名且同上游编号」（即同身份键）的
        /// 重复行。这类行无法用回路身份键区分，属于表格数据错误，必须在刷新
        /// 时报出来让用户修表，而不是等到 U1U 匹配时才失败。
        /// </summary>
        /// <param name="numberedRows">行号（Excel 物理行）与数据行的配对。</param>
        public static IReadOnlyList<string> FindDuplicateNotes(
            IReadOnlyList<System.ValueTuple<int, MachineRow>> numberedRows)
        {
            var notes = new List<string>();
            if (numberedRows == null || numberedRows.Count == 0) return notes;
            var groups = new Dictionary<string, List<System.ValueTuple<int, MachineRow>>>();
            foreach (System.ValueTuple<int, MachineRow> entry in numberedRows)
            {
                if (entry.Item2 == null
                    || string.IsNullOrWhiteSpace(entry.Item2.MachineId)) continue;
                string key = Of(entry.Item2);
                if (!groups.TryGetValue(key, out List<System.ValueTuple<int, MachineRow>> bucket))
                {
                    bucket = new List<System.ValueTuple<int, MachineRow>>();
                    groups[key] = bucket;
                }
                bucket.Add(entry);
            }
            foreach (List<System.ValueTuple<int, MachineRow>> bucket in groups.Values)
            {
                if (bucket.Count < 2) continue;
                MachineRow first = bucket[0].Item2;
                string rows = string.Join("、", bucket.ConvertAll(entry =>
                    entry.Item1.ToString(CultureInfo.InvariantCulture)));
                notes.Add("机台 " + first.MachineId.Trim() + " 回路“"
                    + (first.CircuitName ?? "").Trim() + "”有 " + bucket.Count
                    + " 行同名且同上游编号（Excel 行 " + rows
                    + "），回路身份键无法区分，请修正重复行。");
            }
            return notes;
        }

        /// <summary>
        /// Common folding for identity text: NFKC (full-width → half-width),
        /// drop all whitespace, lowercase. Kept local on purpose —
        /// <see cref="UNCAD.Core.Text.IdentityTextNormalizer"/> semantics are
        /// load-bearing elsewhere and must not change.
        /// </summary>
        private static string NormalizeIdentityText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string folded = value.Normalize(NormalizationForm.FormKC);
            StringBuilder builder = new StringBuilder(folded.Length);
            foreach (char c in folded)
            {
                if (char.IsWhiteSpace(c)) continue;
                builder.Append(c);
            }
            return builder.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// Circuit names keep parentheses: “AC Rack (630A)” and “AC Rack” are
        /// different circuits in the same machine.
        /// </summary>
        private static string NormalizeCircuitName(string value)
        {
            return NormalizeIdentityText(value);
        }

        /// <summary>
        /// Upstream feeder numbers drop trailing annotations. Survey sheets
        /// append breaker-slot ranges and axis notes to the same tap-off:
        /// “2U-UPS5D1-PP-02 40-42(41/Ab)”, “3U-UPS10D1-PP-03 36(43/AC)”. Rule:
        /// cut at the first '(' (axis note), then keep only the first
        /// whitespace-separated token (the feeder code itself). An empty
        /// upstream stays empty (same-name rows without upstream remain
        /// ambiguous and are handled by the matcher, not guessed here).
        /// </summary>
        private static string NormalizeUpstream(string value)
            => NormalizeIdentityText(UpstreamDisplay(value));

        /// <summary>
        /// 上游编号的展示形态（D-057）：去掉槽位区间/轴位标注，只留编号本身
        /// （“3U-UPS10D1-PP-03 36(43/AC)”→“3U-UPS10D1-PP-03”）。XLAYOUT 按上游
        /// 排版用它做行首标签，同一上游的不同写法归进同一行。
        /// </summary>
        public static string UpstreamDisplay(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            string folded = value.Normalize(NormalizationForm.FormKC);
            int cut = folded.IndexOf('(');
            if (cut >= 0) folded = folded.Substring(0, cut);
            string first = folded.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? "";
            return first.Trim();
        }

        /// <summary>上游编号的分组键：展示形态再折叠全半角/空白/大小写。</summary>
        public static string UpstreamKey(string value)
            => NormalizeIdentityText(UpstreamDisplay(value));
    }
}
