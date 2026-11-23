using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Core.Submission;
using UNCAD.Features.Fill;

namespace UNCAD.Features.Submit
{
    /// <summary>Shared adapter that reads one frame's current identity and BOQ state.</summary>
    internal static class FrameIdentityReader
    {
        /// <summary>
        /// Reads only the durable frame identity. Layout and statistics commands do not
        /// need the full BOQ/material extraction path; keeping this path lightweight also
        /// prevents an unrelated malformed table from hiding a valid machine ID.
        /// </summary>
        internal static ExistingFillIdentity ReadIdentity(Transaction transaction,
            FrameRegionGroup region, CadBlockDefinitionReader definitions = null)
        {
            string foreignJsonNote;
            return ReadIdentity(transaction, region, definitions, out foreignJsonNote);
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
        /// 的 MACHINEID-POWER/MACHINEID-DEVICE 属性，设备块 DEVICENAME 不参与，
        /// 也就不存在「图框属性与设备块不一致」的布局中止。设备块按插入点归属
        /// 分区，全图与单机选择的边界集不同会把外来块归进区域——布局身份必须
        /// 与选择集无关。JSON 读取与仲裁语义与 ReadIdentity 完全一致。
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

        /// <summary>
        /// Reads one already-collected entity group with the same identity precedence used by
        /// XLAYOUT, U1S, and automatic U1F/U1U submission. The caller owns the transaction.
        /// </summary>
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
            FrameInfoJsonRecord persistedIdentity = FrameInfoJsonBlockWriter.Read(transaction, ids);
            return SubmissionRecordExtractor.Extract(source, inferLegacySocketPanels,
                persistedIdentity);
        }

        /// <summary>
        /// Reads several frame identities with one read-only CAD transaction.
        /// The result is index-aligned with <paramref name="regions"/>: a null
        /// region yields a null entry instead of shifting later records.
        /// </summary>
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
