# Product Decisions

This file records decisions that should not be reopened from memory or from an
old release note. Add a dated entry when a decision changes; do not silently
rewrite an existing entry.

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
