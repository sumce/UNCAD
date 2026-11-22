# UNCAD Project Context

This is the compact map for day-to-day maintenance. It is intentionally much
shorter than the historical release notes.

## Status

- Current local source version: `3.4.1.10` (2026-11-22), not packaged or
  installed; the installed assembly is `3.4.1.1`.
  The previous local release
  ZIP was generated on 2026-11-20 at
  `artifacts\Pro\UNCAD-Pro-v3.4.1.6.zip` (SHA-256:
  `DCA972DB7532FB9B34582C6F125E29FAA14B701892939DC86385540A46EC7C90`).
  It has not been uploaded. Its bundle checksum differs from the installed
  bundle, and user-drawing acceptance is pending. XSTS frame details use one row per frame, with separate model
  and quantity columns for all drawn BOQ items and blank cells for missing
  items. Near-name hints and the whole-drawing "not drawn" list are removed.
  The local `v3.4.1` tag is present. The `3.4.1.7` archive was published as
  latest on 2026-11-22; size 6,625,853 bytes, SHA-256
  `1AB481606D8BB56E7AEDE77449324A7D0E17E7351260AF3F1666C32A3B729DB8`.
  Server metadata and anonymous download were verified byte-for-byte.
- Current stable release line: `3.4.1` (source patch `3.4.1.10`; latest
  published patch `3.4.1.7`, 2026-11-20;
  D-054 amendment — WPF Viewport3D is the immediate U1U/U13D preview, with an
  optional WebView2/Three.js window launched in a separate process; both
  render identical millimetre geometry, so browser startup cannot block CAD.
  The WPF scene builds synchronously, with no browser process, WebView2 data-directory
  lock, white airspace window, or "starting renderer" state in the default path. The
  reference building model formulas (floor/epoxy/waffle holes/steel/grating/
  devices/busbar glass/columns/1F ground, base-floor rebasing, grid-bay
  extents) were ported verbatim from the retired three.js page, and U13D
  keeps its orthographic camera, axis grid, compass, and info panel. The
  WebView2 in-process preview stack (prewarm host, orphan sweep) is retired;
  the optional WebV process uses a unique local profile and only packaged
  page assets. The login page WebView2 stays. Future fixes increment the fourth version
  part per D-032). Patch .6 adds independent column visibility while preserving
  grid-axis labels, centers those labels on grid center lines, and removes the
  legacy I-Line/socket-panel demo boxes. Patch .7 binds U1U axis labels to the
  two topological route endpoints. Patch .8 matches endpoints by their 4800 mm
  workbook axis regions without requiring route-vector alignment; ambiguous
  matches, branches, or invalid axes show a diagnostic and no guessed
  cabinet/endpoint markers. Since patch .3, reference geometry follows occupied
  route bays plus one neighboring bay, preserving nine waffle holes per
  bay; 1F ground remains visible after floor rebasing. Patch .7 disabled U1U
  style correction for U1L millimetre labels, U1Q trays, U1C conduits, and
  legacy bridge-label migration. Source patch .8 disables the same corrections
  and migration for U1F. U1B label-height and governed-layer correction remain
  enabled; an absorbed reserve marker that cannot be erased now aborts the fill
  transaction instead of leaving the text behind.
  Patch .9 prefixes new CABLE_INFO model displays with `电缆 `; existing
  prefixed and unprefixed frame values remain readable. Patch .10 adds a
  default-off U1SET option to align associated U1L millimetre labels to their
  line using the configured position/offset; it changes neither text nor
  height, and widens only the legacy adjacent-label angle fallback to 15°.
- Product: `UNCAD Pro` only, with online key validation.
- Target host: AutoCAD 2022 / .NET Framework 4.8 / x64.
- Build and package: `build.ps1` and `release.ps1`.
- Detailed engineering rules: `docs/DEVELOPMENT.md`.

## Module Map

| Area | Main entry points | Responsibility |
| --- | --- | --- |
| Fill | `Features/Fill/FillFeature.cs` | U1F/U1U selection, planning, review, and one CAD transaction |
| Excel/Core | `Core/Excel/ExcelMachineReader.cs`, `Core/Fill/` | U_ workbook parsing, BOQ matching, fill planning and diffing |
| QuickLine | `Features/Unl/`, `Core/QuickLine/`, `Cad/QuickLine/` | U1L/U1LX line creation, graph traversal, millimeter labels, and route connectivity (open polylines from U1R/Xauto hops enter the scan as single segments, D-054) |
| Marking | `Features/Unq/`, `Features/Conduit/`, `Features/Unr/`, `Features/Hose/` | U1Q1/U1Q2/U1Q4, U1C, U1R annotations, and the U1B hose outline (`Core/Geometry/HoseOutlinePlanner` + `Cad/HoseCadService`) |
| Layout | `Features/XLayout/`, `Core/Dwg/` | XLAYOUT arrangement, duplicate markers, and Xmerge |
| Statistics | `Features/Stat/`, `Features/Unadd/`, `Core/Stat/` | XSTS and legacy UNADD reports |
| Export/Submit | `Features/DwgExport/`, `Features/Submit/` | U1DWG and U1S outputs; `Core/Submission/OutputLayout` owns folder layout and write-occupancy precheck |
| PDF export | `Features/PdfExport/` | U1PDF: one coloured PDF per frame/circuit via the built-in plot engine, written to the output folder (no upload) |
| Host/Settings | `Infra/`, `Cad/`, `UI/` | registration, settings, licensing, dialogs, and AutoCAD adapters |

