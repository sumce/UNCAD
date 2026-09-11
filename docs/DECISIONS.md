# Product Decisions

This file records decisions that should not be reopened from memory or from an
old release note. Add a dated entry when a decision changes; do not silently
rewrite an existing entry.

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

- Status: accepted
- Decision: frame identity, last update, and replacement history are persisted
  in the `frameinfo_json` block.
- Consequence: U1U should use this record to preserve user-selected
  substitutions instead of rediscovering them from incomplete labels.

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
  must be a bare length — so a lone `2000mm` and every single-line legacy
  label still count as a cable. Re-running `U1Q*`/`U1C` replaces the legacy
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
