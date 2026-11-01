using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using UNCAD.Core.Fill;
using Xunit;

namespace UNCAD.Tests
{
    public sealed class FrameInfoJsonCodecTests
    {
        [Fact]
        public void RoundTrip_PreservesIdentityCableOverrideAndHistory()
        {
            var source = new FrameInfoJsonRecord
            {
                MachineId = "M01",
                DeviceName = "插座01",
                OriginalCableModel = "OLD",
                BoqCableModel = "NEW",
                LastModifiedUtc = "2026-09-02T08:00:00Z",
                LastModifiedUser = "tester"
            };
            source.Changes.Add(new FrameInfoJsonChange
            {
                Field = "BoqCableModel",
                Before = "OLD",
                After = "NEW",
                Command = "U1U"
            });

            FrameInfoJsonRecord actual = FrameInfoJsonCodec.Parse(
                FrameInfoJsonCodec.Serialize(source));

            Assert.NotNull(actual);
            Assert.Equal("2", actual.SchemaVersion);
            Assert.Equal("M01", actual.MachineId);
            Assert.Equal("插座01", actual.DeviceName);
            Assert.Equal("OLD", actual.OriginalCableModel);
            Assert.Equal("NEW", actual.BoqCableModel);
            Assert.Equal("tester", actual.LastModifiedUser);
            Assert.Equal("BoqCableModel", Assert.Single(actual.Changes).Field);
        }

        [Fact]
        public void Parse_AcceptsLegacyCaseAndRejectsDamagedPayload()
        {
            FrameInfoJsonRecord legacy = FrameInfoJsonCodec.Parse(
                "{\"MachineId\":\"M02\",\"DeviceName\":\"回路A\"}");

            Assert.NotNull(legacy);
            Assert.Equal("M02", legacy.MachineId);
            Assert.Equal("回路A", legacy.DeviceName);
            Assert.Null(FrameInfoJsonCodec.Parse("{bad json"));
            Assert.Null(FrameInfoJsonCodec.Parse("not json"));
        }

        [Fact]
        public void Serialize_UsesV2NestedShapeAndEmitsEveryKey()
        {
            string json = FrameInfoJsonCodec.Serialize(new FrameInfoJsonRecord());

            Assert.True(json.Length <= FrameInfoJsonCodec.MaxPayloadLength);
            var root = (IDictionary<string, object>)new JavaScriptSerializer()
                .DeserializeObject(json);
            Assert.Equal(new[]
            {
                "cable", "changes", "downstream", "drawing", "identity", "meta",
                "schemaVersion", "upstream"
            }, root.Keys.Cast<string>().OrderBy(key => key).ToArray());
            Assert.Equal("2", root["schemaVersion"]);

            AssertAllEmptyStrings(root, "identity", "machineId", "deviceName", "region");
            AssertAllEmptyStrings(root, "upstream", "type", "fr", "axis", "floor", "switch");
            AssertAllEmptyStrings(root, "downstream", "axis", "floor");
            AssertAllEmptyStrings(root, "cable", "original", "boq", "busPlugBoxCode",
                "measuredM", "panelReserveMm", "deviceReserveMm", "totalM");
            AssertAllEmptyStrings(root, "drawing", "detail", "seq", "hoseDiameter");
            AssertAllEmptyStrings(root, "meta", "updatedUtc", "updatedBy");
            var changes = Assert.IsAssignableFrom<IEnumerable>(root["changes"]);
            Assert.Empty(changes);
        }

        [Fact]
        public void RoundTrip_PreservesCableReserveFields()
        {
            var source = new FrameInfoJsonRecord
            {
                MachineId = "M03",
                MeasuredCableM = "10",
                PanelReserveMm = "1200",
                DeviceReserveMm = "500",
                TotalCableM = "11.7"
            };

            FrameInfoJsonRecord actual = FrameInfoJsonCodec.Parse(
                FrameInfoJsonCodec.Serialize(source));

            Assert.NotNull(actual);
            Assert.Equal("10", actual.MeasuredCableM);
            Assert.Equal("1200", actual.PanelReserveMm);
            Assert.Equal("500", actual.DeviceReserveMm);
            Assert.Equal("11.7", actual.TotalCableM);
        }