## Command Map

The authoritative IDs are in `src/UNCAD/Infra/CommandIds.cs`; the user-facing
descriptions are in `src/UNCAD/Infra/CommandHelpCatalog.cs`.

| Command family | Commands |
| --- | --- |
| Fill and output | `U1F`, `U1U`, `U1S`, `U1D`/`U1DWG`, `U1PDF` (colour PDF per circuit), `U1SET`, `U1DATA` |
| Drawing helpers | `U1L`, `U1LX`, `U13D`, `U1DT`, `U1C`/`U1C20`/`U1C25`/`U1C38`/`U1C51`, `U1R`, `U1B` (hose), `U1Q1`, `U1Q2`, `U1Q4` |
| Project tools | `Xauto`, `XLAYOUT`, `XSTS`, `Xmerge`, `U1HELP`, `U1A` |
| Online account | `UNC_LOGIN` (WebView2 login; account + single-device online) |
| Compatibility aliases | `UNL`, `UNLX`, `UNR`, `UNQ1`, `UNQ2`, `UNQ4`, `UNADD` |

## Main Data Flows

```text
U1F/U1U
  select frames -> read CAD facts -> query manually refreshed SQLite snapshot
  -> plan BOQ rows -> user review/compare -> one transaction -> verify output

U1SET / U1DATA refresh
  selected local/UNC workbook -> parse unified U_ rows -> SQLite snapshot
  (commands query this snapshot; they do not re-read the source workbook)

U1F/U1U/U1S/U1DWG output gate
  before any CAD/BOQ write, check every circuit of each selected machine against
  the selection (UNC_FILL_REQUIRE_ALL_CIRCUITS) -> U1S aborts with the missing
  circuit list; U1DWG (D-047) filters per machine — incomplete machines are
  skipped by default with a selection GUI to force-export them; U1U/U1F (D-034,
  D-044) keep updating the selected frames and only skip that machine's BOQ,
  and U1U adds a confirmation GUI (D-048) to force BOQ generation for
  incomplete machines from the circuits actually drawn
  -> the drawing is refreshed either way

U1L/U1LX
  select or create lines -> build endpoint graph -> read/write millimeter labels

U13D
  select multiple complete frames -> collect each frame's U1L routes and U1Q
  bridge metadata -> align frames by workbook downstream axis -> synchronously
  build a read-only WPF Viewport3D scene with route/bridge meters and diagnostics

U1PDF
  select frames -> resolve machine/circuit identity (same resolver as U1F/U1U)
  -> per frame: plot the frame window with the built-in engine (DWG To PDF.pc3,
  A3, colours preserved) -> verify %PDF- + %%EOF -> write one PDF per circuit under <root>\<machine>\
  (the plot engine writes to the layout's own plot settings, then the user's
  settings are restored field by field; no third-party batch-plot plugin)

XLAYOUT/XSTS
  collect frame regions -> resolve machine identity -> arrange or compare
```

## Data Contracts

- Only the unified `U_` columns and ordinary `回路名称` are input fields. A
  circuit row with strikethrough on `回路名称` is excluded.
- Optional `U_批次` and `U_序号` values are retained in the workbook snapshot;
  XSTS presents one batch and one sequence field per machine.
- Workbook refresh is explicit in `U1SET` or `U1DATA`; the source may be a
  local/UNC file or an HTTP/HTTPS URL (download-only, D-041). Drawing commands
  never touch the network and nothing is uploaded — account/licence validation
  is the only other network feature.
