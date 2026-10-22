# Installation

## Requirements

- Windows with Rhino 7 and Grasshopper.
- PLAXIS 2D with an available remote scripting feature; tested with 2025.1.
- Separate valid Rhino and PLAXIS installations/licenses.

There is no runtime Python backend. Python is needed only for the developer test suite.

## Packaged release

These instructions apply once a GeoHopper release ZIP is published.

1. Close Rhino after saving your documents.
2. Download the release ZIP from this project's GitHub Releases page.
3. If Windows blocks the downloaded ZIP, use Properties > Unblock before extraction.
4. Extract the ZIP to a permanent local folder. Keep GeoHopper.gha and
   BouncyCastle.Cryptography.dll together, along with the supplied notices.
5. Start Rhino, open Grasshopper and add that folder through GrasshopperDeveloperSettings.
6. Remove obsolete GeoHopper search-folder entries. Load only one GeoHopper copy.
7. Restart Rhino and find the GeoHopper tab.

Do not register the repository root, artifacts or backup folders. Developers register
only their checkout's dist folder instead; see docs/DEVELOPMENT.md.

## Connect to PLAXIS

Open PLAXIS Input and enable its remote scripting server. Supply its local port and
password to GeoHopper Connection. Use localhost and keep encryption enabled.
Connection is read-only. Build Model performs model changes only on an explicit trigger
and requires the deliberate project title GeoHopper:<model ID>.

For results, Read Results discovers the Output port from Input. No separate Python
script or manually configured Output connection is required by the plugin.

## Credentials and updates

GH panels are saved in GH files. Remove real passwords before sharing any definition.
Keep model backups and calculation results outside the public repository.
To update, save/close Rhino, replace the complete release folder, then restart.
Keep the previous version outside Grasshopper search paths if rollback is needed.
