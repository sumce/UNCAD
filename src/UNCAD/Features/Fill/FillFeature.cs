using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Core.Stat;
using UNCAD.Core.Text;
using UNCAD.Infra;
using UNCAD.UI;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// UNC_FILL：按 Excel 机台/设备名称填充清单表，并聚合框选文字的电缆/桥架长度到图框块属性。
    /// 流程（按用户定义顺序）：
    ///   ① 用户先框选内容（表格 / 图框块 / DEVICENAME 设备块 / 统计文字）→ ② 执行命令
    ///      → ③ 弹窗口：机台ID 输入自动补全 → ④ 选设备名称/回路（底部实时预览）
    ///      → ⑤ 自动填入：
    ///        表格：按实际存在的电缆/桥架/线管/软管/插座动态生成有序清单行
    ///        图框块属性：MACHINEID-DEVICE / MACHINEID-POWER / CABLE_INFO /
    ///                    BRIDGE_FRAME_INFO / CONDUIT_INFO
    ///        设备动态块：DEVICENAME = 所选 Excel 回路名称
    ///        统计文字：按 UNC_STAT 规则读取，原内容保持不变
    /// 起始行由 UNC_SET → Excel 填充 → 表格起始行控制（默认 1 = 第一数据行）。
    /// </summary>
    [Feature("fill", "Excel 填充清单表",
        RibbonPanel = "标注",
        Commands = "UNC_FILL",
        Description = "动态填充清单、图框属性以及设备块 DEVICENAME 回路名称")]
    public class FillFeature : CommandBase
    {
        [CommandMethod("UNC_FILL", CommandFlags.UsePickSet)]
        public void UncadFill() => Run();

        protected override void Execute(CadContext ctx)
        {
            // ① 选择：执行命令前已框选的内容优先（SelectImplied 必须在任何提示之前调用）
            ObjectId[] tableIds, textIds, frameBlockIds, deviceBlockIds;
            ObjectId[] upstreamInfoBlockIds, upstreamAxisBlockIds, downstreamAxisBlockIds;
            GetSelection(ctx, out tableIds, out textIds, out frameBlockIds, out deviceBlockIds,
                out upstreamInfoBlockIds, out upstreamAxisBlockIds, out downstreamAxisBlockIds);
            if ((tableIds == null || tableIds.Length == 0)
                && (textIds == null || textIds.Length == 0)
                && (frameBlockIds == null || frameBlockIds.Length == 0)
                && (deviceBlockIds == null || deviceBlockIds.Length == 0)
                && (upstreamInfoBlockIds == null || upstreamInfoBlockIds.Length == 0)
                && (upstreamAxisBlockIds == null || upstreamAxisBlockIds.Length == 0)
                && (downstreamAxisBlockIds == null || downstreamAxisBlockIds.Length == 0))
            {
                ctx.Write("\n[UNC_FILL] 未找到清单表/图框块/设备块/上下游信息块/统计文字。");
                return;
            }
            if (tableIds.Length > 1 || frameBlockIds.Length > 1)
            {
                ctx.Write("\n[UNC_FILL] 一次只允许一个清单表和一个目标图框块，避免批量误写。");
                return;
            }

            // 同一次框选复用 UNC_STAT 规则，统计电缆长度和各规格桥架长度。
            double mmPerGrid = Settings.GetDouble(ConfigKeys.UnaddMmPerGrid, 250.0);
            CableStatResult stat = CalculateSelectionStats(ctx, textIds, mmPerGrid);

            // ② Excel 文件：记住上次路径，不每次弹窗
            string path = Settings.Get(ConfigKeys.FillExcelPath, "");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                using (var dlg = new OpenFileDialog
                {
                    Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
                    Title = "选择机台数据 Excel"
                })
                {
                    if (dlg.ShowDialog(new WindowWrapper(
                            Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle)) != DialogResult.OK)
                        return;
                    path = dlg.FileName;
                }
                Settings.Set(ConfigKeys.FillExcelPath, path);
                ctx.Write("\n[UNC_FILL] 已记住 Excel: " + path);
            }
            else
            {
                ctx.Write("\n[UNC_FILL] 使用上次 Excel: " + path + "（UNC_SET → Excel 填充 可修改）");
            }

            // ③ Sheet1 每次重新读取；固定 Sheet2 可独立配置并按文件版本缓存。
            string catalogPath = Settings.Get(ConfigKeys.FillCatalogPath, "").Trim();
            if (catalogPath.Length > 0 && !File.Exists(catalogPath))
            {
                ctx.Write("\n[UNC_FILL] 固定清单 Excel 不存在: " + catalogPath
                    + "（UNC_SET → Excel 填充 可修改）");
                return;
            }
            FillWorkbookSnapshot workbook;
            try
            {
                workbook = FillWorkbookSnapshot.Load(path, catalogPath);
            }
            catch (System.Exception ex)
            {
                ctx.Write("\n[UNC_FILL] 读取 Excel 失败: " + ex.Message);
                Log.Error("UNC_FILL read excel failed", ex);
                return;
            }
            ctx.Write("\n[UNC_FILL] 机台数据已刷新；固定清单 "
                + (workbook.CatalogCacheHit ? "已使用缓存" : "已重新加载")
                + ": " + workbook.CatalogSourcePath);
            List<string> machineIds = workbook.MachineIds;
            List<ListItem> listItems = workbook.ListItems;
            if (machineIds.Count == 0)
            {
                ctx.Write("\n[UNC_FILL] Excel 中无机台ID数据。");
                return;
            }
            if (listItems.Count == 0)
                ctx.Write("\n[UNC_FILL] 警告：未读取到清单项目，编号列将留空。");

            // 桥架信息（配置 UNC_FILL_BRIDGE，空=不写 BRIDGE_FRAME_INFO）
            string bridgeInfo = Settings.Get(ConfigKeys.FillBridge, "");
            int startRow = (int)Settings.GetDouble(ConfigKeys.FillTableRow, 1.0); // 1 基数据行号（1 = NO.1）
            double fillTextHeight = Settings.GetDouble(
                ConfigKeys.FillTextHeight, TableFillFormatter.DefaultTextHeight);
            if (fillTextHeight <= 0) fillTextHeight = TableFillFormatter.DefaultTextHeight;
            int startRow1 = startRow;

            Func<MachineRow, string> preview = row =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("▼ 回路详情");
                sb.AppendLine("  机台/设备：" + row.MachineId + " / " + row.CircuitName);
                sb.AppendLine("  盘柜类型：" + (string.IsNullOrWhiteSpace(row.Next) ? "未指定" : row.Next));
                sb.AppendLine("  FR：" + row.Fr);
                sb.AppendLine("  配电详情：" + row.Detail);
                sb.AppendLine("  下游轴位：" + row.DownstreamAxis + " ｜ 上游轴位：" + row.UpstreamAxis);
                List<TableFillRow> plannedRows = TableFillPlanner.Build(row, listItems, stat);
                sb.AppendLine("▼ 表格写入（覆盖，从 No." + startRow1 + " 行开始，共 "
                    + plannedRows.Count + " 项，文字高度 "
                    + TextFormatter.FormatNum(fillTextHeight) + "）");
                for (int i = 0; i < plannedRows.Count; i++)
                {
                    TableFillRow planned = plannedRows[i];
                    sb.AppendLine("  No." + (startRow1 + i) + " " + planned.Name
                        + " ｜ " + planned.Unit + " "
                        + (planned.Quantity.Length > 0 ? planned.Quantity : "数量待定")
                        + " ｜ 编号 " + (planned.Code.Length > 0 ? planned.Code : "未匹配"));
                }
                if (frameBlockIds != null && frameBlockIds.Length > 0)
                {
                    sb.AppendLine("▼ 图框块属性（" + frameBlockIds.Length + " 个块）");
                    var vals = FrameBlockFiller.BuildValues(row, bridgeInfo, stat);
                    foreach (var kv in vals)
                        sb.AppendLine("  " + kv.Key + " = " + kv.Value);
                }
                if (deviceBlockIds != null && deviceBlockIds.Length > 0)
                    sb.AppendLine("▼ 设备动态块（" + deviceBlockIds.Length
                        + " 个）：DEVICENAME = " + DeviceBlockFiller.BuildValue(row));
                if (upstreamInfoBlockIds.Length > 0)
                    sb.AppendLine("▼ 上游信息块：" + row.Fr + " / " + row.Detail);
                if (upstreamAxisBlockIds.Length > 0)
                    sb.AppendLine("▼ 上游轴位块：US = " + ConnectionBlockFiller.UpstreamAxis(row));
                if (downstreamAxisBlockIds.Length > 0)
                    sb.AppendLine("▼ 下游轴位块：DS = " + ConnectionBlockFiller.DownstreamAxis(row));
                if (textIds != null && textIds.Length > 0)
                    sb.AppendLine("▼ 统计源文字（" + textIds.Length + " 个，保持原内容）");
                return sb.ToString();
            };

            MachineRow picked;
            using (var f = new MachinePickerForm(machineIds, workbook.FindRows, preview))
            {
                if (f.ShowDialog(new WindowWrapper(
                        Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle)) != DialogResult.OK)
                    return;
                picked = f.Selected;
            }
            ctx.Write("\n[UNC_FILL] 已选择: " + picked.MachineId + " " + picked.CircuitName);

            // ⑤ 先生成结构化清单行，再统一写表和图框属性
            List<TableFillRow> tableRows = TableFillPlanner.Build(picked, listItems, stat);
            ConfigPrinter.Print(ctx, "UNC_FILL",
                ("清单行数", tableRows.Count.ToString()),
                ("顺序", string.Join(" → ", tableRows.ConvertAll(r => r.Name))));

            int filled = FillTable(ctx, tableIds, startRow, tableRows, fillTextHeight);
            int[] blockStat = FillBlockAttributes(ctx, frameBlockIds, picked, bridgeInfo, stat);
            int[] deviceBlockStat = FillDeviceBlockAttributes(ctx, deviceBlockIds, DeviceBlockFiller.BuildValue(picked));
            int[] upstreamInfoStat = FillTaggedBlockAttributes(ctx, upstreamInfoBlockIds,
                ConnectionBlockFiller.TagUpstreamInfo, ConnectionBlockFiller.UpstreamInfo(picked), true);
            int[] upstreamAxisStat = FillTaggedBlockAttributes(ctx, upstreamAxisBlockIds,
                ConnectionBlockFiller.TagUpstreamAxis, ConnectionBlockFiller.UpstreamAxis(picked), false);
            int[] downstreamAxisStat = FillTaggedBlockAttributes(ctx, downstreamAxisBlockIds,
                ConnectionBlockFiller.TagDownstreamAxis, ConnectionBlockFiller.DownstreamAxis(picked), false);
            SelectionService.ClearPickFirst(ctx);
            ctx.Write("\n[UNC_FILL] 完成：表格写入 " + filled + " 行；块属性更新 " + blockStat[0]
                + " 个块共 " + blockStat[1] + " 项；统计电缆 "
                + TextFormatter.FormatNum(stat.CableSum) + "M，桥架规格 " + stat.Bridges.Count
                + " 项，线管规格 " + stat.Conduits.Count + " 项；设备动态块更新 "
                + deviceBlockStat[0] + " 个；上游信息 " + upstreamInfoStat[0]
                + " 个，上游轴位 " + upstreamAxisStat[0] + " 个，下游轴位 "
                + downstreamAxisStat[0] + " 个。");
        }

        /// <summary>
        /// 收集选择：优先预选择（用户先框选再执行命令），否则提示框选。
        /// 返回 Table、统计文字、图框块以及带 DEVICENAME 的设备块。
        /// </summary>
        private static void GetSelection(CadContext ctx, out ObjectId[] tables, out ObjectId[] texts,
            out ObjectId[] frameBlocks, out ObjectId[] deviceBlocks,
            out ObjectId[] upstreamInfoBlocks, out ObjectId[] upstreamAxisBlocks,
            out ObjectId[] downstreamAxisBlocks)
        {
            tables = new ObjectId[0];
            texts = new ObjectId[0];
            frameBlocks = new ObjectId[0];
            deviceBlocks = new ObjectId[0];
            upstreamInfoBlocks = new ObjectId[0];
            upstreamAxisBlocks = new ObjectId[0];
            downstreamAxisBlocks = new ObjectId[0];

            var ss = ctx.Ed.SelectImplied();
            if (ss.Status == PromptStatus.OK && ss.Value != null && ss.Value.Count > 0)
            {
                SplitSelection(ctx, ss.Value.GetObjectIds(), out tables, out texts,
                    out frameBlocks, out deviceBlocks, out upstreamInfoBlocks,
                    out upstreamAxisBlocks, out downstreamAxisBlocks);
                if (tables.Length > 0 || texts.Length > 0 || frameBlocks.Length > 0
                    || deviceBlocks.Length > 0 || upstreamInfoBlocks.Length > 0
                    || upstreamAxisBlocks.Length > 0 || downstreamAxisBlocks.Length > 0)
                {
                    ctx.Write("\n[UNC_FILL] 已使用预选：表格 " + tables.Length + " 个，统计文字 "
                        + texts.Length + " 个，图框块 " + frameBlocks.Length
                        + " 个，DEVICENAME 设备块 " + deviceBlocks.Length
                        + " 个，上游信息块 " + upstreamInfoBlocks.Length + " 个，US/DS 轴位块 "
                        + (upstreamAxisBlocks.Length + downstreamAxisBlocks.Length) + " 个。");
                    return;
                }
            }
            var picked = SelectionService.Pick(ctx, "请框选或点选清单表/图框块/设备块/统计文字: ",
                new TypedValue(0, "TEXT,MTEXT,ACAD_TABLE,INSERT"));
            if (picked == null) return;
            SplitSelection(ctx, picked, out tables, out texts, out frameBlocks, out deviceBlocks,
                out upstreamInfoBlocks, out upstreamAxisBlocks, out downstreamAxisBlocks);
        }

        private static void SplitSelection(CadContext ctx, ObjectId[] ids,
            out ObjectId[] tables, out ObjectId[] texts,
            out ObjectId[] frameBlocks, out ObjectId[] deviceBlocks,
            out ObjectId[] upstreamInfoBlocks, out ObjectId[] upstreamAxisBlocks,
            out ObjectId[] downstreamAxisBlocks)
        {
            var t = new List<ObjectId>();
            var x = new List<ObjectId>();
            var f = new List<ObjectId>();
            var d = new List<ObjectId>();
            var ui = new List<ObjectId>();
            var us = new List<ObjectId>();
            var ds = new List<ObjectId>();
            try
            {
                using (var tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead, true) as Entity; // 容忍已删除
                        if (ent is Table) t.Add(id);
                        else if (ent is DBText || ent is MText) x.Add(id);
                        else if (ent is BlockReference br)
                        {
                            if (IsFillTargetBlock(tr, br)) f.Add(id);
                            if (TryGetBlockValue(tr, br, DeviceBlockFiller.TagDeviceName, out _)) d.Add(id);
                            if (TryGetBlockValue(tr, br, ConnectionBlockFiller.TagUpstreamInfo, out _)) ui.Add(id);
                            if (TryGetBlockValue(tr, br, ConnectionBlockFiller.TagUpstreamAxis, out _)) us.Add(id);
                            if (TryGetBlockValue(tr, br, ConnectionBlockFiller.TagDownstreamAxis, out _)) ds.Add(id);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn("UNC_FILL selection split failed: " + ex.Message);
            }
            tables = t.ToArray();
            texts = x.ToArray();
            frameBlocks = f.ToArray();
            deviceBlocks = d.ToArray();
            upstreamInfoBlocks = ui.ToArray();
            upstreamAxisBlocks = us.ToArray();
            downstreamAxisBlocks = ds.ToArray();
        }

        private static bool IsFillTargetBlock(Transaction tr, BlockReference block)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true) as AttributeReference;
                if (attribute != null && FrameBlockFiller.IsKnownTag(attribute.Tag)) return true;
            }
            return false;
        }

        private static bool TryGetBlockValue(Transaction tr, BlockReference block,
            string name, out string value)
        {
            foreach (ObjectId attributeId in block.AttributeCollection)
            {
                var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true) as AttributeReference;
                if (attribute != null && string.Equals(attribute.Tag, name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = attribute.TextString ?? "";
                    return true;
                }
            }

            if (block.IsDynamicBlock)
            {
                foreach (DynamicBlockReferenceProperty property in block.DynamicBlockReferencePropertyCollection)
                {
                    if (!string.Equals(property.PropertyName, name, StringComparison.OrdinalIgnoreCase)) continue;
                    value = Convert.ToString(property.Value) ?? "";
                    return true;
                }
            }

            var recordIds = new List<ObjectId> { block.BlockTableRecord };
            if (block.IsDynamicBlock && block.DynamicBlockTableRecord != block.BlockTableRecord)
                recordIds.Add(block.DynamicBlockTableRecord);
            foreach (ObjectId recordId in recordIds)
            {
                var record = tr.GetObject(recordId, OpenMode.ForRead, true) as BlockTableRecord;
                if (record == null) continue;
                foreach (ObjectId entityId in record)
                {
                    var definition = tr.GetObject(entityId, OpenMode.ForRead, true) as AttributeDefinition;
                    if (definition == null || !string.Equals(definition.Tag, name,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    value = definition.TextString ?? "";
                    return true;
                }
            }

            value = "";
            return false;
        }

        /// <summary>把选中设备块的 DEVICENAME 替换为 Excel 回路名称。</summary>
        private static int[] FillDeviceBlockAttributes(CadContext ctx, ObjectId[] blockIds,
            string circuitName)
        {
            string value = (circuitName ?? "").Trim();
            if (blockIds == null || blockIds.Length == 0 || value.Length == 0)
                return new[] { 0, 0 };

            int blocks = 0, values = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    bool touched = false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                            as AttributeReference;
                        if (attribute == null || !string.Equals(attribute.Tag, DeviceBlockFiller.TagDeviceName,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        attribute.UpgradeOpen();
                        attribute.TextString = value;
                        attribute.AdjustAlignment(ctx.Db);
                        values++;
                        touched = true;
                    }

                    if (!touched && block.IsDynamicBlock)
                    {
                        foreach (DynamicBlockReferenceProperty property
                            in block.DynamicBlockReferencePropertyCollection)
                        {
                            if (!string.Equals(property.PropertyName, DeviceBlockFiller.TagDeviceName,
                                StringComparison.OrdinalIgnoreCase) || property.ReadOnly) continue;
                            try
                            {
                                block.UpgradeOpen();
                                property.Value = value;
                                values++;
                                touched = true;
                            }
                            catch (System.Exception ex)
                            {
                                Log.Warn("UNC_FILL DEVICENAME dynamic property failed: " + ex.Message);
                            }
                            break;
                        }
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new[] { blocks, values };
        }

        /// <summary>按标签填充普通或 MText 块属性，返回 [块数, 属性数]。</summary>
        private static int[] FillTaggedBlockAttributes(CadContext ctx, ObjectId[] blockIds,
            string tag, string value, bool multiline)
        {
            if (blockIds == null || blockIds.Length == 0) return new[] { 0, 0 };
            int blocks = 0, attributes = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in blockIds)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    bool touched = false;
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = tr.GetObject(attributeId, OpenMode.ForRead, true)
                            as AttributeReference;
                        if (attribute == null || !string.Equals(attribute.Tag, tag,
                            StringComparison.OrdinalIgnoreCase)) continue;

                        attribute.UpgradeOpen();
                        attribute.TextString = value ?? "";
                        if (multiline && attribute.IsMTextAttribute)
                        {
                            using (MText mtext = attribute.MTextAttribute)
                            {
                                if (mtext != null)
                                {
                                    mtext.Contents = value ?? "";
                                    attribute.MTextAttribute = mtext;
                                }
                            }
                            attribute.UpdateMTextAttribute();
                        }
                        attribute.AdjustAlignment(ctx.Db);
                        attributes++;
                        touched = true;
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new[] { blocks, attributes };
        }

        /// <summary>
        /// 按 UNC_STAT 规则统计同一次框选中的 TEXT/MTEXT。
        /// </summary>
        private static CableStatResult CalculateSelectionStats(CadContext ctx, ObjectId[] textIds, double mmPerGrid)
        {
            var lines = new List<string>();
            if (textIds != null && textIds.Length > 0)
            {
                using (var tr = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in textIds)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead, true) as Entity;
                        if (ent is DBText t) lines.Add(t.TextString);
                        else if (ent is MText mt) lines.AddRange(TextParser.SplitMTextLines(mt.Contents));
                    }
                }
            }
            return StatCalculator.Calculate(lines.ConvertAll(TextParser.CleanMText), mmPerGrid);
        }

        /// <summary>
        /// 把选中块参照的属性按标签写入（MACHINEID-DEVICE / MACHINEID-POWER /
        /// CABLE_INFO / BRIDGE_FRAME_INFO / CONDUIT_INFO）。返回 [更新的块数, 更新的属性项数]。
        /// </summary>
        private static int[] FillBlockAttributes(CadContext ctx, ObjectId[] blockIds,
            MachineRow picked, string bridgeInfo, CableStatResult stat)
        {
            if (blockIds == null || blockIds.Length == 0) return new[] { 0, 0 };
            var values = FrameBlockFiller.BuildValues(picked, bridgeInfo, stat);
            if (values.Count == 0) return new[] { 0, 0 };

            int blocks = 0, attrs = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (var id in blockIds)
                {
                    var br = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (br == null || br.AttributeCollection == null || br.AttributeCollection.Count == 0)
                        continue;
                    bool touched = false;
                    foreach (ObjectId attId in br.AttributeCollection)
                    {
                        var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                        if (att == null) continue;
                        if (!values.TryGetValue(att.Tag, out string v)) continue;
                        att.UpgradeOpen();
                        att.TextString = v;
                        att.AdjustAlignment(ctx.Db);
                        attrs++;
                        touched = true;
                    }
                    if (touched) blocks++;
                }
                tr.Commit();
            }
            return new[] { blocks, attrs };
        }

        /// <summary>
        /// 覆盖模式填表：从配置起始数据行开始，按计划顺序写入所有实际存在的项目。
        /// startRow 为 1 基数据行号（1 = 第一数据行，默认）。
        /// </summary>
        private static int FillTable(CadContext ctx, ObjectId[] ids, int startRow,
            List<TableFillRow> plannedRows, double textHeight)
        {
            int filled = 0;
            using (var tr = ctx.Db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var tbl = tr.GetObject(id, OpenMode.ForRead) as Table;
                    if (tbl == null) continue;
                    if (tbl.Rows.Count < 2 || tbl.Columns.Count < 6)
                    {
                        ctx.Write("\n[UNC_FILL] 已跳过格式不兼容的表格：至少需要表头和 1 个数据行、共 6 列。");
                        continue;
                    }
                    tbl.UpgradeOpen();
                    double[] rowHeights = CaptureRowHeights(tbl);
                    double[] columnWidths = CaptureColumnWidths(tbl);

                    // 覆盖模式：从表头下一行（NO.1）起直接覆盖，不再找空行
                    // 起始数据行 = 表头下一行 + (配置-1)，配置默认 1 = NO.1
                    int r = FirstDataRow(tbl) + (Math.Max(1, startRow) - 1);
                    if (r < 0 || r >= tbl.Rows.Count) continue;
                    // 防御：目标行若仍是表头特征行（标题/表头/合并行），继续下移
                    int guard = 0;
                    while (r < tbl.Rows.Count && IsHeaderLike(tbl, r) && guard++ < 8) r++;
                    if (r >= tbl.Rows.Count || plannedRows == null || plannedRows.Count == 0) continue;
                    int available = tbl.Rows.Count - r;
                    if (plannedRows.Count > available)
                    {
                        ctx.Write("\n[UNC_FILL] 表格数据行不足：需要 " + plannedRows.Count
                            + " 行，当前仅剩 " + available + " 行，未写入该表格。");
                        continue;
                    }

                    tbl.SuppressRegenerateTable(true);
                    try
                    {
                        // 清理起始行之后的旧生成内容，保留 NO. 编号列和全部单元格格式。
                        for (int clearRow = r; clearRow < tbl.Rows.Count; clearRow++)
                            for (int column = 1; column <= 5; column++)
                                SetCellTextPreservingFormat(tbl, clearRow, column, "");

                        for (int i = 0; i < plannedRows.Count; i++)
                        {
                            TableFillRow planned = plannedRows[i];
                            int targetRow = r + i;
                            SetCellTextPreservingFormat(tbl, targetRow, 1, planned.Name, textHeight, true);
                            SetCellTextPreservingFormat(tbl, targetRow, 2, planned.Description, textHeight, true);
                            SetCellTextPreservingFormat(tbl, targetRow, 3, planned.Unit, textHeight, true);
                            SetCellTextPreservingFormat(tbl, targetRow, 4, planned.Quantity, textHeight, true);
                            SetCellTextPreservingFormat(tbl, targetRow, 5, planned.Code, textHeight, true);
                        }
                        RestoreTableDimensions(tbl, rowHeights, columnWidths);
                        LockGeneratedRowHeights(tbl, r, plannedRows.Count);
                    }
                    finally
                    {
                        tbl.SuppressRegenerateTable(false);
                        RestoreTableDimensions(tbl, rowHeights, columnWidths);
                        LockGeneratedRowHeights(tbl, r, plannedRows.Count);
                    }
                    filled += plannedRows.Count;
                }
                tr.Commit();
            }
            return filled;
        }

        /// <summary>替换内容并保留文字样式；可指定写入文字高度。</summary>
        private static void SetCellTextPreservingFormat(Table tbl, int row, int column,
            string text, double? targetTextHeight = null, bool fitToCell = false)
        {
            Cell cell = tbl.Cells[row, column];
            double? originalTextHeight = cell.TextHeight;
            ObjectId? textStyleId = cell.TextStyleId;

            cell.TextString = text ?? "";
            cell.TextHeight = targetTextHeight ?? originalTextHeight;
            cell.TextStyleId = textStyleId;
            if (fitToCell)
            {
                foreach (CellContent content in cell.Contents)
                {
                    if (targetTextHeight.HasValue) content.TextHeight = targetTextHeight.Value;
                    content.IsAutoScale = true;
                }
            }
        }

        private static double[] CaptureRowHeights(Table table)
        {
            var heights = new double[table.Rows.Count];
            for (int row = 0; row < heights.Length; row++)
                heights[row] = table.Rows[row].Height;
            return heights;
        }

        private static double[] CaptureColumnWidths(Table table)
        {
            var widths = new double[table.Columns.Count];
            for (int column = 0; column < widths.Length; column++)
                widths[column] = table.Columns[column].Width;
            return widths;
        }

        /// <summary>长文本写入后恢复原行高和列宽，防止表格外框被自动撑大。</summary>
        private static void RestoreTableDimensions(Table table, double[] rowHeights,
            double[] columnWidths)
        {
            for (int row = 0; row < rowHeights.Length && row < table.Rows.Count; row++)
            {
                try { table.Rows[row].Height = rowHeights[row]; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_FILL restore row height " + row + " failed: " + ex.Message);
                }
            }
            for (int column = 0; column < columnWidths.Length && column < table.Columns.Count; column++)
            {
                try { table.Columns[column].Width = columnWidths[column]; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_FILL restore column width " + column + " failed: " + ex.Message);
                }
            }
        }

        /// <summary>本次生成的清单行统一锁定为设计图纸要求的固定行高。</summary>
        private static void LockGeneratedRowHeights(Table table, int startRow, int count)
        {
            int endRow = Math.Min(table.Rows.Count, startRow + count);
            for (int row = Math.Max(0, startRow); row < endRow; row++)
            {
                try { table.Rows[row].Height = TableFillFormatter.GeneratedRowHeight; }
                catch (System.Exception ex)
                {
                    Log.Warn("UNC_FILL lock generated row height " + row + " failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 定位第一个数据行：
        ///   ① 扫描前若干行，取"最后一个表头特征行"（标题/表头都可能带 序号/项目/名称 等字样），
        ///      数据行 = 它下面的第一个非表头行——即使表头在第二行、措辞不同也能命中；
        ///   ② 无表头特征时，找第一个序号列是纯数字的行；
        ///   ③ 最后回退第 1 行。
        /// </summary>
        private static int FirstDataRow(Table tbl)
        {
            int rows = tbl.Rows.Count;
            int limit = Math.Min(rows, 10);
            int header = -1;
            for (int i = 0; i < limit; i++)
                if (IsHeaderLike(tbl, i)) header = i;

            if (header >= 0)
            {
                int r = header + 1;
                while (r < rows && IsHeaderLike(tbl, r)) r++;
                return Math.Min(r, rows - 1);
            }

            for (int i = 0; i < limit; i++)
            {
                string c0 = tbl.Cells[i, 0].TextString.Trim();
                if (TableLayoutClassifier.IsNumberedDataRow(c0)) return i;
            }
            return rows > 1 ? 1 : 0;
        }

        /// <summary>
        /// 是否表头特征行：第 1 列含 NO/序号/编号/项次，
        /// 或第 2 列含 项目/名称/设备/回路，或第 6 列含 编码/编号/项次。
        /// </summary>
        private static bool IsHeaderLike(Table tbl, int i)
        {
            string c0 = tbl.Cells[i, 0].TextString.Trim();
            string c1 = tbl.Cells[i, 1].TextString.Trim();
            string c5 = tbl.Columns.Count > 5 ? tbl.Cells[i, 5].TextString.Trim() : "";
            return TableLayoutClassifier.IsHeaderLike(c0, c1, c5);
        }

    }
}
