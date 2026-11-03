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
                    switch (transaction.GetObject(id, OpenMode.ForRead))
                    {
                        case DBText dbText:
                            text = dbText.TextString ?? "";
                            rotation = dbText.Rotation;
                            handle = dbText.Handle.ToString();
                            break;
                        case MText mText:
                            text = mText.Contents ?? "";
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
