# Excavation demo work packages

The goal is a dry excavation with two plate walls, one horizontal strut, staged removal
of soil, and structural results returned to Grasshopper. Components remain generic.
The demo is built interactively with the maintainer; no GH/PLAXIS examples are published.

## WP1 - Walls and structural elements

Baseline checkpoint: 41c1348. Branch: codex/wp1-plates. Candidate: 0.5.0.0.
Implementation: Plate Material, Plate, optional Model.Plates and Phase.Active plates,
plate creation/material assignment/staged activation, line preview and embedded icons.
Automated validation: build.ps1 passed all 177 assertions and verified 17 icons.
See tests/VALIDATION.md and artifacts/staging logs.
Local installation: 0.5.0.0 published to dist with Rhino closed; prior binaries backed up
under artifacts/backups/20261022-135356-052. No GitHub push or merge performed.
Live acceptance: pending. Follow WP1-PLATES-TEST.md. Do not merge to main until accepted.

## WP2 - Contacts and excavation

Extend contacts to plate sides and revise activation rules for excavated neighbours.
Add line refinement and appropriate initial-state controls; build/solve the first excavation.

## WP3 - Strut and deeper excavation

Add Anchor Material and Node-to-Node Anchor with spacing and staged activation.
Install the strut after the first excavation and calculate subsequent excavation stages.

## WP4 - Structural results

Add Plate Results and Anchor Results, explicit units, coordinates and phase identity.
Compare native results, visualize wall displacement and rerun parameter variations.

## Resuming after interruption

Inspect Git status, this file and the latest commit before editing. Existing functionality
must remain compatible. Preserve English documentation and SI units. Private test models
stay outside the repository. Never publish binaries while Rhino runs. Commit each coherent
implementation state and record pending native acceptance rather than claiming it passed.
