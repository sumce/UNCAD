# Claude Code Notes

Use `AGENTS.md` as the canonical project contract. For a focused task, read:

1. `docs/PROJECT_CONTEXT.md`
2. `docs/DECISIONS.md`
3. The relevant section of `docs/DEVELOPMENT.md`
4. The file list from `scripts/context.ps1 -Area <area>`

Do not treat old release notes, chat history, generated files, or customer
drawings/workbooks as project rules. Current version and command IDs come from
the source and tests, not from this file.

```powershell
.\scripts\context.ps1 -Area Fill -IncludeTests
dotnet test UNCAD.slnx --no-restore
.\release.ps1 -NoRestore
```
