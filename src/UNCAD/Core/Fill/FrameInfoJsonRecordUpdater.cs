using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public static class FrameInfoJsonRecordUpdater
    {
        private static readonly string[] TrackedFields =
        {
            "MachineId", "DeviceName", "Region", "OriginalCableModel", "BoqCableModel", "BoqBusPlugBoxCode",
            "Fr", "Detail", "Seq", "HoseDiameter", "Next", "UpstreamAxis",
            "DownstreamAxis", "DeviceFloor", "PanelFloor", "FacilitySwitch",
            "MeasuredCableM", "PanelReserveMm", "DeviceReserveMm", "TotalCableM"
        };

        public static FrameInfoJsonRecord Update(FrameInfoJsonRecord previous,
            MachineRow machine, FillReviewData review, string commandName, DateTime utcNow)
            => Update(previous, machine, review, commandName, utcNow, null, null);

        /// <summary>
        /// Builds the next v2 record. <paramref name="reserve"/> is optional: when
        /// supplied the cable reserve fields are refreshed; otherwise the previous
        /// values are preserved (same policy as the cable-model override).
        /// </summary>
        public static FrameInfoJsonRecord Update(FrameInfoJsonRecord previous,
            MachineRow machine, FillReviewData review, string commandName, DateTime utcNow,
            string updateUser, CableReserveInfo reserve = null)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            previous = previous ?? new FrameInfoJsonRecord();
            string original = FirstNonEmpty(previous.OriginalCableModel,
                review?.OriginalCableModel, machine.Cable);
            string boq = FirstNonEmpty(review?.BoqCableModel,
                previous.BoqCableModel, original);
            FillReviewItem busPlugBox = review?.BusPlugBoxItem();
            var next = new FrameInfoJsonRecord
            {
                SchemaVersion = FrameInfoJsonCodec.CurrentSchemaVersion,
                MachineId = machine.MachineId ?? "",
                DeviceName = machine.CircuitName ?? "",
                Region = machine.Region ?? "",
                OriginalCableModel = original,
                BoqCableModel = boq,
                BoqBusPlugBoxCode = review == null ? previous.BoqBusPlugBoxCode
                    : busPlugBox?.CatalogMatched == true && busPlugBox.Included
                        ? busPlugBox.Code : "",
                Fr = machine.Fr ?? "",
                Detail = machine.Detail ?? "",
                Seq = machine.Seq ?? "",
                HoseDiameter = FirstNonEmpty(review?.Machine?.Dia, machine.Dia),
                Next = machine.Next ?? "",
                UpstreamAxis = machine.UpstreamAxis ?? "",
                DownstreamAxis = machine.DownstreamAxis ?? "",
                DeviceFloor = machine.DeviceFloor ?? "",
                PanelFloor = machine.PanelFloor ?? "",
                FacilitySwitch = machine.FacilitySwitch ?? "",
                LastModifiedUtc = utcNow.ToUniversalTime().ToString("o"),
                LastModifiedUser = FirstNonEmpty(updateUser, Environment.UserName,
                    Environment.MachineName),
                FilledUtc = ResolveFilledUtc(previous, commandName, utcNow),
                LastCommand = NormalizeCommand(commandName),
                // 框句柄绑定跨更新保留；写入方（FrameInfoJsonBlockWriter）负责盖章。
                FrameHandle = previous.FrameHandle ?? "",
                MeasuredCableM = reserve == null
                    ? (previous.MeasuredCableM ?? "")
                    : FormatDouble(reserve.MeasuredMeters),
                PanelReserveMm = reserve == null
                    ? (previous.PanelReserveMm ?? "")
                    : FormatDouble(reserve.PanelReserveMillimeters),
                DeviceReserveMm = reserve == null
                    ? (previous.DeviceReserveMm ?? "")
                    : FormatDouble(reserve.DeviceReserveMillimeters),
                TotalCableM = reserve == null
                    ? (previous.TotalCableM ?? "")
                    : FormatDouble(reserve.TotalMeters),
                Changes = (previous.Changes ?? new List<FrameInfoJsonChange>())
                    .Select(CloneChange).ToList()
            };
            foreach (string field in TrackedFields)
                AddChange(previous, next, field, commandName);
            if (next.Changes.Count > FrameInfoJsonCodec.MaxChanges)
            {
                next.Changes = next.Changes
                    .Skip(next.Changes.Count - FrameInfoJsonCodec.MaxChanges).ToList();
            }
            return next;
        }

        /// <summary>
        /// 命令名归一（"U1F"/"U1U"；未知按 U1F——与变更历史的兜底一致）。
        /// 写记录与制图信息表取时间共用，保证两处口径一致。
        /// </summary>
        public static string NormalizeCommand(string commandName)
            => string.Equals((commandName ?? "").Trim(), "U1U", StringComparison.OrdinalIgnoreCase)
                ? "U1U" : "U1F";

        /// <summary>
        /// D-057：创建时间的取法。U1F 是重定义——每次 U1F 都算重新创建，取本次
        /// 时间；U1U 沿用上一条记录的创建时间。老记录没有创建时间：退回它的
        /// 最后写入时间（U1U 接手老图框时不把创建时间丢掉）。
        /// </summary>
        public static string ResolveFilledUtc(FrameInfoJsonRecord previous,
            string commandName, DateTime utcNow)
        {
            string now = utcNow.ToUniversalTime().ToString("o");
            if (NormalizeCommand(commandName) == "U1F") return now;
            string inherited = (previous?.FilledUtc ?? "").Trim();
            if (inherited.Length > 0) return inherited;
            string legacy = (previous?.LastModifiedUtc ?? "").Trim();
            return legacy.Length > 0 ? legacy : now;
        }

        private static FrameInfoJsonChange CloneChange(FrameInfoJsonChange change)
            => new FrameInfoJsonChange
            {
                TimestampUtc = change?.TimestampUtc ?? "",
                Command = change?.Command ?? "",
                Field = change?.Field ?? "",
                Before = change?.Before ?? "",
                After = change?.After ?? "",
                Note = change?.Note ?? ""
            };

        private static void AddChange(FrameInfoJsonRecord before,
            FrameInfoJsonRecord after, string field, string command)
        {
            string oldValue = Value(before, field);
            string newValue = Value(after, field);
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
            after.Changes.Add(new FrameInfoJsonChange
            {
                TimestampUtc = after.LastModifiedUtc,
                Command = command ?? "U1F",
                Field = field,
                Before = oldValue,
                After = newValue,
                Note = "U1F/U1U 自动同步"
            });
        }

        private static string Value(FrameInfoJsonRecord record, string field)
            => GetFieldValue(record, field);

        /// <summary>Reads one tracked field by name; shared with the diff builder.</summary>
        public static string GetFieldValue(FrameInfoJsonRecord record, string field)
        {
            if (record == null) return "";
            switch (field)
            {
                case "MachineId": return record.MachineId ?? "";
                case "DeviceName": return record.DeviceName ?? "";
                case "Region": return record.Region ?? "";
                case "OriginalCableModel": return record.OriginalCableModel ?? "";
                case "BoqCableModel": return record.BoqCableModel ?? "";
                case "BoqBusPlugBoxCode": return record.BoqBusPlugBoxCode ?? "";
                case "Fr": return record.Fr ?? "";
                case "Detail": return record.Detail ?? "";
                case "Seq": return record.Seq ?? "";
                case "HoseDiameter": return record.HoseDiameter ?? "";
                case "Next": return record.Next ?? "";
                case "UpstreamAxis": return record.UpstreamAxis ?? "";
                case "DownstreamAxis": return record.DownstreamAxis ?? "";
                case "DeviceFloor": return record.DeviceFloor ?? "";
                case "PanelFloor": return record.PanelFloor ?? "";
                case "FacilitySwitch": return record.FacilitySwitch ?? "";
                case "MeasuredCableM": return record.MeasuredCableM ?? "";
                case "PanelReserveMm": return record.PanelReserveMm ?? "";
                case "DeviceReserveMm": return record.DeviceReserveMm ?? "";
                case "TotalCableM": return record.TotalCableM ?? "";
                case "LastModifiedUser": return record.LastModifiedUser ?? "";
                default: return "";
            }
        }

        private static string FormatDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FirstNonEmpty(params string[] values)
            => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
