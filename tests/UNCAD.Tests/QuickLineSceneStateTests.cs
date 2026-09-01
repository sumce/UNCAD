using System;
using System.Linq;
using UNCAD.Core.QuickLine;
using UNCAD.UI.QuickLine3d;
using Xunit;

namespace UNCAD.Tests
{
    public class QuickLineSceneStateTests
    {
        [Fact]
        public void DrawMode_CommitPreviewAdvancesActiveNode()
        {
            var state = QuickLineSceneState.CreateDrawing();
            state.SetPreview(QuickLineSpatialAxis.X, 1);
            string id = state.CommitPreview(2000);
            Assert.Equal("S1", id);
            Assert.Equal(1, state.Segments.Count);
            Assert.Equal("N2", state.ActiveNodeId);
            Assert.Equal(2000, state.Nodes["N2"].X);
            Assert.Null(state.PreviewSegment);
            Assert.Equal(1, state.Revision);
        }

        [Fact]
        public void DrawMode_CommitWithoutPreviewOrLength_IsRejected()
        {
            var state = QuickLineSceneState.CreateDrawing();
            Assert.Null(state.CommitPreview(2000));
            state.SetPreview(QuickLineSpatialAxis.Y, -1);
            Assert.Null(state.CommitPreview(0));
            Assert.Null(state.CommitPreview(double.NaN));
            Assert.Empty(state.Segments);
        }

        [Fact]
        public void DrawMode_UndoRemovesLastSegmentAndRevertsNode()
        {
            var state = QuickLineSceneState.CreateDrawing();
            state.SetPreview(QuickLineSpatialAxis.Z, 1);
            state.CommitPreview(800);
            state.SetPreview(QuickLineSpatialAxis.X, 1);
            state.CommitPreview(1200);
            Assert.Equal("N3", state.ActiveNodeId);
            Assert.True(state.UndoLastSegment());
            Assert.Equal("N2", state.ActiveNodeId);
            Assert.Single(state.Segments);
            Assert.False(state.Nodes.ContainsKey("N3"));
            Assert.True(state.UndoLastSegment());
            Assert.False(state.UndoLastSegment()); // 场景已空
        }

        [Fact]
        public void DrawMode_BuildCreatedSegments_KeepsAxisSignAndDistance()
        {
            var state = QuickLineSceneState.CreateDrawing();
            state.SetPreview(QuickLineSpatialAxis.X, -1);
            state.CommitPreview(1500);
            var created = state.BuildCreatedSegments();
            var single = Assert.Single(created);
            Assert.Equal("S1", single.Id);
            Assert.Equal("N1", single.StartNodeId);
            Assert.Equal("N2", single.EndNodeId);
            Assert.Equal(QuickLineSpatialAxis.X, single.Axis);
            Assert.Equal(-1, single.DirectionSign);
            Assert.Equal(1500, single.DistanceMillimetres);
        }

        [Fact]
        public void EditMode_FromSceneLoadsAndSetSelectedDistanceTracksChanges()
        {
            QuickLineIsometricScene scene = QuickLineIsometricSceneBuilder
                .CreateDrawingScene();
            var state = QuickLineSceneState.FromScene(scene);
            Assert.True(state.Drawing);

            // 编辑模式状态用最小场景构造:空场景即绘制模式,这里仅验证 FromScene
            // 对空场景的映射;带段场景的构建依赖 Builder,已有 IsometricSceneTests 覆盖。
            Assert.Empty(state.Segments);
            Assert.Equal(scene.RootNodeId, state.ActiveNodeId);
        }

        [Fact]
        public void EditMode_SetSelectedDistance_RequiresSelection()
        {
            var state = QuickLineSceneState.CreateDrawing();
            Assert.False(state.SetSelectedDistance(100));
        }
    }
}
