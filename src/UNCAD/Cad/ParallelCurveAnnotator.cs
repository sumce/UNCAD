using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using UNCAD.Core.Fill;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    public sealed class ParallelAnnotationOptions
    {
        public double CurveOffset { get; set; }
        public double TextOffset { get; set; }
        public double TextHeight { get; set; }
        public bool Above { get; set; }
        public short ColorIndex { get; set; }
        public Func<double, string> LabelFactory { get; set; }

        /// <summary>
        /// 旧版重复标注的比对键：把标注内容折算成旧版单行写法。
        /// 两行标注必须提供，否则旧图纸上同位置的旧单行标注识别不出来、
        /// 重跑一次就会留下两份。为空时按内容原样比较。
        /// </summary>
        public Func<string, string> LegacyLabelKey { get; set; }

        /// <summary>U1Q or U1C; repeated runs replace prior output for each source.</summary>
        public string AnnotationKind { get; set; }
    }

    /// <summary>Creates an offset curve and its label in one CAD transaction.</summary>
    public static class ParallelCurveAnnotator
    {
        public static int Add(CadContext ctx, ObjectId[] ids,
            ParallelAnnotationOptions options)
        {
            if (ctx == null || ids == null || options == null
                || options.LabelFactory == null) return 0;
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                int count = Add(ctx, transaction, ids, options);
                transaction.Commit();
                return count;
            }
        }

        /// <summary>
        /// D-050（2026-11-09）：按"趟"生成——选集先经 ParallelRunPlanner 端点
        /// 连通分组，每个连通组 = 一条连续偏移线（OffsetPolylineBuilder 解析式
        /// miter，构造上不翻侧——根治用户报告的"红线与基线交叉"）+ 一个标注。
        /// runLabelFactory 收到已填好 EstimatedLengthMm（U1L 标签推算）的趟。
        /// 整趟替换：任一成员句柄命中的旧输出（含旧版单段输出）全部先删再写。
        /// </summary>
        public static int AddRuns(CadContext ctx, ObjectId[] ids,
            ParallelAnnotationOptions options,
            Func<Core.Geometry.ParallelRun, string> runLabelFactory)
        {
            if (ctx == null || ids == null || options == null
                || runLabelFactory == null) return 0;
            using (var transaction = ctx.Db.TransactionManager.StartTransaction())
            {
                int count = AddRuns(ctx, transaction, ids, options, runLabelFactory);
                transaction.Commit();
                return count;
            }
        }

        public static int AddRuns(CadContext ctx, Transaction transaction,
            ObjectId[] ids, ParallelAnnotationOptions options,
            Func<Core.Geometry.ParallelRun, string> runLabelFactory)
        {
            if (ctx == null || transaction == null || ids == null || options == null
                || runLabelFactory == null) return 0;

            bool metadataReady = !string.IsNullOrWhiteSpace(options.AnnotationKind)
                && ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);
            ObjectId[] sourceIds = ids.Distinct().ToArray();
            int resolvedGenerated = 0;
            if (metadataReady)
                sourceIds = ResolveSourceIds(ctx, transaction, sourceIds,
                    options.AnnotationKind, out resolvedGenerated);
            if (sourceIds.Length == 0)
            {
                ctx.Write("\n[UNCAD] 选中的对象不是可处理的独立线段，或原始线段已不存在。");
                return 0;
            }

            // 成员提取：失败（不支持/退化/已生成物）逐条跳过并计原因。
            var members = new List<Core.Geometry.ParallelRunMember>();
            int skipped = 0;
            foreach (ObjectId id in sourceIds)
            {
                Curve source;
                try { source = transaction.GetObject(id, OpenMode.ForRead, true) as Curve; }
                catch { skipped++; continue; }
                if (source == null || !IsSupported(source))
                {
                    skipped++;
                    continue;
                }
                if (metadataReady && ParallelAnnotationMetadata.TryRead(source,
                    out _, out _))
                {
                    skipped++;
                    continue;
                }
                if (!ParallelRunGeometry.TryExtractChain(transaction, id,
                        out List<Core.QuickLine.QuickLinePoint> vertices,
                        out double lengthMm)
                    || vertices.Count < 2 || lengthMm <= 0)
                {
                    skipped++;
                    continue;
                }
                members.Add(new Core.Geometry.ParallelRunMember
                {
                    Handle = id.Handle.ToString(),
                    LengthMm = lengthMm,
                    Vertices = vertices
                });
            }
            if (members.Count == 0)
            {
                ctx.Write("\n[UNCAD] 没有可合并标注的受支持直线/二维多段线"
                    + (skipped > 0 ? "（已跳过 " + skipped + " 条）" : "") + "。");
                return 0;
            }

            var runs = Core.Geometry.ParallelRunPlanner.Plan(members);
            var memberHandles = new HashSet<string>(
                members.Select(member => member.Handle),
                StringComparer.OrdinalIgnoreCase);
            // 旧输出快照（SOURCE 命中或 RUN 任一成员命中），新实体不在快照内。
            // 3.2.0.6（审计修复）：按成员句柄保留分桶——此前全部成员的旧输出
            // 合成一个并集，只要有任一趟生成成功就整体删除；A 趟成功、B 趟
            // 中途失败（CreateRunAnnotation 抛错只 skipped++）时，B 趟旧标注
            // 被删且无新输出顶上（数据丢失）。现在只删除成功趟的旧输出；
            // 一趟都没成时仍然全不删（原保护语义不变）。
            Dictionary<string, List<ObjectId>> previousByMember = metadataReady
                ? CollectPrevious(ctx, transaction, options.AnnotationKind,
                    memberHandles)
                : new Dictionary<string, List<ObjectId>>(
                    StringComparer.OrdinalIgnoreCase);
            var previousEntities = new List<ObjectId>();
            var previousSeen = new HashSet<ObjectId>();
            Dictionary<string, double> u1LMillimetres =
                ParallelRunGeometry.CollectU1LMillimetresByLineHandle(ctx, transaction);
            ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);
            // 旧版（无元数据）单行标注快照：整批收集一次，逐趟比对。
            List<LegacyCandidate> legacy = CollectLegacyCandidates(ctx, transaction,
                options.ColorIndex);

            int created = 0;
            int mergedRuns = 0;
            foreach (Core.Geometry.ParallelRun run in runs)
            {
                try
                {
                    if (CreateRunAnnotation(ctx, transaction, run, options,
                        runLabelFactory, u1LMillimetres, metadataReady, styleId,
                        legacy))
                    {
                        created++;
                        if (run.Merged) mergedRuns++;
                        // 只有生成成功的趟才登记它的旧输出待删（3.2.0.6）。
                        foreach (Core.Geometry.ParallelRunMember member in run.Members)
                            if (previousByMember.TryGetValue(member.Handle,
                                    out List<ObjectId> memberOutputs))
                                foreach (ObjectId previousId in memberOutputs)
                                    if (previousSeen.Add(previousId))
                                        previousEntities.Add(previousId);
                    }
                    else skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Warn("Parallel run annotation failed: " + ex.Message);
                }
            }
            // 整趟替换：新输出全部生成后才删旧输出；一趟都没成时不删（不丢标注）。
            if (created > 0) ErasePreviousEntities(transaction, previousEntities);
            if (resolvedGenerated > 0)
                ctx.Write("\n[UNCAD] 已识别已生成的平行标注线，并回溯源线更新 "
                    + resolvedGenerated + " 条。");
            ctx.Write("\n[UNCAD] 本次生成 " + created + " 趟连续标注"
                + (mergedRuns > 0 ? "（其中 " + mergedRuns + " 趟由多段连续线合并）" : "")
                + (skipped > 0 ? "；跳过 " + skipped + " 条不可偏移的源线。" : "。"));
            return created;
        }

        private static bool CreateRunAnnotation(CadContext ctx, Transaction transaction,
            Core.Geometry.ParallelRun run, ParallelAnnotationOptions options,
            Func<Core.Geometry.ParallelRun, string> runLabelFactory,
            Dictionary<string, double> u1LMillimetres, bool metadataReady,
            ObjectId styleId, List<LegacyCandidate> legacy)
        {
            run.EstimatedLengthMm = ParallelRunGeometry.EstimateRunLengthMm(run,
                u1LMillimetres);
            double chainLength = ParallelRunGeometry.ChainLengthMm(run.Vertices);
            ParallelRunGeometry.PointAndDirectionAtDistance(run.Vertices,
                chainLength / 2.0,
                out Core.QuickLine.QuickLinePoint anchor,
                out Core.QuickLine.QuickLinePoint travel);

            // 整趟统一世界侧：以锚点段的"阅读方向 + Above"求期望法向，再判定
            // 它对应链行进方向的左手（+90°）还是右手——拐角处不再翻转。
            double readAngle = ReadableLineAngle(
                new Vector3d(travel.X, travel.Y, 0));
            double desiredAngle = options.Above
                ? readAngle + Math.PI / 2 : readAngle - Math.PI / 2;
            double desiredX = Math.Cos(desiredAngle), desiredY = Math.Sin(desiredAngle);
            // 左手法向 = 行进方向逆时针转 90°（与 OffsetPolylineBuilder 同口径）。
            double leftCross = -travel.Y * desiredX + travel.X * desiredY;
            bool leftOfTravel = leftCross >= 0;

            var outputCurves = new List<Curve>();
            if (!Core.Geometry.OffsetPolylineBuilder.TryBuild(run.Vertices,
                    options.CurveOffset, leftOfTravel,
                    out List<Core.QuickLine.QuickLinePoint> offsetVertices))
            {
                // 病态几何兜底：逐成员独立偏移（平行线数学上不交叉，只是拐角断开）。
                foreach (Core.Geometry.ParallelRunMember member in run.Members)
                    if (Core.Geometry.OffsetPolylineBuilder.TryBuild(member.Vertices,
                            options.CurveOffset, leftOfTravel,
                            out List<Core.QuickLine.QuickLinePoint> memberOffset))
                        outputCurves.Add(BuildPolyline(memberOffset));
                if (outputCurves.Count == 0) return false;
            }
            else
            {
                outputCurves.Add(BuildPolyline(offsetVertices));
            }

            // 标注锚点：源链半程点在偏移折线上的最近点 + 该处切线。
            var anchor3d = new Point3d(anchor.X, anchor.Y, 0);
            Point3d labelPoint = anchor3d;
            Vector3d labelTangent = new Vector3d(travel.X, travel.Y, 0);
            Curve labelCurve = outputCurves[0];
            double bestDistance = double.MaxValue;
            foreach (Curve curve in outputCurves)
            {
                try
                {
                    Point3d nearest = curve.GetClosestPointTo(anchor3d, false);
                    double distance = nearest.DistanceTo(anchor3d);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        labelCurve = curve;
                        labelPoint = nearest;
                        Vector3d tangent = curve.GetFirstDerivative(nearest);
                        if (tangent.Length > 1e-9) labelTangent = tangent;
                    }
                }
                catch { }
            }
            double textAngle = ReadableLineAngle(labelTangent);
            Point3d textPoint = GeoMath.Polar(labelPoint,
                GeoMath.SideDirection(textAngle, options.Above), options.TextOffset);
            string label = runLabelFactory(run);
            string labelKey = LegacyKey(options, label);
            Entity text = CreateLabel(ctx, label, textPoint, options.TextHeight,
                textAngle,
                options.Above ? AttachmentPoint.BottomCenter : AttachmentPoint.TopCenter,
                options.ColorIndex, styleId);
            string firstHandle = run.Members[0].Handle;
            string runHandles = string.Join(
                ParallelAnnotationMetadata.RunHandleSeparator,
                run.Members.Select(member => member.Handle));

            foreach (Curve curve in outputCurves)
            {
                curve.LayerId = ctx.CurrentLayerId;
                if (options.ColorIndex != 0) curve.ColorIndex = options.ColorIndex;
                ctx.AddToCurrentSpace(transaction, curve);
                if (metadataReady)
                    ParallelAnnotationMetadata.Set(curve, options.AnnotationKind,
                        firstHandle, runHandles);
            }
            ctx.AddToCurrentSpace(transaction, text);
            if (metadataReady)
                ParallelAnnotationMetadata.Set(text, options.AnnotationKind,
                    firstHandle, runHandles);
            RemoveLegacyDuplicates(transaction, legacy, text, labelKey, options,
                outputCurves);
            return true;
        }

        private static Polyline BuildPolyline(
            List<Core.QuickLine.QuickLinePoint> vertices)
        {
            var polyline = new Polyline();
            for (int i = 0; i < vertices.Count; i++)
                polyline.AddVertexAt(i,
                    new Point2d(vertices[i].X, vertices[i].Y), 0, 0, 0);
            polyline.Closed = false;
            return polyline;
        }

        private static void ErasePreviousEntities(Transaction transaction,
            List<ObjectId> previous)
        {
            foreach (ObjectId id in previous.Distinct())
            {
                try
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                        as Entity;
                    if (entity != null && !entity.IsErased) entity.Erase();
                }
                catch { }
            }
        }

        /// <summary>Uses a caller-owned transaction so U1C can update all outputs atomically.</summary>
        public static int Add(CadContext ctx, Transaction transaction, ObjectId[] ids,
            ParallelAnnotationOptions options)
        {
            if (ctx == null || transaction == null || ids == null || options == null
                || options.LabelFactory == null) return 0;

            int count = 0;
            int skipped = 0;
            int skippedUnsupported = 0;
            int skippedAlreadyGenerated = 0;
            int skippedGeometry = 0;
            int skippedOffset = 0;
            int skippedLabel = 0;
            ObjectId[] sourceIds = ids.Distinct().ToArray();
            bool metadataReady = !string.IsNullOrWhiteSpace(options.AnnotationKind)
                && ParallelAnnotationMetadata.EnsureApplication(ctx.Db, transaction);
            int resolvedGenerated = 0;
            if (metadataReady)
                sourceIds = ResolveSourceIds(ctx, transaction, sourceIds,
                    options.AnnotationKind, out resolvedGenerated);
            if (sourceIds.Length == 0)
            {
                ctx.Write("\n[UNCAD] 选中的对象不是可处理的独立线段，或原始线段已不存在。");
                return 0;
            }
            HashSet<string> sourceHandles = new HashSet<string>(
                sourceIds.Select(id => id.Handle.ToString()),
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<ObjectId>> previous = metadataReady
                ? CollectPrevious(ctx, transaction, options.AnnotationKind, sourceHandles)
                : new Dictionary<string, List<ObjectId>>(StringComparer.OrdinalIgnoreCase);
            // Legacy (pre-metadata) duplicates are matched against the whole
            // space. Collect a snapshot once instead of re-scanning the space
            // for every source curve (O(sources × entities) before).
            List<LegacyCandidate> legacy = CollectLegacyCandidates(ctx, transaction,
                options.ColorIndex);

            ObjectId styleId = StyleManager.GetDrawingStandardStyle(ctx, transaction);
            foreach (ObjectId id in sourceIds)
            {
                Curve source;
                try { source = transaction.GetObject(id, OpenMode.ForRead, true) as Curve; }
                catch
                {
                    skipped++;
                    skippedUnsupported++;
                    continue;
                }
                if (!IsSupported(source))
                {
                    skipped++;
                    skippedUnsupported++;
                    continue;
                }
                // A previous generated offset can be selected by a window. Never use it as
                // a new source, otherwise each run walks one level farther from the design
                // line. ResolveSourceIds normally maps same-kind output back to its source;
                // this guard remains for foreign kinds or stale metadata.
                if (metadataReady && ParallelAnnotationMetadata.TryRead(source,
                    out _, out _))
                {
                    skipped++;
                    skippedAlreadyGenerated++;
                    continue;
                }

                double length;
                Point3d sourceMid;
                double sourceAngle;
                Vector3d tangent;
                Vector3d desired;
                try
                {
                    // AutoCAD 2022 can reject derivative-at-point calls for ordinary
                    // lines at large drawing coordinates. A line already carries all
                    // required geometry in its endpoints, so avoid that kernel path.
                    if (source is Line sourceLine)
                    {
                        Vector3d direction = sourceLine.StartPoint
                            .GetVectorTo(sourceLine.EndPoint);
                        length = direction.Length;
                        sourceMid = sourceLine.StartPoint + direction / 2.0;
                        sourceAngle = ReadableLineAngle(direction);
                        tangent = direction;
                    }
                    else
                    {
                        length = GetLength(source);
                        sourceMid = source.GetPointAtDist(length / 2.0);
                        tangent = source.GetFirstDerivative(sourceMid);
                        sourceAngle = GeoMath.ReadableAngle(sourceMid,
                            sourceMid + tangent);
                    }
                    if (length <= 0)
                    {
                        skipped++;
                        skippedGeometry++;
                        continue;
                    }
                    if (tangent.Length < 1e-9)
                    {
                        skipped++;
                        skippedGeometry++;
                        continue;
                    }
                    double sideAngle = GeoMath.SideDirection(sourceAngle, options.Above);
                    desired = new Vector3d(Math.Cos(sideAngle), Math.Sin(sideAngle), 0);
                }
                catch (Exception ex)
                {
                    skipped++;
                    Log.Warn("Parallel curve preflight failed for " + id + ": "
                        + ex.Message);
                    continue;
                }

                OffsetCandidate positive = CreateCandidate(source, options.CurveOffset,
                    sourceMid, desired);
                OffsetCandidate negative = CreateCandidate(source, -options.CurveOffset,
                    sourceMid, desired);
                OffsetCandidate selected = SelectCandidate(positive, negative);
                OffsetCandidate rejected = ReferenceEquals(selected, positive)
                    ? negative : positive;
                rejected?.Dispose();
                if (selected == null)
                {
                    skipped++;
                    skippedOffset++;
                    continue;
                }

                Entity text;
                string labelKey;
                try
                {
                    Curve labelCurve = selected.LabelCurve;
                    Point3d curvePoint;
                    Vector3d labelTangent;
                    if (labelCurve is Line labelLine)
                    {
                        labelTangent = labelLine.StartPoint
                            .GetVectorTo(labelLine.EndPoint);
                        curvePoint = labelLine.StartPoint + labelTangent / 2.0;
                    }
                    else
                    {
                        curvePoint = labelCurve.GetClosestPointTo(sourceMid, false);
                        labelTangent = labelCurve.GetFirstDerivative(curvePoint);
                    }
                    double textAngle = labelTangent.Length < 1e-9
                        ? sourceAngle
                        : labelCurve is Line
                            ? ReadableLineAngle(labelTangent)
                            : GeoMath.ReadableAngle(curvePoint,
                                curvePoint + labelTangent);
                    Point3d textPoint = GeoMath.Polar(curvePoint,
                        GeoMath.SideDirection(textAngle, options.Above),
                        options.TextOffset);
                    string label = options.LabelFactory(length);
                    labelKey = LegacyKey(options, label);
                    text = CreateLabel(ctx, label, textPoint,
                        options.TextHeight, textAngle,
                        options.Above ? AttachmentPoint.BottomCenter
                            : AttachmentPoint.TopCenter,
                        options.ColorIndex, styleId);
                }
                catch (Exception ex)
                {
                    selected.Dispose();
                    skipped++;
                    skippedLabel++;
                    Log.Warn("Parallel curve label preflight failed for " + id + ": "
                        + ex.Message);
                    continue;
                }

                try
                {
                    foreach (Entity entity in selected.Entities)
                    {
                        entity.LayerId = ctx.CurrentLayerId;
                        if (options.ColorIndex != 0) entity.ColorIndex = options.ColorIndex;
                        ctx.AddToCurrentSpace(transaction, entity);
                        if (metadataReady)
                            ParallelAnnotationMetadata.Set(entity, options.AnnotationKind,
                                id.Handle.ToString());
                    }
                    ctx.AddToCurrentSpace(transaction, text);
                    if (metadataReady)
                        ParallelAnnotationMetadata.Set(text, options.AnnotationKind,
                            id.Handle.ToString());
                    // Remove output produced by pre-metadata versions as well. The
                    // generated ObjectIds are excluded so a metadata registration
                    // failure cannot erase the result we just added.
                    RemoveLegacyDuplicates(transaction, legacy, text, labelKey,
                        options, selected.Entities);
                    ErasePrevious(transaction, previous, id.Handle.ToString());
                    selected.Detach();
                    count++;
                }
                finally
                {
                    selected.Dispose();
                }
            }
            if (resolvedGenerated > 0)
                ctx.Write("\n[UNCAD] 已识别已生成的平行标注线，并回溯源线更新 "
                    + resolvedGenerated + " 条。");
            if (skipped > 0)
            {
                var reasons = new List<string>();
                if (skippedUnsupported > 0) reasons.Add("对象类型不支持 "
                    + skippedUnsupported);
                if (skippedAlreadyGenerated > 0) reasons.Add("已有平行标注且未找到可用源线 "
                    + skippedAlreadyGenerated);
                if (skippedGeometry > 0) reasons.Add("退化或无长度 " + skippedGeometry);
                if (skippedOffset > 0) reasons.Add("偏移失败 " + skippedOffset);
                if (skippedLabel > 0) reasons.Add("文字位置失败 " + skippedLabel);
                ctx.Write("\n[UNCAD] 已跳过 " + skipped
                    + " 条曲线" + (reasons.Count == 0
                        ? "。" : "（" + string.Join("、", reasons) + "）。"));
            }
            return count;
        }

        /// <summary>
        /// If the user clicks a red line produced by this command, use its recorded source
        /// line instead of silently treating the output as a new source. This preserves the
        /// one-level/idempotent geometry rule while making ordinary click selection usable.
        /// </summary>
        private static ObjectId[] ResolveSourceIds(CadContext ctx, Transaction transaction,
            ObjectId[] ids, string annotationKind, out int resolvedCount)
        {
            resolvedCount = 0;
            var result = new List<ObjectId>();
            foreach (ObjectId id in ids ?? new ObjectId[0])
            {
                Entity entity;
                try { entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity; }
                catch { continue; }
                if (entity == null || entity.IsErased) continue;

                if (ParallelAnnotationMetadata.TryRead(entity,
                    out string existingKind, out string sourceHandle)
                    && string.Equals(existingKind, annotationKind,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // D-050：合并趟输出回溯到全部成员源线（点中趟内任何一段
                    // 的红线都整趟重建）；单段标注仍回溯唯一 SOURCE。
                    List<string> handles = ParallelAnnotationMetadata
                        .TryReadRunHandles(entity);
                    if (handles.Count == 0 && !string.IsNullOrWhiteSpace(sourceHandle))
                        handles.Add(sourceHandle);
                    bool resolvedAny = false;
                    foreach (string handle in handles)
                    {
                        if (!TryResolveHandle(ctx.Db, handle, out ObjectId sourceId))
                            continue;
                        try
                        {
                            Entity source = transaction.GetObject(sourceId,
                                OpenMode.ForRead, true) as Entity;
                            if (source is Curve sourceCurve && IsSupported(sourceCurve))
                            {
                                result.Add(sourceId);
                                resolvedAny = true;
                            }
                        }
                        catch { }
                    }
                    if (resolvedAny) resolvedCount++;
                    // A generated line with a missing/invalid source is not a valid
                    // fallback source; offsetting it would compound the error.
                    continue;
                }
                result.Add(id);
            }
            return result.Distinct().ToArray();
        }

        private static bool TryResolveHandle(Database database, string value,
            out ObjectId id)
        {
            id = ObjectId.Null;
            if (database == null || string.IsNullOrWhiteSpace(value)
                || !long.TryParse(value.Trim(), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long raw)) return false;
            try
            {
                id = database.GetObjectId(false, new Handle(raw), 0);
                return !id.IsNull && id.IsValid;
            }
            catch { return false; }
        }

        private static Dictionary<string, List<ObjectId>> CollectPrevious(CadContext ctx,
            Transaction transaction,
            string kind, HashSet<string> sourceHandles)
        {
            var result = new Dictionary<string, List<ObjectId>>(
                StringComparer.OrdinalIgnoreCase);
            BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                OpenMode.ForRead, false) as BlockTableRecord;
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (entity == null || !ParallelAnnotationMetadata.TryRead(entity,
                    out string existingKind, out string sourceHandle)
                    || !string.Equals(existingKind, kind,
                        StringComparison.OrdinalIgnoreCase)) continue;
                // D-050：旧输出的 SOURCE 命中、或合并趟输出的任一 RUN 成员
                // 命中，都算"这一趟要替换的旧输出"。
                string matchKey = sourceHandles.Contains(sourceHandle)
                    ? sourceHandle : null;
                if (matchKey == null)
                {
                    foreach (string runHandle in ParallelAnnotationMetadata
                        .TryReadRunHandles(entity))
                        if (sourceHandles.Contains(runHandle))
                        {
                            matchKey = runHandle;
                            break;
                        }
                }
                if (matchKey == null) continue;
                if (!result.TryGetValue(matchKey, out List<ObjectId> ids))
                {
                    ids = new List<ObjectId>();
                    result[matchKey] = ids;
                }
                ids.Add(id);
            }
            return result;
        }

        /// <summary>
        /// 3.2.0.6（审计修复，D-053×D-050 交界）：收集当前空间里全部 UNCAD_PARALLEL
        /// 输出实体，按"输出引用的源线句柄"建倒排表（SOURCE 与每个 RUN 成员各记
        /// 一条）。Xauto 重排/开洞时用一个句柄查到要跟随处理的旧输出，避免旧输出
        /// 指向已移动/已删除的线而孤儿化（重跑 U1Q/U1C 也无法按句柄回收）。
        /// </summary>
        internal static Dictionary<string, List<ObjectId>> CollectBySourceHandle(
            CadContext ctx, Transaction transaction)
        {
            var result = new Dictionary<string, List<ObjectId>>(
                StringComparer.OrdinalIgnoreCase);
            if (ctx == null || transaction == null) return result;
            BlockTableRecord space;
            try
            {
                space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                    OpenMode.ForRead, false) as BlockTableRecord;
            }
            catch { return result; }
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                Entity entity;
                try { entity = transaction.GetObject(id, OpenMode.ForRead, true) as Entity; }
                catch { continue; }
                if (entity == null) continue;
                try
                {
                    if (!ParallelAnnotationMetadata.TryRead(entity,
                            out string kind, out string sourceHandle)) continue;
                    AddOutputRef(result, sourceHandle, id);
                    foreach (string runHandle in ParallelAnnotationMetadata
                            .TryReadRunHandles(entity))
                        AddOutputRef(result, runHandle, id);
                }
                catch { continue; }
            }
            return result;
        }

        private static void AddOutputRef(Dictionary<string, List<ObjectId>> map,
            string handle, ObjectId id)
        {
            if (string.IsNullOrWhiteSpace(handle)) return;
            if (!map.TryGetValue(handle, out List<ObjectId> ids))
            {
                ids = new List<ObjectId>();
                map[handle] = ids;
            }
            if (!ids.Contains(id)) ids.Add(id);
        }

        /// <summary>把一组 U1Q/U1C 旧输出整体平移（Xauto 重排时随源线走，
        /// 保留相对位置；句柄未变，重跑 U1Q/U1C 仍能按元数据整趟替换）。</summary>
        internal static void TranslateOutputs(Transaction transaction,
            IEnumerable<ObjectId> entities, Vector3d delta)
        {
            if (delta.IsZeroLength()) return;
            foreach (ObjectId id in entities ?? Array.Empty<ObjectId>())
            {
                try
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                        as Entity;
                    if (entity == null || entity.IsErased) continue;
                    entity.TransformBy(Matrix3d.Displacement(delta));
                }
                catch { /* 单个输出移动失败不阻断重排 */ }
            }
        }

        /// <summary>把输出元数据里的旧线句柄（SOURCE/RUN）改挂到新实体句柄——
        /// Xauto 开洞把 Line 擦除换成带洞 Polyline 后，旧句柄指向已删实体，
        /// 不改挂则重跑 U1Q/U1C 的 CollectPrevious 按句柄匹配落空、旧输出
        /// 永久残留；改挂后重跑即整趟替换。</summary>
        internal static void RetargetHandles(Transaction transaction,
            IEnumerable<ObjectId> entities, string oldHandle, string newHandle)
        {
            if (string.IsNullOrWhiteSpace(oldHandle)
                || string.IsNullOrWhiteSpace(newHandle)
                || string.Equals(oldHandle, newHandle, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (ObjectId id in entities ?? Array.Empty<ObjectId>())
            {
                try
                {
                    Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true)
                        as Entity;
                    if (entity == null || entity.IsErased) continue;
                    if (!ParallelAnnotationMetadata.TryRead(entity,
                            out string kind, out string sourceHandle,
                            out string runHandles)) continue;
                    bool changed = false;
                    if (string.Equals(sourceHandle, oldHandle,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        sourceHandle = newHandle;
                        changed = true;
                    }
                    string retargeted = string.Join(
                        ParallelAnnotationMetadata.RunHandleSeparator,
                        ParallelAnnotationMetadata.TryReadRunHandles(entity)
                            .Select(handle => string.Equals(handle, oldHandle,
                                    StringComparison.OrdinalIgnoreCase)
                                ? newHandle : handle));
                    if (!string.Equals(retargeted, runHandles ?? "",
                            StringComparison.Ordinal)) changed = true;
                    if (changed)
                        ParallelAnnotationMetadata.Set(entity, kind,
                            sourceHandle, retargeted);
                }
                catch { /* 单个输出改挂失败不阻断开洞 */ }
            }
        }

        private static void ErasePrevious(Transaction transaction,
            Dictionary<string, List<ObjectId>> previous, string sourceHandle)
        {
            if (previous == null || !previous.TryGetValue(sourceHandle,
                out List<ObjectId> ids)) return;
            foreach (ObjectId id in ids)
            {
                Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
                if (entity != null && !entity.IsErased) entity.Erase();
            }
            previous.Remove(sourceHandle);
        }

        /// <summary>Snapshot of one untagged entity that may be a legacy duplicate.</summary>
        private sealed class LegacyCandidate
        {
            public ObjectId Id;
            public bool IsText;
            public string Text;
            /// <summary>旧标注落在哪个点上；DBText 的插入点与对齐点都要看。</summary>
            public Point3d[] TextAnchors;
            public Type CurveType;
            public double Length;
            public Point3d ExtentsMin;
            public Point3d ExtentsMax;
        }

        /// <summary>
        /// One pass over the space collecting untagged, same-colour DBText/MText and
        /// Curve snapshots. Older versions did not write metadata, so their
        /// exact-overlap output cannot be associated by source handle.
        /// </summary>
        private static List<LegacyCandidate> CollectLegacyCandidates(CadContext ctx,
            Transaction transaction, short colorIndex)
        {
            var result = new List<LegacyCandidate>();
            BlockTableRecord space = transaction.GetObject(ctx.Db.CurrentSpaceId,
                OpenMode.ForRead, false) as BlockTableRecord;
            if (space == null) return result;
            foreach (ObjectId id in space)
            {
                Entity candidate = transaction.GetObject(id, OpenMode.ForRead, true) as Entity;
                if (candidate == null
                    || ParallelAnnotationMetadata.TryRead(candidate, out _, out _))
                    continue;
                if (candidate is DBText text)
                {
                    if (text.ColorIndex != colorIndex) continue;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = true, Text = text.TextString,
                        TextAnchors = DbTextAnchors(text.Position, text.AlignmentPoint,
                            text.Justify)
                    });
                    continue;
                }
                // 两行标注是 MTEXT；旧版本可能已经被手工改写成 MTEXT 却没有元数据，
                // 同样要纳入重复判定。
                if (candidate is MText mtext)
                {
                    if (mtext.ColorIndex != colorIndex) continue;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = true, Text = mtext.Contents,
                        TextAnchors = new[] { mtext.Location }
                    });
                    continue;
                }
                if (!(candidate is Curve curve) || curve.ColorIndex != colorIndex)
                    continue;
                try
                {
                    Extents3d extents = curve.GeometricExtents;
                    result.Add(new LegacyCandidate
                    {
                        Id = id, IsText = false, CurveType = curve.GetType(),
                        Length = GetLength(curve),
                        ExtentsMin = extents.MinPoint, ExtentsMax = extents.MaxPoint
                    });
                }
                catch { }
            }
            return result;
        }

        /// <summary>
        /// Erases snapshot candidates whose geometry/text is identical to the
        /// newly generated result. The snapshot was taken before this run, so
        /// freshly generated ObjectIds can never be erased.
        /// </summary>
        private static void RemoveLegacyDuplicates(Transaction transaction,
            List<LegacyCandidate> legacy, Entity currentText, string currentKey,
            ParallelAnnotationOptions options, IEnumerable<Entity> currentCurves)
        {
            if (legacy == null || legacy.Count == 0) return;
            Entity[] generated = (currentCurves ?? Enumerable.Empty<Entity>()).ToArray();
            Point3d[] currentAnchors = TextAnchors(currentText);
            foreach (LegacyCandidate candidate in legacy)
            {
                if (candidate.IsText)
                {
                    // 新旧写法不同（旧单行 / 新两行），所以先都折算成旧单行写法再比。
                    if (string.Equals(LegacyKey(options, candidate.Text), currentKey,
                            StringComparison.Ordinal)
                        && SharesAnchor(candidate.TextAnchors, currentAnchors))
                        EraseLegacy(transaction, candidate.Id);
                    continue;
                }
                foreach (Entity created in generated)
                {
                    if (created is Curve newCurve
                        && candidate.CurveType == newCurve.GetType())
                    {
                        try
                        {
                            Extents3d b = newCurve.GeometricExtents;
                            if (SamePoint(candidate.ExtentsMin, b.MinPoint)
                                && SamePoint(candidate.ExtentsMax, b.MaxPoint)
                                && Math.Abs(candidate.Length - GetLength(newCurve)) <= 1e-6)
                            {
                                EraseLegacy(transaction, candidate.Id);
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
        }

        private static void EraseLegacy(Transaction transaction, ObjectId id)
        {
            Entity entity = transaction.GetObject(id, OpenMode.ForWrite, true) as Entity;
            if (entity != null && !entity.IsErased) entity.Erase();
        }

        private static bool SamePoint(Point3d first, Point3d second)
            => first.DistanceTo(second) <= 1e-6;

        /// <summary>
        /// 生成标注实体：两行用 MTEXT（DBText 会把 \P 原样画成反斜杠加 P），
        /// 单行仍用 DBText，保持可直接编辑、与旧读取路径兼容。
        /// </summary>
        private static Entity CreateLabel(CadContext ctx, string label, Point3d anchor,
            double height, double rotation, AttachmentPoint alignment, short colorIndex,
            ObjectId styleId)
        {
            return label.IndexOf(AnnotationLabelPair.Separator,
                StringComparison.Ordinal) >= 0
                ? (Entity)EntityFactory.MText(ctx, label, anchor, height, rotation,
                    alignment, colorIndex, styleId)
                : EntityFactory.DBText(ctx, label, anchor, height, rotation,
                    alignment, colorIndex, styleId);
        }

        /// <summary>把标注内容折算成旧版单行写法；调用方未提供时原样返回。</summary>
        private static string LegacyKey(ParallelAnnotationOptions options, string label)
        {
            if (options.LegacyLabelKey == null) return label ?? "";
            return options.LegacyLabelKey(label) ?? "";
        }

        /// <summary>
        /// DBText 的插入点与对齐点都可能承载实际位置（对齐方式不是 BaseLeft 时
        /// 生效的是对齐点），所以两个都作为锚点候选；任一相同即视为同一位置。
        /// </summary>
        private static Point3d[] DbTextAnchors(Point3d position, Point3d alignmentPoint,
            AttachmentPoint justify)
            => justify == AttachmentPoint.BaseLeft
                ? new[] { position }
                : new[] { position, alignmentPoint };

        private static Point3d[] TextAnchors(Entity text)
        {
            if (text is DBText db)
                return DbTextAnchors(db.Position, db.AlignmentPoint, db.Justify);
            if (text is MText mtext) return new[] { mtext.Location };
            return new Point3d[0];
        }

        private static bool SharesAnchor(Point3d[] first, Point3d[] second)
        {
            if (first == null || second == null) return false;
            foreach (Point3d a in first)
                foreach (Point3d b in second)
                    if (SamePoint(a, b)) return true;
            return false;
        }

        private static bool IsSupported(Curve curve)
            => curve is Line || curve is Polyline || curve is Polyline2d;

        private static double GetLength(Curve curve)
        {
            try
            {
                return Math.Abs(curve.GetDistanceAtParameter(curve.EndParam)
                    - curve.GetDistanceAtParameter(curve.StartParam));
            }
            catch { return 0; }
        }

        private static OffsetCandidate CreateCandidate(Curve source, double distance,
            Point3d sourceMid, Vector3d desired)
        {
            if (source is Line line)
                return CreateLineCandidate(line, distance, desired);

            DBObjectCollection objects = null;
            try
            {
                objects = source.GetOffsetCurves(distance);
                var entities = new List<Entity>();
                Curve labelCurve = null;
                double score = double.NegativeInfinity;
                foreach (DBObject obj in objects)
                {
                    if (!(obj is Entity entity) || !(obj is Curve curve))
                    {
                        obj.Dispose();
                        continue;
                    }
                    entities.Add(entity);
                    Point3d nearest = curve.GetClosestPointTo(sourceMid, false);
                    double candidateScore = sourceMid.GetVectorTo(nearest)
                        .DotProduct(desired);
                    if (candidateScore > score)
                    {
                        score = candidateScore;
                        labelCurve = curve;
                    }
                }
                if (entities.Count == 0 || labelCurve == null)
                {
                    foreach (Entity entity in entities) entity.Dispose();
                    return null;
                }
                return new OffsetCandidate(entities, labelCurve, score);
            }
            catch
            {
                if (objects != null)
                    foreach (DBObject obj in objects) obj.Dispose();
                return null;
            }
        }

        private static OffsetCandidate CreateLineCandidate(Line source, double distance,
            Vector3d desired)
        {
            Vector3d direction = source.StartPoint.GetVectorTo(source.EndPoint);
            double planarLength = Math.Sqrt(direction.X * direction.X
                + direction.Y * direction.Y);
            if (planarLength < 1e-9) return null;

            Vector3d offset = new Vector3d(-direction.Y / planarLength * distance,
                direction.X / planarLength * distance, 0);
            var line = new Line(source.StartPoint + offset, source.EndPoint + offset);
            return new OffsetCandidate(new List<Entity> { line }, line,
                offset.DotProduct(desired));
        }

        private static double ReadableLineAngle(Vector3d direction)
        {
            double angle = Math.Atan2(direction.Y, direction.X);
            if (angle < 0) angle += Math.PI * 2;
            if (angle > Math.PI * 0.5 && angle <= Math.PI * 1.5)
                angle += Math.PI;
            return angle;
        }

        private static OffsetCandidate SelectCandidate(OffsetCandidate first,
            OffsetCandidate second)
        {
            if (first == null) return second;
            if (second == null) return first;
            return first.Score >= second.Score ? first : second;
        }

        private sealed class OffsetCandidate : IDisposable
        {
            private bool _attached;

            public OffsetCandidate(List<Entity> entities, Curve labelCurve, double score)
            {
                Entities = entities;
                LabelCurve = labelCurve;
                Score = score;
            }

            public List<Entity> Entities { get; }
            public Curve LabelCurve { get; }
            public double Score { get; }

            public void Detach() => _attached = true;

            public void Dispose()
            {
                if (_attached) return;
                foreach (Entity entity in Entities) entity.Dispose();
            }
        }
    }
}
