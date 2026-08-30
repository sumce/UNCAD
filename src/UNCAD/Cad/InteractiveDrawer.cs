using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    /// <summary>
    /// 交互式连续绘制（与 AutoCAD L 命令交互一致）：
    /// 点选连续绘制、回车/空格结束、U 撤销上一段、ESC 全部清除。
    /// 每段独立事务立即提交——点一下立即生成。
    /// segmentBuilder：在当前事务内绘制本段，把实体 ObjectId 加入 segIds。
    /// </summary>
    public static class InteractiveDrawer
    {
        public static void Run(CadContext ctx, string firstPrompt, string nextPrompt,
            Action<Point3d, Point3d, Transaction, List<ObjectId>> segmentBuilder)
        {
            var p1res = ctx.Ed.GetPoint("\n" + firstPrompt);
            if (p1res.Status != PromptStatus.OK) return;

            var drawn = new List<List<ObjectId>>(); // 每段的实体
            var allIds = new List<ObjectId>();      // 全部实体（ESC 清除）
            Point3d pPrev = p1res.Value;

            try
            {
                while (true)
                {
                    var opts = new PromptPointOptions("\n" + nextPrompt)
                    {
                        AllowNone = true,          // 回车/空格结束
                        BasePoint = pPrev,         // 橡皮筋
                        UseBasePoint = true
                    };
                    opts.Keywords.Add("U");        // 放弃上一段

                    var pr = ctx.Ed.GetPoint(opts);

                    if (pr.Status == PromptStatus.Keyword)
                    {
                        if (drawn.Count > 0)
                        {
                            var seg = drawn[drawn.Count - 1];
                            Erase(ctx, seg);
                            foreach (var id in seg) allIds.Remove(id);
                            drawn.RemoveAt(drawn.Count - 1);
                            pPrev = drawn.Count > 0
                                ? GetLineEnd(ctx, drawn[drawn.Count - 1][0])
                                : p1res.Value;
                            ctx.Write("\n已放弃上一段操作。");
                        }
                        else
                        {
                            ctx.Write("\n没有可放弃的操作。");
                        }
                        continue;
                    }
                    if (pr.Status == PromptStatus.None) break; // 回车/空格：结束并保留
                    if (pr.Status != PromptStatus.OK) break;

                    Point3d p2 = pr.Value;

                    // 每段独立事务：立即提交显示
                    var segIds = new List<ObjectId>();
                    using (var tx = ctx.Db.TransactionManager.StartTransaction())
                    {
                        segmentBuilder(pPrev, p2, tx, segIds);
                        tx.Commit();
                    }

                    drawn.Add(segIds);
                    allIds.AddRange(segIds);
                    pPrev = p2;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.UserBreak)
            {
                // ESC：与 L 命令一致——清除已绘制的全部内容
                Erase(ctx, allIds);
            }
            catch
            {
                Erase(ctx, allIds);
                throw;
            }
        }

        private static void Erase(CadContext ctx, List<ObjectId> ids)
        {
            if (ids == null || ids.Count == 0) return;
            try
            {
                using (var tx = ctx.Db.TransactionManager.StartTransaction())
                {
                    foreach (var id in ids)
                    {
                        if (id.IsNull || !id.IsValid) continue;
                        var e = tx.GetObject(id, OpenMode.ForWrite, true) as Entity;
                        if (e != null && !e.IsErased) e.Erase();
                    }
                    tx.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("清除失败，图纸可能保留了部分已绘制实体", ex);
                throw new InvalidOperationException("取消操作时清理已绘制实体失败，请检查图纸。", ex);
            }
        }

        private static Point3d GetLineEnd(CadContext ctx, ObjectId id)
        {
            try
            {
                using (var tx = ctx.Db.TransactionManager.StartTransaction())
                {
                    var l = tx.GetObject(id, OpenMode.ForRead) as Line;
                    return l?.EndPoint ?? Point3d.Origin;
                }
            }
            catch (System.Exception ex)
            {
                // 实体可能已被删除/回滚；继续绘制会改变用户指定的连接点，必须停止。
                Log.Error("读取线段端点失败", ex);
                throw new InvalidOperationException("撤销后无法确定下一段起点，绘制已停止。", ex);
            }
        }
    }
}
