# Preparing a public release

This checkout is a standalone source repository. The initial public history starts from
this curated snapshot; earlier private development history is not imported.
No GitHub remote or public repository is configured by the local preparation process.

## Before the first upload

- Review the MIT copyright attribution and third-party notices.
- Run build.ps1 and inspect the tested version and component changes.
- Ensure git status contains no private models, credentials or local build outputs.
- Keep examples/grasshopper and examples/plaxis as placeholders until examples are reviewed.
- Choose the GitHub account/organization and repository name GeoHopper.
- Create an empty GitHub repository without generated README/license commits, add its
  remote URL, and push main only when publication is explicitly intended.

## Release package

Build and publish locally with Rhino closed. Create a release ZIP from these files only:
GeoHopper.gha, GeoHopper.xml, BouncyCastle.Cryptography.dll, BouncyCastle.LICENSE.md,
LICENSE, THIRD-PARTY-NOTICES.md and INSTALL.md.

Do not include Rhino/Grasshopper/PLAXIS assemblies, local configuration, passwords,
artifacts/backups, GH definitions or PLAXIS model/results files. Publish the first build
as a pre-release, with limitations and live validation status visible. Tag the exact
source commit used to build it; do not move an existing public tag to different code.

Use Issues for reproducible sanitized reports and focused branches/pull requests for
changes. CI can be added later; the current full tests require locally installed Rhino
references and the PLAXIS Python encryption module, which are not redistributed.
