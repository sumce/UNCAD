using System;
using System.Collections.Generic;
using System.Linq;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI.QuickLine3d
{
    /// <summary>One editable route segment in the native 3D editor.</summary>
    public sealed class QuickLineMutableSegment
    {
        public QuickLineMutableSegment(string id, string startNodeId, string endNodeId,
            QuickLineSpatialAxis axis, int directionSign, double distanceMillimetres,
            double displayDistanceMillimetres, bool completed)
        {
            Id = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
            Axis = axis;
            DirectionSign = directionSign;
            DistanceMillimetres = distanceMillimetres;
            DisplayDistanceMillimetres = displayDistanceMillimetres;
            Completed = completed;
        }

        public string Id { get; }
        public string StartNodeId { get; }
        public string EndNodeId { get; }
        public QuickLineSpatialAxis Axis { get; }
        public int DirectionSign { get; }
        /// <summary>真实毫米值,用于标注与写回。</summary>
        public double DistanceMillimetres { get; internal set; }
        /// <summary>渲染长度;未确认段可能临时取原线长。</summary>
        public double DisplayDistanceMillimetres { get; internal set; }
        public bool Completed { get; internal set; }
    }

    /// <summary>
    /// 原生 3D 编辑器的共享状态(GL 画布、命令行、分段列表共用),无 GL 依赖。
    /// 绘制模式从根节点逐段追加;编辑模式由 QuickLineIsometricScene 加载。
    /// </summary>
    public sealed class QuickLineSceneState
    {
        private const string RootId = "N1";
        private int _nextNode;
        private int _nextSegment;

        public event Action Changed;

        public QuickLineProjectionMode ProjectionMode { get; private set; }
            = QuickLineProjectionMode.Isometric;
        public bool Drawing { get; private set; }
        public Dictionary<string, QuickLineSpatialPoint> Nodes { get; }
            = new Dictionary<string, QuickLineSpatialPoint>(StringComparer.OrdinalIgnoreCase);
        public List<QuickLineMutableSegment> Segments { get; }
            = new List<QuickLineMutableSegment>();
        public List<string> Diagnostics { get; } = new List<string>();
        public string ActiveNodeId { get; private set; } = RootId;
        /// <summary>绘制模式中当前预览段的方向;null 表示尚未确定。</summary>
        public QuickLineSpatialAxis? PreviewAxis { get; private set; }
        public int? PreviewSign { get; private set; }
        public string SelectedSegmentId { get; private set; }
        public int Revision { get; private set; }

        public static QuickLineSceneState CreateDrawing()
        {
            var state = new QuickLineSceneState
            {
                Drawing = true,
                ProjectionMode = QuickLineProjectionMode.Isometric
            };
            state.Nodes[RootId] = new QuickLineSpatialPoint(0, 0, 0);
            return state;
        }

        public static QuickLineSceneState FromScene(QuickLineIsometricScene scene)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            var state = new QuickLineSceneState
            {
                Drawing = scene.Segments.Count == 0,
                ProjectionMode = scene.ProjectionMode
            };
            foreach (QuickLineIsometricNode node in scene.Nodes)
                state.Nodes[node.Id] = node.Position;
            foreach (QuickLineIsometricSegment segment in scene.Segments)
                state.Segments.Add(new QuickLineMutableSegment(
                    segment.Id, segment.StartNodeId, segment.EndNodeId,
                    segment.Axis, segment.DirectionSign,
                    segment.DistanceMillimetres, segment.DisplayDistanceMillimetres,
                    segment.Completed));
            state.Diagnostics.AddRange(scene.Diagnostics);
            state.ActiveNodeId = scene.RootNodeId;
            state.Revision = 0;
            return state;
        }

        public QuickLineSpatialPoint NodePosition(string nodeId)
            => Nodes.TryGetValue(nodeId ?? "", out QuickLineSpatialPoint position)
                ? position : new QuickLineSpatialPoint(0, 0, 0);

        public QuickLineMutableSegment SelectedSegment => Segments.FirstOrDefault(
            item => string.Equals(item.Id, SelectedSegmentId,
                StringComparison.OrdinalIgnoreCase));

        /// <summary>当前预览段(仅绘制模式且方向已选时非 null)。</summary>
        public QuickLineMutableSegment PreviewSegment => Drawing
            && PreviewAxis.HasValue && PreviewSign.HasValue
            ? new QuickLineMutableSegment("preview", ActiveNodeId, "",
                PreviewAxis.Value, PreviewSign.Value, 0, 0, false)
            : null;

        // ---------- 绘制模式 ----------

        /// <summary>设置预览方向(键选或鼠标拾取)。</summary>
        public void SetPreview(QuickLineSpatialAxis axis, int sign)
        {
            if (!Drawing) return;
            PreviewAxis = axis;
            PreviewSign = sign < 0 ? -1 : 1;
            RaiseChanged();
        }

        public void ClearPreview()
        {
            PreviewAxis = null;
            PreviewSign = null;
            RaiseChanged();
        }

        /// <summary>以给定长度提交预览段;成功返回段 id。</summary>
        public string CommitPreview(double distanceMillimetres)
        {
            if (!Drawing || !PreviewAxis.HasValue || !PreviewSign.HasValue)
                return null;
            if (!IsPositive(distanceMillimetres)) return null;
            string startNodeId = ActiveNodeId;
            string endNodeId = NextNodeId(startNodeId);
            var segment = new QuickLineMutableSegment(
                "S" + ++_nextSegment, startNodeId, endNodeId,
                PreviewAxis.Value, PreviewSign.Value,
                distanceMillimetres, distanceMillimetres, true);
            Segments.Add(segment);
            Nodes[endNodeId] = NodePosition(startNodeId)
                + Displacement(PreviewAxis.Value, PreviewSign.Value, distanceMillimetres);
            ActiveNodeId = endNodeId;
            PreviewAxis = null;
            PreviewSign = null;
            SelectedSegmentId = segment.Id;
            Revision++;
            RaiseChanged();
            return segment.Id;
        }

        /// <summary>撤销最后一段,活动节点回退;返回是否撤销了内容。</summary>
        public bool UndoLastSegment()
        {
            if (Segments.Count == 0) return false;
            QuickLineMutableSegment last = Segments[Segments.Count - 1];
            Segments.RemoveAt(Segments.Count - 1);
            Nodes.Remove(last.EndNodeId);
            ActiveNodeId = last.StartNodeId;
            SelectedSegmentId = null;
            PreviewAxis = null;
            PreviewSign = null;
            if (Segments.Count == 0) _nextSegment = 0;
            Revision++;
            RaiseChanged();
            return true;
        }

        /// <summary>产出写回 CAD 的创建段列表。</summary>
        public IReadOnlyList<QuickLineCreatedSegment> BuildCreatedSegments()
            => Segments.Select(item => new QuickLineCreatedSegment(
                item.Id, item.StartNodeId, item.EndNodeId,
                item.Axis, item.DirectionSign, item.DistanceMillimetres)).ToList();

        // ---------- 编辑模式 ----------

        public bool SelectSegment(string segmentId)
        {
            SelectedSegmentId = Segments.Any(item => string.Equals(item.Id, segmentId,
                StringComparison.OrdinalIgnoreCase)) ? segmentId : null;
            RaiseChanged();
            return SelectedSegmentId != null;
        }

        /// <summary>编辑模式:把选中段的距离改为给定毫米值。</summary>
        public bool SetSelectedDistance(double distanceMillimetres)
        {
            QuickLineMutableSegment segment = SelectedSegment;
            if (segment == null || !IsPositive(distanceMillimetres)) return false;
            segment.DistanceMillimetres = distanceMillimetres;
            segment.DisplayDistanceMillimetres = distanceMillimetres;
            segment.Completed = true;
            ModifiedSegmentIds.Add(segment.Id);
            Revision++;
            RaiseChanged();
            return true;
        }

        /// <summary>编辑模式:产出全部段的距离表(供写回使用)。</summary>
        public IReadOnlyDictionary<string, double> BuildUpdates()
        {
            var updates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (QuickLineMutableSegment segment in Segments)
                updates[segment.Id] = segment.DistanceMillimetres;
            return updates;
        }

        /// <summary>编辑模式:哪些段被本次会话修改过(基于初始快照)。</summary>
        public HashSet<string> ModifiedSegmentIds { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ---------- 内部 ----------

        private string NextNodeId(string startNodeId)
        {
            // 从已有节点分叉时,避免与该节点已有段的目标重名。
            string candidate;
            do
            {
                candidate = "N" + (++_nextNode + 1);
            }
            while (Nodes.ContainsKey(candidate));
            return candidate;
        }

        private static QuickLineSpatialVector Displacement(
            QuickLineSpatialAxis axis, int sign, double distance)
        {
            int s = sign < 0 ? -1 : 1;
            switch (axis)
            {
                case QuickLineSpatialAxis.X:
                    return new QuickLineSpatialVector(s * distance, 0, 0);
                case QuickLineSpatialAxis.Y:
                    return new QuickLineSpatialVector(0, s * distance, 0);
                default:
                    return new QuickLineSpatialVector(0, 0, s * distance);
            }
        }

        private static bool IsPositive(double value)
            => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0.0;

        private void RaiseChanged() => Changed?.Invoke();
    }
}
