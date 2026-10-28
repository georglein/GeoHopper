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
Live acceptance: maintainer reported that WP1 worked successfully. See WP1-PLATES-TEST.md.

## WP2 - Contacts and excavation

Baseline checkpoint: b0d167a. Branch: codex/wp2-excavation. Candidate: 0.6.0.0.
Implemented: Plate Interface, host-aware activation validation, matching opposite faces,
plate-line mesh refinement, and restricted automatic dry K0 initialization.
Build: passed all 210 automated assertions; 18 icons embedded.
Local installation: 0.6.0.0 installed with Rhino closed. Previous binaries backed up
under artifacts/backups/20261026-230542-134.
Live acceptance: pending.
Follow WP2-EXCAVATION-TEST.md. Do not claim native convergence from protocol tests.
No GitHub push or merge is part of this work package.

## WP3 - Strut and deeper excavation

Baseline checkpoint: 2fea739. Branch: codex/wp3-struts. Candidate: 0.7.0.0.
Implemented: Anchor Material, Node-to-Node Anchor, optional plate attachments,
Model.Anchors and Phase.Active anchors, native transfer/activation and matching icons.
Build: passed all 248 automated assertions; 20 component icons embedded.
Local installation: 0.7.0.0 installed with Rhino closed. Previous binaries backed up
under artifacts/backups/20261027-082529-863. Joint live acceptance: confirmed after 0.7.0.2.
The maintainer subsequently confirmed the strut workflow succeeds after the anchor correction.
Follow WP3-STRUTS-TEST.md. No merge to main or GitHub push is part of this package.

## WP4 - Structural results

Checkpoint: f2a0e8a. Branch: codex/wp4-structural-results. Candidate: 0.8.0.0.
WP3 was confirmed working by the maintainer after the 0.7.0.2 correction.
Implemented Plate Results, Anchor Results and Plate Deformation Preview, with matching
icons, English documentation and regression tests. Build: 289 assertions passed;
23 embedded icons verified. Installed 0.8.0.0 with Rhino closed. Prior binaries backed up
under artifacts/backups/20261028-085141-598.
Joint native acceptance: pending. Follow WP4-STRUCTURAL-RESULTS-TEST.md.
No merge or GitHub push is included.

## Resuming after interruption

Inspect Git status, this file and the latest commit before editing. Existing functionality
must remain compatible. Preserve English documentation and SI units. Private test models
stay outside the repository. Never publish binaries while Rhino runs. Commit each coherent
implementation state and record pending native acceptance rather than claiming it passed.

## WP3 hotfix - staged anchor property (0.7.0.1)

Checkpoint: 9abdb31. Replaced the rejected AdjustPrestress setproperties call with
set GH_Anchor_<ID>.AdjustPrestress <phase> False. Progress diagnostics identify the
anchor and phase before the command. Full build: 250 assertions passed.
Published with Rhino closed; prior release backup: artifacts/backups/20261027-234527-810.
Joint native retest remains pending; keep the GH definition unchanged and rebuild.

## WP3 correction - generated anchor elements (0.7.0.2)

Checkpoint: 19b6eed. Native maintainer diagnostics showed the previous setter targeted
a SourceFeature rather than an anchor cut element. Use the confirmed apply command on
NodeToNodeAnchors once per phase; retain per-source activation. The earlier 0.7.0.1 fix
was insufficient. The replacement command passed the maintainer's native Initial-phase
test; the full GH build/solve remains pending. Build passed all 252 assertions.
Installed 0.7.0.2 with Rhino closed. Previous release backup:
artifacts/backups/20261028-082341-447. No GitHub push.

## WP4 result-list compatibility (0.8.0.1)

Checkpoint: 9cf25b1. Native testing confirmed source/part resolution reaches
GH_Plate_LeftWall_1; the first X array failed the JSON-only decoder. Added the native
listable result path with bounded slicing and seven regression assertions.
Build: all 296 assertions passed. Installed 0.8.0.1 with Rhino closed.
Previous release backup: artifacts/backups/20261028-221814-602.
Native read acceptance remains pending.

## Demo acceptance and source publication

The maintainer reported the 0.8.0.1 demo workflow working in the joint Grasshopper test.
Detailed numerical comparisons and engineering validation are not documented as complete.
The maintainer authorized publishing plugin source, icons, tests and documentation to
GitHub. Private GH/Rhino/PLAXIS models remain excluded; example folders are placeholders.
