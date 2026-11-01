using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace UNCAD.Core.Fill
{
    /// <summary>
    /// Serializes/parses the persisted <c>frameinfo_json</c> payload.
    /// <para>
    /// Schema v2 is a compact nested document:
    /// <c>
    /// {"schemaVersion":"2",
    ///  "identity":{"machineId","deviceName","region"},
    ///  "upstream":{"type","fr","axis","floor","switch"},
    ///  "downstream":{"axis","floor"},
    ///  "cable":{"original","boq","busPlugBoxCode","measuredM","panelReserveMm",
    ///           "deviceReserveMm","totalM"},
    ///  "drawing":{"detail","seq","hoseDiameter"},
    ///  "meta":{"updatedUtc","updatedBy"},
    ///  "changes":[{"ts","cmd","field","from","to"}]}
    /// </c>
    /// Every key is always emitted (empty strings instead of nulls), the history is
    /// capped at <see cref="MaxChanges"/> newest entries, and the payload length is
    /// bounded by <see cref="MaxPayloadLength"/> characters: oldest history entries
    /// are dropped first; if the document is still too long an
    /// <see cref="InvalidDataException"/> is thrown instead of writing an oversized
    /// attribute (the old v1 history grew to 23 KB).
    /// </para>
    /// <para>
    /// <see cref="Parse"/> also accepts legacy v1 flat payloads (including the old
    /// <c>timestampUtc/command/before/after/note</c> change keys) so existing
    /// drawings can be migrated on the next U1F/U1U write.
    /// </para>
    /// </summary>
    public static class FrameInfoJsonCodec
    {
        public const string CurrentSchemaVersion = "2";
        public const int MaxChanges = 20;
        public const int MaxPayloadLength = 6000;

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public static string Serialize(FrameInfoJsonRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            List<FrameInfoJsonChange> changes = (record.Changes
                    ?? new List<FrameInfoJsonChange>())
                .Where(change => change != null)
                .ToList();
            if (changes.Count > MaxChanges)
                changes = changes.Skip(changes.Count - MaxChanges).ToList();

            // Trim the oldest history entries until the payload fits. The non-history
            // fields are fixed; if they alone exceed the limit, fail loudly.
            while (true)
            {
                string payload = Json.Serialize(BuildPayload(record, changes));
                if (payload.Length <= MaxPayloadLength) return payload;
                if (changes.Count == 0)
                {
                    throw new InvalidDataException(
                        "frameinfo_json v2 序列化后长度为 " + payload.Length
                        + " 字符，超过 " + MaxPayloadLength
                        + " 字符上限；请精简超长字段后重试。");
                }
                changes.RemoveAt(0);
            }
        }

        public static FrameInfoJsonRecord Parse(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Length == 0 || text[0] != '{') return null;
            try
            {
                var root = Json.DeserializeObject(text) as IDictionary<string, object>;
                if (root == null) return null;
                return IsV2(root) ? ParseV2(root) : ParseV1(root);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ v2 build

        private static Dictionary<string, object> BuildPayload(
            FrameInfoJsonRecord record, List<FrameInfoJsonChange> changes)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["schemaVersion"] = CurrentSchemaVersion,
                ["identity"] = Section(
                    ("machineId", Text(record.MachineId)),
                    ("deviceName", Text(record.DeviceName)),
                    ("region", Text(record.Region))),
                ["upstream"] = Section(
                    ("type", Text(record.Next)),
                    ("fr", Text(record.Fr)),
                    ("axis", Text(record.UpstreamAxis)),
                    ("floor", Text(record.DeviceFloor)),
                    ("switch", Text(record.FacilitySwitch))),
                ["downstream"] = Section(
                    ("axis", Text(record.DownstreamAxis)),
                    ("floor", Text(record.PanelFloor))),
                ["cable"] = Section(
                    ("original", Text(record.OriginalCableModel)),
                    ("boq", Text(record.BoqCableModel)),
                    ("busPlugBoxCode", Text(record.BoqBusPlugBoxCode)),
                    ("measuredM", Text(record.MeasuredCableM)),
                    ("panelReserveMm", Text(record.PanelReserveMm)),
                    ("deviceReserveMm", Text(record.DeviceReserveMm)),
                    ("totalM", Text(record.TotalCableM))),
                ["drawing"] = Section(
                    ("detail", Text(record.Detail)),
                    ("seq", Text(record.Seq)),
                    ("hoseDiameter", Text(record.HoseDiameter))),
                ["meta"] = Section(
                    ("updatedUtc", Text(record.LastModifiedUtc)),
                    ("updatedBy", Text(record.LastModifiedUser))),
                ["changes"] = changes.Select(change => Section(
                    ("ts", Text(change.TimestampUtc)),
                    ("cmd", Text(change.Command)),
                    ("field", Text(change.Field)),
                    ("from", Text(change.Before)),
                    ("to", Text(change.After)))).ToList()
            };
        }

        private static Dictionary<string, object> Section(
            params (string Key, string Value)[] pairs)
        {
            var section = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string key, string value) in pairs)
                section[key] = value;
            return section;
        }

        // ------------------------------------------------------------------ parse

        private static bool IsV2(IDictionary<string, object> root)
        {
            if (Raw(root, "identity") is IDictionary<string, object>) return true;
            return string.Equals(Get(root, "schemaVersion", ""), CurrentSchemaVersion,
                StringComparison.Ordinal);
        }

        private static FrameInfoJsonRecord ParseV2(IDictionary<string, object> root)
        {
            var identity = AsDictionary(Raw(root, "identity"));
            var upstream = AsDictionary(Raw(root, "upstream"));
            var downstream = AsDictionary(Raw(root, "downstream"));
            var cable = AsDictionary(Raw(root, "cable"));
            var drawing = AsDictionary(Raw(root, "drawing"));
            var meta = AsDictionary(Raw(root, "meta"));

            var record = new FrameInfoJsonRecord
            {
                SchemaVersion = CurrentSchemaVersion,
                MachineId = Get(identity, "machineId"),
                DeviceName = Get(identity, "deviceName"),
                Region = Get(identity, "region"),
                Next = Get(upstream, "type"),
                Fr = Get(upstream, "fr"),
                UpstreamAxis = Get(upstream, "axis"),
                DeviceFloor = Get(upstream, "floor"),
                FacilitySwitch = Get(upstream, "switch"),
                DownstreamAxis = Get(downstream, "axis"),
                PanelFloor = Get(downstream, "floor"),
                OriginalCableModel = Get(cable, "original"),
                BoqCableModel = Get(cable, "boq"),
                BoqBusPlugBoxCode = Get(cable, "busPlugBoxCode"),
                MeasuredCableM = Get(cable, "measuredM"),
                PanelReserveMm = Get(cable, "panelReserveMm"),
                DeviceReserveMm = Get(cable, "deviceReserveMm"),
                TotalCableM = Get(cable, "totalM"),
                Detail = Get(drawing, "detail"),
                Seq = Get(drawing, "seq"),
                HoseDiameter = Get(drawing, "hoseDiameter"),
                LastModifiedUtc = Get(meta, "updatedUtc"),
                LastModifiedUser = Get(meta, "updatedBy")
            };
            ReadChanges(root, record);
            return record;
        }

        /// <summary>Legacy v1 flat payload (pre-v2 drawings must remain readable).</summary>
        private static FrameInfoJsonRecord ParseV1(IDictionary<string, object> root)
        {
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
                LastModifiedUser = Get(root, "lastModifiedUser"),
                MeasuredCableM = Get(root, "measuredCableM"),
                PanelReserveMm = Get(root, "panelReserveMm"),
                DeviceReserveMm = Get(root, "deviceReserveMm"),
                TotalCableM = Get(root, "totalCableM")
            };
            ReadChanges(root, record);
            return record;
        }

        private static void ReadChanges(IDictionary<string, object> root,
            FrameInfoJsonRecord record)
        {
            if (!(Raw(root, "changes") is IEnumerable entries)) return;
            foreach (object raw in entries)
            {
                if (!(raw is IDictionary<string, object> item)) continue;
                record.Changes.Add(new FrameInfoJsonChange
                {
                    // v2 uses short keys; v1 used timestampUtc/command/before/after/note.
                    TimestampUtc = FirstOf(item, "ts", "timestampUtc"),
                    Command = FirstOf(item, "cmd", "command"),
                    Field = Get(item, "field"),
                    Before = FirstOf(item, "from", "before"),
                    After = FirstOf(item, "to", "after"),
                    Note = Get(item, "note")
                });
            }
        }

        // ------------------------------------------------------------------ helpers

        private static IDictionary<string, object> AsDictionary(object value)
            => value as IDictionary<string, object>;

        private static object Raw(IDictionary<string, object> values, string name)
        {
            if (values == null) return null;
            foreach (var pair in values)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value;
            }
            return null;
        }

        private static string Get(IDictionary<string, object> values, string name,
            string fallback = "")
        {
            object value = Raw(values, name);
            if (value == null) return fallback;
            return Convert.ToString(value) ?? fallback;
        }

        private static string FirstOf(IDictionary<string, object> values,
            params string[] names)
        {
            foreach (string name in names)
            {
                object value = Raw(values, name);
                if (value == null) continue;
                string text = Convert.ToString(value);
                if (text != null) return text;
            }
            return "";
        }

        private static string Text(string value) => value ?? "";
    }
}