- Outputs: one folder per machine — `<root>\<machine>\` holds that
  machine's BOQ, DWG and PDF together (D-042; no drawing-name or kind layer).
  (default root `Documents\UNCAD输出`).
  Occupied files are detected before the CAD transaction, never after.
- Every command writes one local operation record (JSONL, `%APPDATA%\UNCAD\history`),
  and file logs rotate per day; U1SET → 日志与记录 shows them.
- Config sharing is Base64 JSON limited to shareable `ConfigKeys`; licence,
  clock watermark, update-memory and recent-catalog keys are excluded.
- Parsed machine rows and refresh metadata live in the per-user SQLite snapshot;
  the last successful snapshot remains live until a later refresh succeeds.
- `frameinfo_json` stores machine ID, device/upstream identity, last update,
  cable reserve breakdown and replacement/change history. It is written to the
  frame block's own `FRAMEINFO` attribute (D-040); the standalone
  `frameinfo_json` block and legacy inline attributes stay as read-only legacy
  sources. Schema v2 is the
  nested `identity/upstream/downstream/cable/drawing/meta/changes` document;
  history is capped and the payload has a hard length limit, so a write never
  persists truncated/invalid JSON. Legacy v1 payloads remain readable for
  migration. It is preferred over guessing from labels.
- Old frames and `xframe`/`xframe_v2` are supported. `XFrameTemplate.dwg`
  contains `FRAME_20261119`; legacy frames, including `FRAME_20261102`, migrate
  on the next U1F/U1U write. Matching attribute values are preserved, new
  attributes are added, and attributes absent from the new definition are
  removed. The single-line `INFO2` attribute shows bridge and rigid-conduit
  models in millimetres, separated by a space (for example,
  `镀锌穿线管38mm 4000mm`); multiple lengths use
  `2000 +2500 = 4500mm`. Hose remains in its independent Ruanguan block.
  U1U preserves only unknown bridge/conduit measurements and removes stale hose
  entries. BOQ and statistics report units are unchanged.
- U1F/U1U cable total = measured + panel/box reserve (I-Line 3400 盘内 /
  socket panel 1000 盘内 / busbar box 500 箱内) + device reserve + owner reserve
  (3000 default, U1SET editable). Device reserve is entered in the review GUI,
  written to `frameinfo_json`, and prefilled later. Copied frames do not
  inherit the source machine's device reserve: the copied FRAMEINFO record is
  rejected by frame handle, and the copied CABLE_INFO text is then also
  excluded from reserve recovery (2026-11-06). Since 3.2.0.4 U1F is always a
  full redefine (D-052 appendix 7): the previous record, CABLE_INFO recovery
  and the copied-source prefill never run on U1F, and the sole horizontal
  `0mm` marker is absorbed unconditionally. CABLE_INFO uses millimetres only,
  writes every detail as value（label）, ends with =totalmm, and omits the
  redundant `电缆` prefix before the model; old prefixed values remain readable.
- Current CABLE_INFO output prefixes the displayed model with `电缆 ` as of
  patch .9; historical prefixed and unprefixed values remain readable.
- Existing socket quantities are preserved; an absent socket row may add one.
- U1LX values are display/annotation data only until the deferred decision is
  revisited.
- Governed layers (D-054, 3.3.0; hose added 3.3.3): `U_CABLE` (U1L lines + mm
  labels, colour 7), `U_TRAY` (U1Q output, colour 1), `U_CONDUIT` (U1C output,
  colour 4), `U_HOSE` (U1B output, colour 3 green). New U1L/U1LX output lands
  on `U_CABLE`; U1B output (outline/leader/label) lands on `U_HOSE` with
  `KIND="U1B"` metadata; U1U/U1F correct misplaced plugin entities inside the
  existing write transaction (`UNC_FILL_LAYER_GOVERNOR`, default on) and
  report counts. KIND on parallel annotations decides tray vs conduit vs
  hose; unknown or metadata-less outputs are never moved (no guessing). U1B
  label text follows the Ruanguan hose format and deliberately joins no
  statistics sum (hose metres still come from Ruanguan blocks only).
  The U1U confirmation windows show a 3D route preview (WPF Viewport3D
  perspective camera since 3.4.0) and a report-only connectivity column (isolated
  segments are named, never a block). Preview and list split 50/50 with a
  draggable GridSplitter (collapsed to zero when no preview). Each segment
  shows its millimetre label in a camera-facing overlay; the ground shows
  a two-way column grid (4800 mm bays, toggleable, default on) with
  endpoint axis pins (verbatim workbook text). When workbook regions match,
  a single translation places the measured ends inside their respective bays;
  numeric marks increase east and letters increase north. Tray (red dual lines +
  translucent fill, width from the U1Q label, 200 default when unparsed) /
  conduit (cyan dual lines) channel overlays come from
  ParallelAnnotationOwnership host attribution.

## Performance Rules

- Read configuration and workbook snapshots once per command.
- Keep expensive scans in the relevant frame/connected component.
- Use one transaction for a logical CAD update and avoid opening UI dialogs in
  per-frame loops.
- Do not optimize away intentional XLAYOUT duplicate-label behavior.

## Navigation

- Architecture and layer rules: `docs/DEVELOPMENT.md`
- Product decisions: `docs/DECISIONS.md`
- Deferred/intentional behavior: `docs/KNOWN-ISSUES.md`
- Release steps: `docs/RELEASE.md`
- Focused file list: `scripts/context.ps1`
