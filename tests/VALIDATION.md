# Validation scope

The release build runs 289 automated assertions covering geometry/material validation,
analysis phases, encrypted Input/Output protocol handling, sampled results and numerical
metrics. Icon dimensions and embedded resource presence are also checked during build.

Tests run production C# against isolated local protocol doubles, using the encryption
module from the installed PLAXIS Python distribution as an independent codec oracle.
They do not run the PLAXIS FE engine or validate a physical engineering design.

Live development checks have exercised geometry/material transfer, contact/load creation,
meshing and phase calculation in PLAXIS 2D 2025.1. Production C# Output metadata and
sampled displacement reads have also been checked against a solved development model.
The complete new Grasshopper results workflow still requires interactive acceptance.

No private GH/PLAXIS files or numeric results from the development demo are distributed.
Users must independently check their model assumptions, units, contact sides, phase
activation, convergence and mesh/domain sensitivity. Other software versions remain
unverified until tested explicitly.

## Excavation WP1 (0.5.0.0)

The full build passed 177 assertions, including 27 new plate core/transfer checks.
Coverage includes invalid stiffness/materials, duplicate and overlapping plates, contour
bounds, full activation snapshots, material deduplication, staged GUID replacement,
failure propagation and model fingerprint invalidation. All 17 icons are embedded.
The plate line preview, existing GH document port migration, native plate material
property acceptance and actual PLAXIS meshing remain pending interactive acceptance.
See docs/WP1-PLATES-TEST.md. No live server was modified during this implementation.

## Excavation WP2 (0.6.0.0)

The build passed 210 assertions, including 33 new excavation checks. Tests cover
plate/soil contact geometry, matching opposite faces, excavation activation, stale plate
references, initial-state eligibility, K0 command generation, plate-line refinement, and
failure propagation. All 18 component icons are embedded.

These are core and encrypted protocol-double checks. Native positive/negative side mapping,
mesh behavior, K0 property acceptance and physical excavation convergence remain pending
joint live acceptance in docs/WP2-EXCAVATION-TEST.md. No live PLAXIS model was modified.

## Excavation WP3 (0.7.0.0)

The full build passed 248 assertions, including 38 new anchor core/transfer checks.
All 20 component icons are embedded. Existing component GUIDs remain unchanged.
Coverage includes per-anchor stiffness and spacing, material deduplication, endpoint
attachment validation, activation snapshots, staged object resolution, explicit disabling
of prestress adjustment, removal in later phases, and native-command failure propagation.

Native command names and properties were checked against the installed PLAXIS command
reference. Automated tests use protocol doubles; they do not confirm native property
acceptance, element connectivity or physical convergence. Joint WP2 and WP3 calculation
acceptance remains pending. See docs/WP3-STRUTS-TEST.md. No live model was modified.

## Anchor staged-property hotfix (0.7.0.1)

The full build passed 250 assertions. The protocol double now rejects the invalid
setproperties anchor setter and requires a property-level set with phase and value.
Two additional checks cover phase/anchor failure diagnostics and refusal to calculate
a partial build. The setter follows the installed native Python staged-property API.
The isolated native probe could not establish a new server session; native acceptance
of this hotfix remains pending in the user's running PLAXIS instance.

## Anchor cut-element correction (0.7.0.2)

The maintainer's native diagnostics confirmed GH_Anchor_Strut1 is a SourceFeature,
without AdjustPrestress. Native apply NodeToNodeAnchors "setproperties"
"AdjustPrestress" GH_Phase_Initial False returned GH_Anchor_Strut1_1: OK.
This supersedes the unverified property-level setter introduced in 0.7.0.1.

The full build passed 252 assertions. The fixture rejects direct prestress writes on SourceFeatures and models values on
separate generated anchor elements. Checks cover all elements in all six test phases,
one apply per phase, no apply for anchor-free models, and error propagation.
The complete Grasshopper build and calculation remain pending joint acceptance.

## Excavation WP4 (0.8.0.0)

WP3 was confirmed working by the maintainer after 0.7.0.2. WP4 adds 37 assertions:
18 structural core/preview checks and 19 encrypted Input/Output transfer checks.
The full build passes 289 assertions and embeds 23 component icons.
Coverage includes active IDs and phases, independent Output GUIDs, two cut parts,
duplicate nodal rows, signed forces, modern anchor N, strict m/N units, mismatched
arrays, missing values, coordinate mismatch, project switches and stale solve receipts.
Preview tests cover scale zero, displacement-only exaggeration and coincident nodes.

Reference verification used the installed PLAXIS 2D 2025.1 manual sections 9.4.5/9.4.9,
the installed Python wrapper list protocol and official 2024.1 compatibility notes.
No live PLAXIS model was modified. Output source names, list/array envelopes, native
force/displacement values and GH preview remain pending joint acceptance using
[WP4-STRUCTURAL-RESULTS-TEST.md](../docs/WP4-STRUCTURAL-RESULTS-TEST.md).
