# UNCAD Pro 2.4.10

Release date: 2026-09-27

## Highlights

- Added the read-only `U1L3D` WebView2/Three.js preview for U1L isometric routes. The preview uses the selected frame, cached upstream/downstream machine data, and a 4800 mm axis step.
- Fixed U1U cable over-counting caused by malformed AutoCAD dimension extents crossing unrelated frames. Text and dimension annotations now use their authored text anchors for exclusive frame ownership.
- Stabilized BOQ table writes by avoiding delayed table-regeneration toggles, clearing content-level auto-scale overrides, and applying one native minimum row height across the generated range.
- Fixed Xmerge false incompatibility failures after `MangleName` has already isolated conflicting block definitions.
- Improved adaptive WinForms layout for machine selection and review on high-DPI, small-work-area, and multi-monitor desktops.
- Included all `Web/QuickLine3D` assets in the build, bundle, installer validation, and release package.

## Validation

- Core/unit regression suite: `dotnet test UNCAD.slnx --no-restore`.
- AutoCAD 2022 BOQ table transaction self-test: `UNCAD_TABLE_FILL_SELFTEST`.
- Package integrity and required-file verification are enforced by `release.ps1`.
