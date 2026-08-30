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
                SubmissionRecord record = FrameIdentityReader.Read(ctx, group);
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
            var machineGroups = frames.GroupBy(frame => frame.MachineId,
                StringComparer.OrdinalIgnoreCase).ToList();
            var paths = machineGroups.Select(machine => Path.Combine(outputRoot, machine.Key,
                machine.Key + ".dwg")).ToList();
            // Export all selected machines as one batch. A later failure restores files written
            // earlier in the same command.
            using (var batch = new FileBatchRollback(paths))
            {
                try
                {
                    for (int exportIndex = 0; exportIndex < machineGroups.Count; exportIndex++)
                    {
                        IGrouping<string, DwgExportFrame> machine = machineGroups[exportIndex];
                        string target = paths[exportIndex];
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        IReadOnlyList<DwgFramePlacement> layout = DwgExportLayout.Arrange(machine);
                        WriteMachine(ctx.Db, target, layout, machine.Key);
                    }
                    batch.Complete();
                }
                catch
                {
                    batch.Rollback();
                    throw;
                }
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
                throw new DirectoryNotFoundException("无法确定 DWG 输出文件夹，请先在 U1SET 中设置。");
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
            IReadOnlyList<DwgFramePlacement> layout, string machineId)
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".dwg";
            try
            {
                var sourceIds = new ObjectIdCollection();
                var seen = new HashSet<ObjectId>();
                foreach (DwgFramePlacement placement in layout)
                {
                    DwgExportFrame frame = placement.Item as DwgExportFrame;
                    if (frame == null) throw new InvalidOperationException("DWG 导出布局对象无效。");
                    foreach (ObjectId id in frame.Group.EntityIds)
                    {
                        if (id.IsNull || !id.IsValid)
                            throw new InvalidDataException("图框包含无效实体ID。");
                        if (id.Database != sourceDatabase)
                            throw new InvalidOperationException("图框实体不属于当前源图数据库。");
                        if (seen.Add(id)) sourceIds.Add(id);
                    }
                }
                if (sourceIds.Count == 0)
                    throw new InvalidDataException("没有可导出的图框实体。");

                // One Wblock for the whole machine keeps all selected geometry and all source
                // dependent styles in one database, avoiding cross-database ObjectId errors.
                Database output;
                try
                {
                    output = sourceDatabase.Wblock(sourceIds, Point3d.Origin);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("U1DWG Wblock源实体失败：" + ex.Message, ex);
                }
                using (output)
                {
                    try
                    {
                        TransformExportedFrames(output, layout);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("U1DWG排列图框失败：" + ex.Message, ex);
                    }
                    try
                    {
                        AddMachineMetadata(output, layout[0], machineId);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("U1DWG写入机台信息失败：" + ex.Message, ex);
                    }
                    try
                    {
                        output.SaveAs(temporary, DwgVersion.Current);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("U1DWG保存DWG失败：" + ex.Message, ex);
                    }
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

        private static void AddMachineMetadata(Database database,
            DwgFramePlacement firstPlacement, string machineId)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = transaction.GetObject(database.CurrentSpaceId,
                    OpenMode.ForWrite) as BlockTableRecord;
                TextStyleTable styles = transaction.GetObject(database.TextStyleTableId,
                    OpenMode.ForRead) as TextStyleTable;
                ObjectId textStyle = styles.Has("Standard") ? styles["Standard"] : database.Textstyle;
                double left = firstPlacement.TranslationX + firstPlacement.Item.Boundary.MinX;
                double bottom = firstPlacement.TranslationY + firstPlacement.Item.Boundary.MinY;
                // Reserve a separate margin left of the first frame. Absolute left/base positioning
                // keeps AutoCAD from reinterpreting the text anchor inside the frame.
                double titleX = left - Math.Max(250000d, firstPlacement.Item.Boundary.Width * 0.5d);
                double titleY = bottom + Math.Max(firstPlacement.Item.Boundary.Height * 0.65d, 90000d);
                const double metadataHeight = 6000d;
                const double metadataGap = 9000d;
                AddMetadataText(space, transaction, machineId,
                    new Point3d(titleX, titleY, 0d), 25000d, textStyle);
                AddMetadataText(space, transaction, ProductMetadata.CompanyName + "  " + ProductMetadata.Website,
                    new Point3d(titleX, titleY - 35000d, 0d), metadataHeight, textStyle);
                AddMetadataText(space, transaction,
                    "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    new Point3d(titleX, titleY - 35000d - metadataGap, 0d),
                    metadataHeight, textStyle);
                AddMetadataText(space, transaction,
                    "Software Version: " + AboutInfo.Current().Version,
                    new Point3d(titleX, titleY - 35000d - metadataGap * 2d, 0d),
                    metadataHeight, textStyle);
                AddMetadataText(space, transaction,
                    "Windows User: " + Environment.UserName,
                    new Point3d(titleX, titleY - 35000d - metadataGap * 3d, 0d),
                    metadataHeight, textStyle);
                transaction.Commit();
            }
            try
            {
                SetFirstFrameView(database, firstPlacement);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("首框视图设置失败：" + ex.Message, ex);
            }
        }

        private static void AddMetadataText(BlockTableRecord space, Transaction transaction,
            string value, Point3d anchor, double height, ObjectId textStyle)
        {
            var text = new DBText();
            // Bind the new entity to the Wblock database before assigning database-owned IDs.
            text.SetDatabaseDefaults(space.Database);
            text.TextString = value ?? "";
            text.Height = height;
            text.TextStyleId = textStyle;
            text.Layer = "0";
            text.HorizontalMode = TextHorizontalMode.TextLeft;
            text.VerticalMode = TextVerticalMode.TextBase;
            text.Position = anchor;
            space.AppendEntity(text);
            transaction.AddNewlyCreatedDBObject(text, true);
        }

        private static void SetFirstFrameView(Database database, DwgFramePlacement firstPlacement)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                ViewportTable table = transaction.GetObject(database.ViewportTableId,
                    OpenMode.ForRead) as ViewportTable;
                if (table == null || !table.Has("*Active"))
                {
                    transaction.Commit();
                    return;
                }
                ViewportTableRecord view = transaction.GetObject(table["*Active"],
                    OpenMode.ForWrite) as ViewportTableRecord;
                if (view == null)
                {
                    transaction.Commit();
                    return;
                }
                double left = firstPlacement.TranslationX + firstPlacement.Item.Boundary.MinX;
                double bottom = firstPlacement.TranslationY + firstPlacement.Item.Boundary.MinY;
                view.CenterPoint = new Point2d(
                    left + firstPlacement.Item.Boundary.Width / 2d,
                    bottom + firstPlacement.Item.Boundary.Height / 2d);
                view.Width = Math.Max(1d, firstPlacement.Item.Boundary.Width * 1.25d);
                view.Height = Math.Max(1d, firstPlacement.Item.Boundary.Height * 1.25d);
                transaction.Commit();
            }
        }

        private static void TransformExportedFrames(Database database,
            IReadOnlyList<DwgFramePlacement> layout)
        {
            using (Transaction transaction = database.TransactionManager.StartTransaction())
            {
                BlockTableRecord space = transaction.GetObject(database.CurrentSpaceId,
                    OpenMode.ForRead) as BlockTableRecord;
                foreach (ObjectId id in space)
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForWrite, false) as Entity;
                    if (!TryExportAnchor(entity, out Point3d anchor)) continue;
                    DwgFramePlacement owner = null;
                    foreach (DwgFramePlacement candidate in layout)
                    {
                        if (candidate.Item.Boundary.Contains(anchor.X, anchor.Y))
                        {
                            owner = candidate;
                            break;
                        }
                    }
                    if (owner == null) continue;
                    entity.TransformBy(Matrix3d.Displacement(new Vector3d(
                        owner.TranslationX, owner.TranslationY, 0d)));
                }
                transaction.Commit();
            }
        }

        private static bool TryExportAnchor(Entity entity, out Point3d anchor)
        {
            if (entity is BlockReference block)
            {
                anchor = block.Position;
                return true;
            }
            try
            {
                Extents3d extents = entity.GeometricExtents;
                anchor = new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) / 2d,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) / 2d,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) / 2d);
                return true;
            }
            catch
            {
                anchor = Point3d.Origin;
                return false;
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
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (NotSupportedException) { }
            catch (IOException) { }

            bool movedOriginal = false;
            try
            {
                File.Move(target, backup);
                movedOriginal = true;
                File.Move(temporary, target);
                if (File.Exists(backup)) File.Delete(backup);
            }
            catch
            {
                if (movedOriginal && !File.Exists(target) && File.Exists(backup))
                    File.Move(backup, target);
                throw;
            }
        }
    }
}
