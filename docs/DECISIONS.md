# Product Decisions

This file records decisions that should not be reopened from memory or from an
old release note. Add a dated entry when a decision changes; do not silently
rewrite an existing entry.

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
