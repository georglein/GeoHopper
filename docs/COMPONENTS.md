# Component catalogue

Version 0.7.0.0 adds Anchor Material and Node-to-Node Anchor. Model appends optional
Anchors (input 9); Phase appends optional Active anchors (input 9). Existing port positions
and GUIDs are preserved. WP2 calculation and WP3 live acceptance remain pending.
See WP3-STRUTS-TEST.md for the joint test sequence.

Version 0.6.0.0 adds Plate Interface and appends optional Phase.Initial method (input 8)
and Mesh Settings.Refine plates (input 4). Existing defaults, GUIDs and port positions
are preserved. WP1 plate transfer was accepted by the maintainer; WP2 live acceptance
is pending. See WP2-EXCAVATION-TEST.md.

Version 0.5.0.0 adds Plate Material and Plate. Model appends optional Plates (input 8);
Phase appends optional Active plates (input 7). All existing GUIDs, ports and outputs
retain their order. The maintainer reported successful WP1 interactive testing.

Version 0.4.0.1 adds the supplied component icons. They are embedded in the plugin;
no external graphics folder is required at runtime. GUIDs, ports and categories are unchanged.

Compatibility fix 0.3.0.2: Build resolves staged SourceFeature handles after meshing
before applying phase activations. No changes to Grasshopper wiring are required.

Compatibility fix 0.3.0.1: Interface exports InterfaceStrengthDetermination=Manual
before Rinter, matching PLAXIS 2025.1. Existing ports and component identities are unchanged.

All components are under the **GeoHopper** tab. Material, Region and Model components
only produce definitions and previews. **Build Model** prepares the model; **Calculate**
runs its solver phases. Both are explicit actions.
Numeric material values are SI; geometry coordinates follow the Rhino document units.
Definitions are transient: keep their upstream components connected rather than
internalizing them into persistent generic parameters.

## GeoHopper Connection

Category: Connection. Nickname: GH Connect.
Existing input/output order and GUID are preserved; Connection is appended as output 4.

| Input | Default | Meaning |
|---|---|---|
| Host | localhost | Loopback only |
| Port | 10000 | PLAXIS Input scripting port, 1 to 65535 |
| Password | empty | Server password, 4 to 56 UTF-8 bytes |
| Connect | false | Button; read title once per false-to-true transition |

| Output | Meaning |
|---|---|
| Connected | Last read for the current settings succeeded |
| Info | Status or Project.Title |
| Error | English diagnostic |
| Connection | Validated local endpoint settings for Build Model |

Connection settings are available independently of the read button. Build performs its
own current-project check. Connected is not a continuous health check.
Two read requests each have a three-second timeout. Input changes invalidate old results.
Passwords are not serialized by GeoHopper; a connected GH text panel may still save them.
GUID: 5d6e1399-4f93-4f67-a7f7-23e4b7004dd2.

## Material - Linear Elastic

Category: Materials. Nickname: Elastic.
Creates a non-porous continuum material, suitable for an initial concrete representation.
It does not create a plate element or check concrete strength.

| Input | Default | Contract |
|---|---|---|
| ID | Concrete | Stable material ID |
| E [Pa] | 30000000000 | Young's modulus; positive |
| Poisson ratio | 0.2 | Dimensionless; 0 to 0.499 |
| Unit weight [N/m3] | 24000 | Nonnegative unit weight |
| Colour | Grey | Rhino preview only; no mechanical effect |

Output: Material. Only gammaUnsat is sent as the unit weight. No gammaSat assignment
is sent: PLAXIS treats this property as read-only for the non-porous material.
GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed01.

## Material - Mohr-Coulomb

Category: Materials. Nickname: MC Soil.
Creates a drained Mohr-Coulomb continuum material.

| Input | Default | Contract |
|---|---|---|
| ID | Soil | Stable material ID |
| E [Pa] | 20000000 | Young's modulus; positive |
| Poisson ratio | 0.3 | Dimensionless; 0 to 0.499 |
| Unit weight [N/m3] | 18000 | Unsaturated unit weight |
| Saturated weight [N/m3] | 20000 | At least the unsaturated weight |
| Cohesion [Pa] | 5000 | Nonnegative effective cohesion |
| Friction [rad] | pi/6 | About 0.5235987756 rad = 30 degrees |
| Dilatancy [rad] | 0 | Between zero and friction angle |
| Colour | Brown | Rhino preview only |

