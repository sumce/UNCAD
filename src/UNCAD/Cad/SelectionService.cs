using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace UNCAD.Cad
{
    /// <summary>选择集封装：统一提示、过滤与 PickFirst 机制。</summary>
    public static class SelectionService
    {
        /// <summary>弹出选择提示；取消/空选择时返回 null。</summary>
        public static ObjectId[] Pick(CadContext ctx, string prompt, params TypedValue[] filter)
        {
            var res = ctx.Ed.GetSelection(
                new PromptSelectionOptions { MessageForAdding = prompt },
                new SelectionFilter(filter));
            if (res.Status == PromptStatus.OK && res.Value != null && res.Value.Count > 0)
                return res.Value.GetObjectIds();
            return null;
        }

        /// <summary>优先使用预选，否则按过滤器提示选择。</summary>
        public static ObjectId[] PickFirstOrPrompt(CadContext ctx, string prompt, params TypedValue[] filter)
        {
            var implied = ctx.Ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
                return implied.Value.GetObjectIds();
            return Pick(ctx, prompt, filter);
        }

        /// <summary>
        /// 优先使用预选，否则选择直线/二维多段线；支持 D 修改平行线偏移。
        /// U1C 可额外允许 INSERT，以便同一次选择直接更新 Ruanguan 块。
        /// </summary>
        public static ObjectId[] PickCurvesWithOffset(CadContext ctx, string prompt,
            string distancePrompt, ref double offset, Action<double> persistOffset,
            bool includeInserts = false)
        {
            var implied = ctx.Ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value != null && implied.Value.Count > 0)
            {
                ObjectId[] impliedIds = implied.Value.GetObjectIds();
                // U1Q receives a pick-first set when the user selects a line before
                // starting the command.  AutoCAD does not reapply the selection filter
                // to that set, so a stale text/block selection used to reach the offsetter
                // and end as the vague "0 curves" warning.  U1C intentionally keeps the
                // complete pick-first set because it also accepts frame/INSERT objects.
                return includeInserts
                    ? impliedIds
                    : FilterOffsetCurves(ctx, impliedIds);
            }

            var options = new PromptSelectionOptions { MessageForAdding = prompt };
            options.Keywords.Add("D");
            while (true)
            {
                var result = ctx.Ed.GetSelection(options,
                    new SelectionFilter(new[] { new TypedValue(0,
                        includeInserts
                            ? "LINE,LWPOLYLINE,POLYLINE,INSERT"
                            : "LINE,LWPOLYLINE,POLYLINE") }));
                if (result.Status == PromptStatus.Keyword)
                {
                    var distance = ctx.Ed.GetDistance(distancePrompt + " <" + offset.ToString("0.##") + ">: ");
                    if (distance.Status == PromptStatus.OK && distance.Value > 0)
                    {
                        offset = distance.Value;
                        persistOffset?.Invoke(offset);
                    }
                    continue;
                }
                if (result.Status != PromptStatus.OK || result.Value == null
                    || result.Value.Count == 0) return null;
                return includeInserts
                    ? result.Value.GetObjectIds()
                    : FilterOffsetCurves(ctx, result.Value.GetObjectIds());
            }
        }

        /// <summary>
        /// Applies the same entity contract to pick-first and command-time selections.
        /// The AutoCAD DXF filter accepts both 2D and 3D POLYLINE; the annotator only
        /// supports independent LINE/LWPOLYLINE/2D POLYLINE entities.
        /// </summary>
        private static ObjectId[] FilterOffsetCurves(CadContext ctx, ObjectId[] ids)
        {
            if (ctx == null || ids == null || ids.Length == 0)
                return new ObjectId[0];

            var result = new System.Collections.Generic.List<ObjectId>();
            using (Transaction transaction = ctx.Db.TransactionManager
                .StartOpenCloseTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsNull || !id.IsValid) continue;
                    try
                    {
                        Entity entity = transaction.GetObject(id, OpenMode.ForRead, true)
                            as Entity;
                        if (entity == null || entity.IsErased) continue;
                        if (entity is Line || entity is Polyline || entity is Polyline2d)
                            result.Add(id);
                    }
                    catch
                    {
                        // A stale/erased pick-first id must not prevent valid lines
                        // from being processed.
                    }
                }
                transaction.Commit();
            }
            return result.ToArray();
        }

        /// <summary>清除预选择高亮。</summary>
        public static void ClearPickFirst(CadContext ctx)
        {
            ctx.Ed.SetImpliedSelection(new ObjectId[0]);
        }
    }
}
