using System;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Cad.Frame;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Shared
{
    /// <summary>
    /// 共享机台数据源快照定位与图框→回路身份解析（供 Fill 与 PdfExport 共用，
    /// 保证 PDF 命名与 U1F/U1U 回路归属口径完全一致）。
    /// </summary>
    internal static class FrameCircuitResolver
    {
        internal static string ResolveMachineWorkbookPath(
            CadContext ctx, string configuredPath)
        {
            string path = (configuredPath ?? "").Trim();
            try
            {
                if (!string.IsNullOrEmpty(path)
                    && MachineWorkbookSource.TryGetSnapshot(path,
                        out MachineWorkbookSnapshotInfo snapshot))
                {
                    ctx.Write("\n[U1F/U1U] 使用上次成功刷新的 SQLite 机台快照: "
                        + (snapshot?.SourceDisplay ?? path)
                        + "（本命令不会读取源 Excel）");
                    return path;
                }
            }
            catch (Exception ex)
            {
                ctx.Write("\n[U1F/U1U] SQLite 机台快照不可用，请执行 U1DATA 或在 U1SET 重新刷新: "
                    + ex.Message);
                Log.Warn("U1F/U1U machine SQLite snapshot check failed: " + ex.Message);
                return null;
            }

            if (!string.IsNullOrEmpty(path))
            {
                ctx.Write("\n[U1F/U1U] 机台数据尚未导入 SQLite，请执行 U1DATA 或在 U1SET 点击“刷新”。");
                Log.Warn("机台数据 SQLite 快照不存在，U1F/U1U 未读取源 Excel。源: " + path);
                return null;
            }

            using (var dialog = new OpenFileDialog
            {
                Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                Title = "选择机台数据 Excel"
            })
            {
                if (dialog.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle))
                    != DialogResult.OK) return null;
                path = dialog.FileName;
            }
            Settings.Set(ConfigKeys.FillExcelPath, path);
            ctx.Write("\n[U1F/U1U] 已记住 Excel: " + path
                + "；请执行 U1DATA，或在 U1SET 点击“刷新”导入 SQLite。");
            return null;
        }

        internal static bool TryResolveFrameCircuit(Transaction transaction,
            FrameRegionGroup region, FillWorkbookSnapshot workbook,
            CadBlockDefinitionReader definitions,
            out string machineId, out string circuitName, out string upstreamId,
            out string error)
        {
            machineId = "";
            circuitName = "";
            upstreamId = "";
            error = "";
            if (transaction == null || region == null || workbook == null)
            {
                error = "缺少图框或机台数据。";
                return false;
            }
            ExistingFillIdentity identity;
            try
            {
                identity = FrameIdentityReader.ReadIdentity(transaction, region, definitions);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            MachineRow machine = ExistingFillIdentityResolver.MatchMachine(identity,
                workbook.FindRows(identity.MachineId), out string matchError);
            if (machine == null)
            {
                error = matchError;
                return false;
            }
            machineId = (machine.MachineId ?? "").Trim();
            circuitName = (machine.CircuitName ?? "").Trim();
            if (machineId.Length == 0 || circuitName.Length == 0)
            {
                error = "图框缺少机台ID或回路名。";
                return false;
            }
            int sameName = workbook.FindRows(machineId).Count(row =>
                IdentityTextNormalizer.Equals(row.CircuitName, machine.CircuitName));
            if (sameName > 1)
                upstreamId = CircuitIdentityKey.UpstreamDisplay(machine.Fr);
            return true;
        }
    }
}
