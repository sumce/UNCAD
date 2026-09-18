# UNCAD Project Context

This is the compact map for day-to-day maintenance. It is intentionally much
shorter than the historical release notes.

## Status

- Current release: `2.4.8`.
- Product: `UNCAD Pro` only, with online key validation.
- Target host: AutoCAD 2022 / .NET Framework 4.8 / x64.
- Build and package: `build.ps1` and `release.ps1`.
- Detailed engineering rules: `docs/DEVELOPMENT.md`.

## Module Map

| Area | Main entry points | Responsibility |
| --- | --- | --- |
| Fill | `Features/Fill/FillFeature.cs` | U1F/U1U selection, planning, review, and one CAD transaction |
| Excel/Core | `Core/Excel/ExcelMachineReader.cs`, `Core/Fill/` | U_ workbook parsing, BOQ matching, fill planning and diffing |
| QuickLine | `Features/Unl/`, `Core/QuickLine/`, `Cad/QuickLine/` | U1L/U1LX line creation, graph traversal, and millimeter labels |
| Marking | `Features/Unq/`, `Features/Conduit/`, `Features/Unr/` | U1Q1/U1Q2/U1Q4, U1C, and U1R annotations |
| Layout | `Features/XLayout/`, `Core/Dwg/` | XLAYOUT arrangement, duplicate markers, and Xmerge |
| Statistics | `Features/Stat/`, `Features/Unadd/`, `Core/Stat/` | XSTS and legacy UNADD reports |
| Export/Submit | `Features/DwgExport/`, `Features/Submit/` | U1DWG and U1S outputs |
| Host/Settings | `Infra/`, `Cad/`, `UI/` | registration, settings, licensing, dialogs, and AutoCAD adapters |

## Command Map

The authoritative IDs are in `src/UNCAD/Infra/CommandIds.cs`; the user-facing
descriptions are in `src/UNCAD/Infra/CommandHelpCatalog.cs`.

| Command family | Commands |
| --- | --- |
| Fill and output | `U1F`, `U1U`, `U1S`, `U1DWG`, `U1SET` |
| Drawing helpers | `U1L`, `U1LX`, `U1D`, `U1C`, `U1R`, `U1Q1`, `U1Q2`, `U1Q4` |
| Project tools | `XLAYOUT`, `XSTS`, `Xmerge`, `U1HELP`, `U1A` |
| Compatibility aliases | `UNL`, `UNLX`, `UNR`, `UNQ1`, `UNQ2`, `UNQ4`, `UNADD` |

## Main Data Flows

```text
U1F/U1U
  select frames -> read CAD facts -> query manually refreshed SQLite snapshot
  -> plan BOQ rows -> user review/compare -> one transaction -> verify output

U1SET refresh
  selected local/remote workbook -> parse unified U_ rows -> SQLite snapshot
  (commands query this snapshot; they do not re-read the source workbook)

U1L/U1LX
  select or create lines -> build endpoint graph -> read/write millimeter labels

XLAYOUT/XSTS
  collect frame regions -> resolve machine identity -> arrange or compare
```

## Data Contracts

- Only the unified `U_` columns and ordinary `回路名称` are input fields. A
  circuit row with strikethrough on `回路名称` is excluded.
- Workbook refresh is explicit in `U1SET`; commands do not silently download a
  network workbook.
- Parsed machine rows and refresh metadata live in the per-user SQLite snapshot;
  source-file changes are ignored until the next explicit refresh.
- `frameinfo_json` stores machine ID, device/upstream identity, last update,
  and replacement/change history. It is preferred over guessing from labels.
- Old frame layouts and `xframe` are supported. Migration adds the new frame
  structure without changing an existing table's dimensions.
- Existing socket quantities are preserved; an absent socket row may add one.
- U1LX values are display/annotation data only until the deferred decision is
  revisited.

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
