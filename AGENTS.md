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

`ARCHITECTURE.md` and `CLAUDE.md` contain historical/tool-specific notes. They
are useful references, but this file and the documents above take precedence
when their version numbers or counts are stale.

## Current Product

- Current local source version: `3.7.0.0` (2026-11-25, D-058: U1U 3D route
  preview rebuilt — gap tolerance, block-anchored route ends, tray/conduit by
  text, hose, named issues; new command U1UL millimetre-label alignment),
  released locally on 2026-11-25 as tag `v3.7.0` (commit `5a9b81a`), not
  published. The archive `UNCAD-Pro-v3.7.0.0.zip` (6,566,040 bytes, SHA-256
  `8F1A066B35445BDD405C0DCD7ECCBA3E4A74A0BFE77F963B8C6928B518C687B2`) passed
  every `release.ps1` gate (1836 tests, PERF_SMOKE, MIGRATION_SELFTEST,
  package verification). The development machine runs a local build of the
  same source installed before the package was built (installed `UNCAD.dll`
  SHA-256 `C7F764BADFD69C83C239C778F3AFF0298BE551D980A1205FC65D1E764D6CA6AE`);
  installing the package's own files is pending until AutoCAD is closed. The
  latest published release is `3.6.0.0`
  (2026-11-24, tag `v3.6.0`, commit `da8db81`; D-057 same-name circuits,
  drawing-info table templates, BOQ title templates, XLAYOUT options),
  `UNCAD-Pro-v3.6.0.0.zip` (6,549,352 bytes, SHA-256
  `BDFC5E907A62561F94F9E6F90238B96C0A7CBF474753C72A2451EA3D28C87B36`),
  passed every `release.ps1` gate, published on uncad.aosa.me and verified by
  anonymous download. XSTS frame details use one row per frame, with separate
  model and quantity columns for every drawn BOQ item and blank cells for
  missing items. Near-name hints and the "not drawn" list are removed.
- Product: `UNCAD Pro`. Stable line: `3.4.1` (source patch `3.4.1.11`,
  latest published patch `3.4.1.7`;
  U1U/U13D 3D preview kernel
  switched to the in-box WPF Viewport3D — synchronous scene build, no browser
  process, no user-data-directory lock; the WebView2+three.js preview stack
  is retired, login page WebView2 stays, 2026-11-18, D-054 amendment; full
  WPF UI since 3.0.0, D-038; patches increment the fourth version part per
  D-032. Patch 3.4.0.8 binds U1U axis labels to the two route endpoints by
  topology and workbook axis regions; ambiguous endpoint matches are reported,
  not guessed).
- Runtime: AutoCAD 2022, .NET Framework 4.8, x64.
- The only supported distribution is the online-licensed Pro package.
- The release entry point is `release.ps1`; customer and expiry data come from
  `pro.key`, not from the assembly or package manifest.

## Hard Constraints

- Governed layers (D-054, 3.3.0; `U_HOSE` added 3.3.3): `U_CABLE` (cable
  routes, colour 7), `U_TRAY` (U1Q output, colour 1), `U_CONDUIT` (U1C
  output, colour 4), `U_HOSE` (U1B hose output, colour 3) — names verbatim,
  no spaces. Layer correction runs inside the U1U/U1F write transaction
  (`UNC_FILL_LAYER_GOVERNOR`, default on); outputs are classified by the
  parallel-annotation KIND only (`U1Q`/`U1C`/`U1B`). Unknown or metadata-less
  outputs are never moved and never guessed from colour/text. U1B label text
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
.\release.ps1 -NoRestore
```
