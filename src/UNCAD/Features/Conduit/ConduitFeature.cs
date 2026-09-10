using System.Linq;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Features.ConfigCenter;
using UNCAD.Features.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Conduit
{
    /// <summary>线管标注：生成紫色原生偏移曲线和固定 2000mm 占位文字。</summary>
    [Feature("conduit", "线管标注",
        Commands = CommandIds.ConduitFeatureCommands,
        Description = "选中线段生成紫色线管及固定 2000mm 占位文字")]
    public class ConduitFeature : CommandBase
    {
        [CommandMethod(CommandIds.Conduit, CommandFlags.UsePickSet)]
        public void UncadConduit() => Run(null);

        public void UncadConduit20() => Run("20");

        public void UncadConduit25() => Run("25");

        public void UncadConduit32() => Run("32");

        public void UncadConduitSet() => SettingsFeature.Show(2);

        protected override void Execute(CadContext ctx) => Execute(ctx, null);

        protected override void Execute(CadContext ctx, object state)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Conduit);
            string diameter = NormalizeDiameter(state as string
                ?? Settings.Get(ConfigKeys.ConduitDiameter, "20"));
            double hgt = Settings.GetDouble(ConfigKeys.ConduitHeight, 180.0);
            double lineOff = Settings.GetDouble(ConfigKeys.ConduitLineOff, 15.0);
            double textOff = Settings.GetDouble(ConfigKeys.ConduitTextOff, 0.0);
            string side = Settings.Get(ConfigKeys.ConduitSide, "1");

            // 标注第一行写固定清单的「1.名称」，管径必须先在清单里找到；
            // 找不到就没有可写的型号，按 D-016 直接中止。
            ListItem catalogItem = ListItemReader.EmbeddedCatalogIndex
                .FindRigidConduit(diameter);
            string model = BoqFeatureName.Extract(catalogItem?.Feature);
            if (catalogItem == null || model.Length == 0)
            {
                ctx.Write("\n[U1C] 固定清单中没有 ⌀" + diameter
                    + " 线管的可标注项目，已取消。请在固定清单中补充对应项目后重试。");
                return;
            }
            string label = AnnotationLabelPair.Build(model,
                ConduitLabelFormatter.LengthText);

            var ids = SelectionService.PickCurvesWithOffset(ctx,
                "请选择线管基准线或 [设置紫线距离(D)]: ",
                "\n请输入紫线距基线距离", ref lineOff, value =>
                    Settings.Set(ConfigKeys.ConduitLineOff, value.ToString("0.##",
                        System.Globalization.CultureInfo.InvariantCulture)),
                includeInserts: true);
            if (ids == null || ids.Length == 0)
            {
                ctx.Write("\n[U1C] 未选择线段，已取消。");
                return;
            }

            // U1C also accepts a preselected frame/Ruanguan block. Keep the
            // existing curve annotation path, and update hose labels separately
            // so INSERT entities never reach the curve offsetter.
            FillSelection ruanguanSelection = FillSelectionCollector.Split(ctx, ids);
            if (ruanguanSelection.RuanguanBlockIds.Length == 0)
            {
                FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, ids);
                if (regions.Groups.Count == 1)
                    ruanguanSelection = FillSelectionCollector.Split(ctx,
                        regions.Groups[0].EntityIds.ToArray());
            }
            FillWriteResult ruanguanResult;

            ConfigPrinter.Print(ctx, "U1C",
                ("管径", "⌀" + diameter),
                ("清单型号", "\"" + model + "\""),
                ("高度", TextFormatter.FormatNum(hgt)),
                ("紫线偏移", TextFormatter.FormatNum(lineOff)),
                ("文字偏移", TextFormatter.FormatNum(textOff)),
                ("侧", side == "0" ? "下方" : "上方"));

            int count;
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                ruanguanResult = RuanguanBlockWriter.FillModelAndLength(ctx, transaction,
                    ruanguanSelection.RuanguanBlockIds, diameter);
                count = ParallelCurveAnnotator.Add(ctx, transaction, ids,
                    new ParallelAnnotationOptions
                    {
                        CurveOffset = lineOff,
                        TextOffset = textOff,
                        TextHeight = hgt,
                        Above = side != "0",
                        ColorIndex = 6,
                        AnnotationKind = "U1C",
                        LabelFactory = _ => label,
                        // 旧图纸上的单行“⌀20线管 2000mm”要能被认出来替换掉。
                        LegacyLabelKey = AnnotationLabelPair.CollapseText
                    });
                transaction.Commit();
            }
            if (ruanguanResult.Blocks > 0)
                ctx.Write("\n[U1C] Ruanguan 已更新 " + ruanguanResult.Blocks
                    + " 个块，共 " + ruanguanResult.Values + " 项。");

            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[U1C] 已生成 " + count + " 条 ⌀" + diameter + " 线管标注。");
        }

        private static string NormalizeDiameter(string value)
            => ConduitDiameter.NormalizeOrDefault(value, "20");
    }
}