Output: Material. The adapter converts angles from radians to degrees for PLAXIS.
Defaults are demonstration values, not a validated site-specific material model.
Implementation bounds: E 1e-9 to 1e15 Pa; unit weights 0 to 1e9 N/m3;
cohesion 0 to 1e12 Pa; friction below pi/2 minus 1e-5 radians.
These broad input limits are not engineering suitability checks.
GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed02.

## Region

Category: Geometry. Nickname: Region.

| Input | Meaning |
|---|---|
| ID | Unique region ID; default Region |
| Contour | Closed straight-edge polyline in World XY, Z=0, in Rhino document units |
| Material | One GeoHopper Material output |

| Output | Meaning |
|---|---|
| Region | Immutable geometry/material definition in metres |
| Area [m2] | Cross-sectional area; numerically volume per metre of wall length |
| Colour | Material preview colour |

A shaded mesh is drawn automatically in Rhino; no Boundary Surfaces or Custom Preview
component is required. Upstream geometry previews can be hidden to avoid overlapping
displays. Selection uses Grasshopper's selection highlight; deselect to see material colour.
Colour is not currently transferred to PLAXIS or baked into Rhino objects.

Accepts 3 to 200 vertices, clockwise or counter-clockwise, including concave contours.
Rejects holes, curved edges, repeated vertices, self-intersections and backtracking edges.
Polygonize curves explicitly before connecting them.
Coordinates are limited to +/- 1e6 m. Vertex/planarity tolerance is 1e-7 m;
area must be at least 1e-10 m2. Use local coordinates suitable for geotechnical models.
None/custom Rhino unit systems are rejected. Use Top view for this XY section.
GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed03.

## Model

Category: Model. Nickname: Model.

| Input | Default | Meaning |
|---|---|---|
| ID | Model | Stable model identifier |
| Regions | required | One flattened list of all regions |
| Margin [m] | 0 | Extra contour bounds; must be zero for analysis |
| Interfaces | optional | Flattened Interface definitions |
| Loads | optional | Flattened Line Load definitions |
| Phases | optional | Flattened Phase definitions |
| Mesh | optional | Mesh Settings definition |
| Plates | optional | Flattened Plate definitions; geometry-only builds are allowed |
| Anchors | optional | Flattened Node-to-Node Anchor definitions |

The last four ports are appended in 0.3.0.0. With all disconnected, WP1 behavior remains.
Any analysis input requires Mesh Settings and 2 to 30 phases. Analysis phases must have
exactly one initial phase, valid parents and no cycle. Model topologically sorts them.
Contact boundaries and load hosts are checked geometrically before transfer.

Outputs: Model and Info, including the required PLAXIS project title.
IDs must begin with a letter and contain at most 40 ASCII letters, digits or underscores.
Region IDs must be unique, case-insensitively. Materials sharing an ID must have identical
definitions, including colour; shared definitions are created only once.
Supports 1 to 100 regions. Interior overlap is rejected; shared boundaries and point
contacts are permitted. Do not place a wall inside an uncut soil rectangle.
Partition the soil around the wall into non-overlapping polygons.

WP1 fixes the analysis type to plane strain and the element type to 15-Noded.
Geometry validation does not establish a physically valid calculation model.
GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed04.

## Build Model

Category: Model. Nickname: Build.

| Input | Default | Meaning |
|---|---|---|
| Model | required | One Model output |
| Connection | required | Connection settings output |
| Folder | Documents/GeoHopper/Runs | Absolute folder for models and backups |
| Build | false | Button; one transfer on its rising edge |

| Output | Meaning |
|---|---|
| Success | True only for a completed transfer with the current inputs |
| Info | Progress or semantic region/material mapping |
| Project file | Saved completed model; empty after failure or changed inputs |
| Backup file | Successfully saved pre-build model, if available |
| Error | Failure diagnostic |
| Built model | Appended transient receipt for Calculate; empty without an analysis |

Before the first build, deliberately open a new dedicated PLAXIS Input project and set
its **Title** to **GeoHopper:<Model ID>**, e.g. GeoHopper:WallDemo. Do not mark a valuable
existing project: the marker explicitly designates a project for replacement.
Use the command line below in that dedicated project if needed:

~~~text
setproperties Project "Title" "GeoHopper:WallDemo"
~~~

