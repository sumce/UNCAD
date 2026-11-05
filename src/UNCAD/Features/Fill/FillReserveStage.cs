using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 阶段2前置「上一轮预留恢复」：读 frameinfo_json（带本图框句柄校验）、
    /// 恢复 CABLE_INFO 文本里的设备内预留、识别并决策 D-043 横排毫米标。
    /// 2026-11-05 从 FillFeature 拆出，并把执行时机提到统计之前——吸收的
    /// 毫米标（如 "3000mm"）同时命中电缆长度判据（^数字0mm$），不排除会被
    /// 米数统计双算（统计一次、设备内预留又一次，总长虚高一份）；吸收的
    /// 文本通过 markerExcludedIds 从统计排除。
    /// </summary>
    internal static class FillReserveStage
    {
        /// <summary>恢复上一轮预留并决策预留标记；返回上一轮记录。</summary>
        internal static FrameInfoJsonRecord Resolve(CadContext ctx, FillSelection selection,
            FillRuntimeOptions options, bool updateMode,
            out List<ObjectId> deviceReserveEraseIds,
            out List<ObjectId> markerExcludedIds)
        {
            FrameInfoJsonRecord frameInfoPrevious;
            string foreignJsonNote;
            using (Transaction previousTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                // 迁移会从旧图框块参照上移除内嵌 FRAMEINFO_JSON，因此必须在迁移
                // 前读出独立 JSON 块 + 图框块上的记录；读取带本图框句柄校验——
                // 复制图框带来的源框 JSON（替代型号/设备内预留/更新历史）一律
                // 不继承，本次保存后将盖成本图框自己的句柄。
                frameInfoPrevious = FrameInfoJsonBlockWriter.Read(previousTransaction,
                    FrameInfoJsonBlockWriter.ReadSources(selection),
                    FrameInfoJsonBlockWriter.OwnFrameHandle(previousTransaction,
                        selection.FrameBlockIds),
                    out foreignJsonNote);
                previousTransaction.Commit();
            }
            string command = updateMode ? CommandIds.FillUpdate : CommandIds.Fill;
            if (foreignJsonNote.Length > 0)
                ctx.Write("\n[" + command + "] 检测到复制图框遗留的其他图框 JSON 记录，"
                    + "已忽略：不继承其设备内预留/电缆替代型号/更新历史；本次保存后"
                    + "将写入本图框自己的记录。");

            // 现场需求（2026-11-02）：JSON 没有设备内预留记录时，默认采用图框
            // CABLE_INFO 文本里已有的「数值（设备内）」——清单确认窗直接预填，
            // 用户仍可改；恢复值写回记录并随 JSON 持久化。
            if (frameInfoPrevious == null
                || string.IsNullOrWhiteSpace(frameInfoPrevious.DeviceReserveMm))
            {
                string existingCableInfo = CadBlockAttributeWriter.ReadTagged(ctx,
                    selection.FrameBlockIds, FrameBlockFiller.TagCable);
                if (CableReserveReview.TryParseDeviceReserveMillimeters(
                        existingCableInfo, out double recoveredDeviceReserve))
                {
                    frameInfoPrevious = frameInfoPrevious ?? new FrameInfoJsonRecord();
                    frameInfoPrevious.DeviceReserveMm =
                        TextFormatter.FormatNum(recoveredDeviceReserve);
                }
            }

            // D-043：识别老版设备内预留标记——现场在设备旁横排手写的毫米数文字
            // （本次框选范围内，水平、纯数字±mm）。恰好一处→吸收为设备内预留并
            // 在写事务里删除该标注；多处→不乱取，提示人工确认。
            deviceReserveEraseIds = new List<ObjectId>();
            markerExcludedIds = new List<ObjectId>();
            DeviceReserveMarkerReader.Result reserveMarkers =
                DeviceReserveMarkerReader.CollectInSelection(ctx, selection.TextIds);
            if (reserveMarkers.Hits.Count == 1)
            {
                DeviceReserveMarkerHit marker = reserveMarkers.Hits[0];
                double? decided = DeviceReserveMarker.Decide(
                    options.DeviceReserveAutoDetect, reserveMarkers.Hits,
                    FillFeature.ParseJsonReserve(frameInfoPrevious),
                    out List<int> absorbed);
                if (absorbed.Count > 0)
                {
                    frameInfoPrevious = frameInfoPrevious ?? new FrameInfoJsonRecord();
                    frameInfoPrevious.DeviceReserveMm =
                        TextFormatter.FormatNum(decided.Value);
                    deviceReserveEraseIds = reserveMarkers.EntityIds;
                    markerExcludedIds.AddRange(reserveMarkers.EntityIds);
                    ctx.Write("\n[" + command + "] 已识别设备内预留标记 " + marker.Text
                        + " mm，将删除图上标注、计入设备内预留并不再计入米数统计。");
                }
            }
            else if (reserveMarkers.Hits.Count > 1)
            {
                ctx.Write("\n[" + command + "] 发现 " + reserveMarkers.Hits.Count
                    + " 处横排毫米标（"
                    + string.Join("、", reserveMarkers.Hits.ConvertAll(hit => hit.Text))
                    + "），无法确认哪处是设备内预留，未自动吸收；请人工确认或删掉多余标注。");
            }
            return frameInfoPrevious;
        }
    }
}
