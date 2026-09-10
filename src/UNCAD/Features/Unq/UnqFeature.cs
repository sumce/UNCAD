using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Features.ConfigCenter;
using UNCAD.Infra;

namespace UNCAD.Features.Unq
{
    /// <summary>桥架标注：生成红色原生偏移曲线和规格文字。</summary>
    [Feature("unq", "桥架标注",
        Commands = CommandIds.TrayFeatureCommands,
        Description = "选中线段生成桥架标注（红色桥架线+规格文字）")]
    public class UnqFeature : CommandBase
    {
        public void UncadTray() => Run(null);

        public void UncadTraySet() => SettingsFeature.Show(1);

        [CommandMethod(CommandIds.Tray100, CommandFlags.UsePickSet)]
        public void UncadTray100() => Run("桥架100*100 2500mm");

        [CommandMethod(CommandIds.Tray200, CommandFlags.UsePickSet)]
        public void UncadTray200() => Run("桥架200*100 2500mm");

        [CommandMethod(CommandIds.Tray400, CommandFlags.UsePickSet)]
        public void UncadTray400() => Run("桥架400*100 2500mm");

        [CommandMethod(CommandIds.LegacyTray100, CommandFlags.UsePickSet)] public void Unq1() => UncadTray100();
        [CommandMethod(CommandIds.LegacyTray200, CommandFlags.UsePickSet)] public void Unq2() => UncadTray200();
        [CommandMethod(CommandIds.LegacyTray400, CommandFlags.UsePickSet)] public void Unq4() => UncadTray400();
        public void OpUnq() => UncadTraySet();

        protected override void Execute(CadContext ctx) => Execute(ctx, null);

        protected override void Execute(CadContext ctx, object state)
        {
            string requestedContent = state as string
                ?? Settings.Get(ConfigKeys.UnqText, "桥架200*100 2500mm");
            double mmPerGrid = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0);
            if (double.IsNaN(mmPerGrid) || double.IsInfinity(mmPerGrid) || mmPerGrid <= 0)
                mmPerGrid = 250.0;
            string content = BridgeLabelFormatter.NormalizeOrDefault(requestedContent,
                mmPerGrid, "桥架200*100 2500mm");
            if (!string.Equals((requestedContent ?? "").Trim(), content,
                System.StringComparison.Ordinal))
            {
                ctx.Write("\n[U1Q] 已将桥架标识规范为“" + content
                    + "”（长度统一使用毫米；旧格数按 " + TextFormatter.FormatNum(mmPerGrid)
                    + " mm/格换算）。");
                if (state == null)
                    Settings.Set(ConfigKeys.UnqText, content);
            }
            // Legacy UNQ commands route through the same implementation, so diagnostics use
            // the corresponding current U1Q name determined from the selected specification.
            string commandName = content.IndexOf("100*", System.StringComparison.Ordinal) >= 0
                ? CommandIds.Tray100
                : content.IndexOf("400*", System.StringComparison.Ordinal) >= 0
                    ? CommandIds.Tray400 : CommandIds.Tray200;
            double hgt = Settings.GetDouble(ConfigKeys.UnqHeight, 180.0);
            double lineOff = Settings.GetDouble(ConfigKeys.UnqLineOff, 15.0);
            double textOff = Settings.GetDouble(ConfigKeys.UnqTextOff, 0.0);
            string side = Settings.Get(ConfigKeys.UnqSide, "1");
            ProductMetadata.EnsureCommandAllowed(commandName);

            // 标注第一行写固定清单的「1.名称」，所以规格必须先在清单里找到；
            // 找不到就没有可写的型号，按 D-016 直接中止，绝不写一行没有编码的标注。
            if (!BridgeLabelFormatter.TryParseMillimetreLabel(content, out string spec,
                out double millimetres))
            {
                ctx.Write("\n[" + commandName + "] 无法解析桥架规格“" + content
                    + "”，已取消。");
                return;
            }
            ListItem catalogItem = ListItemReader.EmbeddedCatalogIndex
                .FindBridge(BoqCatalogIndex.NormalizeBridgeSpec(spec));
            string model = BoqFeatureName.Extract(catalogItem?.Feature);
            if (catalogItem == null || model.Length == 0)
            {
                ctx.Write("\n[" + commandName + "] 固定清单中没有桥架规格“" + spec
                    + "”的可标注项目，已取消。请在固定清单中补充对应项目后重试。");
                return;
            }
            string label = AnnotationLabelPair.Build(model,
                BridgeLabelFormatter.FormatLengthText(millimetres));

            var ids = SelectionService.PickCurvesWithOffset(ctx,
                "请选择基准线段或 [设置红线距离(D)]: ",
                "\n请输入红线距基线距离", ref lineOff, value =>
                    Settings.Set(ConfigKeys.UnqLineOff, value.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture)));
            if (ids == null || ids.Length == 0)
            {
                ctx.Write("\n[" + commandName
                    + "] 未找到可处理的独立直线/二维多段线，已取消（块内线段、三维多段线或无效预选不可直接偏移）。");
                return;
            }

            ConfigPrinter.Print(ctx, commandName,
                ("规格", "\"" + content + "\""),
                ("清单型号", "\"" + model + "\""),
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
                AnnotationKind = "U1Q",
                LabelFactory = _ => label,
                // 旧图纸上的单行“桥架200*100 2500mm”要能被认出来替换掉。
                LegacyLabelKey = AnnotationLabelPair.CollapseText
            });

            SelectionService.ClearPickFirst(ctx);
            ctx.Write(count > 0
                ? "\n[" + commandName + "] 桥架标注生成完成：" + count + " 条线段。"
                : "\n[" + commandName + "] 警告：未处理任何受支持的直线/二维多段线。");
        }
    }
}
