# UNCAD Pro 2.4.11

Release date: 2026-09-27

## Fix

- Fixed the update-notes window rendering an empty body because its auto-sized entry panels were assigned zero width by WinForms layout.
- Replaced the fragile entry-panel layout with one read-only, wrapping, scrollable text surface.
- Added a regression test that creates the actual form layout and verifies the released version and first change line are visible in a non-zero-sized body.
- Repeats the 2.4.10 highlights in the 2.4.11 in-app entry because the broken 2.4.10 dialog may already have marked those notes as seen.
