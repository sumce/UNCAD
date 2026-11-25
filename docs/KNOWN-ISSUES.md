# Known Issues And Scope

Keep this list short. An item belongs here only when it is active, intentionally
deferred, or important for support diagnosis.

## Deferred

- U1LX-entered distances are not part of U1U/U1F statistics. This is an
  explicit scope decision, not an accidental omission.
- `UNCAD_XFRAME_SELFTEST` (tests/UNCAD.CadIntegration, not a release gate)
  still asserts migration behaviour that no longer exists: it expects the
  migration to replace an existing BOQ table and erase the old one, while
  `XFrameMigrationService` clones a BOQ table only when the selection has none
  and never erases user tables. By its assertion order the command fails before
  it reaches its drawing-info write check (read from the sources on
  2026-11-25; the console log carries no exception text). The release gates
  are PERF_SMOKE and MIGRATION_SELFTEST; the latter carries the drawing-info
  template write check since 3.6.0. The self-test needs rewriting to the
  current migration contract.

## Intentional Behavior

- XSTS frame details use one row per physical frame, with machine ID, circuit
  and current frame handle, then separate model/quantity pairs for cable,
  tray, hose and conduit. Other drawn items add pairs; absent items stay blank.
  Original item numbers, names, features, units, quantities and codes remain
  in UI tooltips and Excel comments. No item is filtered out by category or
  catalog code. Empty or unreadable frames retain a row; duplicates stay separate.
  Near-name hints and the whole-drawing "not drawn" list were removed at the
  user's request on 2026-11-19, including their extra scan and analysis.
- Repeating XLAYOUT can create another machine-ID label.
- Same-name circuits of one machine (D-056, D-057) are only paired with their
  workbook rows by the identity key. A frame of such a machine that has no
  recorded upstream number (frame older than the v2 record, or a copied frame
  whose source JSON was rejected) is reported as pending (待确认) by XSTS,
  XLAYOUT and the whole-machine gate; it is never guessed, and U1S/U1DWG do
  not export that machine until a U1F/U1U run on the frame records the
  upstream. U1PDF skips such a frame with a command-line reason (no picker on
  that path).
- Route recognition (D-058) is read-only and reports instead of guessing: only
  segments with a millimetre label join by the gap tolerance; a tray/conduit
  text without ownership metadata binds to one nearest labelled segment (that
  segment only) and is reported as a proximity binding; a labelled tray output
  whose host line is gone is reported as orphan, not attached; the preview's
  hose is schematic (it continues the last segment from the device end) and
  writes nothing. U1UL leaves labels that carry no U1L/U1LX association
  untouched and lists their lines.
- BOQ files written before 3.6.0 may hold one merged column for a machine's
  same-name circuits. The first 3.6.0 write renames that column to the first
  circuit (`回路名（上游 X）`) and rewrites its quantities from that frame; run
  U1F/U1U (or U1S) on all same-name circuits of the machine once so the other
  circuit gets its own column.
- U1F/U1U do not write automatic upstream connection geometry to the drawing.
  The U1U 3D preview may show a schematic cabinet connection from its inferred
  upstream endpoint; that line exists only inside the read-only preview.
- A missing online license response fails closed (no offline grace path); the
  customer can enter a new authorization code in the login dialog.
- HTTP/HTTPS machine-workbook sources are download-only (D-041): they refresh
  only via explicit U1DATA/U1SET; drawing commands never touch the network.
- The whole-machine circuit gate: U1S aborts with the missing circuit list
  when a machine's circuits are not all drawn (D-033). U1DWG (D-047) is
  per machine: incomplete machines are skipped by default and a selection
  window lets the user force-export them. Since D-044, U1F/U1U
  keep updating the selected frames and only skip that machine's BOQ, and
  since D-048 U1U shows a confirmation window where incomplete machines can be
  force-given a partial BOQ (drawn circuits only). Disable
  `UNC_FILL_REQUIRE_ALL_CIRCUITS` in U1SET if a project really needs partial
  output everywhere.
- A Base64 config share code never contains licence/account values, so a shared
  profile does not transfer authorization.
- Legacy parallel-annotation outputs (U1Q/U1C generated before the metadata
  era, pre-D-018/D-050) carry no `UNCAD_PARALLEL` metadata. They are plain
  offset lines and cannot be distinguished from hand-drawn cable lines without
  guessing from colour/text — which D-054 forbids. Consequences on such
  drawings: the scanner treats those offset lines as cable segments, so the
  layer governor (3.3.0+) moves them to `U_CABLE` (colour unchanged), they
  join connectivity reporting, and re-running U1Q/U1C on the base line cannot
  collect/replace the old outputs (handle-based `CollectPrevious` needs the
  metadata) — delete the stale outputs manually. Not fixable by identification;
  recorded 3.3.4 (audit Q1).
- Without a machine snapshot (no machine source configured, or no successful
  refresh yet), the whole-machine circuit gate for U1S/U1DWG cannot know the
  expected circuit set and silently skips the check (log-only). Configure
  U1DATA and refresh once to enable the gate. Recorded 3.3.4 (audit B-6).

## Support Notes

- 3.0.0.13–3.0.0.16, U1F on a copied frame with a **different** machine
  picked: the source machine's device reserve leaked into the new machine
  (review-window prefill from the copied CABLE_INFO hint, or the whole
  record on moved-rebind frames), so the cable formula included e.g.
  "3000（设备内）" from a machine that was never on this frame. Fixed in
  3.0.0.17 (machine-redefine guard). Redo U1F on the affected frames,
  picking the intended machine — the reserve now starts from 0.
