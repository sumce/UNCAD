using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using UNCAD.Cad;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Features.Fill
{
    /// <summary>
    /// 从本次框选的统计文字里找老版设备内预留标记（横排毫米数文字，D-043）。
    /// 只看 selection.TextIds（用户本次框选范围，与统计同一输入），
    /// 不做全图扫描——万框图纸性能零增加；识别与决策口径见 DeviceReserveMarker。
    /// </summary>
    internal static class DeviceReserveMarkerReader
    {
        internal sealed class Result
        {
            /// <summary>识别出的候选（含待删除实体与毫米值）。</summary>
            public List<DeviceReserveMarkerHit> Hits { get; } =
                new List<DeviceReserveMarkerHit>();
            /// <summary>候选对应的文字实体（吸收后在写事务里删除）。</summary>
            public List<ObjectId> EntityIds { get; } = new List<ObjectId>();
        }

        /// <summary>扫描统计文字集；skipIds 用于排除图框属性派生文字等非标记来源。</summary>
        internal static Result Collect(Transaction transaction, ObjectId[] textIds)
        {
            var result = new Result();
            if (transaction == null || textIds == null || textIds.Length == 0)
                return result;
            foreach (ObjectId id in textIds)
            {
                if (!id.IsValid) continue;
                string text;
                double rotation;
                string handle;
                try
                {
                    DBObject source = transaction.GetObject(id, OpenMode.ForRead);
                    // 3.2.0.6（审计修复）：带 U1LX 线关联的文字 = UNCAD 自己生成的
                    // U1L/U1LX 毫米标注，不是现场手写的设备预留标。水平线的标注
                    //（正交草图的 "2000mm" 等）与 D-043 判据完全同形，不排除会被
                    // 当预留标吸收：文字被删、长度从米数统计剔除，一箭三伤。
                    // 判据本身不动（决策口径），读侧按来源排除。
                    if (!string.IsNullOrEmpty(
                            UNCAD.Cad.QuickLine.QuickLineMetadataStore
                                .ReadAssociatedLineHandle(source)))
                        continue;
                    switch (source)
                    {
                        case DBText dbText:
                            text = dbText.TextString ?? "";
                            rotation = dbText.Rotation;
                            handle = dbText.Handle.ToString();
                            break;
                        case MText mText:
                            // 3.2.0.2：先剥格式码再判据——旧版软件写的 MText 常带
                            // {\f字体;…}/\A 对齐码，原始 Contents 永远过不了纯数字
                            // 判据（统计链路一直有 CleanMText，这里漏了；用户报告
                            // 老框毫米标不被吸收的候选根因之一）。
                            text = UNCAD.Core.Text.TextParser.CleanMText(
                                mText.Contents ?? "");
                            rotation = mText.Rotation;
                            handle = mText.Handle.ToString();
                            break;
                        default:
                            continue;
                    }
                }
                catch (System.Exception)
                {
                    continue;   // 单个文字读不出不影响整批识别
                }
                if (!DeviceReserveMarker.IsHorizontal(rotation)) continue;
                if (!DeviceReserveMarker.TryParseMillimeters(text, out double millimeters))
                    continue;
                result.Hits.Add(new DeviceReserveMarkerHit(handle, text.Trim(), millimeters));
                result.EntityIds.Add(id);
            }
            return result;
        }

        /// <summary>便捷入口：自开只读事务扫描（调用点没有现成读事务时用）。</summary>
        internal static Result CollectInSelection(CadContext ctx, ObjectId[] textIds)
        {
            var result = new Result();
            if (ctx == null || textIds == null || textIds.Length == 0) return result;
            using (Transaction transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                result = Collect(transaction, textIds);
                transaction.Commit();
            }
            return result;
        }

        /// <summary>
        /// 在写事务里删除已吸收的标记文字；删除数量打日志。返回删除数。
        /// </summary>
        internal static int EraseAbsorbed(Transaction transaction,
            ObjectId[] entityIds, string commandLabel)
        {
            if (transaction == null || entityIds == null || entityIds.Length == 0)
                return 0;
            int erased = 0;
            foreach (ObjectId id in entityIds)
            {
                try
                {
                    DBObject entity = transaction.GetObject(id, OpenMode.ForWrite);
                    if (!entity.IsErased) entity.Erase();
                    erased++;
                }
                catch (System.Exception ex)
                {
                    Log.Warn(commandLabel + " 删除设备预留标记失败: " + ex.Message);
                }
            }
            if (erased > 0)
                Log.Info(commandLabel + " 已吸收并删除 " + erased + " 处设备预留标记。");
            return erased;
        }
    }
}
