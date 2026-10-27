# Excavation WP3 - Strut installation and deeper excavation

Use a private copy of the WP2 Grasshopper model and GeoHopper 0.7.0.0. Confirm the WP2
one-metre excavation calculates successfully before proceeding. WP2 live calculation
has not yet been explicitly confirmed. Do not interpret automated tests as convergence.

## 1. Extend the soil partition

Keep LeftSoil and RightSoil from WP2 and the same two 10 m plates at x=-4 and +4.
Replace CentreDeep with the following bottom rectangle plus a second excavation slice.
Keep Excavation1 from WP2. All five regions use the same demo MC soil.

| ID | xmin | ymin | xmax | ymax |
|---|---|---|---|---|
| LeftSoil | -15 | -15 | -4 | 0 |
| CentreDeep | -4 | -15 | 4 | -2 |
| Excavation2 | -4 | -2 | 4 | -1 |
| Excavation1 | -4 | -1 | 4 | 0 |
| RightSoil | 4 | -15 | 15 | 0 |

Do not retain the old overlapping CentreDeep polygon. All regions remain active initially.

## 2. Split the contacts at the new excavation level

Each wall now has three segments: Lower (-10 to -2), Middle (-2 to -1), Upper (-1 to 0).
Use both inner and outer Plate Interfaces on each segment: twelve contacts in total.
Use IDs LeftOuterLower, LeftOuterMiddle, LeftOuterUpper, LeftInnerLower,
LeftInnerMiddle, LeftInnerUpper and corresponding Right-prefixed IDs.

Outer contacts retain LeftSoil/RightSoil hosts. Inner Lower contacts use CentreDeep,
Inner Middle use Excavation2, Inner Upper use Excavation1. Keep the native positive/negative
face choices established in WP2; recheck all markers after Build. Same-segment opposite
faces must have opposite booleans and matching extents. Merge all twelve into Model.Interfaces.

## 3. Add the strut definition

Draw one straight line from (-4,-0.5,0) to (4,-0.5,0). It lies above the first excavated
base (y=-1) and connects both walls. Create Anchor Material:

| Input | Value |
|---|---|
| ID | StrutMaterial |
| EA [N] | 2100000000 |
| Spacing [m] | 3 |
| Colour | Choose a visible preview colour |

Create Node-to-Node Anchor:

| Input | Value |
|---|---|
| ID | Strut1 |
| Line | The new horizontal line |
| Anchor material | Anchor Material output |
| Start plate | LeftWall |
| End plate | RightWall |

Length must be 8 m. Merge the Anchor output into Model.Anchors (flatten). These values
are demonstration inputs, not a strut section design. The element is elastic in tension
and compression, with no buckling, capacity or prestress. Do not divide EA by spacing.

## 4. Prepare five full phase snapshots

| Phase | Parent | Active soil | Active plates | Active interfaces | Active anchors | Reset |
|---|---|---|---|---|---|---|
| Initial | Empty | All five | None | None | None | False |
| InstallWalls | Initial | All five | Both | All twelve | None | True |
| Excavate1 | InstallWalls | All except Excavation1 | Both | All except InnerUpper on both walls | None | False |
| InstallStrut | Excavate1 | Same as Excavate1 | Both | Same as Excavate1 | Strut1 | False |
| Excavate2 | InstallStrut | LeftSoil, CentreDeep, RightSoil | Both | All outer contacts + LeftInnerLower + RightInnerLower | Strut1 | False |

Only Initial uses Initial method=K0 procedure. Child method inputs remain at the default;
they are Plastic. Do not reset displacements when installing the strut or deepening the
excavation. Active anchors is a full list, not an incremental instruction.

## 5. Build and inspect before calculation

Retain the verified project marker, connection and private output folder. Retain WP2 mesh
settings initially (0.5 m, one refinement pass along both plates). Build once and inspect:

- Five non-overlapping soil regions and the updated twelve contact features.
- Strut1 is a node-to-node anchor, 8 m long, and meets both walls at y=-0.5.
- Native anchor material: Elastic, EA=2.1e9 N, LSpacing=3 m. No prestress adjustment.
- No anchor active in Initial/InstallWalls/Excavate1; Strut1 active from InstallStrut onward.
- Each excavation removes only its intended soil and inner-face contacts.
- Inspect actual mesh connectivity at both endpoints and native contact-side markers.

Declaring Start/End plate validates coordinates and phase activation; it does not add
rigid links or certify mesh connectivity. Report any native command error with its stage.

## 6. Calculate and review in PLAXIS Output

Use the existing Calculate component with the fresh Built model and matching Connection.
All five phases must report native success. Inspect both excavation stages, wall
horizontal displacement and the axial force in Strut1. Review native force units and
whether the displayed result is per individual anchor; do not assume plate-force units.

There is no Anchor Results component yet. Force extraction to Grasshopper belongs to WP4.
Successful solve status does not establish physical suitability, buckling resistance,
mesh/domain independence or design safety. Deeper cuts may require different modelling.

## 7. Regression and parameter checks

- Change spacing or EA in a private copy, rebuild/recalculate and verify native inputs.
- An inactive named wall with an active Strut1 must be rejected before transfer.
- Moving an endpoint off its declared plate must be rejected rather than snapped.
- Old definitions with Anchors unconnected retain previous behavior; verify the wall demo.
- Save GH/PLAXIS files outside the source repository; no public demo upload is intended.
