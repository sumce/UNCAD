# Known Issues And Scope

Keep this list short. An item belongs here only when it is active, intentionally
deferred, or important for support diagnosis.

## Deferred

- U1LX-entered distances are not part of U1U/U1F statistics. This is an
  explicit scope decision, not an accidental omission.

## Intentional Behavior

- Repeating XLAYOUT can create another machine-ID label.
- U1F/U1U do not draw automatic upstream connection geometry.
- A missing online license response fails closed (no offline grace path); the
  customer can enter a new authorization code in the login dialog.
- HTTP/HTTPS machine-workbook sources are intentionally rejected in 2.10.0;
  use a local or UNC path and refresh via U1DATA/U1SET.
- The whole-machine circuit gate can block BOQ/DWG for a machine whose circuits
  are not all drawn yet (U1F/U1S/U1DWG abort; D-033). U1U is exempt since the
  D-034 amendment: it updates the selected frames and only skips that machine's
  BOQ. Disable `UNC_FILL_REQUIRE_ALL_CIRCUITS` in U1SET if a project really
  needs partial output everywhere.
- A Base64 config share code never contains licence/account values, so a shared
  profile does not transfer authorization.

## Support Notes

- U1PDF always plots with `DWG To PDF.pc3`. If a workstation reports
  "PDF 不带颜色", check for third-party plot devices (e.g. `M_PDF.PC3`) or a
  non-colour plot style table; the built-in exporter asserts colour in
  `UNCAD_U1PDF_SELFTEST`.
- Outputs from the pre-2.10.0 flat layout stay in their old folder; moving them
  into the new layout is manual (see `docs/RELEASE-2.10.0.md`).

## Environment Limits

- CAD integration tests and real command acceptance require AutoCAD 2022 and
  its .NET assemblies. Core tests should remain runnable without AutoCAD loaded.
- User drawings, customer workbooks, logs, `bin`, `obj`, and `artifacts` are
  local inputs/outputs and are not source-of-truth project files.

## Resolved In 2.3.0

- QuickLine text-only helpers no longer eagerly resolve AutoCAD RX classes.
- Batch U1U no longer opens the comparison dialog twice.
- The U1U comparison window reserves its left selector area before laying out
  the two comparison tables.
