# Excavation WP1 - Interactive acceptance

This package validates plate geometry, material assignment and phase activation. It does
not yet model excavation, plate interfaces, struts or structural result extraction.
Use a new private GH document; keep the existing wall demo as a regression example.

## 1. Install and inspect

Run build.ps1, close Rhino, then publish.ps1. Start Rhino 7 with metre units and Top view.
Confirm GeoHopper 0.5.0.0 and find Plate Material (Materials) and Plate (Structures).
Open a copy of the previous wall GH document. Verify its existing wiring, values and
outputs are intact; Model should additionally expose Plates, Phase Active plates.
If a new port is absent, report it before rebuilding components manually.

## 2. Define soil regions without overlap

For an initial geometric scaffold use three adjacent rectangles in World XY:

| ID | xmin | ymin | xmax | ymax |
|---|---|---|---|---|
| LeftSoil | -15 | -15 | -4 | 0 |
| CentreSoil | -4 | -15 | 4 | 0 |
| RightSoil | 4 | -15 | 15 | 0 |

Assign the same Mohr-Coulomb definition to all three. These are demonstration coordinates,
not a verified domain-size recommendation. Later split CentreSoil into excavation regions.

## 3. Define the two walls

Create lines (-4,-10,0) to (-4,0,0), and (4,-10,0) to (4,0,0).
Create one Plate Material with its default SI properties. Connect it to two Plate
components, IDs LeftWall and RightWall. Both Length outputs must be 10 m. Change Colour
and check both line previews. Hide upstream curve previews if necessary.

## 4. Build geometry only

Assemble all regions and plates in Model (flatten both lists), ID ExcavationDemo, Margin=0.
Leave phases and mesh disconnected for the first transfer. In a dedicated empty PLAXIS
project set the title to GeoHopper:ExcavationDemo, connect the local server and use Build.
The project marker designates this project for replacement; never mark a valuable model.
Use a private run folder outside this repository.

In PLAXIS confirm: three soil regions, two vertical plates, each 10 m long, 8 m apart,
one shared PlateMat and correct assignments. With project units N/m/s verify EA1=12e9,
EI=160e6, StructNu=0.2, w=9600, Elastic, Isotropic=True and PreventPunching=True.
The plate reference lines should not replace soil with a thick concrete polygon.

## 5. Check phased activation

Add Mesh Settings and two Phase components:

- Initial: empty Parent, all three soil IDs, empty Active plates.
- InstallWalls: Parent=Initial, all three soil IDs, Active plates=LeftWall and RightWall,
  Reset displacements=True.

Rebuild and inspect the mesh and phases in PLAXIS: no active plates in Initial, both
active in InstallWalls. The phase controls refer to stable IDs, not global object indices.
Do not add excavation or plate contacts yet. Calculation is not required to accept WP1.

## 6. Acceptance checks

- Rebuild once with changed EA: the new native material values must match.
- Both walls share exactly one material set; repeated builds do not accumulate objects.
- In a copy, omit LeftWall from InstallWalls: only RightWall is active there.
- Reject duplicate/overlapping plates and a continuum Material connected to Plate.
- Compare a 10 m line with a 10000 mm line in correctly configured documents; both
  produce Length=10 m. Recompute after any Rhino unit change.
- Rebuild the previous wall demo in its own dedicated project; confirm existing behavior.

Report version, failing stage and sanitized diagnostic if any check fails. WP1 is accepted
only after these live checks; automated protocol tests cannot prove native FE behavior.
