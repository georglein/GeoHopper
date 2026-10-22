# GeoHopper development rules

- All source documentation, code comments, plugin UI, errors, scripts and project documentation must be in English.
- Use SI units. Follow docs/UNITS.md; never interpret an unlabelled number as kN or kPa.
- Keep the generic PLAXIS adapter independent of retaining-wall examples and Rhino geometry.
- Preserve published component GUIDs, input/output order and data contracts. Document migrations.
- Document every class and method; public C# APIs use XML documentation. Explain assumptions and side effects.
- Never store real PLAXIS passwords in source, tests, logs or tracked GH examples.
- Keep development files here. dist is the only plugin search folder for this project.
- Build and test with build.ps1; publish only with publish.ps1. Never bypass the Rhino-running guard.
- Do not close applications or overwrite user models to complete a build. Publishing may wait for Rhino to close.
- Track only source, scripts, tests, dependency licenses and documentation. Do not commit private GH/PLAXIS files or calculation results.
- Update CHANGELOG.md, the component catalogue and relevant documentation with behavior changes.
- Build the retaining-wall example interactively with the user. Do not create a fake or unvalidated GH example.

- This directory is the standalone repository root. Do not work in the archived OneDrive checkout.
- Example files are withheld for now. Keep placeholder folders only until the maintainer explicitly approves an example.
- Source and project graphics use MIT; retain third-party notices and never redistribute vendor libraries.

## Work-package checkpoints

- Before starting each new work package, inspect Git status and the relevant diff. Commit pending project changes as a checkpoint before making new implementation changes; the maintainer has authorized these local checkpoint commits without asking again.
- Stage files explicitly after review. Never include credentials, private models, generated outputs or unrelated changes in a checkpoint. Do not overwrite or discard existing work.
- If the working tree is already clean, use the existing HEAD commit as the checkpoint; do not create an empty commit. Report the checkpoint commit ID when starting the work package.
- A checkpoint preserves the current state; it does not imply that the state has passed tests. Record known incomplete work or failures honestly in the commit description or handoff.
- Checkpoint commits are local. Do not automatically push to GitHub, publish a release or change the installed plugin as part of this checkpoint rule.

## Component icons

- Every new component must receive an icon following the existing graphics schema: 24 by 24 SVG viewBox, shared outline style, one category accent/tint, and transparent PNG exports at 24, 48 and 96 pixels. Register the component class in graphics/manifest.json and validate/embed the 24-pixel icon during build. Preserve existing icons and do not regenerate an overview graphic.
