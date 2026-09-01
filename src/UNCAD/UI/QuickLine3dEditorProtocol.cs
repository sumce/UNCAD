using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Web.Script.Serialization;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI
{
    public enum QuickLine3dEditorMessageKind
    {
        Ready,
        Commit,
        Cancel,
        RenderError
    }

    public sealed class QuickLine3dEditorProtocolMessage
    {
        internal QuickLine3dEditorProtocolMessage(
            QuickLine3dEditorMessageKind kind,
            int revision,
            IReadOnlyDictionary<string, double> updates,
            IReadOnlyList<QuickLineCreatedSegment> createdSegments,
            string errorMessage)
        {
            Kind = kind;
            Revision = revision;
            Updates = updates;
            CreatedSegments = createdSegments ?? Array.Empty<QuickLineCreatedSegment>();
            ErrorMessage = errorMessage ?? "";
        }

        public QuickLine3dEditorMessageKind Kind { get; }
        public int Revision { get; }
        public IReadOnlyDictionary<string, double> Updates { get; }
        public IReadOnlyList<QuickLineCreatedSegment> CreatedSegments { get; }
        public string ErrorMessage { get; }
    }

    /// <summary>
    /// Builds the immutable browser snapshot and validates every message that
    /// can cross the WebView2 boundary. It deliberately has no WebView2 or CAD
    /// dependency so the security-sensitive contract can be unit tested.
    /// </summary>
    public sealed class QuickLine3dEditorProtocol
    {
        public const int SchemaVersion = 1;
        private const int MaxMessageCharacters = 4 * 1024 * 1024;
        private const int MaxIdentifierCharacters = 256;
        private const int MaxErrorCharacters = 4096;
        private const int MaxCreatedSegments = 10000;

        private static readonly IReadOnlyDictionary<string, double> EmptyUpdates =
            new ReadOnlyDictionary<string, double>(
                new Dictionary<string, double>(StringComparer.Ordinal));

        private readonly JavaScriptSerializer _json = new JavaScriptSerializer
        {
            MaxJsonLength = 16 * 1024 * 1024,
            RecursionLimit = 64
        };
        private readonly HashSet<string> _segmentIds;
        private bool _readyAccepted;
        private bool _terminalAccepted;

        public QuickLine3dEditorProtocol(QuickLineIsometricScene scene)
        {
            ValidateScene(scene);
            SessionId = Guid.NewGuid().ToString("N");
            InitialRevision = 0;
            _segmentIds = new HashSet<string>(
                scene.Segments.Select(item => item.Id), StringComparer.Ordinal);
            InitializationJson = _json.Serialize(BuildInitialization(scene));
        }

        public string SessionId { get; }
        public int InitialRevision { get; }
        public string InitializationJson { get; }
        public bool IsReady => _readyAccepted;
        public bool IsTerminal => _terminalAccepted;

        public bool TryAccept(string messageJson,
            out QuickLine3dEditorProtocolMessage message, out string error)
        {
            message = null;
            error = "";
            if (_terminalAccepted)
                return Fail("编辑会话已经结束。", out error);
            if (string.IsNullOrWhiteSpace(messageJson))
                return Fail("浏览器消息为空。", out error);
            if (messageJson.Length > MaxMessageCharacters)
                return Fail("浏览器消息超过允许大小。", out error);

            Dictionary<string, object> values;
            try
            {
                values = _json.DeserializeObject(messageJson)
                    as Dictionary<string, object>;
            }
            catch (ArgumentException)
            {
                return Fail("浏览器消息不是有效 JSON。", out error);
            }
            catch (InvalidOperationException)
            {
                return Fail("浏览器消息结构无效。", out error);
            }

            if (values == null)
                return Fail("浏览器消息必须是 JSON 对象。", out error);
            if (!TryReadString(values, "type", MaxIdentifierCharacters,
                    out string type))
                return Fail("浏览器消息缺少有效 type。", out error);
            if (!TryReadInteger(values, "schemaVersion", out int schemaVersion)
                || schemaVersion != SchemaVersion)
                return Fail("浏览器消息协议版本不受支持。", out error);

            switch (type)
            {
                case "ready":
                    return AcceptReady(out message, out error);
                case "commit":
                    return AcceptCommit(values, out message, out error);
                case "cancel":
                    return AcceptCancel(values, out message, out error);
                case "renderError":
                    return AcceptRenderError(values, out message, out error);
                default:
                    return Fail("浏览器消息 type 不受支持。", out error);
            }
        }

        private bool AcceptReady(out QuickLine3dEditorProtocolMessage message,
            out string error)
        {
            message = null;
            error = "";
            if (_readyAccepted)
                return Fail("浏览器重复发送 ready。", out error);
            _readyAccepted = true;
            message = new QuickLine3dEditorProtocolMessage(
                QuickLine3dEditorMessageKind.Ready, InitialRevision,
                EmptyUpdates, Array.Empty<QuickLineCreatedSegment>(), "");
            return true;
        }

        private bool AcceptCommit(IDictionary<string, object> values,
            out QuickLine3dEditorProtocolMessage message, out string error)
        {
            message = null;
            error = "";
            if (!_readyAccepted)
                return Fail("浏览器尚未完成 ready 握手。", out error);
            if (!HasExpectedSession(values))
                return Fail("浏览器提交的 sessionId 不匹配。", out error);
            if (!TryReadInteger(values, "revision", out int revision)
                || revision < InitialRevision)
                return Fail("浏览器提交的 revision 无效。", out error);
            bool drawingCommit = values.TryGetValue("segments", out object rawSegments);
            if (drawingCommit)
                return AcceptCreatedSegments(rawSegments, revision,
                    out message, out error);
            if (!values.TryGetValue("updates", out object rawUpdates)
                || !(rawUpdates is IList updates))
                return Fail("浏览器提交缺少 updates 数组。", out error);
            if (updates.Count > _segmentIds.Count)
                return Fail("浏览器提交的更新数量超过线段数量。", out error);

            var accepted = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (object rawUpdate in updates)
            {
                var update = rawUpdate as Dictionary<string, object>;
                if (update == null)
                    return Fail("浏览器提交包含无效更新项。", out error);
                if (!TryReadString(update, "segmentId", MaxIdentifierCharacters,
                        out string segmentId)
                    || !_segmentIds.Contains(segmentId))
                    return Fail("浏览器提交包含未知线段。", out error);
                if (accepted.ContainsKey(segmentId))
                    return Fail("浏览器提交包含重复线段。", out error);
                if (!TryReadNumber(update, "distanceMm", out double distance)
                    || !IsFinite(distance) || distance < 0.0)
                    return Fail("浏览器提交包含无效毫米距离。", out error);
                accepted.Add(segmentId, distance);
            }

            if (accepted.Count > 0 && revision <= InitialRevision)
                return Fail("包含更新的提交必须提高 revision。", out error);

            _terminalAccepted = true;
            var readOnly = new ReadOnlyDictionary<string, double>(accepted);
            message = new QuickLine3dEditorProtocolMessage(
                QuickLine3dEditorMessageKind.Commit, revision, readOnly,
                Array.Empty<QuickLineCreatedSegment>(), "");
            return true;
        }

        private bool AcceptCreatedSegments(object rawSegments, int revision,
            out QuickLine3dEditorProtocolMessage message, out string error)
        {
            message = null;
            error = "";
            if (!(rawSegments is IList segments)
                || segments.Count == 0 || segments.Count > MaxCreatedSegments)
                return Fail("浏览器提交必须包含 1 至 " + MaxCreatedSegments
                    + " 根新建线段。", out error);

            var accepted = new List<QuickLineCreatedSegment>(segments.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var edges = new List<Tuple<string, string>>();
            foreach (object raw in segments)
            {
                var value = raw as Dictionary<string, object>;
                if (value == null
                    || !TryReadString(value, "id", MaxIdentifierCharacters,
                        out string id)
                    || !TryReadString(value, "startNodeId", MaxIdentifierCharacters,
                        out string start)
                    || !TryReadString(value, "endNodeId", MaxIdentifierCharacters,
                        out string end)
                    || !TryReadString(value, "axis", 8, out string axisText)
                    || !TryReadInteger(value, "directionSign", out int sign)
                    || !TryReadNumber(value, "distanceMm", out double distance)
                    || !ids.Add(id) || string.Equals(start, end,
                        StringComparison.Ordinal)
                    || (sign != -1 && sign != 1)
                    || !IsFinite(distance) || distance <= 0.0)
                    return Fail("浏览器提交包含无效的新建线段。", out error);
                if (!Enum.TryParse(axisText, true, out QuickLineSpatialAxis axis)
                    || !Enum.IsDefined(typeof(QuickLineSpatialAxis), axis))
                    return Fail("浏览器提交包含无效的空间轴。", out error);
                nodes.Add(start);
                nodes.Add(end);
                edges.Add(Tuple.Create(start, end));
                accepted.Add(new QuickLineCreatedSegment(id, start, end,
                    axis, sign, distance));
            }

            if (!nodes.Contains("N1") || !IsConnected("N1", edges))
                return Fail("浏览器提交的新建线段必须从 N1 开始且保持连通。", out error);
            if (revision <= InitialRevision)
                return Fail("包含新建线段的提交必须提高 revision。", out error);

            _terminalAccepted = true;
            message = new QuickLine3dEditorProtocolMessage(
                QuickLine3dEditorMessageKind.Commit, revision, EmptyUpdates,
                new ReadOnlyCollection<QuickLineCreatedSegment>(accepted), "");
            return true;
        }

        private static bool IsConnected(string root,
            IEnumerable<Tuple<string, string>> edges)
        {
            var adjacency = new Dictionary<string, List<string>>(
                StringComparer.Ordinal);
            foreach (Tuple<string, string> edge in edges)
            {
                if (!adjacency.ContainsKey(edge.Item1))
                    adjacency[edge.Item1] = new List<string>();
                if (!adjacency.ContainsKey(edge.Item2))
                    adjacency[edge.Item2] = new List<string>();
                adjacency[edge.Item1].Add(edge.Item2);
                adjacency[edge.Item2].Add(edge.Item1);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal) { root };
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out List<string> next)) continue;
                foreach (string id in next)
                    if (seen.Add(id)) queue.Enqueue(id);
            }
            return adjacency.Keys.All(seen.Contains);
        }

        private bool AcceptCancel(IDictionary<string, object> values,
            out QuickLine3dEditorProtocolMessage message, out string error)
        {
            message = null;
            error = "";
            if (!_readyAccepted)
                return Fail("浏览器尚未完成 ready 握手。", out error);
            if (!HasExpectedSession(values))
                return Fail("浏览器取消消息的 sessionId 不匹配。", out error);

            _terminalAccepted = true;
            message = new QuickLine3dEditorProtocolMessage(
                QuickLine3dEditorMessageKind.Cancel, InitialRevision,
                EmptyUpdates, Array.Empty<QuickLineCreatedSegment>(), "");
            return true;
        }

        private bool AcceptRenderError(IDictionary<string, object> values,
            out QuickLine3dEditorProtocolMessage message, out string error)
        {
            message = null;
            error = "";
            if (!TryReadString(values, "message", MaxErrorCharacters,
                    out string renderError))
                return Fail("浏览器渲染错误消息无效。", out error);
            if (values.ContainsKey("sessionId") && !HasExpectedSession(values))
                return Fail("浏览器渲染错误的 sessionId 不匹配。", out error);

            _terminalAccepted = true;
            message = new QuickLine3dEditorProtocolMessage(
                QuickLine3dEditorMessageKind.RenderError, InitialRevision,
                EmptyUpdates, Array.Empty<QuickLineCreatedSegment>(), renderError);
            return true;
        }

        private bool HasExpectedSession(IDictionary<string, object> values)
            => TryReadString(values, "sessionId", MaxIdentifierCharacters,
                    out string sessionId)
                && string.Equals(sessionId, SessionId, StringComparison.Ordinal);

        private Dictionary<string, object> BuildInitialization(
            QuickLineIsometricScene scene)
        {
            var nodes = scene.Nodes.Select(item => (object)new Dictionary<string, object>
            {
                ["id"] = item.Id,
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = item.Position.X,
                    ["y"] = item.Position.Y,
                    ["z"] = item.Position.Z
                }
            }).ToArray();
            var segments = scene.Segments.Select(item =>
                (object)new Dictionary<string, object>
                {
                    ["id"] = item.Id,
                    ["startNodeId"] = item.StartNodeId,
                    ["endNodeId"] = item.EndNodeId,
                    ["axis"] = item.Axis.ToString(),
                    ["directionSign"] = item.DirectionSign,
                    ["planAngleDegrees"] = item.PlanAngleDegrees,
                    ["distanceMm"] = item.DistanceMillimetres,
                    ["displayDistanceMm"] = item.DisplayDistanceMillimetres,
                    ["completed"] = item.Completed
                }).ToArray();
            var sceneValue = new Dictionary<string, object>
            {
                ["rootNodeId"] = scene.RootNodeId,
                ["projectionMode"] = scene.ProjectionMode.ToString(),
                ["nodes"] = nodes,
                ["segments"] = segments,
                ["diagnostics"] = scene.Diagnostics.ToArray()
            };
            var result = new Dictionary<string, object>
            {
                ["type"] = "initialize",
                ["schemaVersion"] = SchemaVersion,
                ["sessionId"] = SessionId,
                ["revision"] = InitialRevision,
                ["scene"] = sceneValue
            };
            result["mode"] = scene.Segments.Count == 0 ? "draw" : "edit";
            return result;
        }

        private static void ValidateScene(QuickLineIsometricScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (scene.ProjectionMode != QuickLineProjectionMode.Isometric
                && scene.ProjectionMode != QuickLineProjectionMode.Orthographic)
                throw new ArgumentException("3D 场景投影模式无效。", nameof(scene));
            if (scene.Segments == null)
                throw new ArgumentException("3D 场景线段集合无效。", nameof(scene));
            ValidateIdentifier(scene.RootNodeId, "根节点");

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuickLineIsometricNode node in scene.Nodes)
            {
                if (node == null)
                    throw new ArgumentException("3D 场景包含空节点。", nameof(scene));
                ValidateIdentifier(node.Id, "节点");
                if (!nodeIds.Add(node.Id))
                    throw new ArgumentException("3D 场景包含重复节点。", nameof(scene));
                if (!IsFinite(node.Position.X) || !IsFinite(node.Position.Y)
                    || !IsFinite(node.Position.Z))
                    throw new ArgumentException("3D 场景节点坐标无效。", nameof(scene));
            }
            if (!nodeIds.Contains(scene.RootNodeId))
                throw new ArgumentException("3D 场景缺少根节点。", nameof(scene));

            var segmentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (QuickLineIsometricSegment segment in scene.Segments)
            {
                if (segment == null)
                    throw new ArgumentException("3D 场景包含空线段。", nameof(scene));
                ValidateIdentifier(segment.Id, "线段");
                if (!segmentIds.Add(segment.Id))
                    throw new ArgumentException("3D 场景包含重复线段。", nameof(scene));
                if (!nodeIds.Contains(segment.StartNodeId)
                    || !nodeIds.Contains(segment.EndNodeId))
                    throw new ArgumentException("3D 场景线段引用未知节点。", nameof(scene));
                if (!Enum.IsDefined(typeof(QuickLineSpatialAxis), segment.Axis)
                    || (segment.DirectionSign != -1 && segment.DirectionSign != 1)
                    || !IsFinite(segment.PlanAngleDegrees)
                    || !IsFinite(segment.DistanceMillimetres)
                    || segment.DistanceMillimetres < 0.0
                    || !IsFinite(segment.DisplayDistanceMillimetres)
                    || segment.DisplayDistanceMillimetres < 0.0)
                    throw new ArgumentException("3D 场景线段参数无效。", nameof(scene));
            }
        }

        private static void ValidateIdentifier(string value, string field)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Length > MaxIdentifierCharacters)
                throw new ArgumentException(field + " ID 无效。", "scene");
        }

        private static bool TryReadString(IDictionary<string, object> values,
            string key, int maxLength, out string result)
        {
            result = "";
            if (!values.TryGetValue(key, out object raw) || !(raw is string value)
                || string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
                return false;
            result = value;
            return true;
        }

        private static bool TryReadInteger(IDictionary<string, object> values,
            string key, out int result)
        {
            result = 0;
            if (!values.TryGetValue(key, out object raw)
                || !TryConvertNumber(raw, out double number)
                || !IsFinite(number) || Math.Truncate(number) != number
                || number < int.MinValue || number > int.MaxValue)
                return false;
            result = (int)number;
            return true;
        }

        private static bool TryReadNumber(IDictionary<string, object> values,
            string key, out double result)
        {
            result = 0.0;
            return values.TryGetValue(key, out object raw)
                && TryConvertNumber(raw, out result);
        }

        private static bool TryConvertNumber(object value, out double result)
        {
            result = 0.0;
            if (value == null || value is bool || value is char || value is string)
                return false;
            TypeCode code = Type.GetTypeCode(value.GetType());
            switch (code)
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    try
                    {
                        result = Convert.ToDouble(value,
                            System.Globalization.CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch (OverflowException)
                    {
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }

        private static bool IsFinite(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
