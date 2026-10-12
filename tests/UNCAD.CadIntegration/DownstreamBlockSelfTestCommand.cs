using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Excel;
using UNCAD.Core.Fill;
using UNCAD.Features.Fill;

namespace UNCAD.CadIntegration
{
    /// <summary>
    /// Downstream 块专项诊断（**只读**：只遍历块定义与动态属性，不修改任何实体）。
    ///
    /// 目的：在一张真实图纸上确认
    /// 1. 下游端块的**真实块名**是什么（精确名 Downstream？还是别的写法）；
    /// 2. 它的动态属性/可见性状态当前值是什么；
    /// 3. 产品代码 CadDynamicBlockStateService.TryReadDownstreamSocketBox 读出来什么；
    /// 4. 按规则会不会额外加一套 4.x 插座箱/盘（以及"永不加断路器"）。
    ///
    /// 报告写到 %UNCAD_DOWNSTREAM_REPORT%（默认 %TEMP%\uncad-downstream-report.txt）。
    /// </summary>
    public sealed class DownstreamBlockSelfTestCommand
    {
        private readonly List<string> _log = new List<string>();

        [CommandMethod("UNCAD_DOWNSTREAM_SELFTEST")]
        public void Run()
        {
            Document document = Application.DocumentManager.MdiActiveDocument;
            try
            {
                Report(document);
                WriteResult("PASS");
            }
            catch (System.Exception ex)
            {
                Info("异常: " + ex);
                WriteResult("FAIL\r\n" + ex);
                throw;
            }
            finally
            {
                Dump();
            }
        }

        private void Report(Document document)
        {
            Database db = document.Database;
            Info("图纸: " + document.Name);
            Info("块定义总数: " + CountDefinitions(db));
            Info("");
            Info("=== 1) 相关块定义名（产品识别口径） ===");
            var downstreamIds = new List<ObjectId>();
            var deviceIds = new List<ObjectId>();
            var upstreamIds = new List<ObjectId>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId id in blocks)
                {
                    var record = tr.GetObject(id, OpenMode.ForRead) as BlockTableRecord;
                    if (record == null) continue;
                    string normalized = BlockNameNormalizer.RemoveMangledSuffix(record.Name);
                    bool dynamic = false;
                    try { dynamic = record.IsDynamicBlock; } catch { }
                    if (DynamicBlockStatePolicy.IsDownstreamBlock(normalized))
                        Info("  [Downstream] " + record.Name + "  动态块=" + dynamic);
                    else if (DynamicBlockStatePolicy.IsDeviceBlock(normalized))
                        Info("  [Device]     " + record.Name + "  动态块=" + dynamic);
                    else if (DynamicBlockStatePolicy.IsUpstreamBlock(normalized))
                        Info("  [upstream]   " + record.Name + "  动态块=" + dynamic);
                }
                Info("");

                Info("=== 2) 模型空间里的下游/设备/上游块参照及其动态属性 ===");
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in space)
                {
                    var block = tr.GetObject(id, OpenMode.ForRead, true) as BlockReference;
                    if (block == null) continue;
                    string name = EffectiveName(tr, block);
                    string normalized = BlockNameNormalizer.RemoveMangledSuffix(name);
                    bool isDownstream = DynamicBlockStatePolicy.IsDownstreamBlock(normalized);
                    bool isDevice = DynamicBlockStatePolicy.IsDeviceBlock(normalized);
                    bool isUpstream = DynamicBlockStatePolicy.IsUpstreamBlock(normalized);
                    if (!isDownstream && !isDevice && !isUpstream) continue;
                    string kind = isDownstream ? "Downstream" : isDevice ? "Device" : "upstream";
                    Info("  " + kind + " 句柄=" + block.Handle + " 块名=" + name
                        + " IsDynamicBlock=" + block.IsDynamicBlock);
                    if (isDownstream) downstreamIds.Add(id);
                    else if (isDevice) deviceIds.Add(id);
                    else upstreamIds.Add(id);
                    if (!block.IsDynamicBlock) continue;
                    try
                    {
                        foreach (DynamicBlockReferenceProperty property
                            in block.DynamicBlockReferencePropertyCollection)
                        {
                            string allowed = "";
                            try
                            {
                                object[] values = property.GetAllowedValues();
                                if (values != null && values.Length > 0)
                                    allowed = "  允许值=[" + string.Join(" | ",
                                        values.Select(v => Convert.ToString(v)).Take(8)) + "]";
                            }
                            catch (System.Exception ex) { allowed = "  允许值读取失败: " + ex.Message; }
                            Info("        属性 " + property.PropertyName + " = "
                                + Convert.ToString(property.Value) + allowed);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Info("        动态属性读取失败: " + ex.Message);
                    }
                }
                tr.Commit();
            }

