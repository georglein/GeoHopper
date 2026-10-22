# SI units and coordinates

WP3 results are always metres and radians, regardless of Rhino display units.
Ux is right-positive, Uy up-positive, Settlement=-Uy down-positive. Chord rotation
is counterclockwise-positive. Magnitude is sampled sqrt(Ux^2+Uy^2), not a field maximum.
Result Point geometry is converted from Rhino units to metres at definition time.
Material Quantity reports m2, m3/m and m3; its Length input is explicitly metres.
Volume per metre is numerically equal to area for plane-strain extrusion.

Canonical data uses SI base/derived units. Numeric physical inputs are already in their
labelled units; only Rhino geometry coordinates are converted from document units.

| Quantity | Canonical unit | Example |
|---|---|---|
| Length, displacement | m | 5 mm = 0.005 m |
| Area | m2 | Section area |
| Volume | m3 | Requires an explicit out-of-plane length |
| Volume per wall length | m3/m | Numerically equal to section area |
| Force | N | 10 kN = 10000 N |
| Line force | N/m | Native line-load components on a one-metre out-of-plane strip |
| Stress, pressure, elastic modulus | Pa = N/m2 | 20 MPa = 20000000 Pa |
| Unit weight | N/m3 | 18 kN/m3 = 18000 N/m3 |
| Mass density | kg/m3 | Not the same as unit weight |
| Time | s | Explicitly configured at the PLAXIS boundary |
| Angle, rotation | rad | pi/6 rad = 30 degrees |
| Poisson ratio, interface factor | 1 | Dimensionless |

## Implemented boundaries

Region reads the active Rhino document units and converts XY coordinates to metres once.
It rejects undefined/custom units and geometry outside World XY, Z=0 (tolerance 1e-7 m).
Rhino X maps to PLAXIS x; Rhino Y maps to PLAXIS vertical y.
Use Top view. There is no arbitrary model-plane input in WP1.
Preview vertices are converted back to Rhino units for correct display.
After changing Rhino document units, recompute the GH definition and check its dimensions.

Material values and Model.Margin do not depend on Rhino document units.
Region.Area always reports m2, including when the Rhino document uses millimetres.

Build explicitly configures PLAXIS UnitLength=m, UnitForce=N, UnitTime=s before creating
materials. E, cohesion and unit weights are therefore transmitted without a prefix change.
Friction/dilatancy angles are converted from radians to degrees at the command boundary.
Do not type 30 into Friction [rad]; use pi/6 or a Degrees-to-Radians conversion.

## Two-dimensional interpretation

The model is plane strain. A concrete section of area A m2 represents A m3 of concrete
per metre of out-of-plane wall length. Total volume needs an explicit wall length.
Area alone is not a completed cost objective.

Line Load uses qx/qy in N/m, matching the installed native LineLoad property reference.
For a one-metre out-of-plane strip, pressure p [Pa] multiplied by 1 m gives q [N/m],
so a 10 kPa surcharge becomes 10000 N/m. Integrated force applies to that strip.
Do not interpret a total wall force as q. Directions are global XY; negative qy is down.
Mesh Settings uses metres directly and calls meshd; it does not use relative coarseness.
WP3 displacement extraction returns metres and verifies Output.UnitLength before reading.
Hydraulic conductivity, if added, will use m/s. Signed loads must retain their direction.

## Live unit acceptance

Before accepting WP1, compare equivalent 2 m by 4 m contours in metre and millimetre Rhino
documents: both must report 8 m2 and produce 2 m by 4 m geometry in PLAXIS.
Inspect transferred material values with PLAXIS configured in N/m:
E=30000000000 Pa and gamma=24000 N/m3 for the default concrete.
The equivalent kN/m display, if manually selected later, is E=30000000 kPa and
gamma=24 kN/m3. A display-unit change is not a change in material stiffness.
The user confirmed the WP1 geometry/material checks after correcting Rhino units and
recomputing GH. The user subsequently accepted WP2 Build/Calculate. WP3 point readings
were verified via C# against the existing Output model; GH wiring acceptance remains pending.

## Plate section inputs (0.5.0.0)

Plate geometry follows Rhino document units; Plate.Length is always metres.
Plate Material physical values are SI regardless of Rhino units:

- EA: N/m out of plane.
- EI: N m2/m out of plane (dimensionally N m).
- w: N/m/m, weight per plate length per out-of-plane metre (dimensionally N/m2).
- Poisson ratio: dimensionless.

For a homogeneous rectangular section of thickness t per metre out of plane, the
simple section inputs are EA=E*t, EI=E*t^3/12, and w=gamma*t. Do not enter E in the
EA input or volumetric gamma in w. These conversions do not establish cracked concrete
stiffness, reinforcement effects, strength or an appropriate constitutive assumption.
The isotropic defaults E=30e9 Pa, t=0.4 m, gamma=24000 N/m3 give EA=12e9 N/m,
EI=160e6 N m2/m, w=9600 N/m/m. PLAXIS receives these values in its m/N/s project units.
