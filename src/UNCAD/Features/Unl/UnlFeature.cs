using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Text;
using UNCAD.Infra;
using UNCAD.Features.ConfigCenter;

namespace UNCAD.Features.Unl
{
    /// <summary>
    /// U1L：连续绘制线段（交互同 AutoCAD L 命令），每段中心靠边立即生成占位文字（默认 "2000mm"）。
    /// U1SET：打开统一配置中心（线段页签）。
    /// </summary>
    [Feature("unl", "带标注线段",
        Commands = CommandIds.LineFeatureCommands,
        Description = "同 L 命令画线，每段中心靠边立即生成占位文字")]
    public class UnlFeature : CommandBase
    {
        // ===== 规范命令 =====

        [CommandMethod(CommandIds.Line)]
        public void UncadLine() => Run();

        public void UncadLineSet() => SettingsFeature.Show(0);

        // ===== 旧名兼容（后续版本可删除） =====

        [CommandMethod(CommandIds.LegacyLine)]
        public void Unl() => UncadLine();

        public void OpUnl() => UncadLineSet();

        // ===== 主逻辑 =====

        protected override void Execute(CadContext ctx)
        {
            string txt = Settings.Get(ConfigKeys.UnlText, "2000mm");
            double hgt = Settings.GetDouble(ConfigKeys.UnlHeight, 180.0);
            string pos = Settings.Get(ConfigKeys.UnlPos, "1"); // 0=居中 1=靠边下 2=靠边上
            double offset = Settings.GetDouble(ConfigKeys.UnlOffset, 0.0);
            if (offset < 0) offset = 0;

            // 防御性钳制：偏移不应超过 2 倍字高
            if (offset > hgt * 2)
            {
                ctx.Write("\n[U1L] 警告：偏移设置异常（>2 倍字高），已按贴线处理。请用 U1SET 重新设置。");
                offset = 0.0;
            }

            string posName = pos == "0" ? "居中" : pos == "2" ? "靠边上" : "靠边下";
            ConfigPrinter.Print(ctx, "U1L",
                ("文字", "\"" + txt + "\""),
                ("高度", TextFormatter.FormatNum(hgt)),
                ("位置", posName),
                ("偏移", TextFormatter.FormatNum(offset)));

            InteractiveDrawer.Run(ctx, "指定第一点: ", "指定下一点或 [放弃(U)] <结束>:",
                (p1, p2, tx, segIds) =>
                {
                    // 线段（当前图层）
                    var line = EntityFactory.Line(ctx, p1, p2);
                    ctx.AddToCurrentSpace(tx, line);
                    segIds.Add(line.Id);

                    // 跟随图纸 Standard 样式，不再套用 UNC 专属字体/宽度设置
                    ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, tx);

                    // 中心点 + 角度（保持文字正向阅读）
                    Point3d mid = GeoMath.Mid(p1, p2);
                    double ang = GeoMath.ReadableAngle(p1, p2);

                    // 占位文字位置：居中 / 靠边下 / 靠边上（单行文字，锚点即定位点）
                    Point3d txtpt;
                    AttachmentPoint align;
                    if (pos == "2")
                    {
                        txtpt = GeoMath.Polar(mid, GeoMath.SideDirection(ang, true), offset);
                        align = AttachmentPoint.BottomCenter;
                    }
                    else if (pos == "0")
                    {
                        txtpt = mid;
                        align = AttachmentPoint.MiddleCenter;
                    }
                    else
                    {
                        txtpt = GeoMath.Polar(mid, GeoMath.SideDirection(ang, false), offset);
                        align = AttachmentPoint.TopCenter;
                    }

                    var t = EntityFactory.DBText(ctx, txt, txtpt, hgt, ang, align,
                        textStyleId: styleId);
                    ctx.AddToCurrentSpace(tx, t);
                    segIds.Add(t.Id);
                });
        }
    }
}
