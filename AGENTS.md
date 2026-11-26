# UNCAD Agent Contract

This is the short entry point for any human or coding agent working in this
repository. Keep long explanations in `docs/`; keep this file stable.

## Read Order

1. Read `docs/PROJECT_CONTEXT.md` for the current map and non-negotiable rules.
2. Read `docs/DECISIONS.md` for decisions that must not be reopened casually.
3. Read only the relevant section of `docs/DEVELOPMENT.md`.
4. Use `scripts/context.ps1 -Area <area>` to list the source and tests for the
   task. Read those files, not the whole repository.
5. Check `docs/KNOWN-ISSUES.md` before proposing behavior that was previously
   deferred.

## Source Of Truth

For intended product behavior, use this order:

1. Accepted decisions in `docs/DECISIONS.md`.
2. `docs/PROJECT_CONTEXT.md` and the relevant section of `docs/DEVELOPMENT.md`.
3. Compiling source and passing tests as the observed implementation.
4. Release notes and chat history.

When code or a test conflicts with an accepted decision, treat it as a
regression to investigate. Do not preserve an old behavior merely because it
currently passes a stale test.

`docs/archive/` holds retired notes (the original v3 architecture draft and the
retired development plan); `CLAUDE.md` carries tool-specific commands. They are
useful references, but this file and the documents above take precedence when
their version numbers or counts are stale.

## Current Product

- Product: `UNCAD Pro`, online-licensed; the only supported distribution is the
  Pro package. Runtime: AutoCAD 2022, .NET Framework 4.8, x64.
- Current version, tag, package hashes and the installed-copy state live in
  `docs/PROJECT_CONTEXT.md` — that file is the single source. Do not copy the
  status block into this file, `README.md` or `CLAUDE.md`; a duplicated status
  drifts (the last drift left `3.4.1` here while the product was `3.8.0.9`).
- The release entry point is `release.ps1`; customer and expiry data come from
  `pro.key`, not from the assembly or package manifest.
- The release entry point is `release.ps1`; customer and expiry data come from
  `pro.key`, not from the assembly or package manifest.

## Hard Constraints

- Governed layers (D-054, 3.3.0; `U_HOSE` added 3.3.3): `U_CABLE` (cable
  routes, colour 7), `U_TRAY` (U1Q output, colour 1), `U_CONDUIT` (U1C
  output, colour 4), `U_HOSE` (U1B hose output, colour 3) — names verbatim,
  no spaces. Layer correction runs inside the U1U/U1F write transaction
  (`UNC_FILL_LAYER_GOVERNOR`, default on); outputs are classified by the
  parallel-annotation KIND only (`U1Q`/`U1C`/`U1B`). Unknown or metadata-less
  outputs are never moved and never guessed from colour/text. True-white
  entity colours (`ByColor #FFFFFF` / ACI 255) on governed entities are
  normalised to ByLayer by the U1F/U1U governor (3.8.0.10): they are
  invisible on white paper while ACI 7 prints black; all other explicit
  colours stay untouched. U1B label text
  follows the Ruanguan hose format and never joins a statistics sum. U1B hose
  outlines/leaders are never scanned as cable routes (metadata hit skips).
  Connectivity reporting
  is report-level only (never blocks a write); open holed polylines from
  U1R/Xauto are scanned as single segments and whole components containing
  them are never relaid by Xauto.
- Machine workbooks are selected by the user: local/UNC paths or HTTP/HTTPS
  URLs (download-only, D-041). Refresh is explicit in U1SET/U1DATA; drawing
  commands never touch the network, and startup auto-refresh stays removed.
- Nothing is uploaded. Account/licence validation is the only network feature;
  do not reintroduce upload code without a new decision in `docs/DECISIONS.md`.
