# Excavation WP2 - Joint Grasshopper and PLAXIS acceptance

This extends the accepted WP1 model. Save a private copy first. Version 0.6.0.0 adds
Plate Interface, Phase.Initial method and Mesh Settings.Refine plates. Inspect the old GH
wiring after loading; existing GUIDs/ports retain their positions. Tests below are live
acceptance, not a validated excavation design. Do not publish private model files.

## 1. Split the centre soil for one shallow excavation

Keep LeftSoil (-15,-15) to (-4,0) and RightSoil (4,-15) to (15,0).
Replace CentreSoil with two non-overlapping rectangles:

| ID | xmin | ymin | xmax | ymax |
|---|---|---|---|---|
| CentreDeep | -4 | -15 | 4 | -1 |
| Excavation1 | -4 | -1 | 4 | 0 |

Assign the same drained MC soil to all four. Keep the two plates from WP1:
LeftWall (-4,-10) to (-4,0); RightWall (4,-10) to (4,0).
No continuum concrete region is needed for the plates.

## 2. Define contact segments on both wall faces

For each wall create a lower segment from y=-10 to -1 and an upper segment from y=-1
to 0, at x=-4 or +4. All points have Z=0. Use eight Plate Interface components:

| ID | Plate | Segment | Soil region |
|---|---|---|---|
| LeftOuterLower | LeftWall | Lower | LeftSoil |
| LeftOuterUpper | LeftWall | Upper | LeftSoil |
| LeftInnerLower | LeftWall | Lower | CentreDeep |
| LeftInnerUpper | LeftWall | Upper | Excavation1 |
| RightOuterLower | RightWall | Lower | RightSoil |
| RightOuterUpper | RightWall | Upper | RightSoil |
| RightInnerLower | RightWall | Lower | CentreDeep |
| RightInnerUpper | RightWall | Upper | Excavation1 |

Connect the relevant Plate definition (not just its ID), the segment, host region ID and
MC material. Start with Rinter=0.67 as a demo value. Opposite faces of each matching
segment must use opposite Positive side values. Native line directions may differ from
Grasshopper direction, so these values need inspection after Build. Do not infer that
positive always means outside, left or right. This is an explicit user-controlled setting.

Merge all eight outputs into Model.Interfaces and flatten. Remove any obsolete CentreSoil
references. Keep Model.ID=ExcavationDemo and Margin=0.

## 3. Configure phases with full activation lists

| Phase | Parent | Active regions | Active plates | Active interfaces | Reset |
|---|---|---|---|---|---|
| Initial | Empty | All four | None | None | False |
| InstallWalls | Initial | All four | LeftWall, RightWall | All eight | True |
| Excavate1 | InstallWalls | LeftSoil, CentreDeep, RightSoil | LeftWall, RightWall | All except LeftInnerUpper and RightInnerUpper | False |

Set Initial.Initial method to the exact text `K0 procedure`. Leave child Initial method
inputs at the default `Gravity loading`: child phases are always Plastic. The initial
model is full, flat and dry, with all soil active. It must satisfy the K0 eligibility checks.
The excavated-side upper contacts are omitted; the outer upper contacts remain active.

## 4. Refine the wall lines and build

Mesh Settings: Element size=0.5 m, Refinement passes=1, Refine plates=[LeftWall,RightWall].
Refine regions may remain empty. Connect all three phases and Mesh to Model.
Use the dedicated PLAXIS project title GeoHopper:ExcavationDemo and the existing private
output folder, then press Build once. Never use a valuable unrelated project.

## 5. Inspect in PLAXIS BEFORE calculating

- Four soil regions, two plates, eight named contact features (mesh subparts may be split).
- Every positive/negative marker must lie on the named soil side. Check lower and upper
  segments on both walls. If a pair is reversed, swap its Positive side booleans in GH
  and rebuild. Do not fix the managed model manually and continue with an old receipt.
- Mesh is locally finer along both walls; no missing wall/contact segments.
- Initial: all soil, no structures/contacts; K0 procedure and automatic material K0.
- InstallWalls: both walls and all contacts active.
- Excavate1: Excavation1 and its two inner upper contacts inactive; everything else as listed.

## 6. Calculate and compare

Use the existing Calculate component with the new Built model receipt and matching
Connection. Press Calculate once. Success requires every phase CalculationResult=1.
Open final Output, inspect the one-metre excavation, wall displacement and deformation
pattern. Check convergence and phase status rather than accepting an image alone.
There is no strut yet; deeper excavation belongs to WP3. Plate results still remain in
PLAXIS Output until WP4. Absolute convergence or a displacement value is not promised.

Compare one run without plate refinement and one with refinement as an initial sensitivity
check; this does not establish full mesh/domain independence. Rebuild before every changed
calculation. Verify the original wall demo still builds/calculates with default gravity
initialization in its own dedicated project. Keep all GH/PLAXIS tests outside the repository.

## Diagnostic checks

An active contact with Excavation1 inactive must be rejected in Model. A misspelled plate
ID in Refine plates must be rejected. Non-horizontal initial topography must be rejected
in the restricted K0 mode. Report version, stage and full sanitized diagnostic on failure.
