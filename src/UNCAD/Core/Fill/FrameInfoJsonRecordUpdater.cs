using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.Excel;

namespace UNCAD.Core.Fill
{
    public static class FrameInfoJsonRecordUpdater
    {
        private static readonly string[] TrackedFields =
        {
            "MachineId", "DeviceName", "Region", "OriginalCableModel", "BoqCableModel",
            "Fr", "Detail", "Seq", "HoseDiameter", "Next", "UpstreamAxis",
            "DownstreamAxis", "DeviceFloor", "PanelFloor", "FacilitySwitch"
        };

        public static FrameInfoJsonRecord Update(FrameInfoJsonRecord previous,
            MachineRow machine, FillReviewData review, string commandName, DateTime utcNow)
            => Update(previous, machine, review, commandName, utcNow, null);

        public static FrameInfoJsonRecord Update(FrameInfoJsonRecord previous,
            MachineRow machine, FillReviewData review, string commandName, DateTime utcNow,
            string updateUser)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            previous = previous ?? new FrameInfoJsonRecord();
            string original = FirstNonEmpty(previous.OriginalCableModel,
                review?.OriginalCableModel, machine.Cable);
            string boq = FirstNonEmpty(review?.BoqCableModel,
                previous.BoqCableModel, original);
            var next = new FrameInfoJsonRecord
            {
                SchemaVersion = "1",
                MachineId = machine.MachineId ?? "",
                DeviceName = machine.CircuitName ?? "",
                Region = machine.Region ?? "",
                OriginalCableModel = original,
                BoqCableModel = boq,
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
                Changes = (previous.Changes ?? new List<FrameInfoJsonChange>())
                    .Select(CloneChange).ToList()
            };
            foreach (string field in TrackedFields)
                AddChange(previous, next, field, commandName);
            if (next.Changes.Count > 200)
                next.Changes = next.Changes.Skip(next.Changes.Count - 200).ToList();
            return next;
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
                case "LastModifiedUser": return record.LastModifiedUser ?? "";
                default: return "";
            }
        }

        private static string FirstNonEmpty(params string[] values)
            => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }
}
