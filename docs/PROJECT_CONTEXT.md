# UNCAD Project Context

This is the compact map for day-to-day maintenance. It is intentionally much
shorter than the historical release notes.

## Status

- Current local source version: `3.8.0.13` (2026-11-27; 3.8.0.13 fixes
  cross-file copied-frame JSON rebind: `HandleAliveInDatabase` now returns
  false when `TryGetObjectId` finds no handle or when an identically numbered
  frame handle in another drawing clearly belongs to a different machine/
  circuit/upstream, while `ResolveUpdateCableFromExistingTable` and `ReadOwn`
  pass frame identity so copied cross-file frames retain `CircuitKey`, device
  reserve, BOQ cable substitutes, and bus-plug choices; 3.8.0.12 fixes MText
  attribute clearing in `ClearStaleInfo`/`ClearStandaloneJson` + dynamic JSON
  property clearing, `IsForeignTo` inheritance guard in `FillOrMigrate`, MText
  attribute reading in `FillSelectionCollector.TryGetBlockValue`, and closed-window
  guards in `XmergeWindow`; 3.8.0.11 was the
  copied-frame same-name-circuit patch: the physical identity reads the
  upstream block's UPSTREAM_INFO and arbitration compares machine/circuit/
  upstream, dropping a copied frame's stale identity key; 3.8.0.10 was the
  true-white plot-colour patch: U1L/U1LX new entities pin ByLayer and the
  U1F/U1U governor normalises existing true-white/ACI-255 entities on governed
  layers, so routes no longer print white on white; D-059 + 3.8.0.1–3.8.0.8
  field fixes: U1U preview runs the local WebView2 + three.js page with the
  WPF Viewport3D renderer as automatic fallback (never a blank pane); it draws
  only the workbook floors' wireframe planes, the edge axis band, the routes
  and the upstream cabinet; reference building and WebV removed; tray/conduit
  run totals with red-line host recovery; cross-floor rise check measured from
  the upstream floor ground to the device end; pitch-true 4.8 m axis grid
  (both ends inside their bays, a mismatch is named); U1UL position match;
  legacy single-/multi-line tray text/red-line, dimension/curve and
  bent-polyline route recognition; `宽Wx高H` tray width; dead label
  associations re-matched; socket panel lifts the route 1 m; line labels sit
  on their line; confirmation windows 1440×900 / 1440×820 half-and-half;
  preview page camera fits the route, labels avoid each other, single bold cable,
  coordinate lattice at every grid intersection, endpoint axis chips at grid
  corners),
  packaged locally as `artifacts/Pro/UNCAD-Pro-v3.8.0.13.zip` (6,584,019 bytes,
  SHA-256
  `4A277B43B25567107B84EC9DABFF067445F9C2A70DAA85E071F40B05D9F59681`), tagged
  as `v3.8.0.13`. The previous tagged release is
  `v3.8.0.12` (commit `2d399fe`; MText/inheritance/Xmerge fixes; SHA-256
  `728A864E631C8018263513F1C4A341C796F0236301C85CD3FE73B291192C3338`),
  `v3.8.0.11` (commit `e11c7f0`; copied-frame same-name-circuit patch; SHA-256
  `F2E3282E69C040045D76B60432A520497DF1217609903260AA746F44A845440B`),
  `v3.8.0.10` (GitHub Release, CI asset `UNCAD-Pro-v3.8.0.10.zip`, 6,580,117
  bytes, SHA-256
  `384A79B0349F848E30BD4E8EB85058576A2DBCDF7BF5015112E01BFDF58B7C55`);
  before that `v3.8.0.9` (structure/CI cleanup; CI asset SHA-256
  `6852A16D28A03C7F353DC405B5068E641317B72CD63E3D6F39C924B01C00226B`).
  Package/bundle `UNCAD.dll` SHA-256 is
  `C1A42AB2E5890E4AC2F252AD1EC2E4D5873B545B027F3E50D5889302C4A59517`; the
  development machine's installed copy remains 3.8.0.8 until
  `installer.ps1 -Mode InstallUser` (AutoCAD must be closed). The previous
  local tagged release is `3.8.0.12` (tag `v3.8.0.12`); before that
  `3.8.0.11` (tag `v3.8.0.11`), `3.8.0.10` (tag `v3.8.0.10`; true-white
  plot fix), `3.8.0.9` (tag `v3.8.0.9`; structure/CI cleanup), `3.8.0.0`
  (tag `v3.8.0`, commit `7157aa1`; D-059 preview redesign), then `3.7.0.0`
  (tag `v3.7.0`, commit `5a9b81a`).
  The latest GitHub Release is `3.8.0.10` (tag `v3.8.0.10`; CI asset SHA-256
  `384A79B0349F848E30BD4E8EB85058576A2DBCDF7BF5015112E01BFDF58B7C55`);
  the latest site release is `3.6.0.0`
  (2026-11-24, tag `v3.6.0`; D-057 same-name circuits, drawing-info table
  templates, BOQ title templates, XLAYOUT options),
  `UNCAD-Pro-v3.6.0.0.zip` (6,549,352 bytes, SHA-256
  `BDFC5E907A62561F94F9E6F90238B96C0A7CBF474753C72A2451EA3D28C87B36`),
  published on uncad.aosa.me and verified by anonymous download. XSTS frame
  details use one row per frame, with separate model and quantity columns for
  all drawn BOQ items and blank cells for missing items. Near-name hints and
  the whole-drawing "not drawn" list are removed.
