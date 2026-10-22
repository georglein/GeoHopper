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