The builder reads this title before writing. A different or empty title is refused.
For an accepted project, it saves a uniquely named pre-build backup, starts a new project,
sets units to m/N/s, creates materials/polygons, assigns materials, adds stable names
GH_R_<region ID> and GH_P_<region ID>, and saves a unique completed model.

With analysis inputs, Build also creates contacts/loads, generates the mesh and prepares
all dry phases before the final save. It does not run the solver. A contact creates an
additional material copy named GH_Contact_<ID>.

Each press rebuilds the entire managed model. Manual edits are retained in the backup,
but are not merged into the new model. Do not edit PLAXIS during a build.
The target check is a local workflow guard, not a cross-application lock.
Only one GeoHopper build or calculation per local port runs at a time in this Rhino process.
Requests have a 60-second timeout; writes are never retried automatically.
Version 0.2.0.1 reads native command results from their feedback envelope and reports
malformed replies separately from actual PLAXIS command failures.
Version 0.2.0.2 accepts non-JSON environment acknowledgements and includes the failing
build stage in Error. Share the full Error text when diagnosing a failed transfer.
After a failed build, a partial model may remain open. Inspect PLAXIS and the backup
before retrying. Success means transfer/save completed, not that an FE analysis ran.

Changing inputs clears the visible old success and paths. Changes made during a build
do not alter its captured input snapshot. Press Build again after it finishes.
Connect one model and one connection, not a data tree of multiple jobs.
GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed05.

## Interface

Category: Geometry. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed06.

| Input | Default / contract |
|---|---|
| ID | Contact |
| Line | Required open straight World-XY curve, in Rhino units |
| Region A | Wall; first adjacent region ID |
| Region B | BaseSoil; second adjacent region ID |
| Soil material | Required drained MC Material |
| Positive side | True; False selects a negative interface |
| Rinter | 0.67; range 0.01 to 1 |

Output: Interface. The segment must lie on one straight boundary edge of both regions.
Overlapping contact segments are rejected. Region A/B do not determine the interface side.
Inspect the native side markers: coincident geometry may retain an existing line direction.
A custom contact material copies the supplied soil properties, sets InterfaceStrengthDetermination
to Manual, Rinter to the input, TensionCutOff=True, TensileStrength=0 and GapClosure=True.
Its source soil is unchanged. This release does not model hydraulic contact properties.

## Line Load

Category: Loads. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed07.

| Input | Default / contract |
|---|---|
| ID | Surcharge |
| Line | Required open straight World-XY boundary curve, Rhino units |
| Region | Backfill; host region ID |
| qx [N/m] | 0; signed global horizontal line force |
| qy [N/m] | -10000; signed global vertical line force |

Output: Load. Uniform distribution, on a one-metre out-of-plane strip.
Each component is bounded to +/- 1e12 N/m. Negative qy is downward.
Pressure in Pa has the same numerical value only for the specified one-metre strip.
The segment must lie on its host boundary. Overlapping load segments are rejected.
A phase activates the load only if the host region is active.

## Phase

Category: Analysis. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed08.

| Input | Default / contract |
|---|---|
| ID | Initial |
| Parent | Empty for the one initial gravity phase; otherwise a phase ID |
| Active regions | Required full list of active region IDs |
| Active interfaces | Optional full list of active contact IDs |
| Active loads | Optional full list of active load IDs |
| Reset displacements | False; explicitly resets at the start of a non-initial phase |
| Active plates | Optional full list of plate IDs; omitted plates are inactive |
| Initial method | Gravity loading (default) or K0 procedure; initial phase only |
| Active anchors | Optional full list of anchor IDs; omitted anchors are inactive |

Outputs: Phase and ID. Use the ID output as a later phase's Parent.
Lists are complete snapshots; omitted objects are inactive. They are not incremental lists.
The initial phase permits no contacts, loads, plates, anchors or displacement reset. It uses the selected Initial method;
all later phases use Plastic. Region contacts require both adjacent regions. Plate contacts
require their named soil region and plate active; omit contacts facing excavated soil.
Every phase has dry soil conditions and standard plane-strain supports (bottom fixed in
both directions; vertical sides fixed horizontally; top free). Margin must be zero.
Reset removes displacement history at phase start, not the inherited stress state.
Groundwater, consolidation, safety and custom boundary controls are not exposed.
K0 is restricted as documented below; leave Initial method at its default on child phases.

