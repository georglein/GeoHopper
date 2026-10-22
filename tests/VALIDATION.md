# Validation scope

The release build runs 177 automated assertions covering geometry/material validation,
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