        [Fact]
        public void Parse_ReadsLegacyV1FlatPayloadWithLegacyChangeKeys()
        {
            const string v1 = "{\"schemaVersion\":\"1\",\"machineId\":\"M9\","
                + "\"deviceName\":\"回路X\",\"next\":\"插座盘\",\"fr\":\"FR1\","
                + "\"upstreamAxis\":\"U1\",\"downstreamAxis\":\"D1\","
                + "\"deviceFloor\":\"F1\",\"panelFloor\":\"F2\","
                + "\"facilitySwitch\":\"SW\",\"originalCableModel\":\"OLD\","
                + "\"boqCableModel\":\"NEW\",\"boqBusPlugBoxCode\":\"5.6\","
                + "\"detail\":\"N208\",\"seq\":\"2\",\"hoseDiameter\":\"32\","
                + "\"lastModifiedUtc\":\"2026-09-02T08:00:00Z\","
                + "\"lastModifiedUser\":\"u\","
                + "\"changes\":[{\"timestampUtc\":\"t1\",\"command\":\"U1U\","
                + "\"field\":\"MachineId\",\"before\":\"A\",\"after\":\"B\","
                + "\"note\":\"n\"}]}";

            FrameInfoJsonRecord actual = FrameInfoJsonCodec.Parse(v1);

            Assert.NotNull(actual);
            Assert.Equal("1", actual.SchemaVersion);
            Assert.Equal("M9", actual.MachineId);
            Assert.Equal("回路X", actual.DeviceName);
            Assert.Equal("插座盘", actual.Next);
            Assert.Equal("FR1", actual.Fr);
            Assert.Equal("U1", actual.UpstreamAxis);
            Assert.Equal("D1", actual.DownstreamAxis);
            Assert.Equal("F1", actual.DeviceFloor);
            Assert.Equal("F2", actual.PanelFloor);
            Assert.Equal("SW", actual.FacilitySwitch);
            Assert.Equal("OLD", actual.OriginalCableModel);
            Assert.Equal("NEW", actual.BoqCableModel);
            Assert.Equal("5.6", actual.BoqBusPlugBoxCode);
            Assert.Equal("N208", actual.Detail);
            Assert.Equal("32", actual.HoseDiameter);
            FrameInfoJsonChange change = Assert.Single(actual.Changes);
            Assert.Equal("t1", change.TimestampUtc);
            Assert.Equal("U1U", change.Command);
            Assert.Equal("MachineId", change.Field);
            Assert.Equal("A", change.Before);
            Assert.Equal("B", change.After);
            Assert.Equal("n", change.Note);
        }

        [Fact]
        public void Serialize_CapsHistoryAndTrimsPayloadUnderLimit()
        {
            var source = new FrameInfoJsonRecord { MachineId = "BIG" };
            for (int index = 0; index < 40; index++)
            {
                source.Changes.Add(new FrameInfoJsonChange
                {
                    TimestampUtc = "2026-09-02T08:00:00Z",
                    Command = "U1U",
                    Field = "Field" + index,
                    Before = new string('a', 200),
                    After = new string('b', 200)
                });
            }

            string json = FrameInfoJsonCodec.Serialize(source);

            Assert.True(json.Length <= FrameInfoJsonCodec.MaxPayloadLength,
                "payload length was " + json.Length);
            FrameInfoJsonRecord parsed = FrameInfoJsonCodec.Parse(json);
            Assert.NotNull(parsed);
            Assert.InRange(parsed.Changes.Count, 1, FrameInfoJsonCodec.MaxChanges);
            Assert.Equal("Field39", parsed.Changes.Last().Field);
            Assert.Equal("BIG", parsed.MachineId);
        }

        [Fact]
        public void Serialize_KeepsOnlyNewestTwentyHistoryEntriesWhenItFits()
        {
            var source = new FrameInfoJsonRecord { MachineId = "M" };
            for (int index = 0; index < 25; index++)
                source.Changes.Add(new FrameInfoJsonChange { Field = "F" + index });

            FrameInfoJsonRecord parsed = FrameInfoJsonCodec.Parse(
                FrameInfoJsonCodec.Serialize(source));

            Assert.Equal(FrameInfoJsonCodec.MaxChanges, parsed.Changes.Count);
            Assert.Equal("F5", parsed.Changes[0].Field);
            Assert.Equal("F24", parsed.Changes[parsed.Changes.Count - 1].Field);
        }

        [Fact]
        public void Serialize_ThrowsWhenNonHistoryFieldsExceedLimit()
        {
            var source = new FrameInfoJsonRecord
            {
                MachineId = "M",
                DeviceName = new string('x', FrameInfoJsonCodec.MaxPayloadLength + 1000)
            };

            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => FrameInfoJsonCodec.Serialize(source));
            Assert.Contains("6000", error.Message);
        }

        private static void AssertAllEmptyStrings(IDictionary<string, object> root,
            string sectionName, params string[] expectedKeys)
        {
            var section = (IDictionary<string, object>)root[sectionName];
            Assert.NotNull(section);
            Assert.Equal(expectedKeys.OrderBy(key => key).ToArray(),
                section.Keys.Cast<string>().OrderBy(key => key).ToArray());
            foreach (string key in expectedKeys)
                Assert.Equal("", section[key]);
        }
    }
}
