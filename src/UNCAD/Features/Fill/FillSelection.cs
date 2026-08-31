using Autodesk.AutoCAD.DatabaseServices;

namespace UNCAD.Features.Fill
{
    internal sealed class FillSelection
    {
        // Retain the raw selection so update mode can use selected frames as spatial anchors.
        public ObjectId[] SourceIds { get; set; } = new ObjectId[0];
        public ObjectId[] TableIds { get; set; } = new ObjectId[0];
        public ObjectId[] TextIds { get; set; } = new ObjectId[0];
        public ObjectId[] FrameBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] DeviceBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] RuanguanBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] FrameInfoJsonBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] UpstreamInfoBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] UpstreamStateBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] UpstreamAxisBlockIds { get; set; } = new ObjectId[0];
        public ObjectId[] DownstreamAxisBlockIds { get; set; } = new ObjectId[0];

        public bool HasWriteTargets => TableIds.Length > 0 || FrameBlockIds.Length > 0
            || DeviceBlockIds.Length > 0 || UpstreamInfoBlockIds.Length > 0
            || FrameInfoJsonBlockIds.Length > 0
            || UpstreamStateBlockIds.Length > 0
            || UpstreamAxisBlockIds.Length > 0 || DownstreamAxisBlockIds.Length > 0;

        public bool IsEmpty => TableIds.Length == 0 && TextIds.Length == 0
            && FrameBlockIds.Length == 0 && DeviceBlockIds.Length == 0
            && RuanguanBlockIds.Length == 0 && UpstreamInfoBlockIds.Length == 0
            && FrameInfoJsonBlockIds.Length == 0
            && UpstreamStateBlockIds.Length == 0
            && UpstreamAxisBlockIds.Length == 0 && DownstreamAxisBlockIds.Length == 0;
    }
}
