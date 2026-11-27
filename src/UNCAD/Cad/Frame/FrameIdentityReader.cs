using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;

namespace UNCAD.Cad.Frame
{
    /// <summary>Shared adapter that reads one frame's current identity and BOQ state.</summary>
    internal static class FrameIdentityReader
    {
        internal static ExistingFillIdentity ReadIdentity(Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions = null)
        {
            return ReadIdentity(transaction, region, definitions, out _);
        }

        /// <summary>带外来 JSON 检测的轻量身份读取（XSTS/排版等统计入口用）。</summary>
        internal static ExistingFillIdentity ReadIdentity(Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions,
            out string foreignJsonNote)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (region == null) throw new ArgumentNullException(nameof(region));
            FillSelection selection = FillSelectionCollector.Split(transaction,
                region.EntityIds.ToArray(), false, definitions);
            if (!FillSelectionCollector.TryReadExistingIdentity(transaction, selection,
                    region.Handle, out ExistingFillIdentity identity, out string error,
                    out foreignJsonNote))
                throw new InvalidDataException(error);
            return identity;
        }

        /// <summary>
        /// XLAYOUT 专用身份读取（D-045 修订，2026-11-22）：物理身份只读图框自身
        /// 的 MACHINEID-POWER/MACHINEID-DEVICE 属性（及上游块 UPSTREAM_INFO），
        /// 设备块 DEVICENAME 不参与。JSON 读取与仲裁语义与 ReadIdentity 一致。
        /// </summary>
        internal static ExistingFillIdentity ReadLayoutIdentity(Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (region == null) throw new ArgumentNullException(nameof(region));
            FillSelection selection = FillSelectionCollector.Split(transaction,
                region.EntityIds.ToArray(), false, definitions);
            if (!FillSelectionCollector.TryReadExistingIdentity(transaction, selection,
                    region.Handle, out ExistingFillIdentity identity, out string error,
                    out _, false))
                throw new InvalidDataException(error);
            return identity;
        }

        public static SubmissionRecord Read(CadContext ctx, FrameRegionGroup region)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (region == null) throw new ArgumentNullException(nameof(region));
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                SubmissionRecord result = Read(ctx, transaction, region);
                transaction.Commit();
                return result;
            }
        }

        /// <summary>Reads a frame through a caller-owned transaction for batch operations.</summary>
        public static SubmissionRecord Read(CadContext ctx, Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return Read(transaction, region, definitions);
        }

        /// <summary>Reads one frame from a detached DWG using a caller-owned transaction.</summary>
        internal static SubmissionRecord Read(Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions = null)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            if (region == null) throw new ArgumentNullException(nameof(region));
            return Read(transaction, region.EntityIds.ToArray(), true, definitions);
        }

        public static SubmissionRecord Read(CadContext ctx, Transaction transaction,
            ObjectId[] ids, bool inferLegacySocketPanels, CadBlockDefinitionReader definitions = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            return Read(transaction, ids, inferLegacySocketPanels, definitions);
        }

        private static SubmissionRecord Read(Transaction transaction, ObjectId[] ids,
            bool inferLegacySocketPanels, CadBlockDefinitionReader definitions)
        {
            if (transaction == null) throw new ArgumentNullException(nameof(transaction));
            SubmissionSourceData source = CadSubmissionReader.Read(transaction, ids, definitions);
            FrameInfoJsonRecord persistedIdentity = ReadArbitratedIdentity(transaction,
                ids, definitions, out ExistingFillIdentity arbitrated);
            SubmissionRecord record = SubmissionRecordExtractor.Extract(source,
                inferLegacySocketPanels, persistedIdentity);
            if (arbitrated != null)
                record.CircuitKey = (arbitrated.CircuitKey ?? "").Trim();
            return record;
        }

        /// <summary>
        /// 统一 U1S / U1DWG / Xmerge / BOQ 后置读取与 U1U/XSTS 的身份仲裁口径：
        /// 对同图复制或物理属性/UPSTREAM_INFO 已改新的图框，丢弃源框 JSON 里的
        /// 过期机台/回路/CircuitKey；物理属性不完整时才回退到带句柄校验的 JSON 读取。
        /// </summary>
        private static FrameInfoJsonRecord ReadArbitratedIdentity(
            Transaction transaction, ObjectId[] ids, CadBlockDefinitionReader definitions,
            out ExistingFillIdentity arbitrated)
        {
            FillSelection selection = FillSelectionCollector.Split(transaction,
                ids, false, definitions);
            string ownHandle = FrameInfoJsonBlockReader.OwnFrameHandle(transaction,
                selection.FrameBlockIds);
            if (FillSelectionCollector.TryReadExistingIdentity(transaction, selection,
                    ownHandle, out arbitrated, out _, out _))
            {
                return new FrameInfoJsonRecord
                {
                    MachineId = arbitrated.MachineId ?? "",
                    DeviceName = arbitrated.DeviceName ?? "",
                    Fr = arbitrated.UpstreamId ?? ""
                };
            }
            arbitrated = null;
            return FrameInfoJsonBlockReader.Read(transaction,
                FrameInfoJsonBlockReader.ReadSources(selection), ownHandle);
        }

        internal static IReadOnlyList<SubmissionRecord> ReadAll(CadContext ctx,
            IEnumerable<FrameRegionGroup> regions)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var result = new List<SubmissionRecord>();
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                var definitions = new CadBlockDefinitionReader(transaction);
                foreach (FrameRegionGroup region in regions ?? Enumerable.Empty<FrameRegionGroup>())
                {
                    if (region == null)
                    {
                        result.Add(null);
                        continue;
                    }
                    try
                    {
                        result.Add(Read(ctx, transaction, region, definitions));
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("图框 " + region.Handle
                            + " 无法读取提交信息：" + ex.Message, ex);
                    }
                }
                transaction.Commit();
            }
            return result;
        }
    }
}
