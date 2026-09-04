using System;
using System.Collections.Generic;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    /// <summary>One field-level difference between the previous update and this one.</summary>
    public sealed class FrameInfoFieldDiff
    {
        public string Label { get; set; }
        public string Field { get; set; }
        public string Old { get; set; }
        public string New { get; set; }
        public bool Changed { get; set; }
    }

    /// <summary>
    /// Builds the field-by-field comparison shown by U1U before overwriting a
    /// frame: the values recorded in frameinfo_json by the previous update
    /// versus the values about to be written.
    /// </summary>
    public static class FrameInfoUpdateDiff
    {
        private static readonly string[] DiffFields =
        {
            "MachineId", "DeviceName", "Region", "BoqCableModel", "OriginalCableModel",
            "Fr", "Detail", "Seq", "HoseDiameter", "Next", "UpstreamAxis",
            "DownstreamAxis", "DeviceFloor", "PanelFloor", "FacilitySwitch"
        };

        public static List<FrameInfoFieldDiff> Build(FrameInfoJsonRecord previous,
            MachineRow machine, FillReviewData review)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            FrameInfoJsonRecord next = FrameInfoJsonRecordUpdater.Update(
                previous, machine, review, "diff", DateTime.UtcNow, Environment.UserName);
            var diffs = new List<FrameInfoFieldDiff>();
            foreach (string field in DiffFields)
            {
                string oldValue = FrameInfoJsonRecordUpdater.GetFieldValue(previous, field);
                string newValue = FrameInfoJsonRecordUpdater.GetFieldValue(next, field);
                diffs.Add(new FrameInfoFieldDiff
                {
                    Field = field,
                    Label = FieldLabel(field),
                    Old = oldValue,
                    New = newValue,
                    Changed = !string.Equals(oldValue, newValue, StringComparison.Ordinal)
                });
            }
            // 变化优先展示,同组内按固定字段顺序。
            diffs.Sort((left, right) =>
            {
                int byChanged = right.Changed.CompareTo(left.Changed);
                if (byChanged != 0) return byChanged;
                return Array.IndexOf(DiffFields, left.Field)
                    .CompareTo(Array.IndexOf(DiffFields, right.Field));
            });
            return diffs;
        }

        public static string FieldLabel(string field)
        {
            switch (field)
            {
                case "MachineId": return "机台ID";
                case "DeviceName": return "设备名";
                case "Region": return "区域";
                case "BoqCableModel": return "电缆型号(BOQ)";
                case "OriginalCableModel": return "原始电缆型号";
                case "Fr": return "FR";
                case "Detail": return "配电详情";
                case "Seq": return "项目序号";
                case "HoseDiameter": return "软管直径";
                case "Next": return "盘柜类型";
                case "UpstreamAxis": return "上游轴位";
                case "DownstreamAxis": return "下游轴位";
                case "DeviceFloor": return "设备楼层";
                case "PanelFloor": return "盘柜楼层";
                case "FacilitySwitch": return "机台开关";
                default: return field;
            }
        }
    }
}
