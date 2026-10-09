# UNCAD Pro 2.4.13

Release date: 2026-09-29

## U13D multi-frame 3D preview

- Added `U13D` for selecting multiple complete frames.
- Frames are aligned by their workbook downstream axes using 4800 mm grid spacing,
  independent of selection order.
- The read-only preview shows all U1L routes, total cable meters, bridge meters,
  and per-frame axis offsets.
- U1Q bridge geometry is associated with a route only when source metadata gives
  a unique source line. Legacy labels are shown as unassociated instead of
  being guessed.
- U13D now labels every route with its machine and circuit, and draws compact
  upstream/downstream endpoint markers without long connector lines.
- Direction markers now follow the nearest U1L endpoints beside the frame's
  device and upstream blocks; missing anchors fall back to workbook axes with
  a visible diagnostic.
- Connected U1L segments are presented as one route in labels and the route
  list; bends and junctions no longer create separate route identities.
- The grid uses numeric east/west axes and alphabetic north/south axes:
  east increases numbers and north increases letters. XYZ color coding was
  removed from the preview.
- Dense multi-frame previews now hide route labels by default when more than
  24 segments are present. The route list remains complete, and the
  `路线标签` button enables compact, collision-avoiding labels on demand.

## BOQ table height correction

- U1F/U1U no longer enlarge every BOQ Table row to match the longest description.
  Generated rows stay at 5847.8848 drawing units; overflowing text is reduced
  without truncating the description or changing column widths and headers.
- Multi-line descriptions use a compact 0.85 line-spacing factor for a denser,
  more readable BOQ table.
- AutoCAD 2022 regression coverage checks the shipped template, long descriptions,
  repeated writes, native regeneration, DWG save/reopen and impossible margins.
