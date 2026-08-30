using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Submission;
using UNCAD.Infra;

namespace UNCAD.Features.Submit
{
    /// <summary>Submits current CAD frame tables to BOQ Excel without changing the drawing.</summary>
    [Feature("submit", "提交当前图框清单", Commands = CommandIds.SubmitFeatureCommands,
        Description = "只读取图框属性和当前清单，将项目编码、数量及米数提交到 BOQ")]
    public sealed class SubmitFeature : CommandBase
    {
        [CommandMethod(CommandIds.Submit, CommandFlags.UsePickSet)]
        public void SubmitCurrentFrames() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Submit);
            ObjectId[] selected = SelectionService.PickFirstOrPrompt(ctx,
                "\n请框选或点选需要提交的 frame_20260812 图框: ",
                new TypedValue(0, "INSERT"));
            if (selected == null || selected.Length == 0)
            {
                ctx.Write("\n[U1S] 未选择图框，未提交 Excel。");
                return;
            }

            FrameRegionCollection regions = FrameRegionCollector.Collect(ctx, selected);
            if (regions.Errors.Count > 0)
            {
                foreach (string error in regions.Errors)
                    ctx.Write("\n[U1S] 图框识别失败: " + error);
                return;
            }
            if (regions.Groups.Count == 0)
            {
                ctx.Write("\n[U1S] 未找到有效的 frame_20260812 图框。");
                return;
            }

            var identities = new List<KeyValuePair<string, string>>();
            foreach (FrameRegionGroup region in regions.Groups)
            {
                SubmissionRecord record;
                try
                {
                    record = FrameIdentityReader.Read(ctx, region);
                }
                catch (System.Exception ex)
                {
                    throw new InvalidDataException("图框 " + region.Handle
                        + " 无法读取提交信息：" + ex.Message, ex);
                }
                if (record.Materials.Count == 0)
                    throw new InvalidDataException("图框 " + region.Handle
                        + " 的当前清单没有可提交项目，Excel 未修改。");
                identities.Add(new KeyValuePair<string, string>(record.MachineId,
                    record.DeviceName));
                ctx.Write("\n[U1S] 预检图框 " + region.Handle + "："
                    + record.MachineId + " / " + record.DeviceName + "，清单 "
                    + record.Materials.Count + " 项（含当前 M 数量）。");
            }
            AutomaticSubmissionService.ValidateIdentityKeys(identities);

            string outputRoot = AutomaticSubmissionService.PrepareTargetPath();
            AutomaticSubmissionWriteResult result = AutomaticSubmissionService.Write(ctx,
                outputRoot, regions.Groups.Select(region => region.EntityIds.ToArray()));
            ctx.Write("\n[U1S] 提交完成：图框 " + result.RecordCount + " 个，清单 "
                + result.MaterialCount + " 项；Excel: " + result.FilePath
                + "。CAD 图纸未修改。");
        }
    }
}
