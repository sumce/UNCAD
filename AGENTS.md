# UNCAD Agent Contract

This is the short entry point for any human or coding agent working in this
repository. Keep long explanations in `docs/`; keep this file stable.

## Read Order

1. Read `docs/PROJECT_CONTEXT.md` for the current map and non-negotiable rules.
2. Read `docs/DECISIONS.md` for decisions that must not be reopened casually.
3. Read only the relevant section of `docs/DEVELOPMENT.md`.
4. Use `scripts/context.ps1 -Area <area>` to list the source and tests for the
   task. Read those files, not the whole repository.
5. Check `docs/KNOWN-ISSUES.md` before proposing behavior that was previously
   deferred.

## Source Of Truth

For intended product behavior, use this order:

1. Accepted decisions in `docs/DECISIONS.md`.
2. `docs/PROJECT_CONTEXT.md` and the relevant section of `docs/DEVELOPMENT.md`.
3. Compiling source and passing tests as the observed implementation.
4. Release notes and chat history.

When code or a test conflicts with an accepted decision, treat it as a
regression to investigate. Do not preserve an old behavior merely because it
currently passes a stale test.

`ARCHITECTURE.md` and `CLAUDE.md` contain historical/tool-specific notes. They
are useful references, but this file and the documents above take precedence
when their version numbers or counts are stale.

## Current Product

- Product: `UNCAD Pro`, version `2.3.0`.
- Runtime: AutoCAD 2022, .NET Framework 4.8, x64.
- The only supported distribution is the online-licensed Pro package.
- The release entry point is `release.ps1`; customer and expiry data come from
  `pro.key`, not from the assembly or package manifest.

## Hard Constraints

- Machine workbooks are selected by the user and refreshed manually.
- Workbook parsing accepts the unified `U_` fields and ordinary `回路名称`.
  Rows whose circuit name has strikethrough are ignored. Do not reintroduce
  A1/A2 or backup-field guessing.
- Old frames and `xframe` frames remain readable. Migration must not resize a
  user's existing table, row, or column.
- `frameinfo_json` is the persisted frame identity and change-history source.
- `U1LX` distance values are intentionally outside U1U/U1F statistics for now.
- Repeating `XLAYOUT` may add another machine-ID label; this behavior is
  intentional and is not a cleanup task.
- U1F/U1U do not create automatic upstream connection geometry.

## Layer Rules

- `Core`: pure parsing, matching, planning, statistics, and formatting.
- `Cad`: AutoCAD entities, transactions, selection, and drawing adapters.
- `Infra`: settings, logging, metadata, licensing, and registration.
- `Features`: command orchestration and transaction boundaries.
- `UI`: user interaction and editing of passed-in models only.

Do not move Excel matching into `Cad`, put business rules in forms, or make
`Core` depend on AutoCAD or WinForms.

## Change Protocol

- Start with `git status --short` and locate the command entry with `rg`.
- Make the smallest root-cause change that preserves the decisions above.
- Add one focused regression test for every non-trivial behavior change.
- Run a targeted test first, then `dotnet test UNCAD.slnx --no-restore`.
- For a release, run `.\release.ps1 -NoRestore` and inspect the generated
  package under `artifacts\Pro`.
- Do not include `bin`, `obj`, `tmp`, logs, customer drawings, or user Excel
  files in commits.
- Update a decision or known-issue document only when behavior or scope has
  actually changed; do not duplicate implementation details in documents.

## Useful Commands

```powershell
.\scripts\context.ps1 -Area Fill
.\scripts\context.ps1 -Area QuickLine -IncludeTests
dotnet test UNCAD.slnx --no-restore
.\release.ps1 -NoRestore
```