            Info("");
            Info("=== 3) 产品代码读取 Downstream 状态 ===");
            Info("  Downstream 参照数: " + downstreamIds.Count
                + "，Device 参照数: " + deviceIds.Count
                + "，upstream 参照数: " + upstreamIds.Count);
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                bool result = CadDynamicBlockStateService.TryReadDownstreamState(tr,
                    downstreamIds.ToArray(), out DownstreamState readState,
                    out bool stateKnown, out string stateText, out int blockCount);
                Info("  TryReadDownstreamState -> 返回=" + result
                    + "  state=" + readState
                    + "  stateKnown=" + stateKnown
                    + "  命中块数=" + blockCount);
                Info("  状态文字: " + stateText);
                tr.Commit();
            }

            Info("");
            Info("=== 4) 规则判定 ===");
            bool hasOutletPanelInCatalog = false;
            try
            {
                var catalog = new BoqCatalogIndex(ListItemReader.ReadEmbedded());
                hasOutletPanelInCatalog = catalog.OutletPanels.Count > 0;
                Info("  内嵌固定清单里的插座盘(4.x)条目数: " + catalog.OutletPanels.Count);
            }
            catch (System.Exception ex)
            {
                Info("  内嵌清单读取失败: " + ex.Message);
            }
            var rows = DownstreamStatePolicy.Apply(new List<TableFillRow>(),
                new BoqCatalogIndex(new List<ListItem>()), true, true,
                DownstreamState.Unknown, FillRuntimeOptions.DefaultDownstreamSocketAmps,
                FillRuntimeOptions.DefaultDownstreamPanelAmps);
            Info("  断路器规则: Downstream 永不添加断路器（规则由 DownstreamStatePolicy 锁定）");
            Info("  附加清单规则: socket box → 额外一行 4.x 插座箱/盘（额定 "
                + FillRuntimeOptions.DefaultDownstreamSocketAmps.ToString("0.##")
                + "A，受“插座盘”开关控制）；I-line_Panel → 额外一行电盘（额定 "
                + FillRuntimeOptions.DefaultDownstreamPanelAmps.ToString("0.##")
                + "A，受“电盘”开关控制，**不加断路器**）；busbar connector → 只上色。"
                + "两个额定电流都可 U1SET 修改，不再用 U_配电信息 的电流匹配"
                + (hasOutletPanelInCatalog ? "（内嵌清单有 4.x 条目）" : "（内嵌清单没有 4.x 条目）"));
            Info("  基线行数（不触发时）: " + rows.Count);
        }

        private static int CountDefinitions(Database db)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                return blocks.Cast<ObjectId>().Count();
            }
        }

        private static string EffectiveName(Transaction tr, BlockReference block)
        {
            string fallback = "";
            foreach (ObjectId recordId in FrameRegionCollector.DefinitionIds(block))
            {
                try
                {
                    var record = tr.GetObject(recordId, OpenMode.ForRead, true)
                        as BlockTableRecord;
                    string name = record?.Name ?? "";
                    if (fallback.Length == 0) fallback = name;
                    string normalized = BlockNameNormalizer.RemoveMangledSuffix(name);
                    if (DynamicBlockStatePolicy.IsDeviceBlock(normalized)
                        || DynamicBlockStatePolicy.IsUpstreamBlock(normalized)
                        || DynamicBlockStatePolicy.IsDownstreamBlock(normalized))
                        return name;
                }
                catch { }
            }
            return fallback;
        }

        private void Info(string line) => _log.Add(line);

        private void Dump()
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_DOWNSTREAM_REPORT");
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "uncad-downstream-report.txt");
            try { File.WriteAllText(path, string.Join("\r\n", _log.ToArray()),
                new System.Text.UTF8Encoding(false)); }
            catch (System.Exception) { }
        }

        private static void WriteResult(string text)
        {
            string path = Environment.GetEnvironmentVariable("UNCAD_CAD_INTEGRATION_RESULT");
            if (!string.IsNullOrWhiteSpace(path)) File.WriteAllText(path, text);
        }
    }
}