## Mesh Settings

Category: Analysis. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed09.

| Input | Default / contract |
|---|---|
| Element size [m] | 0.5; 0.001 to 10000 m, not exceeding model extent |
| Refine regions | Optional region-ID list |
| Refinement passes | 1; integer 0 to 3 |
| Refine plates | Optional plate-ID list; refines their geometric lines before meshing |

Output: Mesh. Uses native meshd with enhanced refinement and absolute metres, independent
of Rhino document units. Each pass requests refine on the selected region polygons.
No refinement is requested when both ID lists are empty. This is not a mesh-convergence check.

## Calculate

Category: Analysis. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed10.

| Input | Default / contract |
|---|---|
| Built model | Required current Build Model receipt |
| Connection | Same endpoint settings used for the build |
| Calculate | Button; rising edge starts one job |
| Open Output | True; opens final phase after all phases succeed |
| Timeout [min] | 30; 1 to 180 minutes per request |

| Output | Meaning |
|---|---|
| Success | Every phase has native CalculationResult=1 and results were saved |
| Info | Current phase/progress, completion or Output-opening notice |
| Phase status | Per-phase OK or failure information |
| Project file | Prepared/calculated file; this path alone does not mean success |
| Error | Failure diagnostic, with phase/stage context |
| Calculated model | Appended in 0.4.0.0; successful solve receipt for Read Results |

Calculates parent-first, stops at the first unsuccessful phase and exposes native LogInfo.
No automatic retries. Output-opening failure does not invalidate a completed calculation;
Info then instructs the user to open Output manually.

The receipt is not serialized. A later accepted build on the same port invalidates it,
including if that later build fails. Calculation verifies latest run, endpoint settings,
project title and saved filename. Do not modify the open model manually between Build and
Calculate: this guard does not detect every edit to the same project.
Changing model inputs clears the current Build output; rebuild before calculating.
A timeout invalidates the receipt; inspect PLAXIS, which may still be calculating,
before starting a new build. The lock is per Rhino process, not cross-application.

## Result Point

Category: Results. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed11.

| Input | Contract |
|---|---|
| ID | Unique measurement ID; default Top |
| Region | Host region ID; default Wall |
| Point | Finite World-XY point, converted from Rhino document units to metres |

Output: Result point. Does not contact PLAXIS. Read Results checks strict polygon
interior and region activation. Boundary points within 1e-7 m are rejected; use an
interior point comfortably clear of contacts. Recompute after changing Rhino units.

## Read Results

Category: Results. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed12.

| Input | Contract |
|---|---|
| Calculated model | Current successful Calculate receipt |
| Connection | Same Input connection used for the analysis |
| Phase | Phase ID; empty selects the last prepared phase |
| Points | Flattened list of 1 to 100 Result Point definitions with unique IDs |
| Read | Button; rising edge starts one read, never a calculation |

| Output | Contract |
|---|---|
| Success | True only if all points and provenance checks succeeded |
| Info | Progress and selected phase |
| Samples | Immutable samples for Chord Rotation |
| IDs | IDs aligned with every numerical list |
| Ux [m] | Horizontal displacement; right positive |
| Uy [m] | Vertical displacement; up positive |
| Magnitude [m] | sqrt(Ux^2+Uy^2) at each point; not a region maximum |
| Settlement [m] | -Uy; down positive |
| Error | Diagnostic; failed reads produce no partial numerical outputs |

Read opens/selects the chosen phase in Output, discovers its server port and uses the
same credentials. No manual Output connection is needed. It verifies Input/Output file,
title, Output metre units and phase identity. Input requests time out after 60 seconds;
Output requests after 30 seconds. Retry explicitly after inspecting a timeout.
Coordinates are original geometry coordinates; the reader uses the phase's final saved
step and unsmoothed continuum interpolation. Concrete regions use Soil.Ux/Uy like other
continuum materials. Reset-displacement history is retained; no baseline subtraction is
performed. New builds or solve attempts invalidate old receipts. Changes to reader inputs
hide previous results. Receipts do not persist across Rhino restarts: Build/Calculate again.
External edits to the same project are not fully detectable; leave PLAXIS unchanged while reading.

## Chord Rotation

Category: Results. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed13.

