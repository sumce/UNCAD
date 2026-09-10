# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`AGENTS.md` is the canonical project contract and takes precedence over this
file. This file adds the commands and cross-file architecture that those
documents assume you already know.

## Required context before editing

1. `AGENTS.md` — hard constraints, layer rules, change protocol.
2. `docs/PROJECT_CONTEXT.md` — module and command map.
3. `docs/DECISIONS.md` — accepted decisions (D-001…D-016) that are not open for
   re-litigation. Check `docs/KNOWN-ISSUES.md` before "fixing" deferred behavior.
4. The relevant section of `docs/DEVELOPMENT.md` (bilingual, Chinese; the
   engineering rules live here).

Do not treat release notes, chat history, generated files, or the stray
customer `.log` / `.dwg` / `.xlsx` files in the repo root as project rules.
Version numbers and command IDs come from source and tests, never from docs.

## Commands

```powershell
# Focused context pack: prints git branch/status, recent commits, and the exact
# source + test files for an area. Prefer this over reading whole directories.
.\scripts\context.ps1 -Area Fill -IncludeTests
# Areas: All | Fill | QuickLine | Layout | Stats | Settings | Submit

dotnet build UNCAD.slnx                        # or .\build.ps1 (Release, refreshes bundle)
dotnet test UNCAD.slnx --no-restore            # full suite (xunit, net48)
dotnet test UNCAD.slnx --filter "FullyQualifiedName~FrameRegionCollectorTests"
dotnet test UNCAD.slnx --filter "FullyQualifiedName~BoqCatalogIndexTests.Category_IsAutomaticallyResolved"

.\release.ps1 -NoRestore                       # full release gate; output in artifacts\Pro
```

- `build.ps1` / `release.ps1` accept only `-Configuration Release`.
- Both projects reference AutoCAD DLLs from `D:\Program Files\Autodesk\AutoCAD 2022`.
  Override with `-p:AutoCADDir="<path>"`; the build fails without them.
- `build.ps1` cleans first, then syncs `bundle\UNCAD.bundle\` — DLL set is
  replaced wholesale and `checksums.sha256` regenerated. Never hand-edit bundle
  contents; a mismatch fails `release.ps1`.
- `release.ps1 -NoBuild` refuses to run when any source, script, or manifest is
  newer than the built DLL, so a stale shortcut cannot ship.
- `release.ps1` gates on: build → bundle SHA-256 match → full test suite →
  `installer.ps1 -Mode VerifyPackage` → version-label agreement.

## Version bumps are multi-file

`release.ps1` enforces agreement between `ProductMetadata.VersionLabel` and
`PackageContents.xml AppVersion`, and requires a non-empty
`docs\RELEASE-<version>.md`. A version bump touches:

- `src/UNCAD/UNCAD.csproj` (AssemblyVersion / FileVersion / Version)
- `src/UNCAD/Infra/ProductMetadata.cs` (`VersionLabel`, `ReleaseDateUtc`)
- `src/UNCAD/Infra/VersionChangeLog.cs` (new entry)
- `bundle/UNCAD.bundle/PackageContents.xml` (`AppVersion` as `x.y.z.0`)
- `docs/RELEASE-<version>.md`
- `tests/UNCAD.Tests/ConfigKeysTests.cs` (asserts the bare label vs `AboutInfo`)

`ProductMetadata.VersionSuffix` must stay empty while that test asserts the bare
version label.

## Architecture

Layered plugin loaded into AutoCAD 2022 (`net48`, x64). Dependency direction is
one-way: `Core` ← `Cad`/`Infra` ← `Features` ← `UI`. `Core` is pure C# with no
AutoCAD, WinForms, registry, or process-global state — that is what makes it
unit-testable and what the test suite covers.

- `src/UNCAD/Core/` — parsing, matching, BOQ planning, statistics, geometry,
  formatting. Sub-areas: `Excel`, `Fill`, `QuickLine`, `Stat`, `Text`, `Dwg`,
  `Geometry`, `Submission`, `IO`, `Report`, `Contracts`.
- `src/UNCAD/Cad/` — AutoCAD entities, transactions, selection, frame regions.
- `src/UNCAD/Infra/` — `CommandIds` (authoritative command contract),
  `CommandHelpCatalog`, `ConfigKeys`, settings, logging, metadata, licensing.
- `src/UNCAD/Features/<Name>/` — one directory per command; orchestration and
  the single transaction boundary.
- `src/UNCAD/UI/` — WinForms shells that edit a passed-in model only. All dialogs
  are native WinForms; styling comes from `UiTheme` and form sizing from
  `DialogLayout`. The only remaining WebView2 use is `StartupSplashForm`, which
  renders the splash from `docs.html` (embedded as `UNCAD.Assets.StartupSplash.html`)
  with `StartupSplashCanvas` as its native fallback. An HTML-based dialog
  framework was tried and removed — do not reintroduce one without a decision.

Adding a command means touching all of: `CommandIds`, the `CommandMethod`
attribute, `RibbonDefinition`, `CommandHelpCatalog`, the bundle, and
`CommandRegistrationTests` — the tests fail if these drift apart.

### Reuse points (do not reimplement per feature)

- `Cad/FrameRegionCollector` — frame detection.
- `Features/Submit/FrameIdentityReader` — frame identity.
- `Infra/FileBatchRollback` — multi-file batch backup/restore.
- `Infra/CommandHelpCatalog` — user-facing help text.
- `Core/Excel/StableFileCache<T>` — file fingerprint + transient-IO retry.

### Key data flows

```text
U1SET refresh:  user-selected workbook -> parse unified U_ rows -> SQLite snapshot
U1F/U1U:        select frames -> read CAD facts -> query snapshot -> plan BOQ rows
                -> user review -> ONE transaction -> verify -> write BOQ xlsx
