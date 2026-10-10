# UNCAD Pro 2.5.0

Release date: 2026-10-10

This is a breaking release: online authorization moved from authorization codes to
accounts with a single online device. Customers must sign in once on each machine
they intend to use.

## Account authorization replaces authorization codes

- The plugin now authenticates with a **user account** instead of an authorization code.
  The license server address is `uncad.aosa.me`.
- Sign in once through the in-plugin WebView2 window (`UNC_LOGIN`); the device
  credential is stored and renewed automatically afterwards.
- Each installation generates a stable device identifier on first run
  (`HKCU\SOFTWARE\UNSIAO\UNCAD\UNC_DEVICE_ID`). Reinstalling the operating system
  or deleting the registry value makes the machine a new device.
- **Only one device may be online per account at a time.** When the account signs in
  on another device, the previous device is stopped on its next check (every 3 minutes)
  and reports which device took over. Use `UNC_LOGIN` again to move this machine online.
- The account's license mode, expiry and status now come from the server account,
  not from a key file.

## Offline use removed

- The 30-minute offline grace window has been removed. Commands require a successful
  server verdict.
- If the authorization server cannot be reached, commands stop immediately and show a
  Chinese reason explaining that this version does not support offline use.
- Expiry is still decided by the server `Date` header; the local clock is not trusted.

## U1U / U1F / U1DWG upload their results

- `U1F`, `U1U` and `U1DWG` now upload their results to the server as they complete.
- The minimum unit is the **circuit**, which corresponds to one frame in the drawing;
  a machine normally has several circuits.
  - `U1F` / `U1U` upload that circuit's bill-of-quantities rows.
  - `U1DWG` registers the circuits covered by the export and uploads the exported
    DWG file (one horizontal DWG per machine).
- Uploads run in the background and never block or roll back the drawing: a failed
  upload is logged and retried on the next run. If the account has been taken over by
  another device (HTTP 409), the plugin asks the user to sign in again.
- Only ledger/list data and the DWG are uploaded. Drawing-side bookkeeping
  (`frameinfo_json`), cable lengths and other CAD-produced values stay in the drawing.

## Multi-frame 3D preview alignment

- `U1L3D` / `U13D` multi-frame previews now share one axis grid built from both the
  device and upstream workbook axes, so the visible marks cover every placed frame.
- Fixed the scene origin: the resolved downstream node is now placed on the workbook
  axis position instead of the first collected segment's arbitrary local origin.
- Numeric and alphabetic axis marks are emitted to the preview payload and rendered.

## Compatibility notes

- Authorization codes are no longer accepted by the plugin. Server-side compatibility
  for existing codes remains only for already-deployed older builds.
- The version reported in the authorization heartbeat and in uploaded data is now
  `2.5.0`, so server-side records identify the release that produced them.