- Product: `UNCAD Pro` only, with online key validation.
- Target host: AutoCAD 2022 / .NET Framework 4.8 / x64.
- Build and package: `build.ps1` and `release.ps1`.
- Detailed engineering rules: `docs/DEVELOPMENT.md`.

This block is the single source of the version/tag/hash status. `AGENTS.md`,
`README.md` and `CLAUDE.md` must link here instead of copying it — the previous
copy in `AGENTS.md` still claimed `3.4.1` while the product was `3.8.0.10`.


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
| Drawing helpers | `U1L`, `U1LX`, `U1UL` (millimetre-label alignment), `U13D`, `U1DT`, `U1C`/`U1C20`/`U1C25`/`U1C38`/`U1C51`, `U1R`, `U1B` (hose), `U1Q1`, `U1Q2`, `U1Q4` |
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
  collect frame regions -> resolve machine identity -> pair each machine's
  frames with its workbook rows (CircuitMatcher: identity key first, name only
  when unique in the machine, same-name frames without a usable key pending)
  -> arrange or compare

U1U route preview (D-058/D-059; read-only, U1F builds none)
  whole-space scan (segments + labels + unbound labels, parallel outputs,
  annotation texts — each once per command) -> per frame: segments whose
  midpoint is inside the boundary -> connectivity (strict 1.0, plus gap
  tolerance only between two labelled segments) -> tray/conduit runs
  (metadata ownership -> red-line pieces -> nearest segment; the annotation
  carries the run total) -> isometric scene from the labelled segments ->
  route ends from the upstream/device blocks (nearest open end) -> hose from
  the Ruanguan block -> floor planes from the workbook floors (own
  elevations), upstream cabinet by 盘柜类型, cross-floor rise check ->
  issues named (unlabelled lines, unbound labels, tolerated gaps, orphan
  runs, missing axis blocks, skipped directions) -> preview model handed to
  the confirmation window (floors + edge axis band + route + cabinet only)
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
  cable reserve breakdown and replacement/change history. `meta` also carries
  `filledUtc` (latest U1F, the creation time) and `lastCommand` (D-057); the
  circuit identity key is derived from the stored plaintext, never persisted.
  The drawing-info table's six columns are filled from the U1SET templates
  (`UNC_FILL_INFO_*`, empty = leave the cell untouched). It is written to the
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

- Architecture, modules and shared-capability ownership: `docs/ARCHITECTURE.md`
- Engineering rules: `docs/DEVELOPMENT.md`
- Product decisions: `docs/DECISIONS.md`
- Deferred/intentional behavior: `docs/KNOWN-ISSUES.md`
- Release steps: `docs/RELEASE-RUNBOOK.md`
- Historical release notes (1.x / 2.x archive): `docs/releases/`
- Focused file list: `scripts/context.ps1`
