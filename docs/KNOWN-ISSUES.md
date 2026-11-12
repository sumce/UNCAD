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