Inputs: Samples (list), Point A (ID, default Base), Point B (ID, default Top).
Outputs: Rotation [rad], Absolute rotation [rad].
Both IDs must occur exactly once, in the same snapshot, phase and region. Coincident
points and collapsed deformed chords are rejected. The signed angle is
atan2(cross(original chord, deformed chord), dot(original chord, deformed chord)),
positive counterclockwise. This measures two-point orientation change, not a rigid-body
fit, local material rotation or maximum rotation across the whole wall.

## Material Quantity

Category: Results. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed14.

Inputs: Model (validated definition), Material (ID, default Concrete), Length [m]
(default 1; positive, up to 1e6 m).
Outputs: Section area [m2], Volume per length [m3/m], Volume [m3].
Sums all model regions with the selected material, irrespective of phase activation.
For plane-strain extrusion, area and volume per metre are numerically equal; total
volume is area times Length. This pure geometric quantity does not require calculation.
Use the same Model that feeds Build; changing geometry requires Build/Calculate/Read
before comparing the new quantity to updated deformation results. No cost is inferred.

## Later work

Interactive WP3 acceptance, mesh/domain checks and result interpretation precede
optimizer synchronization and additional structural primitives.

Stable library GUID: e3f53ab2-26c7-47ed-ae5c-315c4ea5830c.

## Plate Material

Category: Materials. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed15.

| Input | Default | Meaning |
|---|---|---|
| ID | WallPlateMaterial | Stable plate material ID; distinct from continuum/contact material IDs |
| EA [N/m] | 12000000000 | Positive axial stiffness per metre out of plane |
| EI [N m2/m] | 160000000 | Positive bending stiffness per metre out of plane |
| Poisson ratio | 0.2 | Dimensionless, 0 to 0.499 |
| Weight [N/m/m] | 9600 | Nonnegative weight per plate length per out-of-plane metre |
| Prevent punching | True | Explicit native PLAXIS option; assess its suitability |
| Colour | Grey-blue | Rhino preview only |

Output: Plate material. This is an isotropic elastic `PlateMat`, not a `SoilMat`.
The adapter uses MaterialType=Elastic, Isotropic=True, EA1, EI, StructNu, w and
PreventPunching. EA2 and the equivalent thickness are not independently assigned.
Defaults represent a simple 0.4 m rectangular section with E=30 GPa and gamma=24 kN/m3;
they are illustrative, not a validated design. No strength, orthotropy, prestress or
plastic capacity is exposed. Shared IDs require identical definitions including colour.

## Plate

Category: Structures. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed16.

| Input | Meaning |
|---|---|
| ID | Unique plate ID, default WallPlate |
| Line | One open straight line in World XY, Z=0, in Rhino document units |
| Plate material | Plate Material output; continuum materials are rejected |

Outputs: Plate, ID (for Phase Active plates), Length [m]. The line preview uses the
material colour; selection uses green. Hide the upstream line preview if it overlaps.
The line is a structural reference line, not a rendered thickness or concrete volume.

Connect all plates as a flattened list to Model.Plates. Up to 100 plates are supported.
IDs are case-insensitively unique. Collinear overlap is rejected; meeting endpoints and
crossing lines are allowed, subject to native meshing. Endpoints must be within the
model contour. This bounds check does not prove soil support or structural stability.
A plate does not remove soil, cut a concrete volume out of the region, create an
interface, or establish a hydraulic boundary. Use Plate Interface to define optional segmented soil contacts.

Build creates each shared plate material once, assigns each returned Plate object,
and names it GH_Plate_<ID>. After meshing it resolves staged handles by these names.
Every phase first deactivates all plates and then activates exactly its Active plates.
Install plates in a non-initial phase. Existing phases with no plate input keep all
plates inactive. Existing Read Results/Material Quantity still address continuum regions,
not plate displacements, stiffness-based volumes or structural forces.

See [the WP1 acceptance guide](WP1-PLATES-TEST.md) before using the new workflow.

## Plate Interface

Category: Geometry. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed17.

| Input | Default / meaning |
|---|---|
| ID | PlateContact; unique among all interfaces |
| Plate | Source Plate definition; must match the same ID/definition in Model.Plates |
| Segment | Straight Rhino XY curve on the plate and one soil region boundary |
| Soil region | Adjacent soil region ID |
| Soil material | Drained MC source for a separate interface material |
| Positive side | True; native positive or negative face, inspect in PLAXIS |
| Rinter | 0.67; allowable input 0.01 to 1 |

