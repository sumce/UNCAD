# Product Decisions

This file records decisions that should not be reopened from memory or from an
old release note. Add a dated entry when a decision changes; do not silently
rewrite an existing entry.

## D-059: Preview Redesign — Floor Planes Only, WebV Removed, Run Totals, U1UL Position Match (2026-11-25)

- Status: accepted (user instruction 2026-11-25)
- Problem: the U1U preview was slow on real orders (the reference building —
  waffle plates, I-beams, grating, columns, busbar glass — buried the route and
  dominated the triangle count and transparent blending), the axis display and
  the millimetre labels were hard to read, and the user's requirement changed:
  **only the floor planes of the workbook floors, and axis labels**. Two
  recognition gaps were reported as well: tray/conduit annotations whose hosts
  cannot be resolved (merged polylines, shortened red lines, several runs
  sharing one label) and millimetre labels that U1UL skipped because they carry
  no association metadata. WebV (the optional WebView2/three.js viewer) is not
  wanted at all.
- Decision:
  1. **Preview draws floors, axes, routes, cabinet only.** Floor planes are the
     workbook's upstream and device floors (all floors between them). Elevations
     come from the U1SET level list ("NF地面 / NF高架地板面", raised floor wins —
     equipment stands on it); unknown floors are extrapolated by the 7.8 m floor
     height from the nearest known ground. Nothing of the 3.3.7–3.4.0 reference
     building returns; the old bay-based footprint planner and its 4096-bay limit
     are deleted (a long route cannot fail the preview any more).
  2. **Axis display is a drawing-style edge band.** Numeric and letter axis
     numbers are printed along two adjacent outer edges of the top floor plane,
     extrapolated from the resolved endpoint axes at the 4800 mm pitch — not a
     full combination label on every column.
  3. **Labels are chips.** Millimetre labels read in metres ("14.4 m") on a
     dark backing; tray/conduit labels are one per run ("桥架 400 · 19.2m");
     floor names carry their relative elevation; the ends are "上游端 48/V" /
     "设备端 37/V". All chips participate in collision avoidance; a millimetre
     chip that cannot find a free spot still shows at its least-overlapping
     position instead of disappearing.
  4. **WebV is deleted** (`UNCAD.Preview.exe`, the three.js page, the launcher,
     its project reference, bundle payload entries and the U13D/U1U toolbar
     buttons). WPF Viewport3D stays the only 3D kernel; the login window keeps
     its WebView2 (D-025 unchanged). Physical line widths derive from screen
     pixels so thin grids stay readable at any zoom.
  5. **Upstream cabinet placement** (workbook 盘柜类型): I-Line panel stands on
     the upstream floor's ground; socket panel sits 1 m above it (bottom);
     busbar plug box is drawn at the route end height. The base point is not
     blindly the route endpoint: when the endpoint segment is a vertical riser
     going up, the riser's bottom is the equipment base point. Nothing is drawn
     when the type is empty or the direction is unresolved.
  6. **Cross-floor height check**: when upstream and device lie on different
     floors, the vertical distance between the two route endpoints must equal
     the floor elevation difference, allowing 0.2 m per storey (2F→3F: 7.6–7.8
     m). A mismatch is reported as a warning on the pane, the command line and
     the batch remarks; nothing is blocked.
  7. **Tray/conduit runs and red-line recovery**: an annotation's length is the
     **whole run total**, never per segment. Hosts resolve in order: metadata →
     the annotation's own red line(s), split into pieces that each find the
     nearest parallel, overlapping labelled cable (adjacent label-less red lines
     of the same kind merge into the run) → nearest segment only (reported as a
     proximity binding). A run that still finds no cable is reported as an
     orphan; the batch remarks name it. Several annotations of the same kind may
     share one red-line chain (one label, several runs) — the lengths add.
  8. **Unbound millimetre labels and axis blocks are reported**: every
     millimetre label must belong to a segment — one that belongs to none is
     named (handle and text) on the pane/command line/batch remarks; the
     device-reserve marker form (horizontal `\d+0mm` without a dangling
     association) is excluded. A frame must carry both the upstream and the
     downstream axis block; a missing one is reported.
  9. **U1UL also aligns labels matched by position**: a label without the
     U1L/U1LX association is claimed when it sits beside the segment (within the
     along-line range, off-line at most max(4× text height, 300), angle within
     10°). It is aligned but never gains an association; anything farther or
     more skewed is left untouched and counted (`PositionRejected`).
- Consequence: do not bring back the reference building model, the bay-based
  footprint/4096 limit, or WebV; do not compute tray/conduit length by summing
  segment labels (the annotation carries the run total); do not claim a
  millimetre label by position outside the stated box, and never write an
  association from U1UL; U1LX and Xauto keep the strict endpoint tolerance and
  are unaffected.
- Amendment (2026-11-25, 3.8.0.1 — field acceptance of D-059):
  1. Floor planes are **wireframe only** (outline + column grid). WPF's
     semi-transparent surfaces still write depth, so a filled plane hid every
     route and label on its far side ("穿过楼层就看不到电缆"). The socket-panel
     chip sits at the cabinet's mid-height and a thin guide runs from the floor
     to the box bottom, so "离地 1 m" is visible instead of looking like it
     stands on the floor.
  2. Tray/conduit annotation text is never a cable millimetre label — this
     includes a second line such as `3500mm` in a multi-line tray annotation.
     For legacy annotations without ownership metadata the red line is found
     from the text's geometric-extents centre, and **parallelism between the
     text and the line is not required** (vertical tray text beside a diagonal
     red line is a real drawing form). A red line used by an annotation is
     classified as an annotation line, not reported as an unlabelled cable.
     The red-line-to-cable perpendicular limit is 600 mm.
  3. Cable segments may also be closed LWPOLYLINEs whose vertices are all
     collinear (the closing edge overlaps the line), 3D polylines, and splines
     (open curve, first/last point). In the fill/U1U scan only, a linear
     dimension with no real line at its witness points becomes a synthetic
     segment between those points, so the drafter's `300mm` dimension is part of
     the route; the default U1LX scan is unchanged, and synthetic dimension
     segments are never moved to `U_CABLE`.
  4. D-059's other rules (run totals, orphan/unbound reports, axis blocks,
     cross-floor rise check, U1UL position match) are unchanged.
- Amendment (2026-11-25, 3.8.0.2 — second field round):
  1. **Socket panel lifts the whole route.** The box is only a placeholder: for
     a socket panel the reconstructed route is raised 1 m as a whole (no
     end-height arithmetic), so its upstream end sits 1 m above the upstream
     floor. The box bottom sits at floor + 1 m when the floor is known, and on
     the route's base point otherwise. I-Line panels and bus plug boxes are not
     lifted.
  2. **The placeholder box is scaled with the scene.** Its footprint is
     enlarged so the box width is at least 5 % of the view diagonal (route +
     floor bounds), capped at 4×; heights stay 2200 / 1000 / 700 mm.
  3. **Line labels sit on their line.** Millimetre, tray/conduit-run and hose
     chips are rotated along the on-screen projection of their segment
     (readable: left-to-right, or bottom-to-top beside a vertical line), fixed
     beside it (millimetres and hose above, tray/conduit below). They do not
     take part in collision avoidance, so they never drift when the camera
     orbits; the only move is flipping to the other side of the same line when
     the preferred side overlaps an already placed line label. Other chips
     (ends, cabinet, floor, axis edges) avoid the line labels.
  4. **A dead label association is no association.** When a millimetre label
     carries a U1L association whose handle is 0 or points at a deleted entity
     (AutoCAD zeroes XData handles that are copied without their target), the
     label takes part in geometric matching again. This is the cause of the
     field's "300mm not recognised": the text sat on the line's midpoint with
     `associatedLine=0`. An association that still resolves to a live
     non-line entity remains unresolved (reported, not guessed).
  5. **Not millimetre labels**: text carrying a cross-section unit (`mm²`,
     `mm2`, `㎡` — cable model / BOQ info, even when it ends in `=14800mm`) and
     hose-length text (the Ruanguan block's `软管长度` attribute, free text
     containing `软管`).
  6. **Bent polylines are routes drawn in one stroke.** In the fill/U1U scan
     an open, bulge-free LWPOLYLINE whose legs all lie on the drafting axes
     (0°, 30°, 90°, 150°, ±5°) and are not all collinear is split into legs;
     each leg is identified as `handle/seq` and gets its own label (labels
     associated with the polyline handle go through geometric matching). The
     default U1LX/Xauto/U1R scan is unchanged, and hop-detour polylines (bulge
     or off-axis legs) are not split.
- Amendment (2026-11-25, 3.8.0.3 — third field round):
  1. **The cross-floor rise is measured from the upstream floor to the device
     end** (user: "上下游端点距离改成 2F 地面到设备的距离，在 7.6~7.8"). The
     measured value is the upstream end's height above the upstream floor
     ground (socket panel 1 m, others 0) plus the route's vertical rise from
     its base point (the upstream end, or the riser bottom when the end
     segment is a riser standing on it). Before, the route's end-to-end rise
     was compared with the floor elevation difference — the socket panel's
     metre is not in the drawing (a frame cannot show the ground), so the
     field saw "应在 7.6~7.8 m，实际 6.2 m" for a route that is 7.2 m above the
     2F ground. The warning names the level-list entries the band comes from
     (`按 U1SET 标高清单 2F地面 → 3F高架地板面 差 7.8 m`) and how the measured
     value is composed (`上游端离地 1 m + 线路竖向 6.2 m`). The band itself is
     unchanged: device standing level minus upstream ground, minus 0.2 m per
     storey; both levels come from the U1SET list, so a project with other
     storey heights edits the list (the default list has 2F ground 7.8 m, 3F
     ground 15.0 m, 3F raised floor 15.6 m).
  2. **Single-line tray/conduit annotations are annotations.** A single line
     with the keyword, a spec and the run length at the end —
     `梯形桥架200Wx100H-1300mm` — is a tray annotation (length = the trailing
     millimetres, width from the `宽Wx高H` spec), never a cable millimetre
     label. A spec ending in a digit needs a separator before the length and
     the length must not be 0 (so `250` is never split into `25` + `0mm`);
     text after `mm` (`… 2500mm 预留`) and a keyword without a spec
     (`桥架内穿管 20mm`) are not annotations. The `宽Wx高H` spec is read as
     the tray width everywhere (U1Q's own output `梯形桥架100Wx100H` used to
     fall back to the 200 mm default).
  3. **Shared trays are left alone** (user, 2026-11-25: "共用桥架不用管"):
     text carrying `共用` stays outside rule 2 — its length is still not read
     and nothing about it changed. Do not "fix" it without a new instruction.
- Amendment (2026-11-25, 3.8.0.4 — axis grid):
  1. **The bay is 4.8 m and each grid line carries one axis number.** The grid
     labels are generated at the pitch (adjacent numbers/letters differ by 1
     per line), never interpolated between the two endpoint labels — the old
     interpolation squeezed an 11-bay difference into a few dozen metres, so
     the drawn grid no longer had 4.8 m bays ("一个轴位是 4.8M").
  2. **Both route ends stay inside their own bays.** The drift between the
     workbook axis difference and the plan displacement is split equally, so
     each end is at most half a bay from its own axis line; a device that is
     not at an axis centre is still bracketed by the axes its label names
     ("设备很大概率不在轴位中心 只要两端能被正确轴位保住就行"). The increase
     direction is derived from the labels and the plan displacement (never
     hard-coded); with both letters equal the documented convention applies.
  3. **Convention fallback.** The default convention is letters along +X
     (north, up-right) and numbers along −Y. When the default cannot hold
     both ends but the rotated convention (numbers along +X, letters along
     −Y) can, the rotated one is used. When neither holds, the grid is
     anchored at the upstream axis and the frame reports
     `轴位与图面距离对不上：…` (expected bays/distance vs measured span, pane
     issue list and command line). **A mismatch is never hidden** and a bay is
     never stretched ("算出来的网格轴位和设备/上游轴位对不上也不提示").
  4. Direction resolution is unchanged: connection blocks first; with no
     blocks the fixed-convention axis-region match remains the fallback
     (deriving the sign from the geometry would make both orientations fit
     equally, so it cannot decide upstream/device).
- Amendment (2026-11-25, 3.8.0.5 — U1U preview kernel back to WebView2;
  user instruction "3D预览换成 WebView2吧 … 不要踩之前的无法显示的坑"):
  this supersedes D-059's "WebView2 is retired / WPF Viewport3D is the only
  3D kernel" clause and D-054 3.4.0's "no web surface" clause for the U1U
  confirmation pane. The out-of-process WebV viewer (`UNCAD.Preview.exe`,
  deleted in 3.8.0) stays deleted — only the in-process local page returns.
  1. **Two layers, never a blank pane.** The pane hosts the existing WPF
     renderer (`QuickLineOrbitPreviewControl`) as the always-present base
     layer (scene built synchronously on `SetModel`), and a WebView2 above it.
     The browser starts as a visible 1×1 control (a WebView2 that first
     Loads while Collapsed binds no visual — the 3.3.6.1 field lesson), is
     initialised in the background through
     `SharedWebView2Environment.EnsureControllerAsync` (30 s controller
     timeout, fallback profile directory), and covers the WPF layer only after
     the page posts `ready` **and** the first frame is built. Any failure —
     missing runtime, controller timeout, navigation error, process failure,
     page script error (`pageError`) or `renderError` — collapses the browser
     and leaves the WPF scene plus a one-line reason visible. Cold starts
     (6–33 s measured in 3.3.x) therefore never show an empty or white pane.
  2. **Security posture unchanged (D-025 / D-054 3.4.0.4).** The page is local:
     virtual host `u1orbit.uncad.invalid` mapped to the plugin's `Web` folder
     with `CoreWebView2HostResourceAccessKind.Deny`; `NavigationStarting`
     cancels anything that is not that HTTPS host; no host objects, no
     downloads, no new windows, permissions denied, DevTools/context menus
     off, no network. The page receives the same PascalCase envelope as the
     WPF renderer and only replies `ready` / `rendered` / `renderError` /
     `pageError`; it never writes CAD data.
  3. **No prewarm, no resident browser.** The 3.3.8 `OrbitPrewarmHost`
     (off-screen window + `msedgewebview2` orphan sweep) and its
     `System.Management` dependency are not restored: the WPF base layer
     already gives an instant picture, so the browser can initialise lazily
     per window and die with it.
  4. **The page is part of the package** (`Web/QuickLineOrbit/**`, seven
     files including the vendored three.js), listed in `build.ps1` /
     `installer.ps1` payloads and compared by `release.ps1`; the bundle grows
     from 17 to 24 files.
  5. **Confirmation windows are half-and-half at a usable size.** Both U1U
     windows default to 1440×900 / 1440×820 (MinWidth 1200), keep the 1:1
     star split with a 6 px splitter, and clamp the preview half at 420 px
     and the list half at 520 px so the 3D pane cannot be dragged into a
     strip. With a preview the right half opens on the 清单选择 table (the
     identity tab remains one click away and validation still switches to
     it).
  6. **Page framing and labels (3.8.0.6).** The page camera fits the route
     bounds (segments, cabinet, endpoints, hose), never the whole floor slab —
     a 100 m slab must not shrink a 30 m route to a corner. Labels avoid each
     other in screen space with a fixed priority (line labels first, then
     direction/pin, cabinet, floor, axis last; axis labels hide when they
     collide). The page omits the far edge axis band (it covered a large slab
     in numbers/letters) and keeps only the in-plane ticks/letters near the
     route. Endpoints carry one «上游端 …»/«设备端 …» chip — no duplicate pin
     chips. Segment labels are shown in metres (`8000mm` → `8 m`), matching
     the WPF overlay. Missing/NaN envelope fields never poison the camera.
  7. **One cable colour; axis marks on the grid.** Route segments are drawn in
     one colour (orange) — the isometric X/Y/Z axes are reconstruction
     internals, not a legend (user: "你加XYZ干嘛"). Axis numbers/letters sit at
     the ends of their grid lines on two adjacent slab edges (not floating in
     the middle of the floor) and are collision-filtered. The endpoint axis
     chips («上游端 55/C», «设备端 56/J») are placed at the **grid intersection**
     of that number line and letter line — the grid corner — joined to the real
     route end by a thin line, so an endpoint that is not on the axis centre is
     visible at a glance (user: "轴位肯定在网格夹角"). The WPF fallback uses the
     same single colour and legend.
  8. **Coordinate lattice at every grid intersection (3.8.0.8).** Each column
     grid crossing carries its axis coordinate (e.g. `55/C`) across the whole
     floor; the lattice is generated from the in-plane tick/letter values at
     the 4.8 m pitch and filtered by a screen-cell occupancy hash (labels too
     close on screen hide, and reappear as the user zooms in). The route is a
     single bold orange tube so it reads at a glance against the lattice. This
     supersedes the edge-band and in-plane middle-row placements of
     3.8.0.6/3.8.0.7.

