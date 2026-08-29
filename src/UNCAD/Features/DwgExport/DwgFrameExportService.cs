using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Cad;
using UNCAD.Core.Dwg;
using UNCAD.Core.Submission;
using UNCAD.Features.Submit;
using UNCAD.Infra;

namespace UNCAD.Features.DwgExport
{
    /// <summary>Summary returned after one U1DWG operation.</summary>
    internal sealed class DwgExportResult
    {
        public string FilePath { get; set; }
        public int MachineCount { get; set; }
        public int FrameCount { get; set; }
    }

    /// <summary>Source frame with the recognized machine and device identity.</summary>
    internal sealed class DwgExportFrame : DwgFrameLayoutItem
    {
        public DwgExportFrame(FrameRegionGroup group, string machineId, string deviceName)
            : base(machineId, deviceName, group.Boundary)
        {
            Group = group;
        }

        public FrameRegionGroup Group { get; }
    }

    /// <summary>Exports selected frame contents without modifying the active source database.</summary>
    internal static class DwgFrameExportService
    {
        public static DwgExportResult Export(CadContext ctx, IEnumerable<FrameRegionGroup> groups)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            var frames = new List<DwgExportFrame>();
            int index = 0;
            foreach (FrameRegionGroup group in groups ?? Enumerable.Empty<FrameRegionGroup>())
            {
                index++;
                SubmissionRecord record = SubmissionRecordExtractor.Extract(
                    CadSubmissionReader.Read(ctx, group.EntityIds.ToArray()));
                string machineId = (record.MachineId ?? "").Trim();
                string deviceName = (record.DeviceName ?? "").Trim();
                ValidatePathPart(machineId, "机台ID");
                if (deviceName.Length == 0)
                    throw new InvalidDataException("第 " + index + " 个图框缺少设备名称。");
                frames.Add(new DwgExportFrame(group, machineId, deviceName));
            }
            if (frames.Count == 0)
                throw new InvalidDataException("没有可导出的有效图框。");

            string outputRoot = ResolveOutputRoot();
            var paths = new List<string>();
            foreach (IGrouping<string, DwgExportFrame> machine in frames
                .GroupBy(frame => frame.MachineId, StringComparer.OrdinalIgnoreCase))
            {
                var devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DwgExportFrame frame in machine)
                    if (!devices.Add(frame.DeviceName))
                        throw new InvalidDataException("机台 " + machine.Key
                            + " 存在重复设备名称：" + frame.DeviceName + "。");
                string machineFolder = Path.Combine(outputRoot, machine.Key);
                Directory.CreateDirectory(machineFolder);
                string target = Path.Combine(machineFolder, machine.Key + ".dwg");
                IReadOnlyList<DwgFramePlacement> layout = DwgExportLayout.Arrange(machine);
                WriteMachine(ctx.Db, target, layout);
                paths.Add(target);
            }
            return new DwgExportResult
            {
                FilePath = string.Join("; ", paths),
                MachineCount = paths.Count,
                FrameCount = frames.Count
            };
        }

        private static string ResolveOutputRoot()
        {
            string configured = Settings.Get(ConfigKeys.SubmitFolder, "").Trim();
            string folder = configured.Length > 0
                ? configured
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (folder.Length == 0)
                throw new DirectoryNotFoundException("无法确定 DWG 输出文件夹，请先在 U1S 中设置。");
            Directory.CreateDirectory(folder);
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("DWG 输出文件夹不存在：" + folder);
            if (!string.Equals(configured, folder, StringComparison.OrdinalIgnoreCase))
                Settings.Set(ConfigKeys.SubmitFolder, folder);
            return folder;
        }

        private static void ValidatePathPart(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == ".."
                || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException(label + "包含非法文件名字符：" + value);
        }

        private static void WriteMachine(Database sourceDatabase, string target,
            IReadOnlyList<DwgFramePlacement> layout)
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".dwg";
            try
            {
                using (var output = new Database(true, true))
                {
                    foreach (DwgFramePlacement placement in layout)
                    {
                        var sourceIds = new ObjectIdCollection();
                        DwgExportFrame frame = placement.Item as DwgExportFrame;
                        if (frame == null) throw new InvalidOperationException("DWG 导出布局对象无效。");
                        foreach (ObjectId id in frame.Group.EntityIds)
                            sourceIds.Add(id);
                        if (sourceIds.Count == 0) continue;

                        var mapping = new IdMapping();
                        sourceDatabase.WblockCloneObjects(sourceIds, output.CurrentSpaceId,
                            mapping, DuplicateRecordCloning.Ignore, false);
                        using (Transaction transaction = output.TransactionManager.StartTransaction())
                        {
                            foreach (ObjectId sourceId in frame.Group.EntityIds)
                            {
                                if (!mapping.Contains(sourceId)) continue;
                                IdPair pair = mapping[sourceId];
                                if (!pair.IsCloned) continue;
                                Entity entity = transaction.GetObject(pair.Value, OpenMode.ForWrite, false)
                                    as Entity;
                                entity?.TransformBy(Matrix3d.Displacement(new Vector3d(
                                    placement.TranslationX, placement.TranslationY, 0d)));
                            }
                            transaction.Commit();
                        }
                    }
                    output.SaveAs(temporary, DwgVersion.Current);
                }
                ReplaceFile(temporary, target);
                temporary = null;
            }
            finally
            {
                try { if (!string.IsNullOrWhiteSpace(temporary) && File.Exists(temporary)) File.Delete(temporary); }
                catch { }
            }
        }

        private static void ReplaceFile(string temporary, string target)
        {
            if (!File.Exists(target))
            {
                File.Move(temporary, target);
                return;
            }
            string backup = target + ".bak";
            try
            {
                File.Replace(temporary, target, backup, true);
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch
            {
                File.Copy(temporary, target, true);
                File.Delete(temporary);
                if (File.Exists(backup)) File.Delete(backup);
            }
        }
    }
}
