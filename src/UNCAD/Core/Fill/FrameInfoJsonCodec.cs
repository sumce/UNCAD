using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace UNCAD.Core.Fill
{
    public static class FrameInfoJsonCodec
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static string Serialize(FrameInfoJsonRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var payload = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = record.SchemaVersion,
                ["machineId"] = record.MachineId,
                ["deviceName"] = record.DeviceName,
                ["region"] = record.Region,
                ["originalCableModel"] = record.OriginalCableModel,
                ["boqCableModel"] = record.BoqCableModel,
                ["boqBusPlugBoxCode"] = record.BoqBusPlugBoxCode,
                ["fr"] = record.Fr,
                ["detail"] = record.Detail,
                ["seq"] = record.Seq,
                ["hoseDiameter"] = record.HoseDiameter,
                ["next"] = record.Next,
                ["upstreamAxis"] = record.UpstreamAxis,
                ["downstreamAxis"] = record.DownstreamAxis,
                ["deviceFloor"] = record.DeviceFloor,
                ["panelFloor"] = record.PanelFloor,
                ["facilitySwitch"] = record.FacilitySwitch,
                ["lastModifiedUtc"] = record.LastModifiedUtc,
                ["lastModifiedUser"] = record.LastModifiedUser,
                ["changes"] = (record.Changes ?? new List<FrameInfoJsonChange>()).Select(change =>
                    new Dictionary<string, object>
                    {
                        ["timestampUtc"] = change.TimestampUtc,
                        ["command"] = change.Command,
                        ["field"] = change.Field,
                        ["before"] = change.Before,
                        ["after"] = change.After,
                        ["note"] = change.Note
                    }).ToList()
            };
            return Json.Serialize(payload);
        }

        public static FrameInfoJsonRecord Parse(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0 || text[0] != '{') return null;
            try
            {
                var root = Json.DeserializeObject(text) as IDictionary<string, object>;
                if (root == null) return null;
                var record = new FrameInfoJsonRecord
                {
                    SchemaVersion = Get(root, "schemaVersion", "1"),
                    MachineId = Get(root, "machineId"),
                    DeviceName = Get(root, "deviceName"),
                    Region = Get(root, "region"),
                    OriginalCableModel = Get(root, "originalCableModel"),
                    BoqCableModel = Get(root, "boqCableModel"),
                    BoqBusPlugBoxCode = Get(root, "boqBusPlugBoxCode"),
                    Fr = Get(root, "fr"),
                    Detail = Get(root, "detail"),
                    Seq = Get(root, "seq"),
                    HoseDiameter = Get(root, "hoseDiameter"),
                    Next = Get(root, "next"),
                    UpstreamAxis = Get(root, "upstreamAxis"),
                    DownstreamAxis = Get(root, "downstreamAxis"),
                    DeviceFloor = Get(root, "deviceFloor"),
                    PanelFloor = Get(root, "panelFloor"),
                    FacilitySwitch = Get(root, "facilitySwitch"),
                    LastModifiedUtc = Get(root, "lastModifiedUtc"),
                    LastModifiedUser = Get(root, "lastModifiedUser")
                };
                if (root.TryGetValue("changes", out object rawChanges)
                    && rawChanges is IEnumerable changes)
                {
                    foreach (object raw in changes)
                    {
                        if (!(raw is IDictionary<string, object> item)) continue;
                        record.Changes.Add(new FrameInfoJsonChange
                        {
                            TimestampUtc = Get(item, "timestampUtc"),
                            Command = Get(item, "command"),
                            Field = Get(item, "field"),
                            Before = Get(item, "before"),
                            After = Get(item, "after"),
                            Note = Get(item, "note")
                        });
                    }
                }
                return record;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Get(IDictionary<string, object> values,
            string name, string fallback = "")
        {
            object value = values.FirstOrDefault(pair =>
                string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
            return value == null ? fallback : Convert.ToString(value) ?? fallback;
        }
    }
}
