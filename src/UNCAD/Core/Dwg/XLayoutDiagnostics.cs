using UNCAD.Core.Geometry;
using UNCAD.Core.Text;

namespace UNCAD.Core.Dwg
{
    /// <summary>
    /// Formats actionable XLAYOUT frame diagnostics without accessing AutoCAD state.
    /// Callers provide the already-collected frame location and entity count.
    /// </summary>
    public static class XLayoutDiagnostics
    {
        public static string DescribeFrameIdentityFailure(string handle,
            FrameRectangle boundary, int index, int total, int entityCount, string reason)
        {
            string normalizedHandle = string.IsNullOrWhiteSpace(handle)
                ? "未知" : handle.Trim();
            string location = "位置未知";
            if (boundary != null)
            {
                location = "中心(" + TextFormatter.FormatNum(
                        (boundary.MinX + boundary.MaxX) / 2d) + ","
                    + TextFormatter.FormatNum((boundary.MinY + boundary.MaxY) / 2d)
                    + ")，范围X[" + TextFormatter.FormatNum(boundary.MinX) + ".."
                    + TextFormatter.FormatNum(boundary.MaxX) + "] Y["
                    + TextFormatter.FormatNum(boundary.MinY) + ".."
                    + TextFormatter.FormatNum(boundary.MaxY) + "]";
            }
            return "第 " + index + "/" + total + " 个图框，句柄 " + normalizedHandle + "，"
                + location + "，包含实体 " + entityCount + " 个："
                + (reason ?? "未知原因");
        }
    }
}
