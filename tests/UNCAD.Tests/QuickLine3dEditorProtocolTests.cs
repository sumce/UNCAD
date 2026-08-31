using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using UNCAD.Core.QuickLine;
using UNCAD.UI;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLine3dEditorProtocolTests
    {
        [Fact]
        public void InitializationJson_UsesVersionedCamelCaseEnvelope()
        {
            var protocol = new QuickLine3dEditorProtocol(Scene());
            var json = new JavaScriptSerializer().DeserializeObject(
                protocol.InitializationJson) as Dictionary<string, object>;

            Assert.NotNull(json);
            Assert.Equal("initialize", json["type"]);
            Assert.Equal(QuickLine3dEditorProtocol.SchemaVersion,
                Convert.ToInt32(json["schemaVersion"]));
            Assert.Equal(protocol.SessionId, json["sessionId"]);
            var scene = Assert.IsType<Dictionary<string, object>>(json["scene"]);
            Assert.False(string.IsNullOrWhiteSpace((string)scene["rootNodeId"]));
            Assert.Equal("Isometric", scene["projectionMode"]);
            var segments = Assert.IsType<object[]>(scene["segments"]);
            var segment = Assert.IsType<Dictionary<string, object>>(segments[0]);
            Assert.Equal("A", segment["id"]);
            Assert.Equal("X", segment["axis"]);
            Assert.Equal(1250d, Convert.ToDouble(segment["distanceMm"]));
        }

        [Fact]
        public void ReadyThenCommit_AcceptsKnownUniqueFiniteUpdates()
        {
            var protocol = new QuickLine3dEditorProtocol(Scene());
            Assert.True(protocol.TryAccept(
                "{\"type\":\"ready\",\"schemaVersion\":1}",
                out QuickLine3dEditorProtocolMessage ready, out string readyError),
                readyError);
            string commit = "{\"type\":\"commit\",\"schemaVersion\":1,"
                + "\"sessionId\":\"" + protocol.SessionId + "\","
                + "\"revision\":1,\"updates\":[{\"segmentId\":\"A\","
                + "\"distanceMm\":2300.5}]}";

            Assert.True(protocol.TryAccept(commit,
                out QuickLine3dEditorProtocolMessage accepted, out string error), error);
            Assert.Equal(QuickLine3dEditorMessageKind.Ready, ready.Kind);
            Assert.Equal(QuickLine3dEditorMessageKind.Commit, accepted.Kind);
            Assert.Equal(1, accepted.Revision);
            Assert.Equal(2300.5, accepted.Updates["A"]);
            Assert.True(protocol.IsTerminal);
        }

        [Theory]
        [InlineData("{\"segmentId\":\"UNKNOWN\",\"distanceMm\":10}")]
        [InlineData("{\"segmentId\":\"A\",\"distanceMm\":-1}")]
        [InlineData("{\"segmentId\":\"A\",\"distanceMm\":\"10\"}")]
        public void Commit_RejectsUnknownOrInvalidDistanceUpdate(string update)
        {
            var protocol = ReadyProtocol();
            string commit = Commit(protocol, 1, update);

            Assert.False(protocol.TryAccept(commit, out _, out string error));
            Assert.NotEmpty(error);
            Assert.False(protocol.IsTerminal);
        }

        [Fact]
        public void Commit_RejectsDuplicateSegmentAndStaleRevision()
        {
            var duplicate = ReadyProtocol();
            string update = "{\"segmentId\":\"A\",\"distanceMm\":10}";
            Assert.False(duplicate.TryAccept(
                Commit(duplicate, 1, update + "," + update), out _, out _));

            var stale = ReadyProtocol();
            Assert.False(stale.TryAccept(Commit(stale, 0, update), out _, out _));
        }

        [Fact]
        public void Commit_RejectsWrongSchemaSessionAndMessagesBeforeReady()
        {
            var beforeReady = new QuickLine3dEditorProtocol(Scene());
            string validUpdate = "{\"segmentId\":\"A\",\"distanceMm\":10}";
            Assert.False(beforeReady.TryAccept(
                Commit(beforeReady, 1, validUpdate), out _, out _));

            var wrongSession = ReadyProtocol();
            string message = Commit(wrongSession, 1, validUpdate)
                .Replace(wrongSession.SessionId, "wrong-session");
            Assert.False(wrongSession.TryAccept(message, out _, out _));

            var wrongSchema = new QuickLine3dEditorProtocol(Scene());
            Assert.False(wrongSchema.TryAccept(
                "{\"type\":\"ready\",\"schemaVersion\":2}", out _, out _));
        }

        [Fact]
        public void Cancel_RequiresMatchingSessionAndEndsSession()
        {
            var protocol = ReadyProtocol();
            string cancel = "{\"type\":\"cancel\",\"schemaVersion\":1,"
                + "\"sessionId\":\"" + protocol.SessionId + "\"}";

            Assert.True(protocol.TryAccept(cancel,
                out QuickLine3dEditorProtocolMessage accepted, out string error), error);
            Assert.Equal(QuickLine3dEditorMessageKind.Cancel, accepted.Kind);
            Assert.True(protocol.IsTerminal);
            Assert.False(protocol.TryAccept(cancel, out _, out _));
        }

        private static QuickLine3dEditorProtocol ReadyProtocol()
        {
            var protocol = new QuickLine3dEditorProtocol(Scene());
            Assert.True(protocol.TryAccept(
                "{\"type\":\"ready\",\"schemaVersion\":1}", out _, out _));
            return protocol;
        }

        private static string Commit(QuickLine3dEditorProtocol protocol,
            int revision, string updates)
            => "{\"type\":\"commit\",\"schemaVersion\":1,"
                + "\"sessionId\":\"" + protocol.SessionId + "\","
                + "\"revision\":" + revision + ",\"updates\":[" + updates + "]}";

        private static QuickLineIsometricScene Scene()
        {
            double radians = Math.PI / 6.0;
            var segment = new QuickLineSegment("A", new QuickLinePoint(0, 0),
                new QuickLinePoint(Math.Cos(radians) * 100,
                    Math.Sin(radians) * 100));
            return QuickLineIsometricSceneBuilder.Build(
                QuickLineGraph.Build(new[] { segment }), "A",
                new Dictionary<string, double>(StringComparer.Ordinal)
                {
                    ["A"] = 1250
                });
        }
    }
}
