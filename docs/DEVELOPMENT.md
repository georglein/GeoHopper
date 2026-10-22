# Development workflow

## One source tree and one loaded release

Work in this repository's src folder and the accompanying scripts, tests and documentation.
Grasshopper loads GeoHopper/dist through GrasshopperDeveloperSettings.
The conventional Components Folder is for installed releases, not source development.

Register the absolute dist path of your own checkout. Do not add a second copy of the plugin.

## Build

Run .\build.ps1. It:

1. Invalidates the previous candidate manifest.
2. Verifies and extracts the pinned Bouncy Castle package.
3. Compiles source against the installed Rhino 7 / Grasshopper references.
4. Treats compiler warnings as errors and generates GeoHopper.xml from XML comments.
5. Compiles console hosts for model/analysis/results checks and HTTP protocol/transfer tests.
6. Runs core tests and encrypted read/build/calculate/Output tests against temporary loopback servers
   using the installed vendor encryption module. No existing PLAXIS server is contacted.
7. Writes a timestamped manifest with the assembly version and SHA-256 file/source hashes.

Outputs go to artifacts/staging. Build failures do not change dist.
The graphics manifest maps component classes to 24-pixel PNG resources embedded in the
assembly. Build validates dimensions and resource presence. Publication checks the
manifest and PNG hashes as well as C# source hashes; rerun build after changing icons.
Do not run concurrent builds: the staging directory is intentionally shared.
The normal Rhino session may remain open during this step.

## Publish and reload

Save documents, close Rhino, and run .\publish.ps1.

Publishing checks the passed manifest, candidate file integrity and current source hashes.
The script creates a verified backup of the previous deployment in artifacts/backups.
It copies only the release files to dist and removes the backed-up legacy
PlaxisDirectTest.gha to avoid duplicate component registration.
If a copy fails, it attempts to restore the previous deployment files and reports failure.

No application is terminated by the scripts. A running Rhino process blocks publication.
Restart Rhino and open the saved GH definition after a successful update.
Changing sliders or geometry does not require restarting Rhino; changing compiled code does.

Use .\publish.ps1 -ValidateOnly to check a candidate without publishing.
Never add artifacts to Grasshopper's library folders.

## Rollback

1. Save documents and close Rhino.
2. Identify the required dated folder in artifacts/backups.
3. Move the current dist release files to a separate backup outside plugin search paths.
4. Restore all files from the selected backup to dist, keeping dependencies together.
5. Ensure dist contains only one of GeoHopper.gha or the legacy PlaxisDirectTest.gha.
6. Restart Rhino.

A restored prototype may use the old PLAXIS tab and component name; the component GUID
is unchanged. Source history and binary backups serve different purposes.

## Version control

This directory is the standalone Git repository root. Use main for reviewed changes and focused branches for development. Build outputs, binary caches, private models and saved GH definitions are ignored.

Create a small commit after a working milestone. Source changes should include matching
documentation and tests for behavior that can fail meaningfully. Retain the original
third-party license. Use a version tag only for a deliberately reviewed release.

The assembly version is defined once in src/VersionInfo.cs. Increase it for a new
user-facing release and add a CHANGELOG entry. Do not change GUIDs to rename a component.

## Documentation requirements

- All UI text, errors, comments and documentation are English.
- Every class and method explains its responsibility; public C# APIs have XML comments.
- Describe units, valid ranges, failure behavior and side effects for each new component.
- Explain non-obvious protocol and numerical decisions, not every assignment.
- Distinguish implemented functionality from planned contracts.
- Keep architecture, component catalogue, units and examples synchronized with changes.

## Compatibility and data

Keep existing input/output order and stable component identities. Additive changes still
need an explicit compatibility check using an existing GH definition.
Store physical data using the SI contract. Convert at system boundaries once.
Use stable semantic IDs for model objects; do not persist a PLAXIS object's temporary GUID
as a permanent model identity.

Saved GH definitions can contain panel values. Strip passwords before committing examples.
Use the user's live PLAXIS project only for explicitly scoped operations. Connection tests
are read-only; model construction requires the explicit Build action and a deliberately
marked dedicated PLAXIS project. See docs/COMPONENTS.md for the replacement/backup contract.

## Later project tooling

The current scripts use the installed compiler and avoid requiring Visual Studio.
A solution/project file can be introduced when the plugin grows; it must keep the same
staging, testing and publication contract. No Git hosting service is required for local history.
