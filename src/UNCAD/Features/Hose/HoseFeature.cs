using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using UNCAD.Cad;
using UNCAD.Core.Contracts;
using UNCAD.Core.Geometry;
using UNCAD.Core.QuickLine;
using UNCAD.Core.Text;
using UNCAD.Infra;

namespace UNCAD.Features.Hose
{
    /// <summary>
    /// U1B：绘制软管（3.3.3，D-054 Amendment；用户原话："绘制软管 输入 U1B 然后
    /// 让用户 自己来用类似 LINE 的方案来连线(支持多段) 回车后 用线条绘制一个
    /// 平面的管子 管子直径默认100 绿色 然后要求用户鼠标点击一个位置 点击后
    /// 自动将这个多段线的中点连向这个位置并生成文本 软管xxxxxxx 注意需要
    /// 处理好拐弯处 和首尾端"）。
    /// 交互：LINE 式连续取点（U 回退上一点，回车/空格结束）→ 至少 2 点才画
    /// （不足礼貌退出）→ 立即在写事务里生成管子轮廓（双线+拐弯圆弧+端帽，
    /// 绿色 U_HOSE）→ 提示标注位置（橡皮筋自长度中点）→ 提示标注文字
    /// （默认"100mm软管:NNNNmm"，可改）→ 引线+文字。全程一个写事务，一次 U
    /// 连同管子/引线/文本全回退；取点阶段取消=未绘制任何实体。
    /// </summary>
    [Feature("hose", "绘制软管",
        Commands = CommandIds.HoseFeatureCommands,
        Description = "LINE 式连线绘制平面软管（默认直径100绿色）并生成软管标注")]
    public class HoseFeature : CommandBase
    {
        [CommandMethod(CommandIds.Hose)]
        public void DrawHose() => Run();

        protected override void Execute(CadContext ctx)
        {
            ProductMetadata.EnsureCommandAllowed(CommandIds.Hose);
            double diameter = ReadDiameter(ctx);
            double textHeight = Settings.GetDouble(ConfigKeys.UnlHeight, 180.0);
            if (double.IsNaN(textHeight) || double.IsInfinity(textHeight)
                || textHeight <= 0)
            {
                textHeight = 180.0;
                ctx.Write("\n[U1B] 文字高度设置无效，已回退为 180。请用 U1SET 重新设置。");
            }
            ConfigPrinter.Print(ctx, "U1B",
                ("直径", TextFormatter.FormatNum(diameter)),
                ("颜色", "绿（U_HOSE）"),
                ("文字高度", TextFormatter.FormatNum(textHeight)));

            List<Point3d> points = CollectPoints(ctx);
            if (points.Count < 2)
            {
                ctx.Write("\n[U1B] 至少需要两个点才能绘制软管，本次未绘制。");
                return;
            }
            var path = points
                .Select(point => new QuickLinePoint(point.X, point.Y))
                .ToList();
            // 几何预检：退化路径（清理后不足 2 点等）礼貌退出，不开事务。
            if (!HoseOutlinePlanner.TryPlan(path, diameter, out _, out double length))
            {
                ctx.Write("\n[U1B] 路径退化（重复/共线点过多），本次未绘制。");
                return;
            }

            // 全程一个写事务：先画管子（用户随即能看到），再交互标注，最后提交。
            // 中途取消/异常 → Dispose 未提交 = 整体回滚（UNCAD_TXPROBE 语义），
            // 不留半成品。
            using (Transaction transaction =
                ctx.Db.TransactionManager.StartTransaction())
            {
                HoseDrawResult result = HoseCadService.DrawOutline(ctx, transaction,
                    path, diameter);
                if (result == null)
                {
                    ctx.Write("\n[U1B] 路径退化，本次未绘制。");
                    return; // using 释放未提交事务 = 回滚
                }

                Point3d? labelPoint = PromptLabelPoint(ctx, path, length);
                if (labelPoint.HasValue)
                {
                    string text = PromptLabelText(ctx,
                        HoseAnnotationDefaults.BuildDefaultText(diameter, length));
                    if (text == null)
                    {
                        // 文字取消 → 引线一并省去（没有指向物的引线是孤儿线）。
                        labelPoint = null;
                        ctx.Write("\n[U1B] 未生成标注。");
                    }
                    else
                    {
                        HoseCadService.DrawAnnotation(ctx, transaction, result,
                            path, labelPoint.Value, text, textHeight);
                    }
                }
                else
                {
                    ctx.Write("\n[U1B] 未生成标注。");
                }
                transaction.Commit();
            }
            ctx.Write("\n[U1B] 软管绘制完成（一次 U 可整体撤销）。");
        }

