using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Text;
using UNCAD.Features.ConfigCenter;
using UNCAD.Infra;

namespace UNCAD.Features.Unq
{
    /// <summary>桥架标注：生成红色原生偏移曲线和规格文字。</summary>
    [Feature("unq", "桥架标注",
        RibbonPanel = "标注",
        Commands = "UNC_TRAY;UNC_TRAY100;UNC_TRAY200;UNC_TRAY400;UNC_TRAY_SET",
        Description = "选中线段生成桥架标注（红色桥架线+规格文字）")]
    public class UnqFeature : CommandBase
    {
        [CommandMethod("UNC_TRAY", CommandFlags.UsePickSet)]
        public void UncadTray() => Run(null);

        [CommandMethod("UNC_TRAY_SET")]
        public void UncadTraySet() => SettingsFeature.Show(1);

        [CommandMethod("UNC_TRAY100", CommandFlags.UsePickSet)]
        public void UncadTray100() => Run("桥架100*100 10格");

        [CommandMethod("UNC_TRAY200", CommandFlags.UsePickSet)]
        public void UncadTray200() => Run("桥架200*100 10格");

        [CommandMethod("UNC_TRAY400", CommandFlags.UsePickSet)]
        public void UncadTray400() => Run("桥架400*100 10格");

        [CommandMethod("UNQ1", CommandFlags.UsePickSet)] public void Unq1() => UncadTray100();
        [CommandMethod("UNQ2", CommandFlags.UsePickSet)] public void Unq2() => UncadTray200();
        [CommandMethod("UNQ4", CommandFlags.UsePickSet)] public void Unq4() => UncadTray400();
        [CommandMethod("OPUNQ")] public void OpUnq() => UncadTraySet();

        protected override void Execute(CadContext ctx) => Execute(ctx, null);

        protected override void Execute(CadContext ctx, object state)
        {
            string content = state as string
                ?? Settings.Get(ConfigKeys.UnqText, "桥架200*100 10格");
            double hgt = Settings.GetDouble(ConfigKeys.UnqHeight, 180.0);
            double lineOff = Settings.GetDouble(ConfigKeys.UnqLineOff, 15.0);
            double textOff = Settings.GetDouble(ConfigKeys.UnqTextOff, 0.0);
            string side = Settings.Get(ConfigKeys.UnqSide, "1");

            var ids = SelectionService.PickCurvesWithOffset(ctx,
                "请选择基准线段或 [设置红线距离(D)]: ",
                "\n请输入红线距基线距离", ref lineOff, value =>
                    Settings.Set(ConfigKeys.UnqLineOff, value.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture)));
            if (ids == null || ids.Length == 0)
            {
                ctx.Write("\n[UNC_TRAY] 未选择线段，已取消。");
                return;
            }

            ConfigPrinter.Print(ctx, "UNC_TRAY",
                ("规格", "\"" + content + "\""),
                ("高度", TextFormatter.FormatNum(hgt)),
                ("红线偏移", TextFormatter.FormatNum(lineOff)),
                ("文字偏移", TextFormatter.FormatNum(textOff)),
                ("侧", side == "0" ? "下方" : "上方"));

            int count = ParallelCurveAnnotator.Add(ctx, ids, new ParallelAnnotationOptions
            {
                CurveOffset = lineOff,
                TextOffset = textOff,
                TextHeight = hgt,
                Above = side != "0",
                ColorIndex = 1,
                LabelFactory = _ => content
            });

            SelectionService.ClearPickFirst(ctx);
            ctx.Write(count > 0
                ? "\n[UNC_TRAY] 桥架标注生成完成：" + count + " 条线段。"
                : "\n[UNC_TRAY] 警告：未处理任何受支持的直线/二维多段线。");
        }
    }
}