- U1S aborts with the missing-circuit list when a machine's circuits are not
  all in the selection (`UNC_FILL_REQUIRE_ALL_CIRCUITS`, default on). U1DWG
  (D-047) is evaluated per machine: incomplete machines are skipped by
  default, complete machines always export, and a selection GUI lets the user
  force-export incomplete ones. U1F/U1U keep updating frames
  on partial selections but skip that machine's BOQ (D-034, D-044); U1U adds a
  confirmation GUI (D-048) where the user can force BOQ generation for
  incomplete machines from the circuits actually drawn. **U1F is redefine
  mode** (D-052 appendix 7, 3.2.0.4): every run wipes the frame's old info
  (no record reuse, no recovery fallback, unconditional stale-attribute
  clear) and absorbs the sole horizontal millimeter reserve marker
  unconditionally, deleting the text. U1U keeps the update semantics:
  record reuse, recovery fallback, copied-source prefill, and the
  copied-frame marker guard (marker kept but excluded from statistics).
  Marker candidates must be single-line integers ending in `0` with a
  mandatory `mm` suffix (D-043 as amended; multiple candidates are never
  auto-absorbed; `UNC_FILL_DEVICE_RESERVE_AUTO`, default on). XLAYOUT row
  order has three modes — machine readiness,
  machine ID, upstream number — with in-row order from the workbook's Excel
  row sequence (D-045, `UNC_XLAYOUT_ARRANGE`). Output layout is one
  folder per machine — `<root>\<machine>\` holds that machine's BOQ, DWG and
  PDF together (D-042; the drawing-name and kind layers and their two toggles are
  gone). Write-occupancy is checked before the CAD transaction. Batch U1U reports
  per-machine results in a window, not by dumping paths to the command line.
- Same-name circuits (D-056, D-057): a circuit is identified by the key over
  machine ID + circuit name + upstream number, derived on demand from the
  persisted plaintext and never stored. Every consumer that compares frames
  with the workbook (XSTS, XLAYOUT, the whole-machine gate, BOQ columns, DATA
  rows, PDF names, DWG counts) goes through `Core/Fill/CircuitMatcher`: key
  pairing first, name pairing only for a name unique in the machine, and a
  same-name frame without a usable key is reported as pending (待确认), never
  guessed and never counted as covered. Do not reintroduce grouping by circuit
  name alone.
- The drawing-info table's six columns (专业 / 楼层 / 制图 / 审核 / 日期 / 版本)
  come from the U1SET templates (`UNC_FILL_INFO_*`); an empty template leaves
  the cell untouched. `(U1F创建时间)` reads `meta.filledUtc`, which U1F
  re-stamps (redefine mode) and U1U preserves.
- Route recognition (D-058): a cable segment is proven by its millimetre
  label — a line without one is reported, never guessed. Only labelled
  segments join by the gap tolerance (`UNC_ROUTE_GAP_TOLERANCE`, capped at
  500); U1LX and Xauto keep the strict 1.0 tolerance. U1U builds the 3D
  preview (U1F does not), inside the inspection stage and inside try/catch.
  Route ends come from the connection blocks (nearest open end to the
  upstream / device block), never from the drawing direction of a line; the
  workbook-axis rule is the fallback only. Tray/conduit by text is a
  preview-only fallback for outputs without ownership metadata; metadata
  ownership always wins and layer governance never guesses from text. U1UL
  changes only a label's distance and angle — never its side, text, height or
  position along the line — and only for labels carrying the U1L/U1LX
  association.
- U1U preview (D-059): draws only the workbook floors' planes, the edge axis
  band, the routes, the hose and the upstream cabinet. Floor planes are
  **wireframe only** (outline + column grid): WPF's transparent fill still
  writes depth and hides routes on the far side. The reference building
  (waffle/steel/grating/columns/busbar/ground slabs), the bay-based footprint
  and its 4096-bay limit, and WebV (`UNCAD.Preview.exe` + three.js) are gone
  and must not return. The U1U preview kernel is the local WebView2 +
  three.js page (`Web/QuickLineOrbit`, virtual host, vendored three.js) with
  the WPF Viewport3D renderer as an **automatic fallback**: the WPF scene is
  built synchronously and stays as the base layer, the browser initialises in
  the background and only covers it after `ready` + first frame, and every
  browser failure collapses it and leaves the WPF scene plus a reason visible
  — never a blank/white pane (3.8.0.5). The login window's WebView2 is the
  only other consumer; do not restore the out-of-process WebV viewer or the
  3.3.8 prewarm/orphan sweep. The axis grid is
  **pitch-true** (3.8.0.4): one axis number/letter per 4800 mm line, never
  interpolated between the endpoint labels; the drift is split so each route
  end is within half a bay of its own axis line (a device off the axis centre
  still sits in its named bay); the default convention is letters along +X
  (north, up-right) and numbers along −Y, the rotated convention is used only
  when the default cannot hold both ends, and a frame whose plan distance
  still cannot host the workbook axis difference is reported
  (`轴位与图面距离对不上…`) instead of stretched or hidden. Tray/conduit
  lengths come
  from the annotation (one run total, never a sum of segment labels); hosts
  resolve metadata → red line (split into pieces, adjacent label-less red
  lines merge) → nearest single segment, and an unresolved run is reported.
  Legacy annotations without ownership metadata find their red line from the
  text's geometric-extents centre; text/line parallelism is not required, and
  a used red line is never reported as an unlabelled cable. Annotation text
  (including a multi-line second `3500mm`, and a single line with the run
  length at the end such as `梯形桥架200Wx100H-1300mm`) is never a cable
  millimetre label; the `宽Wx高H` spec supplies the tray width. Text carrying
  `共用` (shared tray) is deliberately left outside these rules (user,
  2026-11-25: "共用桥架不用管").
  Cable segments include open/closed-collinear LWPOLYLINE, 3D polyline and
  spline; the fill/U1U scan also treats a linear dimension with no real line
  at its witness points as a segment (U1LX's default scan is unchanged, and a
  dimension is never moved to `U_CABLE`). In the fill/U1U scan an open,
  bulge-free bent LWPOLYLINE whose legs all lie on the drafting axes is split
  into legs (`handle/seq` keys), each with its own label; the default U1LX scan
  is unchanged and hop-detour polylines are not split.
  A millimetre label whose U1L association is dead (handle 0 or deleted — what
  AutoCAD leaves after copying without the target) is matched geometrically
  like a legacy label; an association that still resolves to a live non-line
  entity stays unresolved. Text with a cross-section unit (`mm²`/`mm2`/`㎡`)
  and hose-length text (`软管长度` attribute, text containing `软管`) are never
  cable millimetre labels.
  Every millimetre label must belong to a segment — unbound ones are named
  (reserve-marker form excluded) — and both axis blocks are required.
  Cross-floor routes are checked from the upstream floor ground to the device
  end: the upstream end's height above that ground (socket panel 1 m, others
  0) plus the route's rise from its base point (the riser bottom when the end
  segment is a riser standing on it) must match the device standing level
  minus the upstream ground within 0.2 m per storey (2F→3F: 7.6–7.8 m; both
  levels from the U1SET level list, named in the warning), otherwise a
  warning is reported; nothing is blocked (3.8.0.3 — never compare the bare
  end-to-end rise with the floor difference again). U1UL also aligns
  labels matched by position (beside the segment, ≤ max(4× height, 300)
  off-line, ≤10° skew) and never writes an association.
- U1U preview placement and labels (3.8.0.2): a socket panel lifts the whole
  reconstructed route 1 m (no end-height arithmetic — the box is a
  placeholder); I-Line panels and bus plug boxes are not lifted. The
  placeholder box footprint scales with the scene (width ≥ 5 % of the view
  diagonal, capped at 4×; heights stay true). Millimetre, tray/conduit-run and
  hose labels are rotated along their segment's on-screen direction and fixed
  beside it (readable: never upside down); they do not take part in collision
  avoidance and may only flip to the other side of the same line. Do not
  reintroduce free-floating chips for line labels.
- Workbook parsing accepts the unified `U_` fields and ordinary `回路名称`.
  Rows whose circuit name has strikethrough are ignored. Do not reintroduce
  A1/A2 or backup-field guessing.
- Old frames and `xframe` frames remain readable. Migration must not resize a
  user's existing table, row, or column.
- `frameinfo_json` is the persisted frame identity and change-history source.
- `U1LX` distance values are intentionally outside U1U/U1F statistics for now.
- Repeating `XLAYOUT` may add another machine-ID label; this behavior is
  intentional and is not a cleanup task.
- U1F/U1U do not create automatic upstream connection geometry.

## Layer Rules

- `Core`: pure parsing, matching, planning, statistics, and formatting.
- `Cad`: AutoCAD entities, transactions, selection, and drawing adapters.
- `Infra`: settings, logging, metadata, licensing, and registration.
- `Features`: command orchestration and transaction boundaries.
- `UI`: user interaction and editing of passed-in models only.

Do not move Excel matching into `Cad`, put business rules in forms, or make
`Core` depend on AutoCAD or WinForms.

Full layer, module, shared-capability ownership and migration rules:
`docs/ARCHITECTURE.md`. New code follows that contract; existing violations are
listed there as debt, not precedents.

## Change Protocol

- Start with `git status --short` and locate the command entry with `rg`.
- Make the smallest root-cause change that preserves the decisions above.
- Add one focused regression test for every non-trivial behavior change.
- Run a targeted test first, then `dotnet test UNCAD.slnx --no-restore`.
- For a release, run `.\release.ps1 -NoRestore` and inspect the generated
  package under `artifacts\Pro`; release steps and gate-failure handling are
  in `docs/RELEASE-RUNBOOK.md`.
- Do not include `bin`, `obj`, `tmp`, logs, customer drawings, or user Excel
  files in commits.
- Update a decision or known-issue document only when behavior or scope has
  actually changed; do not duplicate implementation details in documents.

## Useful Commands

```powershell
.\scripts\context.ps1 -Area Fill
.\scripts\context.ps1 -Area QuickLine -IncludeTests
dotnet test UNCAD.slnx --no-restore
dotnet build UNCAD.slnx -p:UseAutoCadNuGet=true   # no AutoCAD install; compile-only
.\release.ps1 -NoRestore
```
