# Third-party notices

## BouncyCastle.Cryptography 2.6.2

Used for encrypted communication with the PLAXIS scripting API.
Copyright (c) 2000-2025 The Legion of the Bouncy Castle Inc.
Its original license is retained in lib/BouncyCastle.LICENSE.md and packaged alongside
its assembly in binary distributions. The pinned NuGet package is restored and checked
by SHA-256 by scripts/restore-dependencies.ps1. Cached binaries are not committed.

## Rhino / Grasshopper and PLAXIS

These are separately licensed products. GeoHopper builds against locally installed
RhinoCommon, Grasshopper and GH_IO assemblies; they are not included in this repository
or release packages. PLAXIS executables, libraries, manuals, licensing data and its
Python distribution are not redistributed here.

Tests use the encryption module from the developer's installed PLAXIS Python distribution
as a local independent oracle. The module is not copied into this repository or releases.

Product names identify interoperability requirements; no endorsement or affiliation is implied.
