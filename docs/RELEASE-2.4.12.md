# UNCAD Pro 2.4.12

Release date: 2026-09-28

## Conduit models

- Added `U1C20`, `U1C25`, `U1C38`, and `U1C51` for generating the corresponding fixed BOQ rigid-conduit model directly.
- Kept `U1C` as the default-specification command driven by `U1SET`.
- Limited the visible rigid-conduit choices to the BOQ models 20, 25, 38, and 51 mm while preserving the legacy 32-to-38 mm catalog alias.
- Aggregated different rigid-conduit diameters into separate BOQ rows; repeated labels are summed only within the same diameter.

## Statistics fix

- Prevented the bare length line of a conduit-like multiline MTEXT from leaking into the cable total when the complete annotation cannot be matched.
- Preserved strict catalog-model pairing, so prefixed or altered labels are not accepted as valid BOQ conduit annotations.
- Continued recognizing legacy two-line `diameter + length` conduit labels without counting their length as cable.
