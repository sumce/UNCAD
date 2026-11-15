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
        /// <summary>恢复上一轮预留并决策预留标记；返回上一轮记录。
        /// frameIdentity（3.0.0.17）：图框当前的物理身份（MACHINEID-* 属性读出）。
        /// 复制框上它=源机台身份——U1F 随后改选别的机台时，FrameRedefineGuard
        /// 用它判「图框原属谁」，把旧记录/建议值挡在新机台之外（现场报告：
        /// U1F 会把复制之前的预留米数算进新机台）。
        /// copiedFrameDetected：检测到本框携带源框元数据（外来 JSON 被拒）——
        /// 写事务里据此显式清空 CABLE_INFO/CONDUIT_INFO/FRAMEINFO 旧信息后
        /// 再写新机台数据（2026-11-06 用户要求 U1F 定义要清干净）。
        /// deviceReserveHintMm（D-052-B）：复制图框时从源框 CABLE_INFO 残留解析
        /// 出的设备内预留建议值——不静默恢复（3.0.0.8 守卫不变），仅供清单确认窗
        /// 预填 + 来源标注，用户核对确认。</summary>
        internal static FrameInfoJsonRecord Resolve(CadContext ctx, FillSelection selection,
            FillRuntimeOptions options, bool updateMode,
            out List<ObjectId> deviceReserveEraseIds,
            out List<ObjectId> markerExcludedIds,
            out bool copiedFrameDetected,
            out double? deviceReserveHintMm,
            out ExistingFillIdentity frameIdentity)
        {
            FrameInfoJsonRecord frameInfoPrevious;
            string foreignJsonNote;
            using (Transaction previousTransaction = ctx.Db.TransactionManager.StartTransaction())
            {
                // 迁移会从旧图框块参照上移除内嵌 FRAMEINFO_JSON，因此必须在迁移
                // 前读出独立 JSON 块 + 图框块上的记录；读取带本图框句柄校验——
                // 复制图框带来的源框 JSON（替代型号/设备内预留/更新历史）一律
                // 不继承，本次保存后将盖成本图框自己的句柄。
                // D-052-C：句柄失配先走搬场重绑判别（身份一致 + 原句柄已不在
                // 本图 = 跨文件复制/移动整机台）→ 收下记录，不算外来。
                FillSelectionCollector.TryReadExistingIdentity(previousTransaction,
                    selection, out ExistingFillIdentity ownIdentity, out _);
                frameIdentity = ownIdentity;
                string movedJsonNote;
                frameInfoPrevious = FrameInfoJsonBlockWriter.Read(previousTransaction,
                    FrameInfoJsonBlockWriter.ReadSources(selection),
                    FrameInfoJsonBlockWriter.OwnFrameHandle(previousTransaction,
                        selection.FrameBlockIds),
                    ownIdentity,
                    out foreignJsonNote, out movedJsonNote);
                previousTransaction.Commit();
                if (movedJsonNote.Length > 0)
                    ctx.Write("\n[" + (updateMode ? CommandIds.FillUpdate : CommandIds.Fill)
                        + "] " + movedJsonNote);
            }
            string command = updateMode ? CommandIds.FillUpdate : CommandIds.Fill;

            // 3.2.0.4（用户规格）：U1F = 重新定义，第一步就清理图框所有旧信息
            // ——旧 JSON 记录不用于恢复（预留/替代型号/建议值一律不带出，首跑
            // 也不预填），CABLE_INFO 恢复兜底与 D-052-B 复制源预填只在 U1U 生效；
            // 复制框毫米标守卫（不吸收、保文字）也只在 U1U——U1F 里图上横排
            // 0mm 标记是现场可见的真实意图，直接吸收为本机台设备内预留。
            // 身份读取与 copiedFrameDetected 检测照常（写事务据此+U1F 无条件
            // 清旧属性）。U1U 的恢复/复用/守卫语义全部不变。
            if (!updateMode)
            {
                frameInfoPrevious = null;
                deviceReserveHintMm = null;
            }
            if (foreignJsonNote.Length > 0)
                ctx.Write("\n[" + command + "] 检测到复制图框遗留的其他图框 JSON 记录，"
                    + "已忽略：不继承其设备内预留/电缆替代型号/更新历史；本次保存后"
                    + "将写入本图框自己的记录。");

            // 现场需求（2026-11-02）：JSON 没有设备内预留记录时，默认采用图框
            // CABLE_INFO 文本里已有的「数值（设备内）」——清单确认窗直接预填，
            // 用户仍可改；恢复值写回记录并随 JSON 持久化。
            // 2026-11-06 修复（用户报告：复制图框 U1F 自动带出 3000 设备预留）：
            // 整框复制会把源框的 CABLE_INFO 属性一起带过来，JSON 被句柄校验
            // 拒绝后，恢复兜底又从这份源框残留文本里把「数值（设备内）」救活
            // ——等于换条路继承。凡检测到外来 JSON（= 本框元数据是复制来的），
            // CABLE_INFO 同源同样不可信，一并跳过恢复；设备内预留由用户重新
            // 填写。老图框（从未写过 JSON、也非复制）不受影响。
            deviceReserveHintMm = null;
            // 3.2.0.4：恢复兜底（块A）与 D-052-B 建议值（块B）只在 U1U——
            // U1F 上面刚清空记录，这里再从旧 CABLE_INFO 文本救回就是换条路
            // 继承旧信息。
            if (updateMode && foreignJsonNote.Length == 0
                && (frameInfoPrevious == null
                    || string.IsNullOrWhiteSpace(frameInfoPrevious.DeviceReserveMm)))
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
            else if (updateMode && foreignJsonNote.Length > 0
                && (frameInfoPrevious == null
                    || string.IsNullOrWhiteSpace(frameInfoPrevious.DeviceReserveMm)))
            {
                // D-052-B：跳过 ≠ 从 0 重填——可解析的残留值作为建议值带出，
                // 清单确认窗预填 + 来源标注，用户核对确认。不构成静默继承。
                string copiedCableInfo = CadBlockAttributeWriter.ReadTagged(ctx,
                    selection.FrameBlockIds, FrameBlockFiller.TagCable);
                if (CableReserveReview.TryParseDeviceReserveMillimeters(
                        copiedCableInfo, out double copiedDeviceReserve))
                {
                    deviceReserveHintMm = copiedDeviceReserve;
                    ctx.Write("\n[" + command + "] 本框携带源框的 CABLE_INFO 文本，"
                        + "未自动恢复其设备内预留 "
                        + TextFormatter.FormatNum(copiedDeviceReserve)
                        + " mm；清单确认窗已按该值预填（复制源），请核对后确认。");
                }
                else
                {
                    ctx.Write("\n[" + command + "] 本框携带源框的 CABLE_INFO 文本，"
                        + "为避免继承源机台的设备内预留，未做恢复；请在清单确认窗重新填写。");
                }
            }

            // D-043：识别老版设备内预留标记——现场在设备旁横排手写的毫米数文字
            // （本次框选范围内，水平、纯数字±mm）。恰好一处→吸收为设备内预留并
            // 在写事务里删除该标注；多处→不乱取，提示人工确认。
            // 3.0.0.16：复制框（外来 JSON）例外——随框带过来的毫米标是源框残留，
            // 不能自动吸收（否则源机台的设备内预留从这条暗道静默继承回来，还把
            // 图上文字删了）。只从统计排除（“3000mm”不排除会当电缆长度双算），
            // 保留图上文字给用户对照，设备内预留交清单确认窗重新填写。
            deviceReserveEraseIds = new List<ObjectId>();
            markerExcludedIds = new List<ObjectId>();
            copiedFrameDetected = foreignJsonNote.Length > 0;
            DeviceReserveMarkerReader.Result reserveMarkers =
                DeviceReserveMarkerReader.CollectInSelection(ctx, selection.TextIds);
            // 3.2.0.4：吸收守卫只在 U1U（复制框外来 JSON 时保文字点名）；
            // U1F 无条件吸收（重新定义，图上横排 0mm 是现场意图）。
            if (reserveMarkers.Hits.Count == 1
                && (!updateMode || foreignJsonNote.Length == 0))
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
            else if (reserveMarkers.Hits.Count == 1)
            {
                markerExcludedIds.AddRange(reserveMarkers.EntityIds);
                ctx.Write("\n[" + command + "] 复制图框携带源框的横排毫米标 "
                    + reserveMarkers.Hits[0].Text + "，未自动吸收为设备内预留"
                    + "（源框残留，避免静默继承），也不计入米数统计；图上文字保留，"
                    + "如适用请在清单确认窗手动填写。");
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
