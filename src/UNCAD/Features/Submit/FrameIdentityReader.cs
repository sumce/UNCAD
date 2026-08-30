using System;
using System.Linq;
using UNCAD.Cad;
using UNCAD.Core.Submission;

namespace UNCAD.Features.Submit
{
    /// <summary>Shared adapter that reads one frame's current identity and BOQ state.</summary>
    internal static class FrameIdentityReader
    {
        public static SubmissionRecord Read(CadContext ctx, FrameRegionGroup region)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (region == null) throw new ArgumentNullException(nameof(region));
            return SubmissionRecordExtractor.Extract(CadSubmissionReader.Read(ctx,
                region.EntityIds.ToArray()));
        }
    }
}