## D-058: Route Recognition Rebuilt — U1U 3D Pane, Gap Tolerance, Block-Anchored Ends, U1UL (2026-11-25)

- Status: accepted (user instruction 2026-11-25)
- Problem: since 3.4.1 the U1U confirmation windows carried no 3D route
  preview at all — `FillRouteInspection.Preview` was never assigned
  (`BuildPreview` stayed in the stage as dead code behind a contract asserting
  "不构建 3D 场景"). Route recognition joined segments only inside the strict
  1.0 endpoint tolerance, so a hand-drawn frame whose segments "look connected
  but show a hairline gap when zoomed" fell apart into several chains. The
  fill-time label placement (default-off U1SET switch) always pushed
  millimetre labels back to the configured side, discarding a side the user
  had chosen by hand.
- Decision — the user's recognition rule: "线段肯定带 mm 标，不带就是有问题；
  桥架旁边肯定跟着桥架文本；还有软管".
  1. **U1U builds the 3D preview again** (single-frame review window and batch
     confirmation window; left half, layout unchanged). U1F still does not.
     The model is attached inside the inspection stage (`AttachPreview`) in
     the same read transaction and wrapped in try/catch — a failing scene
     degrades to one diagnostic line and never blocks the write. Batch
     collects annotation texts once per batch (same rule as parallel outputs).
  2. **Gap tolerance** (`UNC_ROUTE_GAP_TOLERANCE`, drawing units, default 50,
     0 = strict only, capped at 500): two segments are joined when their
     endpoints lie within the tolerance **and both carry a millimetre
     label**. Unlabelled lines never join by tolerance. It applies to the
     U1F/U1U connectivity report ("…（容错接上 N 处断口）"), the U1U scene and
     U13D. U1LX traversal and Xauto keep the strict 1.0 tolerance (they move
     geometry).
  3. **Problems are named, never guessed.** Lines in the frame without a
     millimetre label are listed by handle on the pane, the command line and
     the batch remarks. A labelled segment whose direction is not an
     isometric/orthographic axis is named as skipped, not dropped silently.
  4. **Tray/conduit by text** (amends the 3.3.1 consequence "do not draw
     tray/conduit channels from colour/text guessing" for the preview only):
     a tray/conduit text (the statistics grammar) that carries no ownership
     metadata binds to the nearest parallel labelled segment, is drawn on
     that segment only and is reported as a proximity binding. Metadata
     ownership always wins and is never overridden; a labelled tray/conduit
     output whose host line no longer exists is reported as orphan. A
     parallel unlabelled line hugging a labelled segment that has a channel
     is the annotation's own line (legacy pre-metadata output) and is not
     reported as a missing label. Layer governance is unchanged — it still
     never guesses from text.
  5. **Route ends come from the connection blocks** ("离设备块最近的末端是
     设备端，离上游块最近的是上游端"): the upstream end is the open route end
     nearest the upstream block, the device end the one nearest the device
     block. One block kind missing → the other end is the unique remaining
     end of a two-ended route. Neither block locatable → workbook-axis
     inference (3.4.0.8 rule). Both ends equally near, or both blocks nearest
     the same end → unresolved with the reason shown; nothing is guessed.
  6. **Hose in the preview** (display only): length from the Ruanguan block
     attribute, or from a U1B hose label when the frame has no Ruanguan
     block; drawn as a green tube continuing from the device end along the
     last segment. It is schematic, writes nothing and enters no statistic.
  7. **U1UL — millimetre-label alignment**: changes only the perpendicular
     distance (U1SET offset, or centred on the line) and the angle (parallel
     to the line, keeping the label's reading direction). The side the label
     is on now is kept — a label moved to the other side stays there — and
     so is its position along the line. Only labels carrying the U1L/U1LX
     association are moved; labels matched by position alone are listed and
     left untouched. One transaction (one U). Frame selection = whole frame;
     line or label selection = those only.
- Consequence: do not disable the U1U preview again without a new decision;
  do not let unlabelled lines join by tolerance, and do not raise the
  tolerance above the cap; U1UL must never change text, height or side;
  preview recognition is read-only and never feeds BOQ or statistics; the
  default-off fill-time placement switch is unchanged (it is opt-in).

## D-057: Same-Name Circuits Everywhere, Drawing-Info Table Templates, Layout Options (2026-11-24)

- Status: accepted
- Problem: D-056 fixed the U1U row match, but everything downstream still
  grouped by circuit **name**. A machine with two same-name circuits fed from
  different upstreams was reported wrong or silently merged: XSTS collapsed
  them to one circuit and flagged the second frame as a duplicate; XLAYOUT drew
  the yellow duplicate frame around both, collapsed the expected count, put
  both frames into one upstream row with the row order of the first; the BOQ
  device column summed both circuits (and zeroed the other one when only one
  frame was in the batch) while the DATA sheet kept only the last row; the PDF
  name `<machine>_<circuit>.pdf` told them apart by a `-2` suffix in selection
  order, so re-exporting one circuit replaced the other's PDF; the U1DWG
  selection window counted 2 frames as 1 circuit.
- Decision:
  1. **One pairing rule for every consumer.** `Core/Fill/CircuitMatcher` pairs
     a machine's workbook rows with its frames: pass 1 consumes frames by
     circuit identity key; a frame left over may pair by name only when that
     name is **unique** in the machine (legacy frames, re-homed upstream); a
     same-name frame without a usable key is never guessed — it is *pending*
     (待确认); a frame whose key equals an already-paired row, or a surplus
     frame of a name whose rows are all paired, is a *duplicate*; a name the
     workbook does not know is *extra*. XSTS, XLAYOUT and the whole-machine
     gate (U1S/U1DWG/U1U evaluation) all call it, so the three agree.
  2. **Pending frames do not count as covered.** The gate reports the machine
     incomplete and names the frames ("机台有 N 条同名回路，图框 … 未记录上游
     编号，无法判定对应哪一条；其中缺 N 张"); it never states which specific
     same-name row is missing, because that cannot be known. The fix is a
     U1F/U1U run on that frame, which records the upstream. Before this change
     the name pass let a copied frame stand in for an undrawn circuit.
  3. **XSTS** counts circuits, not distinct names; legitimate same-name
     circuits raise no issue; the machine sheet gains 重复图框 / 待确认 columns
     after the existing ones, and the frame sheet gains 上游编号 after
     回路名称 (later columns shift right by one). A frame with the same
     identity as an earlier frame is still reported as a duplicate.
  4. **XLAYOUT** marks only the surplus frame (same identity, or surplus of a
     same-name group), never a legitimate same-name circuit. In upstream mode
     each frame goes to the row of its own upstream; upstream numbers are
     grouped by the annotation-stripped form (D-056 normalization), so one
     feeder written two ways lands in one row and the row label shows the
     stripped number. A same-name frame with no usable key goes to a trailing
     「同名回路待确认」 row, not to 「未配上游」. Row order per circuit follows
     that circuit's own workbook row. Machine-readiness rank and label colour
     both come from the frames arranged in this run (previously rank came from
     the whole selection), and in upstream mode the row label takes the worst
     machine of the row. The workbook snapshot is read once per command.
  5. **Outputs.** BOQ device columns are grouped by circuit identity and,
     when the workbook has same-name circuits for that machine, named
     `回路名（上游 X）`; the rename is decided from the workbook, not from the
     batch, so the column name is stable across runs. A legacy merged column
     with the plain name is taken over (renamed) by the first such circuit, so
     no stale summed column stays in the total. The DATA sheet keys rows by
     machine + name + upstream and gains an 上游编号 column at the end; a
     legacy row without upstream is reused and stamped once. U1PDF adds the
     upstream number to the file name only when the machine has same-name
     circuits. The U1DWG selection count and the U1DWG result list dedupe by
     identity.
  6. **Drawing-info table templates.** The six columns 专业 / 楼层 / 制图 /
     审核 / 日期 / 版本 of the frame's small table take their content from
     per-column templates (U1SET → 图框与排版): literal text and placeholders
     `(名称)` / `（名称）`, with an optional `:format` on time placeholders.
     An empty template leaves that cell untouched, so hand-written values in
     the frame survive; the defaults reproduce the previous behaviour exactly
     (楼层 `(楼层)`, 日期 `(日期)`, the rest empty). Unknown placeholders are
     written literally and are called out on the settings page.
  7. **Creation time.** `frameinfo_json` `meta` gains `filledUtc` (the latest
     U1F — U1F is redefine mode, so each U1F re-creates the record) and
     `lastCommand`. `(U1U更新时间)` is empty until a U1U has run since the
     last U1F. A U1U on a record without `filledUtc` adopts that record's
     `updatedUtc` as the creation time. Old JSON parses with both empty.
  8. **More options.** BOQ title lines A1–A3 are templates (blank = keep the
     template file's text). XLAYOUT spacing, label text height, label distance
     and the yellow warning frame become settings whose defaults are the
     previous constants.
- Rationale: crews hit same-name circuits on real machines (two `AC Rack`
  rows on MEDPC01). The identity key already existed; the remaining defects
  were name-keyed grouping in consumers. One shared pairing rule removes the
  class instead of each instance. The drawing-info table was hard-coded to two
  cells and had no creation time to show.
- Consequence: visible output changes — XSTS machine sheet has two extra
  columns and the frame sheet's 上游编号 column; BOQ files of machines with
  same-name circuits get suffixed device headers and a DATA column; PDF file
  names of those machines gain the upstream number; XLAYOUT no longer frames
  both frames of a same-name pair. Machines without same-name circuits and
  default templates produce the same files as 3.5.0 apart from the XSTS column
  additions. Re-introducing name-only grouping, or making the gate accept a
  pending frame, needs a new decision.

## D-056: Circuit Identity Key For Same-Name Circuits (2026-11-23)

- Status: accepted
- Problem: one machine may carry multiple circuits with the **same name but
  different upstream feeders** (e.g. two `ECS` rows fed from different panels).
  U1U matched by machine+name and took the first row (`FirstOrDefault`), so a
  live workbook refresh that reorders rows could silently attach the frame's
  quantities to the wrong same-name circuit. The U1S/U1DWG whole-machine
  coverage check deduplicated by circuit name, collapsing same-name circuits
  into one and passing incomplete machines.
- Decision (3.5.0.1):
  1. **Circuit identity key** = normalized `U_机台ID + 回路名称 + U_上游编号`
     (NFKC width folding, whitespace stripped, case-folded; circuit names keep
     parentheses — `AC Rack (630A)` ≠ `AC Rack`; upstream codes drop trailing
     annotations: first `(` cuts the axis note, then only the first
     whitespace-separated token is kept, so `2U-UPS5D1-PP-02 40-42(41/Ab)`
     normalizes to `2u-ups5d1-pp-02`). Volatile construction attributes
     (facility switch, cable model, floors, axes) are **not** part of identity;
     they flow through the existing per-field change history.
  2. **The key is derived on demand, never persisted** (amendment after the
     first implementation stored a hash in `identity.circuitKey`): the three
     plaintext components are already persisted in frameinfo_json
     (machineId/deviceName/fr), so the key is a computed property of the
     record. The JSON stays byte-identical to before, remains human-readable
     and reversible; both sides (workbook row vs. frame record) compute with
     the same normalizer, so the comparison cannot drift.
  3. **U1U matching order**: key match (exact, unique) → single name match →
     same-name ambiguity returns the candidate rows to the caller. Single
     frame and batch U1U show the CircuitAmbiguityWindow (dense candidate
     grid: circuit/upstream/switch/cable/floor+axis/type, full-field detail
     strip; double-click or confirm returns the choice, cancel skips that
     frame). The user's pick writes the plaintext identity through the normal
     U1F/U1U path, so the next run's computed key matches directly — no
     persistent marker. Paths without a picker (U1PDF naming, 3D preview)
     report the ambiguity and skip. Two workbook rows with the same key
     (same name AND same upstream) are a data error and are reported, never
     guessed. An upstream re-home (frame key stale, name now unique) still
     updates in place; the `Fr` field change is recorded as usual.
  4. **Coverage by key**: U1S hard gate, U1U soft gate (BOQ skip), and U1DWG
     evaluation compare expected workbook rows against selected frames by
     computed key first, falling back to name matching (legacy frames).
     Matching is one-to-one (a selected frame satisfies at most one expected
     circuit), so two same-name circuits with only one drawn frame correctly
     report the other missing; missing same-name entries display an upstream
     suffix (`ECS（上游 3N-11B1A-PP-02）`). U1DWG's per-machine circuit count
     deduplicates by key.
  5. **Refresh diagnostics**: machine-workbook refresh (U1SET and background)
     reports same-machine rows whose computed keys collide (same name and
     upstream) with their Excel row numbers, so duplicates are fixed at the
     source before they can reach matching.
  6. **UI compaction** (user request「能展示更多内容」): the U1U compare
     window becomes a master-detail layout — a dense frame grid (one row per
     frame: machine/circuit/upstream/switch/added/removed/quantity, row
     height 24) above the selected frame's detail (field changes, cable card,
     side-by-side tables), one window size for single and multi frame. The
     batch confirmation window gains upstream/switch columns (row height 28)
     and a collapsible selected-row detail strip; the batch result window
     gains a key-deduplicated circuit count column.
- Rationale: row order in a live workbook is not identity; a name alone is not
  identity when same-name circuits exist; but hashing identity into the JSON
  adds an opaque, non-reversible field that duplicates data already stored in
  plaintext. Deriving the key from the persisted plaintext at comparison time
  keeps the JSON unchanged and readable while making the comparison stable.

## D-055: XLAYOUT Filter-List Import And Result Export (2026-11-22)

- Status: accepted
- Decision: two XLAYOUT additions (3.5.0):
  1. **Filter-list import = pre-check, not a hard filter.** The selection
     window gains an「导入清单…」button that loads a machine/circuit list from
     an Excel (.xlsx) or CSV file. Matching entries get checked, everything
     else unchecked, and a summary names the entries not found on the drawing;
     the user can still adjust any check before confirming. The file dialog
     and parsing live in the Features layer (UI never reads files). Columns
     are auto-detected by header aliases — machine:
     `机台ID/U_机台ID/机台/MachineID/机台编号`; circuit:
     `回路名称/U_回路名称/设备名称/回路/设备` — matched whitespace-insensitively;
     the circuit column is optional (a machine-only row pre-checks the whole
     machine). An unrecognized machine column aborts the import with the
     acceptable header list (no positional guessing). CSV supports UTF-8 BOM,
     strict-UTF-8, and GBK (936) fallback, with the delimiter (`,` `;` TAB)
     sniffed from the header line. Rows with an empty machine ID are skipped;
     duplicate machine+circuit entries are collapsed. Matching follows
     IdentityTextNormalizer (D-021: internal whitespace ignored).
  2. **Result export, list-anchored.** After the layout completes, the
     statistics window gains an「导出 Excel」button that writes
     `<输出根>\UNCAD_XLAYOUT_<时间戳>.xlsx` (same root rule as XSTS, D-035;
     normalized root written back, write-occupancy prechecked, atomic
     tmp+replace write). When a list was imported, the workbook's 清单结果
     sheet has one row per list entry with the outcome `Done` (matched and
     arranged this run, frame handles listed), `未参与排版` (matched on the
     drawing but unchecked for this run), or `找不到` (no matching frame),
     plus machine-level notes (missing/duplicate circuits). The 排版图框 sheet
     always lists every arranged frame. Without an imported list only the
     frame sheet exists. Export failures show in the window and never affect
     the completed layout.
- Rationale: field crews stage layout scopes from external machine lists and
  need a Done/not-found receipt after arranging — pasting IDs into the filter
  box (3.2.0.9) covered finding, not selection receipts.
- Consequence: the imported list influences only pre-checked state; every
  existing selection-window capability (region filter, arrange modes, manual
  checks) is unchanged. Identity for matching comes from the frame's own
  attributes (D-045 amendment, 3.4.1.11), so list matching is
  selection-independent.

## D-040: Frame Metadata Lives Inside The Frame Block (FRAME_20261102, 2026-11-02)

- Status: accepted
- Decision: the product requirement has **always been** that the frame JSON
  lives in the frame block's own text attribute. The 2.9.1–2.10.0
  implementation (a standalone `frameinfo_json` block placed near the frame,
  associated geometrically) was a misreading of that requirement, not a design
  choice — it silently worked until real drawings were copied and moved, which
  produced phantom machines, hijacked JSON blocks and lost device reserves
  (see the 2026-11-02 incidents). This decision restores the requirement: the
  template becomes the user-authored `FRAME_20261102` block whose
  `FRAMEINFO` text attribute stores the JSON, and U1F/U1U auto-migrate all
  older generations (frame / frame_20260812 / xframe / xframe_v2) to it.
  Standalone `frameinfo_json` blocks and legacy embedded `FRAMEINFO_JSON`
  attributes remain readable as compatibility sources for drawings produced
  during the deviation era; they are cleared (when they belong to the frame)
  after the first write, and no new standalone blocks are created.
- Consequence: metadata follows the frame entity natively — copying or moving
  a frame can no longer detach its record. The 2.11.0-dev mitigations
  (`meta.frameHandle` binding, JSON-vs-label identity arbitration,
  `BelongsToMachine` inheritance guard) stay as compatibility layers for
  legacy sources. Old drawings keep working unchanged; a frame whose legacy
  JSON was already hijacked re-prompts its device reserve once.
  XFrameTemplate.dwg (user-supplied) is the single template source and ships
  from `Resources/XFrameTemplate.dwg`.

- Amendment (2026-11-19, `FRAME_20261119`): the user-supplied template now
  defines `FRAME_20261119`, superseding `FRAME_20261102`. U1F/U1U migrate
  older frame definitions to it in the existing write transaction, preserve
  same-name attribute values, add newly defined attributes, and remove
  attributes absent from the new definition without resizing existing tables.
  Its single-line `INFO2` attribute lists available fixed-catalog bridge,
  conduit, and hose models with their measured millimetre segments and totals.
  U1U preserves only the INFO2 categories whose measurements are unknown;
  measured or confirmed-empty categories are refreshed independently.
- Amendment (2026-11-19, 3.4.1.4 — frame bridge and conduit length display):
  show frame attribute lengths in millimetres. A single segment is shown as
  `4000mm`; multiple segments are shown as `2000 +2500 = 4500mm`. The retired
  `CONDUIT_INFO` attribute uses the same format when present for compatibility.
  BOQ quantities and statistics reports retain their existing units.
- Amendment (2026-11-19, 3.4.1.5 — INFO2 display scope and format):
  `INFO2` shows only bridge and rigid-conduit entries. It uses a space between
  model and length (`镀锌穿线管38mm 4000mm`), with multiple segments formatted
  as `2000 +2500 = 4500mm`; conduit names use `镀锌穿线管{diameter}mm`.
  Hose entries are not shown in `INFO2`; the separate Ruanguan block update is
  unchanged. U1U retains old bridge/conduit entries only when that category's
  measurement is unknown, and removes old hose entries during the same update.

## D-043: Legacy Device-Reserve Marker Auto-Absorption (2026-11-03)

- Status: accepted
- Decision: U1F/U1U scan the selected frame range (the same text set the
  statistics stage reads) for the field's legacy device-reserve marker — a
  horizontal plain-number text (optionally suffixed mm, 0-100000, tilt within
  ~1 degrees). Exactly one candidate is absorbed as the device reserve
  (overriding any stale JSON value) and the marker text is deleted in the write
  transaction. Multiple candidates are never absorbed automatically (they may
  be axis labels or sequence numbers); the candidates are surfaced in the batch
  result window notes and the command line for manual confirmation.
  `UNC_FILL_DEVICE_RESERVE_AUTO` (U1SET list/table page, default on) turns the
  whole feature off.
- Amendment (2026-11-15, 3.2.0.4/3.2.0.5, D-052 appendix 7): the candidate must
  be a **single-line integer ending in `0` with a mandatory `mm`/`MM` suffix**
  (bare numbers, `毫米` suffixes, decimals, `0mm` and multi-line text are no
  longer candidates; `3000mm`/`550mm` qualify, `555mm` does not). **U1F absorbs
  unconditionally** (redefine mode — the marker is the user's intent for
  whatever machine they pick); the copied-frame guard is **U1U-only**. The
  absorbed value rides an empty-machine-id session record that survives
  machine re-picks (3.2.0.5 audit fix).
- Amendment (2026-11-15, 3.2.0.6 — audit round): ① zero-valued same-form
  candidates (`00mm`/`000mm` parse to 0 and matched the regex) are **rejected**
  — absorbing 0 while deleting the text is pure data loss; ② labels carrying a
  `UNCAD_U1LX` line association (i.e. UNCAD's own U1L/U1LX millimetre labels,
  which on horizontal lines share the marker's exact shape) are **excluded on
  the reader side** — the D-043 marker is a hand-written standalone text, never
  a UNCAD-generated length label (D-028: U1L labels are cable lengths and must
  stay in statistics).
- Rationale: the reserve journey was JSON first-ask, then CABLE_INFO recovery;
  drawings produced with the old manual habit still carry the horizontal mm
  text, so crews retyped the number into the prompt and deleted the text by
  hand. Absorb-and-erase makes the old drawing self-migrating.
- Consequence: a horizontal mm text anywhere inside the selected range that is
  the sole candidate will be consumed — a legit standalone number that should
  stay must be turned into non-plain text (or the toggle turned off). The
  absorbed value follows the existing pipeline (JSON persistence, CABLE_INFO
  `\\数值（设备内）\\`, review-window editing), so downstream behavior is unchanged.

## D-044: U1F Fills Frames On Partial Coverage Too (2026-11-03)

- Status: accepted
- Decision: amends D-033/D-034. U1F joins U1U's partial-coverage behavior: when
  the whole-machine circuit gate fails, U1F still fills the selected frames
  (identity, table, block attributes, reserve) and only skips that machine's
  BOQ. U1S and U1DWG keep the hard gate. The U1SET toggle
  (`UNC_FILL_REQUIRE_ALL_CIRCUITS`) still disables the gate entirely.
- Rationale: field crews draw and fill one circuit at a time; the old U1F hard
  stop left the frame untouched until every circuit existed, which fought the
  actual workflow. Frame data depends only on that frame's own statistics and
  machine row, so filling early is never wrong data — the data-integrity risk
  stays confined to the BOQ, which is already gated.
- Consequence: a partially filled machine has frames with data but no BOQ until
  `U1S` runs after the last circuit lands.

## D-045: XLAYOUT Arrange Modes (2026-11-03)

- Status: accepted
- Decision: XLAYOUT gains three arrange modes, picked in the selection window
  (remembered as the next default via `UNC_XLAYOUT_ARRANGE`):
  1. Machine readiness (default) — one row per machine, machines ordered by
     health: complete > duplicated/extra > missing-circuits > no-baseline;
  2. Machine ID order — one row per machine, alphabetical;
  3. By upstream — all circuits sharing the same `U_\\上游编号\\` land on
     one row (rows alphabetical, circuits without an upstream in a trailing
     "\\未配上游\\" row, and the row label shows the upstream number instead of
     the machine ID).
  Within a row, circuits follow the machine workbook's Excel row order; when
  the snapshot is unavailable the layout falls back to the drawing's left-to-
  right order (previous behavior). Machine summary statistics, label colors and
  the result window stay per-machine in every mode.
- Rationale: crews stage the layout around different work units — health-first
  for chasing incomplete machines, upstream grouping to match how cables are
  actually pulled in the field — and the workbook's row order is the review
  order they already trust.
- Consequence: rows are no longer guaranteed one-machine-only in upstream mode,
  so per-machine statistics aggregate across rows; the trailing
  "\\未配上游\\" row signals workbook gaps to fix.
- Amendment (2026-11-22, 3.4.1.11 — layout identity reads frame attributes
  only): XLAYOUT frame identity comes exclusively from the frame's own
  `MACHINEID-POWER`/`MACHINEID-DEVICE` attributes (plus the FRAMEINFO JSON
  with unchanged arbitration); device-block `DEVICENAME` never participates,
  so the "frame attribute vs DEVICENAME mismatch" abort is structurally
  impossible on the layout path. Field case (2204-frame whole-drawing run):
  eight MTPWM04 frames aborted the layout on `Hot DIW-1`/`LP` device values
  while selecting the same machine's frames alone succeeded — device blocks
  are attributed to frame regions by insert point, and the whole-drawing vs
  single-machine boundary sets differ, so the attributed device set (and thus
  the read identity) drifted with the selection. Decoupling identity from
  device-block attribution makes both selections read the same identity. The
  DEVICENAME hard check stays for U1S/U1F/U1U, which match Excel circuits by
  it; frames without composite attributes still fail per the existing
  semantics.

## D-046: WPF Design Tokens And Window Adaptation (2026-11-03)

- Status: accepted
- Decision: all 3.0 WPF windows share one design-token dictionary
  (`UI/Wpf/FluentKit.xaml`, merged per window by `UiShell.ApplyFluentTheme`;
  settings pages additionally merge it per page because the WPF-UI
  NavigationView frame does not inherit window resources). Sizes: page title
  20 semibold, section title 14 semibold, body/field label 13, caption 12;
  cards pad 18/16 with a uniform 12 bottom gap. Every window is clamped to
  92% of the screen work area and centered on startup (edges managed by
  SizeToContent are untouched).
- Rationale: pages had grown ad-hoc margins (8/12/20 mixed), mismatched field
  widths, and unusable behavior on small screens; the U1F machine picker's
  suggestion list pushed the layout down and the circuit list had no filter.
- Consequence: changing the look means editing one file; new windows get the
  tokens automatically (styles are referenced with DynamicResource, so
  standalone instantiation degrades to defaults instead of crashing). The
  U1F picker keeps its suggestion list in a Popup overlay and gains a circuit
  filter with a "显示 X / N" counter; long text columns in the five result
  windows trim with tooltips.

## D-042: One Folder Per Machine, Batch Results In A Window (2026-11-03)

- Status: accepted
- Decision: amends D-033 output clause. Every output lands in the machine own
  folder — `<root>\\<machine>\\` holds that machine `[BOQ]<machine>.xlsx`,
  `<machine>.dwg` and per-circuit PDFs together. The drawing-name layer, the
  BOQ/DWG/PDF kind layer, and their two U1SET toggles (`UNC_OUTPUT_BY_DRAWING`,
  `UNC_OUTPUT_SEPARATE_KINDS`) are removed. Batch U1U ends with a per-machine
  result window (frames / table rows / attribute writes / BOQ status + path /
  notes) and prints a single summary line; per-step progress and file-path lists
  no longer stream to the command line — progress stays in the progress window,
  details in the result window and log.
- Rationale: this is an efficiency tool; field users read windows, not terminal
  scroll-back. Grouping by drawing name scattered one machine BOQ/DWG/PDF into
  three sibling trees and made handoff to customers harder.
- Consequence: `OutputLayout` composes machine-folder paths only; XSTS report
  still writes directly into the configured root (D-035 unchanged). Old layered
  folders on disk are left as-is; rerunning a machine writes to the new location.

## D-041: Download-Only Network Workbook Sources Return (2026-11-02)

- Status: accepted
- Decision: amends D-033. `UNC_FILL_EXCEL` may again be an HTTP/HTTPS URL.
  Downloading happens only inside an explicit refresh (U1SET "刷新" button or
  `U1DATA`); drawing commands keep reading only the SQLite snapshot and never
  touch the network. Old D-023 startup auto-refresh stays removed — a dead URL
  must not produce error spam on every AutoCAD start.
- Guardrails (carried from D-033): nothing is uploaded — GET only, 100 MB cap,
  20 s timeouts; `ClientSubmissionUploader`, `BackgroundUploadQueue` and
  `MachineLedgerReporter` stay deleted. A failed download with no prior
  snapshot is a Chinese error; with a prior snapshot the old snapshot is kept
  (`UsedCachedFallback`) and the warning surfaces in U1SET.
- Rationale: field teams serve the machine workbook over HTTP from internal
  servers; 2.10.0's blanket removal broke them. The 2.10.0 pain (multi-hour
  stalls, ~5 GB peaks, error spam) came from the upload mirror and the startup
  queue, not from the on-demand download.

## D-039: Version Channels — Development Builds vs Stable Releases (2026-11-02)

- Status: accepted
- Decision: 3.0-era work accumulated on a **development channel** labelled
  `X.Y.Z-dev` in `ProductMetadata.VersionLabel` (the 3.0.0 series used
  `3.0.0-dev`, closed when it shipped as `3.0.0` on 2026-11-03; a future dev
  channel needs a new dated decision).
  Dev builds ship only as acceptance installers via `release.ps1 -Dev`; they
  skip the stable-only gates (RELEASE page, git tag) while keeping all test,
  performance, and migration gates. A **stable release** requires stripping
  `-dev`, a `docs/RELEASE-X.Y.Z.md` page, and an annotated tag —
  `release.ps1` refuses `-Dev` for stable labels and refuses dev labels
  without `-Dev`. D-032's X.Y.Z/fourth-part rules apply to stable releases
  only; dev build numbers increment the assembly fourth part freely.
- Consequence: adding features no longer mints a customer release (2.11.0 was
  retracted before tagging and re-labelled `3.0.0-dev`). Customers see only
  deliberate stable versions; the stable line moved to `3.0.0` on 2026-11-03
  after real-machine acceptance.

## D-038: 3.0 UI Scope — Every Dialog Moves To WPF (2026-11-02)

- Status: accepted
- Decision: amends D-037 — during 3.0 **all** user-facing dialogs migrate to WPF
  with the WPF-UI Fluent library, not only the major screens. The design
  language and per-screen layout patterns are codified in
  `docs/UI-3.0-DESIGN.md`; each form migrates one form per commit with its
  tests, and the replaced WinForms file is deleted in the same change. The
  QuickLine 3D preview stays WebView2 content per D-025. Inside the AutoCAD
  process, WPF-UI theme/control dictionaries are merged **per window** —
  never into `Application.Current.Resources`, because AutoCAD owns the
  process-wide WPF Application and its Ribbon resources must not be touched.
- Consequence: `UiShell` (UI layer) is the single owner of Fluent theme
  application for plugin windows; Features keep invoking dialogs through
  AutoCAD's modal APIs (`ShowModalWindow` for WPF, `ShowModalDialog` only
  during the transition). Layer rules are unchanged — UI still edits
  passed-in models only. 3.0 stays multi-track: the UI migration runs in
  parallel with the release-gate performance budget, the security hardening
  (shipped as 2.10.0.6), the FillFeature decomposition, and the frame
  migration self-check suite. UI is one track of 3.0, not the release.

## D-037: UNCAD 3.0 Direction — WPF-UI Front-End, 2022-Only, Phased Delivery (2026-11-02)

- Status: accepted
- Decision: UNCAD 3.0 adopts WPF as the front-end stack, using the WPF-UI
  (lepoco) 4.x Fluent library — verified end-to-end on net48 by the standalone
  demo under `demo/UiDemo` (restore, build, runtime, light/dark themes, real
  screenshots). WinUI 3 is excluded: Windows App SDK requires .NET 6+ while
  AutoCAD 2022 hosts only .NET Framework 4.8. WebView2 remains limited to
  low-risk read-only content per D-025 (3D preview, later help/changelog);
  core dialogs stay native. AutoCAD support stays 2022-only throughout 3.0.
  Delivery is phased: guard tests and groundwork gates first, then screen-by-
  screen WPF migration, and the rename to "UNCAD 3.0" (dropping "Pro") last.
  Existing customers upgrade for free; the installer UpgradeCode and the
  `%APPDATA%\UNCAD` paths must not change.
- Consequence: no big-bang UI rewrite — every migrated screen ships with its
  own tests and the migration can stop midway. `LayerGuardTests` freezes the
  module boundaries as tests: Core stays free of AutoCAD/WinForms/WPF, NPOI
  stays confined to Core, UI stays free of AutoCAD except `RibbonBuilder.cs`
  (the AutoCAD-side ribbon constructor, known debt until the UI migration
  decides its home), Cad stays free of Excel namespaces, and the four
  historical frame block names stay readable. Phase 1 (2.11) = perf budget in
  the release gate, the three security fixes, and the FillFeature stage
  split; none of these wait for the UI work.

## D-036: U1L Esc Keeps The Drawn Segments (2026-11-01)

- Status: accepted
- Decision: pressing Esc during `U1L` ends the command and **keeps** every
  already-committed segment and its millimetre labels. The per-segment
  transaction model stays; `U` continues to undo segment by segment. Real
  exceptions during drawing still erase the pending batch so no half-built
  geometry survives.
- Rationale: field users draw long polylines segment by segment; losing minutes
  of confirmed work because Esc was pressed at the end was the complaint.
  This also aligns the code with the COMMAND-AUDIT matrix, which already
  documented "Esc 结束并保留已完成段" while the implementation erased.
- Consequence: do not "restore L-command parity" by re-adding the erase.

## D-035: XSTS Report Writes Directly Into The Configured Output Root (2026-11-01)

- Status: accepted (amends D-033's layered output for XSTS only)
- Decision: the XSTS report goes to `<输出根>\UNCAD_XSTS_<时间戳>.xlsx` —
  directly in the configured output root (`UNC_SUBMIT_FOLDER`, default
  `Documents\UNCAD输出`), not into `<输出根>\<图纸>\XSTS\`. The write-occupancy
  precheck and the default-root write-back are kept. BOQ/DWG/PDF keep the
  layered `<root>\<drawing>\<kind>\<machine>` layout unchanged.
- Rationale: field users collect XSTS reports per work session in one folder;
  burying them under a per-drawing subfolder made the reports appear "missing".
  XSTS reports are ad-hoc statistics, not per-machine deliverables, so the
  per-drawing layering has no aggregation value for them.
- Consequence: `ResolveOutputFile(root, now)` is the single path rule; the
  `UNC_OUTPUT_BY_DRAWING`/`UNC_OUTPUT_SEPARATE_KINDS` switches no longer affect
  XSTS (they keep governing BOQ/DWG/PDF). Restoring a layered XSTS path
  requires a new decision.

## D-034: U1U Partial-Circuit Updates Proceed And Skip Only That Machine's BOQ (2026-11-01)

- Status: accepted (amends D-033)
- Decision: the whole-machine circuit gate stays a hard abort for `U1F` (new
  BOQ), `U1S`, and `U1DWG`. `U1U` — single-frame and batch — no longer aborts
  when a selected machine's circuits are not all in the selection: the selected
  frames are updated normally, and only that machine's BOQ output is skipped
  with a command-line note ("回路不全…不生成 BOQ；补全后执行 U1S").
- Rationale: U1U exists to refresh whichever frames changed; blocking the whole
  command because other circuits are not drawn yet made frame-by-frame updates
  unusable. The partial-machine BOQ remains forbidden — a BOQ rebuilt from a
  subset of circuits loses the missing circuits' quantities, so skipping the
  file is the safe side.
- Consequence: `UNC_FILL_REQUIRE_ALL_CIRCUITS` (default on) still suppresses
  the check when disabled. `EnsureCircuitCoverage` remains the hard gate;
  `EvaluateCircuitCoverage`/`CoverageIncompleteMachines` are the U1U evaluation
  entry points. Reintroducing a U1U abort, or writing a partial-machine BOQ,
  both require a new decision.

## D-033: 2.10.0 — No Cloud Sync, Whole-Machine Circuit Gate, Layered Output, Local History (2026-11-01)

- Status: accepted (the workbook-source clause is amended by D-041:
  download-only HTTP/HTTPS sources are back; the output-layout clause is
  amended by D-042: one folder per machine; zero upload is unchanged)
- Decision: the Pro package uploads nothing and downloads no machine data over
  HTTP/HTTPS. Account/licence validation (login, online state, single-device
  binding) is the only network feature. `U1F/U1U/U1S/U1DWG` refuse to generate
  BOQ/DWG unless every circuit of each selected machine is in the current
  selection (`UNC_FILL_REQUIRE_ALL_CIRCUITS`, default on); the missing circuits
  are listed and neither the drawing nor the BOQ is written. Output layout is
  `<root>\<drawing>\<BOQ|DWG|PDF>\<machine>\...` (default root
  `Documents\UNCAD输出`) with a write-occupancy precheck before the CAD
  transaction. File logs rotate per day + configurable level/retention; every
  command writes one local operation record (JSONL, 6 months default). Settings
  can be shared as Base64 that excludes licence/clock/update-memory keys.
- Rationale: field use never needed the server mirror; it produced multi-hour
  batch stalls, ~5GB memory peaks, network-error spam and a recovery path that
  could not actually retry. A partial-machine BOQ is a data-integrity defect,
  not a convenience trade-off.
- Consequence: `ClientSubmissionUploader`, `BackgroundUploadQueue` and
  `MachineLedgerReporter` are deleted; `U1PDF` keeps exporting locally. The
  command contract stays at 35 commands. Do not reintroduce an upload path or a
  remote data source without a new decision.

## D-032: Version Numbering — Features Move X.Y.Z, Fixes Move The Fourth Part (2026-10-11)

- Status: accepted
- Decision: `X.Y.Z` (`ProductMetadata.VersionLabel`) identifies a **feature
  release**. Adding a feature bumps it (fourth component resets to 0) with a new
  tag, a new `docs/RELEASE-X.Y.Z.md`, and a `VersionChangeLog` entry.
  Any change that does **not** add a feature (bug fix, test/build
  infrastructure, docs, cleanup) must leave `X.Y.Z` untouched and bump only the
  fourth component of `AssemblyVersion`, `FileVersion`, and
  `PackageContents.xml AppVersion` — e.g. `2.6.9.0` → `2.6.9.1`. No new tag and
  no new release note; the existing `RELEASE-X.Y.Z.md` covers the patch.
- Rationale: rapid `X.Y.Z` bumps for fixes and chores made the shipped version
  history unreadable (2.6.5/2.6.6/2.6.7/2.6.8/2.6.9 in one day, several for
  non-features) and produced a stream of near-identical "releases".
- Consequence: `release.ps1` and `BundleLoadingTests` compare only the first
  three components; `installer.ps1` still requires the DLL `FileVersion` to equal
  `AppVersion` exactly, so both must move together. Use
  `scripts/Bump-Version.ps1 -Kind Patch` for a patch and
  `-Kind Feature -Label X.Y.Z` for a feature. The historical 2.6.5–2.6.9 bumps
  are not rewritten.

## D-030: Downstream Block Rules (2026-10-11, amended same day)

- Status: accepted
- Block identity: the downstream end may be a block named exactly `Downstream`
  (Xmerge-mangled `$0$` suffix allowed). No family/prefix matching: `DS` axis
  blocks, `upstream` and `Downstream_GW` are not affected.
- States: the block carries the same three visibility states as `upstream`
  (`I-line_Panel` / `socket box` / `busbar connector`) and is colored with the
  device-side color.
- Material rules:
  - `socket box` → one extra 4.x socket-panel/box row, gated by the `插座盘`
    switch; rating from `U1SET` "下游插座箱额定电流" (default 20A).
  - `I-line_Panel` → one extra electrical-panel row, gated by the `电盘` switch;
    rating from `U1SET` "下游电盘额定电流" (default 1000A).
  - `busbar connector` → color only, no material.
- Never: no 8.x outlet row (the `Device_Build20260716` state owns it) and **no
  breaker** — breakers come only from the upstream `U_上游类型 = I-Line盘` rule.
  Do not "complete the symmetry".
- Ratings always come from settings, never from `U_配电信息`: that value is the
  switchboard main breaker current, and using it left the row unmatched
  (`BOQ-CATALOG-MISSING`) in the field.
- Conflicting states inside one frame abort the fill instead of guessing; an
  unreadable state must not disturb the other rows.

## D-028: U1L Labels Are Cable Length (2026-10-11)

- Status: accepted
- Decision: a bare millimetre label written by `U1L` (for example `2000mm`) is the
  cable length of that segment and **is counted** in U1U/U1F cable statistics.
  D-006 excludes only distances the user enters through `U1LX` as a separate data
  source; it must not be read as "exclude U1L labels from the cable sum".
- Consequence: do not add a "U1L/U1LX annotation is not cable" filter to the
  statistics reader. `U1L` exists to draw the line and annotate its cable length;
  `StatisticsTextReader` deliberately reads those DBText labels as cable sources,
  and `AnnotationLabelPair` keeps two-line bridge/conduit labels from leaking into
  that sum. A single-line `2000mm` stays cable.

## D-029: Flexible-Conduit Block Family Includes Ruanguan Variants (2026-10-11)

- Status: accepted
- Decision: the flexible-conduit (`Ruanguan`) block family includes names derived
  from it with a separator, such as `Ruanguan_GW`. Recognition is decided by the
  first segment of the normalised block name, so `Ruanguan`, `Ruanguan_GW`,
  `Ruanguan-GW` and `Ruanguan$0$` all belong to the family, while a glued name
  like `RuanguanXYZ` does not. `BlockNameNormalizer.IsFlexibleConduitBlock` is the
  single implementation used by frame selection and the Ruanguan writer.
- Consequence: length reading, model/length writing, U1F/U1U hose handling and U1C
  treat every family member identically. Adding another documented variant means a
  new name that keeps `Ruanguan` as its first segment — no per-variant code.

## D-001: One Pro Distribution

- Status: accepted
- Decision: publish only `UNCAD Pro`; customer, licensee, and expiry data come
  from the online `pro.key` response.
- Consequence: do not restore Trial, JSWY, JSHY, or compile-time edition paths.

## D-002: Unified Workbook Fields

- Status: accepted
- Decision: read only `U_` fields plus ordinary `回路名称`.
- Consequence: remove A1/A2 selection, backup-field fallback, and schema guessing.
  A struck-through circuit name is ignored.

## D-003: Manual Workbook Refresh

- Status: accepted
- Decision: the user selects the workbook and explicitly presses refresh in
  `U1SET`.
- Consequence: U1F/U1U/XSTS must use the cached snapshot and must not perform a
  hidden network request during a command.

## D-004: Frame Compatibility Without Resizing

- Status: accepted
- Decision: read both legacy frames and `xframe`; migrate a legacy frame when
  required using the bundled template.
- Consequence: migration must preserve user drawing geometry and must not alter
  table row heights, column widths, or overall table dimensions.

## D-005: `frameinfo_json` Is the Identity Record

- Status: accepted; storage amended by D-040 (2026-11-02): the record lives in
  the frame block's own `FRAMEINFO` attribute; the standalone block and legacy
  inline attributes remain read-only legacy sources.
- Decision: frame identity, last update, cable reserve breakdown, and
  replacement history are persisted in the standalone `frameinfo_json` block
  (schema v2). Legacy embedded `FRAMEINFO_JSON` attributes are read for
  migration and then cleared.
- Consequence: U1U should use this record to preserve user-selected
  substitutions and the confirmed device reserve instead of rediscovering them
  from incomplete labels or asking again on every run. Payload validity and
  length are enforced before writing; never persist a truncated JSON.

## D-006: U1LX Is Not a Quantity Source

- Status: deferred by request
- Decision: distances entered through U1LX are not included in U1U/U1F
  quantities.
- Consequence: do not silently add them to cable, bridge, or conduit totals.

## D-007: No Automatic Upstream Connection Geometry

- Status: accepted
- Decision: U1F/U1U update data and colors but do not create automatic upstream
  connection lines.
- Consequence: block shapes and existing drawing geometry remain user-owned.

## D-008: Preserve Existing Socket Quantities

- Status: accepted
- Decision: detect socket presence, add a missing socket row with quantity one,
  and never overwrite an existing user-entered quantity.

## D-009: XLAYOUT Duplicate Labels Are Intentional

- Status: accepted
- Decision: repeated XLAYOUT execution may add another machine-ID label.
- Consequence: performance improvements must not change this behavior unless a
  new product decision explicitly replaces it.

## D-010: Release Metadata Must Agree

- Status: accepted
- Decision: assembly, product metadata, Bundle manifest, tests, and release
  package must report the same numeric release version.
- Consequence: use `release.ps1` verification instead of hand-copying package
  files or embedding customer authorization data.

## D-011: Physical-Row Workbook Extraction (2026-09-05)

- Status: accepted
- Decision: after the header is bound, machine data is read only from the
  corresponding `U_` columns and ordinary `回路名称` column on the same
  physical row. Merged regions are ignored.
- Consequence: blank cells remain blank and values are never inherited from a
  merged region, another row, or an unrelated column.

## D-012: SQLite Workbook Snapshot (2026-09-05)

- Status: accepted
- Decision: parse the selected machine workbook only when the user presses
  “刷新” in `U1SET`, and persist the parsed rows and refresh metadata in the
  per-user SQLite snapshot.
- Consequence: `U1F`, `U1U`, `XSTS`, and `XLAYOUT` query the last successful
  snapshot and never inspect the source workbook or access the network during
  command execution. Changes in the source workbook take effect only after a
  later explicit refresh; a failed refresh leaves the previous snapshot intact.

## D-013: Ignore Rows Without Machine Identity (2026-09-05)

- Status: accepted
- Decision: during workbook refresh, a physical row with a non-empty
  `回路名称` but a blank `U_机台ID` is ignored.
- Consequence: an empty or uncached formula result in `U_机台ID` cannot abort
  the workbook refresh; other valid machine rows continue into the SQLite
  snapshot.
- Amendment (2026-11-22): rows excluded for a blank `U_机台ID` are reported
  after refresh with their count and the first eight physical Excel row
  numbers. They remain excluded; refresh does not fill down fields or infer an
  identity from another column or row.

## D-014: 350A Bus Plug Box Uses The 400A Catalog Item (2026-09-07)

- Status: superseded by D-015
- Decision: when a bus-plug upstream is rated `350A` and the fixed catalog has
  no exact `350A` item, use the fixed `400A` bus-plug-box item.
- Consequence: U1F/U1U write the `400A` catalog code, description, unit, and
  quantity without raising an unmatched-catalog prompt. Other ratings remain
  exact-match only.

## D-015: Ask For Unmatched Bus Plug Box Ratings (2026-09-07)

- Status: accepted
- Decision: unmatched bus-plug-box ratings, including `350A`, require the user
  to select a replacement rating from the fixed catalog. Do not automatically
  round up or use an uncoded default row.
- Consequence: batch U1U lists the affected machine and circuit and requires
  an explicit selection before writing CAD or BOQ. Persist the confirmed code
  in `frameinfo_json`; reuse it only while the frame identity and supply data
  remain unchanged and the catalog item is still valid.

## D-016: CAD And BOQ Must Share Catalog Identity (2026-09-07)

- Status: accepted
- Decision: every material row written by U1F/U1U and every material row
  exported to the automatic BOQ must carry the same fixed-catalog project
  code. Uncoded or unmatched rows are not allowed to be written to CAD as a
  batch fallback.
- Consequence: an unmatched row stops the batch before the CAD transaction or
  BOQ file is changed. The user must delete the row or choose a fixed-catalog
  replacement; the confirmed replacement is persisted in `frameinfo_json`.

## D-017: Dialogs Are Native WinForms (2026-09-10)

- Status: accepted
- Decision: every user-facing dialog is native WinForms styled through
  `UiTheme` and sized through `DialogLayout`. The parallel WebView2 + HTML
  dialog framework (the `UseWebUI` switch, its `WebForm`/`WebBridge` base
  types, and the HTML dialog resources) is removed.
- Consequence: do not reintroduce an HTML dialog layer without a new decision.
  `UseWebUI` and `WebDevDir` are no longer valid configuration keys — an
  existing `UNC_USE_WEB_UI` registry value is inert and needs no migration.
  WebView2 remains a dependency only for the startup splash
  (`StartupSplashForm`); removing that package would require rewriting the
  splash to its existing `StartupSplashCanvas` fallback and updating the
  bundle payload, installer, and packaging tests together.

## D-018: U1Q/U1C Labels Are Two-Line MTEXT Carrying The Catalog Name (2026-09-10)

- Status: accepted; automatic migration scope amended by D-019
- Decision: bridge (`U1Q1/U1Q2/U1Q4`) and conduit (`U1C`) annotations are
  written as a two-line MTEXT — line 1 is the full fixed-catalog `1.名称`
  value for the selected specification, line 2 is the length. Alignment stays
  bottom-centre (or top-centre below the line) so the block attaches at the
  same point the single-line `DBText` used to. A specification with no
  catalog row stops the command (`U1Q`) or aborts with a message (`U1C`)
  instead of writing an unlabelled annotation.
- Consequence: the statistics engine only understands the single-line forms
  (`桥架200*100 2500mm`, `⌀20线管 2000mm`), so the reader
  (`StatisticsTextReader`) pairs the two lines back into that form per MTEXT.
  Pairing is strict — line 1 must be a catalog bridge/conduit name and line 2
  must be a bare length. A rejected MTEXT that still clearly contains a bridge
  or conduit heading remains one statistics input so its bare length cannot
  leak into the cable sum; a generic `说明\\P2000mm`, a lone `2000mm`, and
  every single-line legacy cable label still count as cable. Re-running
  `U1Q*`/`U1C` replaces the legacy
  single-line labels in the command's own selection; nothing else is migrated
  automatically. `Φ32` resolves through the catalog `别名1` to the `38mm`
  row, so a `U1C` run at diameter 32 labels the drawing with that row's name
  and the statistics report the matching `⌀38线管`.

## D-019: U1F/U1U Upgrade Legacy Bridge Labels (2026-09-11)

- Status: accepted
- Decision: during the final CAD transaction, U1F/U1U automatically replace a
  selected/frame-contained legacy bridge label such as `桥架200*100 2500mm`
  (or its readable grid-count form) with the two-line fixed-catalog annotation
  `梯形桥架200Wx100H` + `2500mm`.
- Consequence: this supersedes D-018's "nothing else is migrated
  automatically" clause for bridge labels only. Migration occurs only when
  the catalog model exists and the resulting two-line label collapses back to
  the same statistics input; otherwise the original text is preserved.
- Amendment (2026-11-20, 3.4.1.7): U1F retains this migration. Single-frame and
  batch U1U no longer rewrite or rotation-repair legacy bridge labels; existing
  annotations remain as drawn.
- Amendment (2026-11-22, 3.4.1.8): U1F also leaves legacy bridge labels as drawn;
  neither U1F nor U1U migrates them automatically.

## D-020: Machine Batch And Sequence Metadata (2026-09-18)

- Status: accepted
- Decision: the unified machine workbook may provide optional `U_批次` and
  `U_序号` columns. XSTS reads these values from the refreshed SQLite snapshot
  and reports one batch cell and one sequence cell per machine, not one row per
  circuit.
- Consequence: repeated values across a machine's circuit rows are collapsed
  into the machine summary; old snapshots without the batch column are migrated
  in place with an empty value and remain readable.

## D-021: Identity Matching Ignores Internal Whitespace (2026-09-20)

- Status: accepted
- Decision: machine IDs and device names are compared case-insensitively after
  removing Unicode whitespace, including spaces inside the value. The original
  source text remains the display and persisted value.
- Consequence: values such as `M Q-01`/`MQ-01` and `设备 A`/`设备A` identify
  the same machine or circuit across frame identity, workbook snapshots,
  statistics, submission records, and BOQ grouping; matching never rewrites
  the user's visible text merely to remove whitespace.

## D-022: Xmerge Isolates Incompatible Same-Name Blocks (2026-09-21)

- Status: accepted
- Decision: Xmerge first reuses same-name block definitions when clone
  validation confirms that their geometry, dynamic state, attributes, and
  styles are compatible. If a source definition is incompatible, the failed
  transaction is discarded and that source is retried with AutoCAD's
  `MangleName` policy so its block references keep their own geometry.
- Consequence: merging sources with different versions of `xframe`, device, or
  other named blocks no longer stops the whole operation or silently changes a
  source block to the target version. Compatible duplicates remain shared;
  only conflicting definitions receive AutoCAD-generated `$N$` names.

## D-023: Remote Workbook Startup Refresh (2026-09-22)

- Status: superseded by D-033 (2026-11-01) — remote refresh and HTTP/HTTPS
  workbook sources were removed; U1DATA/U1SET only refresh local/UNC files.
  Download-only sources (without startup auto-refresh) are reinstated by
  D-041 (2026-11-02).
- Historical decision:

- Status: accepted
- Decision: this supersedes D-003 and D-012 only for a configured HTTP/HTTPS
  machine-workbook source. UNCAD queues one asynchronous refresh when AutoCAD
  loads; a local workbook remains manual. `U1DATA` explicitly refreshes either
  source type.
- Consequence: drawing commands keep reading the last successful SQLite
  snapshot while a refresh runs and never access the workbook or network
  themselves. Parsing or refresh failure leaves the previous snapshot intact.
  `U1D` is the shortcut for `U1DWG`; aligned-dimension conversion uses `U1DT`.

## D-024: U1L3D Read-Only WebView Preview (2026-09-24)

- Status: accepted
- Decision: `U1L3D` may host a dedicated read-only WebView2/Three.js preview.
  It accepts one complete frame selection, reconstructs the contained U1L route
  in 3D from its isometric directions and millimetre labels, and displays the
  machine workbook's upstream/downstream axes from the last successful SQLite
  snapshot. Adjacent grid axes are 4800 mm apart.
- Consequence: the preview never writes CAD entities, never refreshes a
  workbook or accesses the network, and does not restore the removed generic
  HTML dialog framework. Other user dialogs remain native WinForms.

## D-025: U13D Multi-Frame Read-Only Preview (2026-09-29)

- Status: accepted
- Decision: `U13D` accepts multiple complete frames, aligns them in one
  4800 mm axis coordinate system using each frame's downstream workbook axis,
  and shows U1L route/cable meters plus bridge meters.
- Consequence: bridge-to-route association is only reported when U1Q metadata
  identifies a unique source line. Legacy bridge labels without source
  metadata remain visible as unassociated; the preview never guesses.

## D-026: U13D Direction And Circuit Labels (2026-09-29)

- Status: accepted
- Decision: U13D labels each route with its machine and circuit, and marks
  downstream/upstream endpoints in the scene with compact dot/square markers;
  it does not draw long connector lines between axis endpoints.
  East/west uses numeric axes and north/south uses alphabetic axes; moving east
  increases the number and moving north increases the letter.
- Consequence: a route is one endpoint-connected U1L component, not one CAD
  line segment. Direction markers first attach to terminal endpoints of that
  component near the frame's device and upstream blocks; junctions and bends
  are never treated as upstream/downstream endpoints. When those blocks are
  unavailable, the marker falls back to the workbook axis coordinate and
  reports the fallback in diagnostics. The preview does not use XYZ colors as
  the primary orientation cue. Missing frame device text may use the uniquely
  matched cached machine row's circuit name for display, without changing
  drawing data.

## D-027: Fixed BOQ Data-Row Height (2026-10-09)

- Status: accepted
- Decision: U1F/U1U keep generated BOQ Table rows, including cleared rows, at
  5847.8848 drawing units. Long descriptions must not increase the row height
  or stretch the other rows. Text in an overflowing row is reduced to fit;
  a later update starts again from the configured text height.
- Consequence: all description text is retained, column widths and headers
  remain unchanged, and impossible cell margins are rejected before writing.
  The description column uses a 0.85 line-spacing factor for multi-line
  content. This applies to the generated BOQ data range, not frame migration
  or the read-only 3D preview sidebar.

## D-047: U1DWG Per-Machine Coverage With Force-Export GUI (2026-11-04)

- Status: accepted (user request; narrows D-033 for U1DWG)
- Decision: U1DWG evaluates the whole-machine circuit gate per machine instead
  of aborting the whole command. Machines whose circuits are all selected
  always export; incomplete machines are skipped by default. When any
  incomplete machine exists, a selection window lists every machine with its
  coverage status and missing circuits, and lets the user tick incomplete
  machines to force-export them as drawn. Cancel exports nothing. The filter
  runs before any file write. U1S keeps the hard abort (D-033); the
  `UNC_FILL_REQUIRE_ALL_CIRCUITS` off switch still disables the gate
  everywhere.
- Consequence: one incomplete machine no longer withholds a complete
  machine's DWG. Force-exported machines produce drawings from the circuits
  actually drawn; BOQ behaviour is unchanged (U1U already skips only the
  incomplete machine's BOQ per D-034/D-044). The window is the only place a
  partial machine can be exported; there is no silent fallback.

## D-048: U1U Force-BOQ Confirmation For Incomplete Machines (2026-11-04)

- Status: accepted (user request; extends D-034/D-044, mirrors D-047 for BOQ)
- Decision: when a U1U selection contains machines whose circuits are not all
  drawn, a confirmation window lists those machines with drawn/expected
  circuit counts and the missing circuit names. Machines are ticked by default
  NOT to generate their BOQ; the user can tick individual machines to force
  BOQ generation from the circuits actually drawn. Confirm with an empty
  selection means "skip all" (previous behaviour). Forced machines are marked
  in the batch result window remarks and the command line. Single-frame U1U
  shows the same window with one row; U1F (fill while drawing) does not offer
  forcing. Cancel equals no force.
- Consequence: a forced BOQ intentionally misses rows for undrawn circuits;
  the operator has been told so in the window, the result remarks, and the
  command line, and can rerun U1S after completing the circuits to get the
  full BOQ. Drawing updates are never blocked by this window.

## D-050: U1Q/U1C Continuous Runs, Analytic Offsets, And Independent Conduit Lane (2026-11-09)

- Status: accepted (user request + user-reported crossing defect; D-049 stays
  reserved for the pending ghost-frame proposal)
- Decision: `U1Q1/U1Q2/U1Q4` and `U1C` first group the selected curves into
  endpoint-connected runs (tolerance 1.0, same as U1LX); each simple chain
  becomes one run rendered as ONE continuous offset polyline plus ONE two-line
  label. Branched/self-loop components are not merged — each member keeps its
  own single-segment annotation (never guess). The offset polyline is built
  analytically (per-segment normal offset + miter joins, bevel fallback,
  whole-polyline side validation); `Curve.GetOffsetCurves` output must not be
  drawn directly anymore, because on cornered paths whose local reach is
  shorter than the offset it self-intersects and crosses the baseline (the
  user-reported "red line crosses the baseline" defect). The U1Q label length
  is initialized from the sum of the U1L millimetre labels bound to the member
  lines (geometry length for unlabelled members), rounded to the nearest 10 mm
  because the statistics reader only accepts trailing-zero lengths; the user
  may still edit it (a bridge run is usually a bit longer than its cable).
  U1C keeps the fixed `2000mm` placeholder. Outputs carry a `RUN` field with
  all member handles (`SOURCE` stays the first member for existing readers);
  re-running on any member rebuilds the whole run. U1C conduit geometry
  settings split from U1Q into independent keys; text height stays shared.
  Bridge label angle repair checks tangents against any run member before
  rotating.
- Amendment (2026-11-15, 3.2.0.6 — user: "有线管的线不会有桥架"): tray and
  conduit are mutually exclusive per baseline line, so the independent-lane
  default has nothing to separate from. The conduit default lane moves onto
  the tray lane — above, offset 15, exactly the U1Q defaults. The independent
  `UNC_CONDUIT_*` keys stay: explicit user settings still win, text height
  stays shared via `UNQ_HEIGHT`, and the consequence "do not read `UNQ_*`
  keys for conduit geometry" is unchanged.
- Consequence: do not reintroduce per-segment labels for connected selections,
  do not bypass `OffsetPolylineBuilder` with raw `GetOffsetCurves`, and do not
  read `UNQ_*` keys for conduit geometry. Statistics semantics are unchanged
  (label text only); a merged label anchors to exactly one frame.

## D-051: Xmerge Per-File Tolerance For Unreadable Source Drawings (2026-11-10)

- Status: accepted (user-reported XMERGE `eDwgNeedsRecovery` failure where one
  damaged source drawing aborted the whole merge with no file name)
- Decision: `Xmerge` reads source drawings one by one; a file that cannot be
  read is collected with a user-actionable reason (needs-recovery drawings get
  the "open it in AutoCAD to auto-recover, save, then merge again" guidance;
  damaged/invalid files and file-sharing locks are named as such) instead of
  aborting the command. When at least one file is readable, a confirmation
  dialog lists every unreadable file and asks whether to skip them and merge
  the rest; declining (or no callback) cancels with the same list. When every
  selected file is unreadable, the merge is rejected with the full list. A
  real-machine probe proved that side-database `ReadDwgFile` fails for damaged
  drawings under every FileOpenMode/allowCPConversion combination, so no silent
  auto-recovery is attempted.
- Consequence: do not add hidden retry/recovery logic around `ReadDwgFile`;
  keep the per-file catch with the named-file reporting, and keep skipping
  explicit (user-confirmed) rather than silent. Skipped files are listed in
  the command-line summary (`SkippedFiles`).

## D-052: Batch U1U All-Category Substitution, Copied-Frame Reserve Prefill, Moved-Frame Metadata Rebind (2026-11-10)

- Status: accepted (field reports: batch U1U "I already picked a replacement,
  why the unmatched warning again and nothing generated"; cross-file copied
  frames prompting for device reserve with default 0 despite a visible value)
- Decision (three clauses, one incident):
  1. **All-category substitution window.** Batch U1U preflight creates a
     catalog request for *every* review item that requires catalog
     confirmation — any category, and multiple items per category — instead
     of only the first cable and bus-plug-box item. After the user confirms
     the picker, every selection is applied back to its item; the hose
     follow-up still runs only when a cable item was replaced. A category
     with no selectable catalog items fails preflight with a named per-frame
     error (same wording as the old cable/bus-plug-box checks).
  2. **Copied-frame reserve prefill.** When a frame is detected as a copy
     (foreign JSON rejected), the recoverable `数值（设备内）` value in the
     copied CABLE_INFO text is offered as a *suggestion*: the batch reserve
     window pre-fills the row (new source-note column) and the single-frame
     review pre-fills the field. Silent adoption stays forbidden
     (D-052 keeps the 3.0.0.8 skip); the user confirms the visible value.
  3. **Moved-frame metadata rebind.** A handle-mismatched frameinfo_json
     record is accepted when (a) the record's machine/device identity equals
     the claiming frame's current physical identity and (b) the recorded
     handle no longer resolves to a live entity in this drawing — that
     combination means the whole frame *moved* (cross-file copy/paste, or
     move-with-delete), so its reserve/substitution/history are kept and the
     handle is re-stamped on the next write. Same-file copies (source frame
     still present) and renamed/ghost records stay rejected exactly as in
     3.0.0.6/3.0.0.8/3.0.0.9.
- Amendment (2026-11-11, 3.0.0.14 — field report "CABLE_INFO never appears
  on copied frames"): two corrections, both regression-tested on real AutoCAD
  (migration selftest phase 5 ④ / phase 6 ③):
  1. **Clear-before-write order.** The 3.0.0.9 explicit cleanup must run
     *before* `FillFrame`, not after. The shipped 3.0.0.9–3.0.0.13 order
     (write, then clear) wiped the freshly written CABLE_INFO/CONDUIT_INFO on
     every frame detected as a copy, so a copied frame's cable info was always
     empty. It stayed hidden until 3.0.0.13 unblocked batch writes that
     previously aborted at the unmatched-catalog guard. After the fix: clear
     stale first, then write; with no measurements the copy stays empty
     (the 2026-11-06 "clear it" requirement), with measurements the new
     formula survives.
  2. **Handle-collision guard for the rebind check.** "Recorded handle still
     in this drawing" must mean *still pointing at a live block reference
     carrying frame attributes* (FRAMEINFO/CABLE_INFO/CONDUIT_INFO/
     MACHINEID-*). A same-numbered unrelated entity (layer/linetype table
     record, line, text) no longer counts as "source frame present" —
     collisions are near-certain when pasting into a populated drawing, and
     they misrouted genuine moved frames into the copy-reject path.
- Amendment (2026-11-12, 3.0.0.16 — U1F/U1U audit): two more copied-frame
  dark-path closures plus two batch-flow corrections, locked by contract
  tests and migration selftest phase 5 ⑤:
  3. **D-043 marker absorption joins the foreign-JSON guard.** A horizontal
     millimeter marker carried over by a whole-frame copy is source residue,
     not this machine's reserve: do not absorb it, do not delete the text,
     but still exclude it from cable statistics ("3000mm" matches the cable
     length pattern; without exclusion the total double-counts). Non-copied
     frames keep the sole-marker auto-absorb behavior (D-043 unchanged).
  4. **Copied frames never use the update-preserve flags.** "Preserve the
     existing quantity when nothing new was measured" preserves a value the
     cleanup just wiped to empty — a copied frame without labels ended with
     CABLE_INFO empty on U1U but model-only on U1F. Copied frames always
     write from scratch (U1F semantics); normal frames keep U1U preserve.
  5. **Batch U1U flow corrections** (not copied-frame related): when every
     selected machine fails circuit coverage and none is force-generated,
     `boqPlans` is empty and the whole BOQ stage must be skipped (calling
     Write anyway threw "no frame records" *after* the CAD commit and showed
     a false "BOQ failed, rerun U1S" error — which the U1S hard gate then
     blocks: a dead end). And the preflight-error dialog must precede the
     device-reserve window (users filled a whole form before being told the
     batch aborts).
  6. **Machine-redefine guard for U1F** (2026-11-12 field report: "U1F
     computes the reserve carried over from before the copy"). The prior
     record and the D-052-B CABLE_INFO hint belong to the machine the frame
     *physically* claims — but U1F lets the user pick a different machine
     (copy-and-renumber / reassign). Nothing re-validated after the pick, so
     the source machine's device reserve flowed into the new machine's
     review window prefill and formula; on a moved-rebind frame the whole
     record (reserve + substitutions + history) leaked the same way. Fix:
     `FrameRedefineGuard` (pure Core) runs after the pick, before reserve
     resolution — different machine ⇒ drop record + hint with a named
     command-line note; same machine ⇒ keep everything (re-run U1F reuse,
     second frame of the same machine, cross-file relocation all intact).
     U1U never picks, so its machine always equals the frame identity and
     the guard is a no-op there.
- Consequence: do not reintroduce a per-category request whitelist in batch
  preflight; do not restore the "re-enter reserve from 0" behavior for copied
  frames; do not accept a handle-mismatched record on identity alone without
  the old-handle-gone check (that would reintroduce same-frame-copy
  inheritance). Full-catalog persistence (`substitutions[]`, `lastRun`
  snapshot) was proposed and deferred — decide separately before implementing.

  Appendix 7 (2026-11-15, 3.2.0.4 — user's explicit U1F/U1U split): **U1F is
  redefine-mode.** Step 1 wipes every piece of old frame info unconditionally
  (previous frameinfo_json record is not reused for reserve recovery or
  prefill hints; the CABLE_INFO recovery fallback and the D-052-B copied-source
  prefill do not run; the write transaction clears stale attributes on every
  run, not just copied frames). Step 2 absorbs the sole horizontal
  single-line `0mm`-suffixed marker **unconditionally** — the copied-frame
  marker guard now applies to **U1U only** (on U1F the visible marker is the
  user's on-drawing intent for whatever machine they pick). The D-043 marker
  criteria tighten to: horizontal, single-line, integer ending in `0` with a
  mandatory `mm` suffix (`3000mm` ✓; `3000`, `555mm`, decimals, multi-line ✗).
  U1U keeps every pre-3.2.0.4 semantic unchanged (guards, recovery, record
  reuse, moved-frame rebind). Appendix 7 addendum (3.2.0.5 audit): the
  absorbed marker value rides an **empty-machine-id session record** that
  `FrameRedefineGuard` must keep on both same-machine and re-pick paths, and
  `FillOrMigrate` skips legacy standalone-block inheritance on U1F so the
  redefine really starts from a clean slate.

- Amendment (2026-11-19, 3.4.1.6 — CABLE_INFO model display): new CABLE_INFO
  values show the engineering model prefix directly (for example,
  `ZB-YJVR-3*2.5mm²`) without the redundant word `电缆`. The same applies to
  model-only values when there are no measurements. Existing values with the
  old `电缆` prefix remain readable for U1U and submission extraction.
- Amendment (2026-11-22, 3.4.1.9): new CABLE_INFO values prefix displayed cable
  models with `电缆 ` (for example, `电缆 ZB-YJVR-3*2.5mm²`), including
  model-only values without measurements. Existing prefixed and unprefixed
  values remain readable; BOQ model matching is unchanged.

## D-053: Xauto Proportion Relay And Camera-Depth Hops (2026-11-15)

- Status: accepted
- Decision: new command `Xauto` optimises a selected scope of frames in one
  shot. Scope is **box-selected complete frames** (same interaction as
  U13D). Two stages, both driven purely by the mm labels:
  1. **Proportion relay.** Each endpoint-connected component is re-embedded
     through `QuickLineIsometricSceneBuilder` with real millimetre display
     distances (unconfirmed trailing-00mm placeholders keep their current
     drawn length — the same input convention as the 3D preview), then
     re-projected onto the sketch plane. Final drawn length ∝ real mm;
     short segments are clamped to a per-segment readability floor of
     max(1.5 × hop diameter, mm-label text width × 1.15). **Amended
     2026-11-15 (3.2.0.1)** — the original rule scaled k against the sketch's
     current envelope (a small scribble stayed small) and floored only by hop
     diameter (segments came out shorter than their own mm labels); both
     field-reported. The relay target is now the selected frame's usable
     interior (frame boundary inset by one hop diameter): all components of
     the frame share one scale k (binary-searched so their joint envelope
     fills the usable rect), jointly centred on the frame centre with
     inter-component offsets preserved. A frame too small to inset falls
     back to the original envelope-capped behaviour. Cable models play no
     role — only mm labels matter.
  2. **Automatic hops.** Crossings are detected on the final geometry
     (strict interior intersections only; shared endpoints and T-junctions
     are junctions). The segment nearer to the camera opens the semicircle:
     closeness = z−x−y along the projection's null direction (1,1,−1), so
     an elevated run hops over a floor run under the from-above convention.
     Hop diameter reuses the U1R `UNR_DIAMETER` setting.
- Consequence: crossings whose depth cannot be decided (either side
  unclassifiable, mixed projection families, or equal depth = a true 3D
  intersection) are **skipped and named**, never guessed — the user hops
  them manually with U1R. A component containing any unlabelled or
  unclassifiable line is skipped as a whole and named; partial moves are
  forbidden (they would tear endpoint connections). One write transaction
  for the whole command (one U undoes everything); label text is never
  rewritten; hopped lines get their U1LX metadata re-linked with the
  COMPLETED marker preserved; statistics (U1F/U1U) are unaffected.
- Amendment (2026-11-15, 3.2.0.6 — audit round, D-053×D-050 boundary): existing
  U1Q/U1C parallel-annotation outputs referencing moved or hopped source lines
  are no longer orphaned. Pure relayout **translates** the outputs with their
  source line (handle unchanged — a later U1Q/U1C rerun replaces the run as
  usual); a hop (Line erased, holed Polyline created) **re-points** the
  outputs' SOURCE/RUN handles to the new polyline so a rerun can collect and
  replace them. Annotation geometry is not regenerated by Xauto itself. Do not
  add heuristic hop decisions for undeterminable crossings, and do not
  reintroduce scale factors unrelated to the mm labels.
- Amendment (2026-11-17, 3.2.0.11 — fill ratio & placeholder ratchet): field
  report "Xauto 有问题 拉的太长了，纵向的线 就2M 一下弄特别长" — in a
  banner frame (usable rect ≈ 75000×7500, 10:1) the binary-searched k is
  decided entirely by width, so a 2 m vertical run drawn at 2 m×k occupied
  half the frame height. From 3.2.0.11 the target mode scales by **fill
  ratio**: final scale = k_fit × fillRatio (`UNC_XAUTO_FILL_RATIO`, default
  0.65, valid range 0.2–1.0; 1.0 restores the previous fill-the-rect behaviour
  byte-for-byte). The post-install clamp/shrink validation loop is unchanged —
  final geometry must still land inside the usable rect. Second fix in the
  same amendment: **placeholders never grow with k>1** — an unconfirmed
  trailing-00mm placeholder keeps its current drawn length as the display
  distance, and previously k>1 enlarged it, so the next Xauto run adopted the
  stretched length as the new baseline (a ratchet that only grew). Now a
  placeholder's final drawn length is capped at max(current drawn length,
  readability floor); only the readability floor may raise it. Segments with
  confirmed real millimetres are untouched by this cap.

## D-054: Governed Layers, Route Connectivity Report, U1U 3D Preview Pane, Polyline Scan (2026-11-17)

- Status: accepted (user-decided layer names; 3.3.0 feature pack, four clauses)
- Decision:
  1. **Governed layers + auto-correction.** Three statutory layer names,
     verbatim and all-uppercase, no spaces: `U_CABLE` (cable routes — U1L
     segments + millimetre labels, ACI colour 7), `U_TRAY` (tray — U1Q
     output, ACI colour 1 red), `U_CONDUIT` (conduit — U1C output, ACI
     colour 4 cyan). Missing layers are created with those colour indices.
     Identification: cable lines are the U1L segment entities recognized by
     the scanner; millimetre labels are texts associated via
     QuickLineMetadataStore or matched by the scanner → `U_CABLE`. Entities
     carrying ParallelAnnotationMetadata are outputs resolved by the
     existing KIND field: `U1Q` → `U_TRAY`, `U1C` → `U_CONDUIT` (KIND has
     been written unconditionally since the metadata was introduced —
     D-018/D-050 — so there is no "has-metadata-but-no-kind" state). An
     output whose KIND is neither U1Q nor U1C is **not moved and named
     "无法判定"**; an output without metadata cannot be identified as a
     plugin entity at all and is likewise never moved — the repository
     philosophy is to never guess. New U1L/U1LX lines and labels land
     directly on `U_CABLE`. U1U/U1F list "图层矫正：N 个实体移正（电缆
     x/桥架 y/线管 z/无法判定 w）" in the confirmation window / batch
     preflight remarks, and the correction runs inside the existing write
     transaction (one U undoes it together with the fill). Toggle
     `UNC_FILL_LAYER_GOVERNOR` (default on).
  2. **Connectivity check, report-level only.** A pure Core analyzer
     (`CircuitConnectivityAnalyzer`, endpoint tolerance identical to
     U1LX) reports, per frame bucket: connected components, isolated
     segments (degree 0 at both ends) and the "every segment should touch
     at least one neighbour" violation list. The batch U1U confirmation
     window gains a 连通性 column (✔ N 段一条链 / ⚠ 孤立段：句柄…), the
     batch preflight copies the same text into the result-window remarks,
     and single-frame U1F/U1U print it on the command line. **Report only,
     never an abort** — escalate only after field feedback.
  3. **U1U 3D preview pane.** The U1U confirmation windows (single-frame
     review window and the batch confirmation window) gain a left preview:
     the frame's U1L routes are rebuilt with `QuickLineIsometricSceneBuilder
     .BuildAll` from the millimetre labels, ground-normalized (all node Z
     minus min Z — the lowest segment endpoint sits at 0), and drawn on a
     WPF Canvas with a pure-math orthogonal orbit camera (azimuth/elevation
     + zoom; drag to orbit, wheel to zoom, double-click to reset). Axis
     summary parses the workbook axes tolerantly (`48/V`, `48-V`, `48`,
     `V`; failures show the original text — no guessing) and renders like
     "上游 48/V ↔ 下游 37/V · Δ11 跨 × 4.8m ≈ 52.8m". In the batch window
     the preview follows the selected row (scenes are pre-built per frame
     before the window opens). Windows only draw the passed-in model;
     scene/camera models are built in the Features stage (layer rules).
     No third-party 3D library (HelixToolkit etc.).
  4. **Open polylines enter the scan.** `QuickLineCadService.Scan` accepts
     open LWPOLYLINEs (U1R/Xauto holed lines carry bulges) as single
     segments — start = first vertex, end = last vertex; the chord
     participates in endpoint connection and crossing detection, and the
     millimetre label is picked up through the existing metadata
     association. Closed polylines are never cable routes and are skipped.
     `SketchProportionPlanner` skips any component containing a polyline
     (bridge-holed ones are named explicitly) — strictly safer than
     before, when such lines were invisible to the scan and neighbouring
     relayouts tore the sketch open. U1Q/U1C attribution (SOURCE/RUN →
     host cable segments) is exposed as `ParallelAnnotationOwnership` and
     consumed by the layer governor; the preview deliberately does not
     draw tray/conduit overlays (deferred).
- Consequence: do not reintroduce per-output layer guessing from colour or
  text heuristics; do not make the connectivity report block a write; do not
  build scenes inside windows; do not scan closed polylines as route
  segments. Entity colours are untouched by the governor — only layer
  membership changes (U1C keeps its explicit magenta 6 entity colour on
  the cyan U_CONDUIT layer).
- Amendment (2026-11-17, 3.3.0.1 — route-inspection scope): the inspection
  stage originally filtered scanned segments by the frame's `EntityIds`.
  That set never contains Line/LWPolyline cable segments —
  `FrameRegionCollector.Collect` (the strict mode U1U single-frame and the
  batch preflight use) anchors only tables, text and blocks into
  `EntityIds`; that is the historical fill-statistics design and stays
  unchanged (Xauto's `CollectForLayout` full-entity mode is unaffected).
  Field symptom (user report on 3.3.0: "U1U 没有 3D 预览、桥架/线的图层
  没变"), reproduced on a real drawing via `UNCAD_ROUTE_DIAG`: scan 24
  segments, 17 labelled, zero inside `EntityIds`. Fix: segment membership
  is geometric — a segment belongs to the frame when its midpoint lies in
  the frame `Boundary` rectangle (`EntityIds` remains the fallback when no
  boundary exists, e.g. U1F explicit picks). Parallel-annotation outputs
  are attributed per host cable segment (an output belongs to the frame
  that contains its host; outputs whose host is deleted or unknown belong
  to no frame and are skipped — still no guessing). The governor is fed
  only the frame's own segments; it never touches geometry outside the
  frame. Preview scene construction is wrapped in try/catch (a sketch
  with unclassifiable directions downgrades to "no preview", it can no
  longer abort U1U). Regression lock: `UNCAD_ROUTE_INSPECT_SELFTEST`
  (real frame definition + 3-segment orthogonal chain + 3 mm labels:
  asserts EntityIds hit = 0 documenting the root cause, boundary
  membership yields a 3-segment preview, one chain, and all three lines
  planned into U_CABLE); `UNCAD_ROUTE_DIAG` kept as an on-drawing
  per-stage counter dump.
- Amendment (2026-11-17, 3.3.1 — U1U 3D preview enhancements; user field
  feedback "3D预览内展示柱位 你没展示"、"不要跟清单确认挤到一块 相当于一个
  窗口分成两半"、"3D预览没有展示桥架"、"每条线也没有米数标记"):
  1. **Half-and-half window split.** The single-frame review window and the
     batch confirmation window lay the preview and the list as two `*`
     columns split by a draggable `GridSplitter` (both columns keep a ~250px
     minimum so the splitter cannot be dragged through; ~30% of the 900px
     window minimum). The fixed 300/320px preview strips are gone; the
     preview host fills its column. No preview (U1F / legacy) collapses the
     preview column back to width 0 and hides the splitter.
  2. **Per-segment millimetre labels.** Every segment's label text (from the
     CAD millimetre label) is drawn at its projected midpoint at a fixed
     screen font size (12px), on a light backing for readability; a label
     hides when the segment's projected span falls below 24px (policy is a
     pure Core predicate `QuickLineOrbitLabelPolicy`).
  3. **Column grid in the scene.** The ground (Z=0) shows a 4800mm-bay
     column grid covering the scene envelope (light thin lines, toggleable,
     default on), the route's two X-extreme endpoints are pinned with the
     upstream/downstream axis text verbatim from the workbook (parse
     failures show the original text), and numeric axis ticks are
     interpolated linearly over the route's X span between the two endpoint
     numbers (48, 47, 46 …; out-of-range or duplicate rounded numbers are
     skipped). When either endpoint lacks a parseable numeric axis only the
     grid is drawn. The upstream endpoint is the envelope's minimum-X node
     (ties: min Y, then min Z) and the number mapping is aligned so the
     upstream number sits at the upstream pin. This replaces no persisted
     geometry — preview only.
  4. **Tray/conduit dual-line channels** (this clause **supersedes the D-054
     clause "the preview deliberately does not draw tray/conduit overlays
     (deferred)"**). U1Q/U1C outputs attributed to a frame's host cable
     segments (via `ParallelAnnotationOwnership`, the same host-based
     attribution as the layer governor; outputs whose host is deleted or
     unknown never appear) are drawn as: tray — two red (#E8443C) parallel
     lines offset by tray width/2 from the cable line (offset floors at 6px
     projected, zoom-adaptive) with a translucent red fill band between
     them; conduit — cyan (#2EA8C0) dual lines at a small fixed offset, no
     fill. Tray width parses from the U1Q annotation text
     (`BridgeLabelFormatter.TryParseTrayWidthMillimetres`); unparsable
     widths use the 200mm default and the legend says so. A legend corner
     names all five colours (orange X / green Y / blue Z / red = tray
     (width) / cyan = conduit). Tray and conduit are mutually exclusive
     per D-050; if both were ever attributed to one segment the tray wins
     (no stacked guessing).
  5. **Zoom actually zooms.** The 3.3.0 autofit recomputed the fit from the
     zoomed projection envelope each render, which cancelled the camera
     zoom entirely (the wheel was a visual no-op). The fit is now computed
     zoom-neutrally and the zoom factor applied after it; at reset the
     picture is pixel-identical to 3.3.0.
- Consequence (3.3.1): do not reintroduce fixed-pixel preview strips; do
  not draw tray/conduit channels from colour/text guessing (only host
  attribution); do not invent axis numbers when either endpoint fails to
  parse; the column grid is a preview aid, never written to the drawing.
- Amendment (2026-11-17, 3.3.2 — preview kernel switched to
  HelixToolkit.Wpf; supersedes the 3.3.0 clause "No third-party 3D library
  (HelixToolkit etc.)" and the 3.3.1 Canvas implementation): after seeing
  the 3.3.1 orthogonal Canvas preview the user reported "轴位不应该这样
  显示吧" (the axis display is wrong — column positions are a two-way
  orthogonal grid of numeric × letter axes, and the reconstructed scene X
  is an abstract axis with no fixed correspondence to the real column
  grid) and "我旋转的时候明显感觉这不对 这不像是3D的 像是一个伪3D的"
  (orthographic projection reads as fake 3D). A selection table
  (HelixToolkit.Wpf / WPF built-in + 3DTools / Canvas with hand-rolled
  perspective / WebView2 + three.js) was walked through with the user and
  **HelixToolkit.Wpf** was chosen; the WebView2 plan is abandoned.
  1. **Rendering kernel.** `HelixToolkit.Wpf` 2.27.0 (NuGet, exact pin,
     MIT, zero transitive dependencies — only HelixToolkit.Wpf.dll enters
     the bundle; payload allow-lists in build.ps1/installer.ps1 updated,
     24 → 25 files). The new control `QuickLineHelixPreviewControl`
     embeds a `HelixViewport3D`: built-in PerspectiveCamera and orbit
     interaction (left-drag rotate, wheel zoom, right-drag pan,
     double-click reset), route lines as constant-width screen-space
     `LinesVisual3D` (X orange / Y green / Z blue kept), millimetre and
     axis labels as `BillboardTextVisual3D` (camera-facing, world height
     proportional to the scene diagonal). The viewport is created lazily
     on the first non-null model (U1F / no-preview paths never touch 3D).
     On window Unloaded all scene children are detached so Helix's
     process-wide weak rendering-event listeners go dormant — a second
     STA thread rendering a Helix viewport crashes on Helix theme-shared
     unfrozen Freezables (proven with a standalone minimal repro, an
     upstream HelixToolkit.Wpf characteristic, irrelevant to production
     AutoCAD's single UI thread; the xunit harness runs every Helix
     window on one shared STA dispatcher).
  2. **Two-way column grid replaces the 3.3.1 single-direction numeric
     interpolation.** The ground (Z=0) shows a two-way orthogonal grid
     (numeric × letter axes, 4800mm bays covering the envelope,
     toggleable, default on). The numeric axis direction is the plan
     direction with the larger endpoint displacement (ties → X) — the
     scene X axis is never assumed to be the numeric axis. Numeric ticks
     interpolate between the two parsed endpoint numbers exactly as in
     3.3.1 (clamped, deduplicated). Letter labels run on the
     perpendicular axis: equal endpoint letters → anchored at the
     upstream letter and extended along the grid at 4800 bays
     (U/V/W…; +coordinate is the documented A→Z direction); different
     letters → linear interpolation clamped between the endpoints;
     coincident positions or unparsable letters → that direction is not
     labelled (no guessing). Endpoint pins keep the verbatim workbook
     axis text. The downstream endpoint tie-break changes to max-Y/then
     max-Z (3.3.1's min-Y collapsed both pins of a pure-Y route onto one
     node). `QuickLineColumnGridPlanner` gained `LetterLabels`,
     `NumericAxisIsX` and the envelope; `InterpolateTicks` math is
     unchanged.
  3. **Retirements.** QuickLineOrbitPreviewControl (Canvas),
     QuickLineOrbitCamera (orthographic math) and
     QuickLineOrbitLabelPolicy (projected-span label hiding — billboards
     always face the camera, the concept disappears) are deleted. The
     data model (QuickLineOrbitPreviewModel/Segment/Channel), the ground
     normalization, host attribution, the 200mm default tray width, the
     legend, the half-and-half GridSplitter layout, null-preview
     fallback and per-row batch switching all carry over unchanged.
- Consequence (3.3.2): do not reintroduce a hand-rolled projection or a
  browser-hosted preview for the U1U pane; do not assume the scene X axis
  is the numeric (or letter) axis; do not extend letter labels beyond the
  grid anchor rule above; keep Helix viewports on one thread per process
  (production) or the shared STA runner (tests).
- Amendment (2026-11-17, 3.3.3 — U1B hose command, U_HOSE layer; user
  request "绘制软管 输入 U1B…注意需要处理好拐弯处 和首尾端"):
  1. **U_HOSE joins the statutory layer family.** `U_HOSE` (hose — U1B
     output, ACI colour 3 green) is added verbatim to
     `UncadLayerStandard.GovernedLayerNames`/`ColorIndexOf`/
     `TryResolveAnnotationLayer`. Missing layers are still created
     idempotently; the correction report text gains a hose bucket
     ("电缆 x/桥架 y/线管 z/软管 h/无法判定 w").
  2. **Command U1B and KIND=U1B.** LINE-style chained point collection
     (U undoes the last picked point, Enter/Space finishes, duplicate
     points are ignored; fewer than 2 points exits politely drawing
     nothing), then the hose is drawn immediately inside the single write
     transaction: a closed LWPolyline outline built analytically by the
     pure-Core `HoseOutlinePlanner` — centred double-line offset
     ±diameter/2 (NEVER LWPolyline ConstantWidth), outer corners are
     radius=pipe-radius fillet arcs (tangent points are the offset
     endpoints), inner corners are offset-line intersections (miter; a
     near-reversal spike whose miter leg exceeds 4×radius degrades to a
     bevel — bounded, no crash), and both ends get semicircle caps of the
     same radius. Degenerate inputs (duplicate/collinear points) are
     collapsed. Diameter comes from `UNC_HOSE_DIAMETER` (default 100,
     U1SET draw page). The centreline itself is not kept. All outputs
     (outline / leader / text) carry `ParallelAnnotationMetadata` with
     `KIND="U1B"` (SOURCE = the outline's own handle), ACI 3 entity
     colour, layer U_HOSE.
  3. **Annotation.** After the outline the user picks a label point; a
     thin leader is drawn from the path's by-length midpoint to it and a
     single-line DBText (height = the UNL_HEIGHT family) is created. The
     default text reuses the existing hose-chain format
     (`RuanguanLabelFormatter` → `100mm软管:2500mm`, length rounded to
     the nearest 10) and is user-editable at the prompt. No new text
     format is invented: the whole line matches neither the cable
     (`^\d+0mm$`), bridge, conduit statistics patterns nor the D-043
     device-reserve marker pattern, so it never leaks into the cable sum
     and is never absorbed as a reserve. Hose statistics keep reading
     only Ruanguan block attributes — U1B standalone text deliberately
     does not join that sum (the SUM-STAT engine has no hose text
     category; do not add one without a new decision).
  4. **Scan exclusion, same rule as U1Q/U1C.** `QuickLineCadService.Scan`
     skips any entity carrying ParallelAnnotationMetadata (TryRead hit) —
     the U1B outline (also closed, which is never a cable route anyway),
     leader and label can never be picked up as cable routes. Layer
     governance resolves KIND U1B → U_HOSE automatically; frame-scoped
     attribution still skips U1B outputs because their SOURCE never
     resolves to a scanned cable segment (host unknown → skip, no
     guessing).
- Consequence (3.3.3): do not "simplify" U1B to a ConstantWidth polyline
  (the outline is real geometry with arcs and caps); do not make U1B
  annotation text a statistics source or a reserve marker candidate; do
  not scan U1B outputs as cable routes; one write transaction per U1B
  run (one U undoes outline + leader + text together).
- Amendment (2026-11-18, 3.3.4 — layer-governor completion for the new
  features; from the whole-command audit):
  1. **U1B outputs reach the layer governor via a boundary direct
     channel.** This supersedes the 3.3.3 sentence "frame-scoped
     attribution still skips U1B outputs": with the old wiring the
     HoseEntities counter and the U1B→U_HOSE move branch were
     unreachable (the outline's self-referenced SOURCE can never resolve
     to a scanned cable segment), so a manually misplaced hose could
     never be corrected. `FilterOutputsByHostFrame` now keeps entries
     whose KIND=U1B when their geometric-extents centre falls inside the
     frame boundary (`OutputCenterInsideBoundary`; unreadable/erased
     entities stay excluded). Other KINDs with unknown hosts are still
     skipped — no guessing (D-054 rule unchanged). Batch U1U summary
     counters and the closing copy now include the hose bucket and the
     "非 U1Q/U1C/U1B" undeterminable wording (3.3.3 had only updated the
     single-frame side).
  2. **U1F frame-block selections carry the frame boundary.**
     `FillSelectionCollector.ExpandSingleFrame` attaches the collected
     region (Boundary) when it expands a lone frame-block selection, so
     U1F's route inspection / layer governor / connectivity report no
     longer no-op on frame content (audit B-5; strict EntityIds never
     contain cable lines). Opportunistic only: partition failure, empty
     partition, or genuine partial explicit picks keep the established
     EntityIds fallback. U1U behaviour is unchanged (its own assignment
     is a same-value overwrite).
  3. **Legacy metadata-less outputs are documented, not guessed.**
     Pre-D-018/D-050 U1Q/U1C outputs carry no UNCAD_PARALLEL metadata
     and cannot be distinguished from hand-drawn cable lines without
     colour/text guessing (forbidden). On such drawings they are treated
     as cable segments (moved to U_CABLE, colour kept) and re-running
     U1Q/U1C cannot collect the stale outputs — recorded in
     KNOWN-ISSUES (audit Q1), together with the silent circuit-gate
     skip when no machine snapshot exists (audit B-6).
- Amendment (2026-11-18, 3.3.5 — U1U preview kernel switched back to
  WebView2+three.js; user field feedback "这生成的 3D 有严重问题！放弃这个
  方案转 WebV 吧" after seeing the 3.3.2/3.3.4 Helix preview): this
  supersedes the 3.3.2 kernel-selection amendment (HelixToolkit.Wpf is
  retired and its package reference removed from csproj/build/installer/
  bundle contracts). The U1U preview pane hosts a local read-only WebView2
  page (Web/QuickLineOrbit) reusing the three.js + OrbitControls already
  vendored for U1L3D; wiring and security match U1L3DPreviewWindow (D-025:
  virtual host maps only the local Web directory, non-local navigation is
  cancelled, no host objects/downloads/permissions, render-process failure
  degrades to an inline message). Scene semantics stay out of the renderer:
  a pure-Core envelope builder (`QuickLineOrbitPreviewEnvelope`) computes
  the column-grid plan, tray/conduit rail offsets, and label anchors (same
  ratios as the retired Helix control), serializes them as JSON
  (PascalCase property names are the C#/JS contract, locked by tests), and
  the page is a dumb renderer (perspective camera, HTML overlay labels —
  CJK text rendered by Chromium). The data-level window tests now assert
  envelope-derived counters and run with WebView2 auto-init disabled; a
  page-side window.postMessage injection hook exists for browser smoke
  tests. Do not reintroduce HelixToolkit.Wpf for this pane without a new
  decision.
- Amendment (2026-11-18, 3.3.6 — U1U 3D preview reference levels; user
  request "能把这些参照物也附进去吗？3D的页面内" with the building
  elevation chain 1F..3F and equipment heights): the preview draws amber
  horizontal reference frames with corner labels ("2F地面 7800" etc.)
  spanning the scene envelope. The list is data, not code: U1SET
  `UNC_PREVIEW_REF_LEVELS` ("标签=毫米", semicolon/newline separated;
  invalid entries skipped, empty = draw none) with the user's 12 levels
  as the shipped default (floor chain converted to cumulative
  elevations, equipment heights verbatim). Heights are relative to the
  scene ground (lowest cable endpoint Z=0, established semantics).
  Parsing is a pure-Core function (`PreviewReferenceLevels`), carried in
  the preview envelope, rendered by the page into a toggleable group.
  Do not hardcode project elevations into the web page.
- Amendment (2026-11-18, 3.3.7 — reference objects become a simplified
  building model; user feedback "你就光弄个线条" then "3楼地面是华夫孔洞
  （圆孔） 二楼地面是环氧树脂 一楼应该也有地面 第一层/第二层第三层母线
  应该用半透明玻璃的 柱子不是每 4.8M 一个柱位吗 4个柱位之间有9个华夫孔"):
  the entry syntax gains an optional third segment — "标签=毫米=类型".
  Types (normalized from Chinese/English keywords; unknown or absent =
  `frame`, the 3.3.6 amber outline, so old lists keep working):
  `floor` slab, `epoxy` glossy green resin slab, `waffle` plate with real
  round cutouts (**3×3 = 9 holes per structural bay** between adjacent
  column grid lines; uniform-array fallback without a grid), `steel`
  I-beam array, `grating` slat array, `raised` thin raised-floor plate,
  `device` solid boxes along the bottom edge, `bus` **translucent amber
  glass plates** marking the three busbar levels. The page draws solid
  lit geometry (Lambert/Phong + two lights + light fog), structural
  columns land on the **column-grid intersections** (the U1L grid, i.e.
  the ~4.8 m pitch) up to the top reference level, and a 1F ground
  plate is auto-laid at the scene zero plane (labelled "1F地面 0"; the
  zero-datum semantics make it 1F ground — no config entry needed or
  possible for Z=0, the parser keeps rejecting ≤0). Heights stay exact
  from the list; plan positions without data (device row, extents) are
  indicative — the pane remains a display piece. Defaults encode this
  project's semantics (2F=环氧地坪, 3F=华夫板); other projects change
  U1SET, and the page still must not hardcode project elevations or
  semantics.
- Amendment (2026-11-18, 3.3.7 round 3 — scene space is inferred, the
  scene lives on a floor, steel/grating hidden by default; user:
  "葡萄架和工字钢默认不显示 这个空间默认是通过推算来渲染的 分析回路
  会占用多大的空间 然后渲染出来更大的空间 上游是有楼层的！如果是2F
  那上游就在2楼的 第一/2/3层母线放到二层"): (1) the model extent is
  **inferred**, not a percentage margin — the route footprint is covered
  by whole structural bays of the column grid and expanded by one extra
  bay per side (6% margin fallback without a grid). (2) A directive
  line `本层=2F地面` names the machine's floor; the whole building is
  rebased so that floor sits at scene Z=0 (upstream lives on that
  floor; label heights read relative to it; without the directive the
  3.3.6 absolute-from-1F semantics are kept). (3) Entry heights may be
  floor-relative via a 4th segment anchor `标签=毫米=类型=锚定标签`
  (single-hop, resolved in the envelope: 2F地面+3660; unknown anchors
  keep the raw value — no guessing). The shipped default sets 本层=
  2F地面 with devices and the three busbar glass levels anchored to
  2F地面 (3.66/4.22/4.9 m above the machine's own floor). (4) Steel
  beam arrays and grating default to hidden behind a separate
  「结构」 toggle (columns/slabs/bus glass stay on); the frame
  toggle still hides the entire reference model. Also fixed in passing:
  the per-frame label updater re-showed labels hidden by the
  grid/reference toggles since 3.3.5.
- Amendment (2026-11-18, 3.4.0 — preview kernel switched to the in-box WPF
  Viewport3D; user instruction "算了 用 Viewport3D吧" after the WebView2
  kernel kept white-screening on the dev machine — the field log shows the
  3.3.8 mitigations working (10:32 controller-creation 30s timeout fired,
  15:02 prewarm window built in 123ms) yet the pane still failed, because
  the browser-kernel pathology class has no root cure: external
  msedgewebview2 processes holding the user-data-directory lock,
  synchronous STA blocking at controller creation, the white pre-init
  airspace hwnd, and browsers exiting when the last controller closes):
  this supersedes the 3.3.5 kernel-selection amendment for both preview
  hosts (the U1U pane and the U13D batch window). The WebView2 3D stack
  is retired and deleted: QuickLineWebPreviewControl, OrbitPrewarmHost
  (prewarm + orphan sweep), and the Web page trees with the vendored
  three.js (payload allow-list 26 to 16 files; csproj drops System.Management;
  Bootstrap drops the prewarm hook). The new kernel is the in-box
  Viewport3D over a QuickLine3dScene primitive layer (builder-computed
  geometry, group toggles, hand-rolled view-projection label overlay):
  SetModel and the window constructor build the scene synchronously, so
  the "starting renderer" state no longer exists. The retired pages'
  visual formulas (tube radii, tray/conduit dual rails and fill band,
  waffle 3x3 holes per structural bay, steel/grating/ground slabs, bus
  glass, grid columns, base-floor rebasing, grid-bay extents, the U13D
  orthographic camera and info panel) are ported verbatim into
  QuickLineOrbitSceneBuilder and QuickLine3dBatchScene. The login window
  keeps WebView2 via SharedWebView2Environment (D-025 unchanged —
  account/licence validation stays the only network feature). Still no
  third-party 3D library (the 3.3.2 in-box option realized; the
  "do not reintroduce HelixToolkit" rule stands). D-025's local read-only
  posture transfers trivially: the preview now has no web surface at all
  — it cannot navigate, download, or script, and it still never writes
  CAD entities, never refreshes workbooks, never touches the network.
- Amendment (2026-11-18, 3.4.0.1 — user feedback "这毫米标位置不对！
  很多标记显示的位置都不对！这做的柜子是啥意思啊 柜子是连着上游的
  并且根据上游类似摆放柜子！插座盘离地面有1M"):
  1. **Screen labels use the WPF row-vector view basis in the correct column
     order.** Every world anchor now projects through the same camera basis as
     the 3D geometry; text is centered on its projected anchor, and collision
     avoidance offsets survive the final placement update.
  2. **U1U preview cabinets follow the upstream.** The envelope carries the
     planner's upstream/downstream endpoints; preview cabinets are arranged
     beside the upstream endpoint, aligned perpendicular to the route, with
     visible connector lines from that endpoint. This is preview-only and
     does not create or alter CAD geometry.
  3. **Socket-panel mounting elevation is 1000 mm above the selected floor.**
     The default duplicate socket-panel entry is removed; already-saved
     configurations with the matching legacy "top/install" pair render one
     panel, while distinct configured entries remain.
- Amendment (2026-11-18, 3.4.0.2 — user clarification of the building and
  equipment dimensions, including "华夫孔是圆的，一个轴位置有9个孔"):
  1. **Equipment dimensions use distinct base and height values.** The I-L
     panel rises 2200 mm from the selected floor. The socket panel's
     installation elevation is its bottom at +1000 mm and its box height is
     1000 mm (top +2000 mm); legacy "插座盘顶面" entries are interpreted using
     this clarified installation convention.
  2. **Waffle openings are circular mesh cutouts.** Each 4800 mm column-grid
     bay contains 3×3 openings. The mesh is batched with the rest of the
     scene; up to 8000 hole cells are retained, and larger previews sample
     complete bays evenly across the extent. This is display geometry only,
     not fabrication geometry.
  3. The 1F steel top, 2F epoxy floor, 2F steel top, 2F grating top, 3F waffle
     slab, 600 mm raised-floor surface, and three busbar elevations remain at
     the cumulative/user-provided values in `PreviewReferenceLevels.DefaultList`.
- Amendment (2026-11-18, 3.4.0.3 — user: "根据回路占用的空间来渲染"):
  1. Reference geometry follows the union of whole 4800 mm bays touched by
     route segments and channel widths, expanded by one neighboring bay.
     A route on a grid boundary touches both adjacent bays. Empty interiors
     of L/U routes and gaps between disconnected routes are not filled.
     Floors, bus layers, waffle slabs, columns, grid lines and optional
     steel/grating share this footprint.
  2. Every retained bay has all nine circular openings; the 3.4.0.2
     whole-rectangle sampling rule is superseded. Above 4096 occupied bays
     including the margin, the preview reports that the selection is too
     large rather than silently omitting bays. This does not block BOQ work.
  3. 1F ground and column bases remain present after floor rebasing:
     with 2F as scene zero, 1F is at -7800 mm. Camera framing includes it.
     This patch does not redefine upstream/downstream endpoint inference.
- Amendment (2026-11-18, 3.4.0.4 — user: "支持 WebV + 当前方案 的 3D 预览
  双功能支持吧！", following the 2500 mm route-length mismatch report):
  1. **Both preview modes are supported.** WPF Viewport3D remains the immediate
     in-dialog/default renderer. A toolbar action opens an optional WebView2 +
     Three.js window in `UNCAD.Preview.exe`, outside AutoCAD's STA process.
     The Web renderer receives the exact material-batched meshes, camera, and
     label anchors produced from the same `QuickLine3dScene`; it does not
     reconstruct lengths or building geometry. Each window gets a GUID-scoped
     temporary profile/session. The local virtual host denies remote resources,
     navigation, downloads, permissions, and host objects. A stalled UI watchdog
     may terminate only the helper process; WebV failures leave WPF and BOQ intact.
  2. **Preview geometry uses the labelled millimetre length.** U1U and U13D no
     longer substitute drawn sketch length for an unconfirmed `00mm` placeholder.
     That display-length fallback remains available to Xauto, where it is an
     explicit planning rule. Completion markers still affect completion state,
     not geometry scale. Route segments, labels, and configured reference levels
     therefore share millimetres in both renderers.
  3. The 3.4.0 release-package contract includes the isolated viewer executable
     and its local Three.js assets. If WebView2 is unavailable, WPF stays usable.
     This reintroduces neither WebView2 inside AutoCAD nor prewarming/orphan
     process sweeping.
- Amendment (2026-11-18, 3.4.0.5, included in package 3.4.0.6 — U1U response time,
  joined U1L entities, and fill-time style correction):
  1. **Reuse an already-collected single-frame boundary.** Selecting one frame
     has already run the spatial collection; U1U reuses that region instead of
     collecting the entire current space a second time. The existing timings
     report selection, route scan, route ownership, layer planning, preview,
     and batch phases so a real drawing can identify the remaining bottleneck.
  2. **Recover joined-route labels only from a proven stale association.**
     U1F/U1U may reattach an explicitly U1L-associated label to an open,
     straight joined polyline only if its original handle no longer resolves
     anywhere in the database and exactly one path is nearest within the
     configured limit.
     Live, malformed, distant, or ambiguous associations are not guessed.
     Recovered labels join the cable layer and style correction; they are not
     treated as additional route geometry.
  3. **Apply the captured U1SET drawing styles inside the existing write
     transaction.** Owned U1L labels use configured height/position/offset;
     U1Q/U1C outputs are regenerated when their stored style stamp differs;
     U1B labels use the configured cable-label height. Unknown or
     metadata-less annotation outputs remain untouched, and a failed fill
     transaction rolls these corrections back with the fill.
- Consequence: do not enable stale-label recovery for U1LX,
  Xauto, or preview scans; do not infer a deleted source from proximity alone;
  keep all drawing-style edits inside the U1F/U1U write transaction. The
  remaining U1U timing must be checked on a representative AutoCAD drawing
  before claiming a field performance improvement.
- Amendment (2026-11-18, 3.4.0.6 — user requested removable columns and axis
  labels centered in the grid):
  1. U1U preview has an independent column visibility toggle. Hiding columns
     hides only column solids; grid lines and grid-axis labels remain visible.
     WebV receives the same hidden group when opened.
  2. Numeric and letter axis labels sit on the corresponding grid's center
     lines, rather than along the outer boundary.
  3. The preview no longer draws the I-Line/socket-panel demonstration boxes
     from the legacy default reference-level entries. Other configured device
     reference entries remain supported; no CAD entities are changed.
- Consequence: column visibility is preview-only. The U1U optimization in the
  included 3.4.0.5 changes has not yet been timed on a representative customer
  drawing.
- Amendment (2026-11-19, 3.4.0.7 — user clarification: "轴位就是在这个多方向多段线的两端"):
  1. U1U preview finds route ends from the reconstructed segment graph's
     degree-one nodes. Exactly two ends are required; branching and disconnected
     components do not get collapsed into guessed global ends.
  2. With no located connection blocks, the upstream/downstream workbook axes
     orient those ends using the confirmed isometric convention (+X=north,
     +Y=west, increasing numeric axes=east). Axis values must parse and the
     direction vector must align with the route plane.
  3. If either condition fails, keep route and grid geometry but hide endpoint
     pins, interpolated axis marks, and upstream cabinets; show a preview
     diagnostic. This is read-only preview behavior and never blocks U1U fill.
- Amendment (2026-11-19, 3.4.0.8 — clarification: "两个端点正好能在轴位的区域内就行"):
  1. Workbook axes describe 4800 mm grid regions, not a required route vector.
     With no located connection blocks, compare the two topological endpoints
     against both upstream/downstream assignments using the established X/Y
     axis convention. Each axis component may differ from the axis-region
     center offset by at most one grid pitch; this is equivalent to allowing
     each endpoint anywhere inside its one-bay-wide region after translation.
     Render that same fit: center the grid between the two possible upstream
     center estimates. Keep the route and the 4800 mm pitch unchanged.
  2. Resolve direction only when exactly one endpoint assignment fits both
     axis regions. If neither or both assignments fit, keep direction
     unresolved and report the reason. Do not apply an angular-alignment
     threshold.
  3. Located connection blocks remain authoritative; an axis-region mismatch
     is diagnostic only and does not override their endpoint assignment.
     Branch, endpoint-count, and read-only preview behavior from 3.4.0.7 remain.
- Amendment (2026-11-19, 3.4.1.2 — complete column axes and responsive preview):
  1. U1U column labels use the complete numeric/letter combination, including
     multi-letter axes such as `48/AA`, at each occupied column's X/Y position.
     The fitted axis-region center is the column-grid intersection; the region
     remains one 4800 mm bay centered on it. This supersedes the separate
     center-line labels from 3.4.0.6 without changing endpoint matching or
     measured route lengths. Unresolved axes are still not guessed.
  2. WebV exposes its own column visibility switch. Column solids and grid
     labels remain independent in both renderers; hiding columns never edits
     CAD entities. U13D labels both reference axes with complete combinations
     at their intersections, without generating every cross-product label.
  3. WPF builds hidden geometry only when enabled, retains visibility between
     frame selections, reuses an unchanged model, and coalesces orbit/pan
     updates at rendering time. Both renderers support middle-button panning
     and cursor-centered zoom. Reset restores the complete initial camera.
  4. Route scanning uses a conservative spatial candidate index before the
     existing scoring and global assignment. Explicit, legacy, stale-label,
     observation, and dimension ownership rules remain unchanged. Selected
     frame filtering occurs only after global assignment, preserving competing
     labels across frame boundaries. Synthetic CAD timing is not evidence of
     end-to-end performance on the customer's large drawing.
- Amendment (2026-11-20, 3.4.1.7 — U1U annotation correction removed):
  U1U no longer applies U1SET styles to U1L millimetre labels, U1Q tray runs,
  or U1C conduit runs, in either single-frame or batch updates. U1F retains
  these corrections. U1B hose-label height correction and governed-layer
  correction remain unchanged; legacy bridge-label migration follows D-019.
- Amendment (2026-11-22, 3.4.1.8 — U1F annotation correction removed):
  U1F no longer applies U1SET styles to U1L millimetre labels, U1Q tray runs,
  or U1C conduit runs, and no longer migrates legacy bridge labels. U1B
  hose-label height correction and governed-layer correction remain enabled.
  If a detected U1U device-reserve marker cannot be erased in the fill write
  transaction, the transaction fails and rolls back rather than committing the
  reserve value while leaving its source text.
- Amendment (2026-11-22, 3.4.1.10 — opt-in millimetre-label placement):
  U1SET's drawing page adds a default-off switch for U1F/U1U to position and
  rotate associated U1L millimetre labels using the configured label position
  and offset. It does not change label text or height, nor tray/conduit output.
  With the switch on, the legacy adjacent-label fallback accepts up to 15
  degrees of label-angle difference (the existing approximately 10-degree
  limit remains when off). Edits stay inside the fill write transaction.
  U1R's retargeted label metadata and unique stale-label recovery for deleted
  straight-line joins remain the only supported ownership paths; ambiguous
  labels are not guessed.
