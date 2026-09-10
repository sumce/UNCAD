using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Dimension
{
    /// <summary>U1D converts selected aligned-dimension labels into editable single-line text.</summary>
    [Feature("dimension-text", "对齐标注转文字",
        Commands = CommandIds.DimensionTextFeatureCommands,
        Description = "将对齐标注文字转换为单行文字，保留原尺寸线")]
    public sealed class DimensionTextFeature : CommandBase
    {
        [CommandMethod(CommandIds.DimensionText, CommandFlags.UsePickSet)]
        public void ConvertDimensionText() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.DimensionText);
            ObjectId[] ids = SelectionService.PickFirstOrPrompt(ctx,
                "请选择需要转换的对齐标注: ",
                new TypedValue(0, "DIMENSION"));
            if (ids == null || ids.Length == 0) return;

            double textHeight = Settings.GetDouble(ConfigKeys.UnlHeight, 180.0);
            DimensionTextConversionResult result;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                result = AlignedDimensionTextConverter.Convert(ctx, transaction,
                    ids, textHeight);
                transaction.Commit();
            }

            ctx.Write("\n[U1D] 已将 " + result.ConvertedCount
                + " 个对齐标注转换为单行文字，文字高度 "
                + TextFormatter.FormatNum(textHeight) + "；原尺寸线已保留。");
            if (result.AlreadyConvertedCount > 0 || result.SkippedCount > 0)
                ctx.Write(" 已转换过 " + result.AlreadyConvertedCount
                    + " 个，非对齐标注或无效对象 " + result.SkippedCount + " 个。");
        }
    }
}