Outputs: Interface (connect to Model.Interfaces), ID (for Phase.Active interfaces).
Segment direction is normalized to the source plate. This does NOT establish the native
side: coincident pre-existing lines may retain a different native direction. Inspect the
positive/negative markers and ensure they face the named soil BEFORE calculating.
There is no automatic side detection or side-to-region geometric certification.

Split contacts at every soil/excavation boundary. Where both faces overlap, use identical
segment extents, the same plate and opposite native sides. Same-face or partially matching
overlap is rejected. Two-region Interface behavior remains unchanged. Contacts are not
silently auto-activated/deactivated: each Phase specifies the full active-ID list. Keeping
a contact active with its soil removed or plate inactive is rejected before any API writes.
Custom interface material creation retains the existing drained MC/Rinter/gap-closure rules.

## Initial method and K0 scope

Initial method accepts exactly `Gravity loading` (legacy default) or `K0 procedure`.
Set K0 only on the phase whose Parent is empty. All child phases remain Plastic; their
Initial method input must remain the default. The initial phase has no active structures,
interfaces or loads and cannot reset displacements.

The first K0 implementation is dry, drained MC with automatic K0 determination. All soil
regions must initially be active and fill an axis-aligned rectangle without holes/steps.
Each partition must be rectangular; at each elevation material properties must be laterally
uniform. Horizontal layers with different materials are allowed. These deliberately narrow
checks do not certify geological suitability or equilibrium. Slopes, lateral material
changes, non-rectangular partitions and initially excavated regions are rejected in K0 mode.

The builder explicitly sets soil K0Determination=Automatic and initial DeformCalcType to
K0 procedure. Custom K0, overconsolidation history and groundwater controls are not added.
For other geometries retain Gravity loading and assess its applicability independently.

## Anchor Material

Category: Materials. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed18.

| Input | Default | Contract |
|---|---|---|
| ID | StrutMaterial | Unique material ID, distinct from soil/plate/generated contact material IDs |
| EA [N] | 2100000000 | Axial rigidity per individual anchor, positive, finite |
| Spacing [m] | 3 | Out-of-plane distance between anchors; 1e-6 to 1e6 m |
| Colour | Blue-grey | Rhino preview only |

Output: Anchor material. Native AnchorMat with MaterialType=Elastic, EA and LSpacing.
EA is NOT per metre of wall and is NOT a spring stiffness [N/m]. For E=210 GPa and
A=0.01 m2, EA=2.1e9 N. The element spring stiffness is EA/L; PLAXIS applies out-of-plane
spacing separately. Defaults are illustrative, not a selected steel section or design.
This elastic model allows tension and compression; it has no capacity limits, buckling,
prestress, connection slip or bending. It is not a compression-only strut formulation.

## Node-to-Node Anchor

Category: Structures. GUID: 84a71ed2-4b66-4ce5-972f-c35bab71ed19.

| Input | Contract |
|---|---|
| ID | Unique anchor ID; default Strut1 |
| Line | One open straight World-XY curve, in Rhino document units |
| Anchor material | Anchor Material output; other material types are rejected |
| Start plate | Optional plate ID at the line start; blank disables this check |
| End plate | Optional plate ID at the line end; blank disables this check |

Outputs: Anchor, ID (for Phase.Active anchors), Length [m]. Material-coloured line preview.
At most 100 anchors. IDs are case-insensitively unique. Overlapping collinear anchors,
conflicting shared material definitions and endpoints outside the model contour are rejected.
Named attachment plates must exist and contain the corresponding endpoint. The adapter
never snaps points or introduces rigid links. Reversing the line requires reversing the
start/end plate references. If an attachment is declared, that plate must be active in
every phase in which the anchor is active. Blank references allow generic usage but do
not certify support, connectivity or structural stability.

Build creates/deduplicates AnchorMat sets, assigns returned NodeToNodeAnchor objects and
names them GH_Anchor_<ID>. After meshing it re-resolves staged handles. Every phase starts
with all anchors inactive and then activates exactly Active anchors. AdjustPrestress is
explicitly False in every phase. The initial phase cannot activate anchors. Geometry-only
builds without phases remain possible. No anchor forces are returned to Grasshopper yet;
inspect PLAXIS Output until the structural-results package is implemented.
