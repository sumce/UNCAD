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
