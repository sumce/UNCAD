# UNCAD Pro 2.6.0

Release date: 2026-10-11

This release adds on-demand PDF output for a single circuit (one frame) and uploads
it so the customer portal can preview it online. It also changes the authorization
model: **offline use is no longer supported**.

## New command: `U1PDF` (PDF per circuit)

- Select frames with the same collector used by `U1DWG`, then run `U1PDF`. Every
  **frame becomes one PDF**, because one frame is one circuit — a machine with several
  circuits produces several PDFs. This is deliberately different from `U1DWG`, which
  exports **one DWG per machine** covering all of its circuits.
- Output is **colour**: the plot keeps layer colours and does not apply a monochrome
  plot style table. The plot device is AutoCAD's built-in `DWG To PDF.pc3`; if that
  device is missing the command says so in the command line instead of failing
  silently.
- The plot window is the frame's own extents, scaled to fit and centred, so the PDF
  contains exactly the frame.
- Each PDF is uploaded to `/api/client/submissions/file` with `kind=pdf` and exactly
  one `circuits` value. The file name is `<circuit>.pdf`, sanitised by the server.
- Batch plotting reports progress per frame (`正在出图 2/7：…`) and finishes with
  `出图完成：成功 N 份 / 失败 M 份`, listing the reasons for up to three failures.
  One failure never stops the remaining frames.
- Uploads run on a background thread: a failed or skipped upload only writes a log
  line plus a command-line hint (for example, `409` asks the user to run `UNC_LOGIN`
  again). It never rolls back or blocks the drawing, and it never opens a modal
  window over AutoCAD.

## Authorization: no offline use

- The 30-minute offline grace window has been removed. UNCAD commands require a live
  server verdict: without a response from the authorization server (not validated yet,
  or the network is unreachable) the commands disable immediately and print a clear
  Chinese reason telling the user to check the network.
- Only `active` enables commands. `pending`, `unavailable`, `device-kicked`,
  `token-invalid`, `unbound`, `device-missing`, `expired`, `suspended`, `revoked` and
  `unknown` all disable them right away.
- The historical `UNC_LICENSE_LAST_OK` registry value is kept so that upgrading does
  not scramble existing configuration, but it is no longer read or written.

## Regression coverage

`tests/UNCAD.Tests/PdfExportTests.cs` and the existing registration tests lock the new
behaviour:

- `U1PDF` is registered exactly once and appears in `CommandIds.Registered`;
- the upload request uses `kind=pdf`, exactly one circuit, and a `.pdf` file name;
- a frame's circuit name is resolved with the same resolver that `U1F`/`U1U` use
  (`FrameIdentityReader` identity + `ExistingFillIdentityResolver.MatchMachine`), so a
  PDF cannot be attached to the wrong circuit;
- a frame that fails to plot is skipped while the other frames are still queued;
- PDF file names and the `%PDF-` file-header check.

## Upgrade note

Nothing to do. Install 2.6.0 over 2.5.x. An existing device binding keeps working; if
the machine is signed out, run `UNC_LOGIN`. Keep in mind that a network connection to
the authorization server is now required while working.
