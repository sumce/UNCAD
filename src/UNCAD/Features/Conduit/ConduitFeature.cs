using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Text;
using UNCAD.Features.ConfigCenter;
using UNCAD.Infra;

namespace UNCAD.Features.Conduit
{
    /// <summary>线管标注：生成紫色原生偏移曲线和按实际曲线长度计算的规格文字。</summary>
    [Feature("conduit", "线管标注",
        Commands = CommandIds.ConduitFeatureCommands,
        Description = "选中线段生成紫色线管及按实际长度计算的规格文字")]
    public class ConduitFeature : CommandBase
    {
        [CommandMethod(CommandIds.Conduit, CommandFlags.UsePickSet)]
        public void UncadConduit() => Run(null);

        [CommandMethod(CommandIds.Conduit20, CommandFlags.UsePickSet)]
        public void UncadConduit20() => Run("20");

        [CommandMethod(CommandIds.Conduit25, CommandFlags.UsePickSet)]
        public void UncadConduit25() => Run("25");

        [CommandMethod(CommandIds.Conduit32, CommandFlags.UsePickSet)]
        public void UncadConduit32() => Run("32");

        [CommandMethod(CommandIds.ConduitSettings)]
        public void UncadConduitSet() => SettingsFeature.Show(2);

        protected override void Execute(CadContext ctx) => Execute(ctx, null);

        protected override void Execute(CadContext ctx, object state)
        {
            string diameter = NormalizeDiameter(state as string
                ?? Settings.Get(ConfigKeys.ConduitDiameter, "20"));
            double hgt = Settings.GetDouble(ConfigKeys.ConduitHeight, 180.0);
            double lineOff = Settings.GetDouble(ConfigKeys.ConduitLineOff, 15.0);
            double textOff = Settings.GetDouble(ConfigKeys.ConduitTextOff, 0.0);
            string side = Settings.Get(ConfigKeys.ConduitSide, "1");

            var ids = SelectionService.PickCurvesWithOffset(ctx,
                "请选择线管基准线或 [设置紫线距离(D)]: ",
                "\n请输入紫线距基线距离", ref lineOff, value =>
                    Settings.Set(ConfigKeys.ConduitLineOff, value.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture)));
            if (ids == null || ids.Length == 0)
            {
                ctx.Write("\n[UNC_CONDUIT] 未选择线段，已取消。");
                return;
            }

            ConfigPrinter.Print(ctx, "UNC_CONDUIT",
                ("管径", "⌀" + diameter),
                ("高度", TextFormatter.FormatNum(hgt)),
                ("紫线偏移", TextFormatter.FormatNum(lineOff)),
                ("文字偏移", TextFormatter.FormatNum(textOff)),
                ("侧", side == "0" ? "下方" : "上方"));

            int count = ParallelCurveAnnotator.Add(ctx, ids, new ParallelAnnotationOptions
            {
                CurveOffset = lineOff,
                TextOffset = textOff,
                TextHeight = hgt,
                Above = side != "0",
                ColorIndex = 6,
                LabelFactory = length => ConduitLabelFormatter.Build(diameter, length)
            });

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[UNC_CONDUIT] 已生成 " + count + " 条 ⌀" + diameter + " 线管标注。");
        }

        private static string NormalizeDiameter(string value)
            => ConduitDiameter.NormalizeOrDefault(value, "20");
    }
}