- 3.0.0.13–3.0.0.15, batch U1U: when *every* selected machine failed the
  whole-machine circuit gate and none was force-generated, the command ended
  with a false "BOQ 输出失败，请重跑 U1S" error dialog. The drawings were
  updated correctly and no BOQ files were touched — rerunning U1S was never
  the right advice (its circuit gate would abort). Fixed in 3.0.0.16 (the
  BOQ stage is skipped entirely, matching single-frame behavior). No drawing
  repair is needed.
- 3.0.0.9–3.0.0.13: on any frame detected as a copy (foreign JSON rejected),
  the freshly written CABLE_INFO/CONDUIT_INFO was wiped by the cleanup step
  that ran *after* the write — the frame's cable info ended up empty even
  with measured labels. Fixed in 3.0.0.14 (clear-before-write order; the
  same release also stops same-numbered unrelated entities from being
  misread as "source frame still present" in the moved-frame rebind check).
  Redo U1F/U1U on the affected frames to regenerate the info.
- U1PDF always plots with `DWG To PDF.pc3`. If a workstation reports
  "PDF 不带颜色", check for third-party plot devices (e.g. `M_PDF.PC3`) or a
  non-colour plot style table; the built-in exporter asserts colour in
  `UNCAD_U1PDF_SELFTEST`.
- Outputs from the pre-2.10.0 flat layout stay in their old folder; moving them
  into the new layout is manual (see `docs/RELEASE-2.10.0.md`).

## Xauto Scope Limits (3.2.0)

- Hopped Polylines (lines a manual U1R already converted) take part in
  neither the proportion relay nor the crossing detection: the scanner
  reads LINE entities only. After a relay their hop positions no longer
  match the moved geometry — erase the old hops and rerun Xauto (it lays
  its own hops at the final crossings).
- Crossing detection covers the U1L lines of the box-selected frames only;
  a line crossing *into* the selection from a frame outside it is not
  examined. Crossings between two selected frames are detected.
- The relay does not de-collide mm labels: densely clamped short segments
  can end up with labels close together; adjust by hand if needed.
- Unconfirmed placeholder labels (trailing 00mm without a completion
  marker) relay by their current drawn length — their mm value is neither
  guessed nor rewritten; run U1LX to confirm them, then rerun Xauto.

## Environment Limits

- WebV 是可选预览窗口，需安装 Microsoft Edge WebView2 Runtime；如果运行时
  缺失、控制器超时或 GPU 上下文丢失，窗口会显示错误，WPF 内嵌预览仍可用。
  WebV 在独立进程和独立临时用户目录运行，不会阻塞 AutoCAD。
- U1U route ends come from the upstream/device blocks when those blocks can be
  located (D-058): the end nearest each block is that end; when both ends are
  equally near, or both blocks point at the same end, direction stays
  unresolved and the reason is shown. Only when neither block can be located
  does the workbook-axis rule apply, and then direction is resolved only for
  an unbranched route with exactly two topological endpoints and parseable
  workbook axes whose 4800 mm regions match exactly one endpoint assignment.
  Route-vector angular alignment is not required. Branches, disconnected route
  groups, invalid axes, and zero or multiple region matches leave cabinet and
  endpoint-axis markers hidden and report why; they do not block fill or BOQ
  work.
- U1U previews exceeding 4096 occupied column bays including their margin
  show a selection-size error. Bays and waffle openings are not sampled.
  This limit affects the preview only, not the drawing or BOQ.
- CAD integration tests and real command acceptance require AutoCAD 2022 and
  its .NET assemblies. Core tests should remain runnable without AutoCAD loaded.
- User drawings, customer workbooks, logs, `bin`, `obj`, and `artifacts` are
  local inputs/outputs and are not source-of-truth project files.

## Quarterly Review — Suspended Decisions

Deferred is not rejected. Walk this list roughly once a quarter and either
keep deferring (with the current reason) or open the decision again.

- D-006 (deferred by request): U1LX-entered distances stay out of U1U/U1F
  statistics; do not silently add them to cable, bridge, or conduit totals.
- D-049: number stays reserved for the pending ghost-frame proposal
  (预留号待定；see the D-050 status note in `docs/DECISIONS.md`).
- D-052 appendix: full-catalog substitution persistence (`substitutions[]`
  for every category) plus a `lastRun` snapshot JSON expansion — the user
  deferred both to a separate decision; do not implement before reopening
  D-052.

## Premise Risk — Batch U1U Chain And FrameRedefineGuard

- `FrameRedefineGuard` runs on the shared fill path but is a no-op for the
  batch U1U chain: the premise is that a U1U machine always comes from the
  frame's own identity, so it can never differ from the recorded machine
  (see the comment at `src/UNCAD/Features/Fill/FillFeature.cs:152-158`).
- Any future feature that lets batch U1U re-pick machines or renumber them
  breaks that premise and must amend D-052 first; otherwise the
  "source machine's reserve leaks into the new machine" defect fixed in
  3.0.0.17 revives in the batch chain.

## Resolved In 2.3.0

- QuickLine text-only helpers no longer eagerly resolve AutoCAD RX classes.
- Batch U1U no longer opens the comparison dialog twice.
- The U1U comparison window reserves its left selector area before laying out
  the two comparison tables.
