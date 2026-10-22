# Contributing

GeoHopper is an early-stage project. Small, focused changes and reproducible bug reports
are welcome once the public repository is available.

## Bug reports

Include GeoHopper, Rhino, Windows and PLAXIS versions, the failing component/operation,
expected behavior and the complete sanitized diagnostic. Remove passwords, private
paths and project data. Do not attach confidential GH or PLAXIS models.

## Changes

1. Create a focused branch from main; coding-agent branches use codex/<topic>.
2. Follow AGENTS.md. Keep code, UI and documentation in English and physical data in SI.
3. Preserve component GUIDs and existing port order. Document any additive ports.
4. Add meaningful regression coverage for behavioral changes and update documentation.
5. Run build.ps1 in the documented Windows developer environment.
6. Describe what changed, why, how it was validated and any untested live behavior.

Do not commit credentials, calculation files, manufacturer binaries or local build outputs.
Build must not operate on an existing PLAXIS model. Interactive validation is a separate,
explicitly scoped activity. Publish locally only while Rhino is closed.

Contributions must be yours to submit and compatible with the repository's MIT License.
Keep third-party attribution and licensing information intact.
