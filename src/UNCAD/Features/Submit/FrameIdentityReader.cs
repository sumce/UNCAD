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
