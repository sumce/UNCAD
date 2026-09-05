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
