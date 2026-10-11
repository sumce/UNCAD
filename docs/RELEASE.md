# Release Checklist

The supported release is the unified Pro package. Keep this checklist short;
the scripts perform the mechanical validation.

1. Update `src/UNCAD/UNCAD.csproj`, `src/UNCAD/Infra/ProductMetadata.cs`, and
   `bundle/UNCAD.bundle/PackageContents.xml` together.
2. Update version assertions and `docs/RELEASE-<version>.md` when the release
   has user-visible behavior changes.
3. Run the focused tests for the changed module, then:

   ```powershell
   dotnet test UNCAD.slnx -c Release --no-restore
   ```

4. Build and verify the package:

   ```powershell
   .\release.ps1 -NoRestore
   ```

5. Confirm the archive is under `artifacts\Pro`, the manifest says
   `UNCAD Pro`, `LicenseMode="Online"`, and has no embedded expiry/customer
   metadata.
6. Run `git diff --check`, `git status --short`, and commit only source,
   tests, documentation, and required bundle metadata.
7. Tag and commit before packaging: `git tag -a v<version> -m "UNCAD Pro <version>"`.
8. Publish the installer so users can actually download it (the server hosts the
   package itself; no external hosting):

   ```powershell
   $env:UNCAD_ADMIN_PASSWORD = '<admin password>'
   .\tools\publish-release.ps1 -Version <version>.0 `
       -ZipPath artifacts\Pro\UNCAD-Pro-v<version>.0.zip `
       -NotesPath docs\RELEASE-<version>.md
   ```

   The script uploads the ZIP, points `DownloadUrl` at
   `/api/releases/<version>/download`, records the real SHA-256, promotes the
   version to the exclusive `IsLatest` (the server demotes the previous one), and
   then verifies `/api/releases/latest` plus an anonymous download byte-for-byte.
9. CAD behaviour is verified on a real AutoCAD, not only by unit tests:
   `tests\UNCAD.CadIntegration\run.ps1 -Command UNCAD_U1PDF_SELFTEST`
   (see `docs/DEVELOPMENT.md`, section "AutoCAD 真机自检").

