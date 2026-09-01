using System;
using System.Windows.Forms;
using OpenTK;
using UNCAD.Core.QuickLine;

namespace UNCAD.UI.QuickLine3d
{
    /// <summary>六个等轴测方向的方向键映射与鼠标方向拾取。</summary>
    public static class QuickLineAxisInput
    {
        /// <summary>方向键 → (轴, 符号)。←→ = X∓/±,↓↑ = Y∓/±,PgUp/PgDn = Z±。</summary>
        public static bool TryFromKey(Keys key, out QuickLineSpatialAxis axis, out int sign)
        {
            switch (key)
            {
                case Keys.Right:
                    axis = QuickLineSpatialAxis.X; sign = 1; return true;
                case Keys.Left:
                    axis = QuickLineSpatialAxis.X; sign = -1; return true;
                case Keys.Up:
                    axis = QuickLineSpatialAxis.Y; sign = 1; return true;
                case Keys.Down:
                    axis = QuickLineSpatialAxis.Y; sign = -1; return true;
                case Keys.PageUp:
                    axis = QuickLineSpatialAxis.Z; sign = 1; return true;
                case Keys.PageDown:
                    axis = QuickLineSpatialAxis.Z; sign = -1; return true;
                default:
                    axis = QuickLineSpatialAxis.X; sign = 0; return false;
            }
        }

        /// <summary>光标世界坐标相对活动节点的方向 → 最贴近的 6 轴之一。</summary>
        public static bool PickAxis(Vector3 cursorWorld, Vector3 activeNode,
            out QuickLineSpatialAxis axis, out int sign)
        {
            Vector3 delta = cursorWorld - activeNode;
            axis = QuickLineSpatialAxis.X;
            sign = 0;
            if (delta.LengthSquared < 1e-6) return false;
            float best = 0f;
            bool found = false;
            foreach (var candidate in Axes())
            {
                float dot = Vector3.Dot(delta, candidate.vector)
                    / (delta.Length * candidate.vector.Length);
                if (!found || dot > best)
                {
                    best = dot;
                    axis = candidate.axis;
                    sign = candidate.sign;
                    found = true;
                }
            }
            return found && best > 0.35; // 死区:方向不明确时保持上次选择
        }

        private static (QuickLineSpatialAxis axis, int sign, Vector3 vector)[] Axes()
            => new[]
            {
                (QuickLineSpatialAxis.X, 1, Vector3.UnitX),
                (QuickLineSpatialAxis.X, -1, -Vector3.UnitX),
                (QuickLineSpatialAxis.Y, 1, Vector3.UnitY),
                (QuickLineSpatialAxis.Y, -1, -Vector3.UnitY),
                (QuickLineSpatialAxis.Z, 1, Vector3.UnitZ),
                (QuickLineSpatialAxis.Z, -1, -Vector3.UnitZ)
            };
    }
}