U1L/U1LX:       create/select lines -> endpoint graph -> millimeter labels
XLAYOUT/XSTS:   collect frame regions -> resolve machine identity -> arrange/compare
```

- The SQLite snapshot is the only machine-data source at command runtime.
  Commands never read the source workbook, check its timestamp, or touch the
  network (D-003, D-012). Changes take effect only after an explicit `U1SET`
  refresh; a failed refresh keeps the previous snapshot.
- Workbook input is strictly the unified `U_` columns plus ordinary `回路名称`,
  read per physical row with merged regions ignored (D-002, D-011, D-013). Rows
  with a struck-through circuit name are excluded. Never reintroduce A1/A2 or
  backup-field guessing.
- `frameinfo_json` is the persisted frame identity / change-history record and is
  the source of user-confirmed catalog substitutions (D-005, D-015).
- The fixed catalog is the embedded resource `Resources/embedded_catalog.tsv`.
  After editing the source spreadsheet, run `scripts/GenerateEmbeddedCatalog.ps1`
  and bump the version; users never supply a catalog file.
- Every row written to CAD must carry the same fixed-catalog project code as the
  exported BOQ row; unmatched rows stop the batch instead of writing uncoded
  (D-016).

### Behavior that looks like a bug but is intentional

Decisions you must not "clean up": XLAYOUT duplicate labels (D-009), preserved
socket quantities (D-008), no automatic upstream connection geometry (D-007),
U1LX distances excluded from statistics (D-006), frame migration never resizing
an existing table (D-004).

## Testing

- `tests/UNCAD.Tests` (xunit) is the gate that runs in CI/release. Behavior and
  boundary tests live here; string-matching tests over source files only verify
  registration metadata.
- `tests/UNCAD.CadIntegration` is **not** run by `dotnet test` — it builds
  self-test commands (`*SelfTestCommand.cs`) that must be `NETLOAD`ed into a live
  AutoCAD 2022 session for manual verification.
- Every non-trivial behavior change gets one focused regression test; per
  `docs/DEVELOPMENT.md`, each new command also needs a registration test, a Core
  rule test, and a failure-branch test.
- Test names encode the contract (e.g. `ExcelMachineReaderStrictTests`,
  `BatchFillUpdateContractTests`) — match the existing style when adding.

## Commits

Exclude `bin`, `obj`, `tmp`, logs, customer drawings, and user Excel files —
`.gitignore` already covers these, along with bundle DLLs, `artifacts/`, and
non-fixture `.xlsx`/`.dwg`.

Note that untracked files are not all junk: working-tree additions such as
`src/UNCAD/Features/Dimension/` and `src/UNCAD/Core/Text/DimensionTextFormatter.cs`
are real source. Run `git status --short` before assuming anything untracked is
disposable.