        /// <summary>读软管直径（UNC_HOSE_DIAMETER，默认 100）：非法/超域回退 100。</summary>
        private static double ReadDiameter(CadContext ctx)
        {
            double diameter = Settings.GetDouble(ConfigKeys.HoseDiameter, 100.0);
            if (double.IsNaN(diameter) || double.IsInfinity(diameter)
                || diameter <= 0 || diameter > ConduitDiameter.MaximumMillimeters)
            {
                ctx.Write("\n[U1B] 软管直径设置无效（应在 1–"
                    + ConduitDiameter.MaximumMillimeters.ToString("0",
                        CultureInfo.InvariantCulture)
                    + "），已回退为 100。请用 U1SET 重新设置。");
                return 100.0;
            }
            return diameter;
        }

        /// <summary>
        /// LINE 式连续取点：U 回退上一点（回退到空时重新要起点），回车/空格结束，
        /// 取消/ESC 结束并返回已取点（数量不足由调用方礼貌退出）。重复点静默忽略。
        /// </summary>
        private static List<Point3d> CollectPoints(CadContext ctx)
        {
            var points = new List<Point3d>();
            PromptPointResult first = ctx.Ed.GetPoint("\n指定软管起点: ");
            if (first.Status != PromptStatus.OK) return points;
            points.Add(first.Value);
            while (true)
            {
                var options = new PromptPointOptions(
                    "\n指定下一点或 [放弃(U)] <结束软管路径>:")
                {
                    AllowNone = true, // 回车/空格 = 结束取点
                    BasePoint = points[points.Count - 1],
                    UseBasePoint = true
                };
                options.Keywords.Add("U");
                PromptPointResult result = ctx.Ed.GetPoint(options);
                if (result.Status == PromptStatus.Keyword)
                {
                    if (points.Count > 0)
                    {
                        points.RemoveAt(points.Count - 1);
                        ctx.Write(points.Count == 0
                            ? "\n已放弃起点。"
                            : "\n已放弃上一点。");
                    }
                    else
                    {
                        ctx.Write("\n没有可放弃的点。");
                    }
                    if (points.Count == 0)
                    {
                        PromptPointResult restart = ctx.Ed.GetPoint("\n指定软管起点: ");
                        if (restart.Status != PromptStatus.OK) return points;
                        points.Add(restart.Value);
                    }
                    continue;
                }
                if (result.Status != PromptStatus.OK) break;
                if (result.Value.DistanceTo(points[points.Count - 1]) < 1e-7)
                    continue; // 重复点：静默忽略，不结束取点。
                points.Add(result.Value);
            }
            return points;
        }

        /// <summary>标注位置取点（橡皮筋自路径长度中点）；取消/回车返回 null=跳过标注。</summary>
        private static Point3d? PromptLabelPoint(CadContext ctx,
            List<QuickLinePoint> path, double length)
        {
            QuickLinePoint mid = HoseOutlinePlanner.PointAtDistance(path, length / 2.0);
            var options = new PromptPointOptions("\n指定软管标注位置 <不标注>:")
            {
                AllowNone = true,
                BasePoint = new Point3d(mid.X, mid.Y, 0),
                UseBasePoint = true
            };
            PromptPointResult result = ctx.Ed.GetPoint(options);
            return result.Status == PromptStatus.OK ? result.Value : (Point3d?)null;
        }

        /// <summary>标注文字（默认值可改）；回车=默认（UseDefaultValue），取消返回 null。</summary>
        private static string PromptLabelText(CadContext ctx, string defaultText)
        {
            var options = new PromptStringOptions("\n输入软管标注文字 <"
                + defaultText + ">: ")
            {
                DefaultValue = defaultText,
                UseDefaultValue = true
            };
            PromptResult result = ctx.Ed.GetString(options);
            if (result.Status == PromptStatus.OK)
            {
                string value = (result.StringResult ?? "").Trim();
                return value.Length > 0 ? value : defaultText;
            }
            if (result.Status == PromptStatus.None) return defaultText;
            return null;
        }
    }
}
