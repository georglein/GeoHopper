# Architecture

## Implemented through 0.4.0.0

Native Grasshopper geometry -> Region + Material -> Model -> Build Model -> Calculate -> PLAXIS Input / Output viewer
GeoHopper Connection --------------------------------------------^

GeoHopper executes inside Rhino 7 and talks directly to the encrypted remote scripting
HTTP API. There is no Python backend, exchange file or script to execute in PLAXIS.
HTTP request/response bodies still use JSON as the protocol format.

## Responsibilities

- ConnectionComponent: existing asynchronous title probe, plus transient endpoint settings.
- DefinitionComponents: material editors, Rhino-unit conversion, filled preview and assembly.
- ModelData: immutable, Rhino-independent material, region and model snapshots in SI units.
- Geometry2D: polygon validation, triangulation and positive-area overlap detection.
- BuildComponent: explicit button, progress, input fingerprints and UI scheduling.
- ModelBuilder: project ownership check, backup, reset, command sequencing and saved run paths.
- PlaxisClient: encrypted transport and the public read-only connection probe.
- AnalysisData: immutable contacts, line forces, phase snapshots and mesh controls.
- AnalysisComponents: data editors and straight-curve conversion.
- AnalysisRunner: contact/load/mesh/phase preparation and guarded phase-by-phase solving.
- CalculateComponent: explicit solve button, progress and result-code status.
- ResultsData: immutable measurement requests/samples, provenance and pure derived metrics.
- ResultsReader: verified Input-to-Output handoff, scalar decoding and all-or-nothing reads.
- ResultsComponents: measurement editors, asynchronous reader, chord rotation and quantities.
- VersionInfo: assembly and UI version metadata.

A gravity wall uses ordinary regions and materials. No wall-specific creation command or
model class exists. This foundation can also describe other 2D continuum models.

## Transfer lifecycle

1. Validate definitions locally without contacting PLAXIS.
2. Capture an immutable model/connection/folder snapshot on a rising Build edge.
3. Acquire the in-process lock for that local port.
4. Read Project.Title and require the deliberate GeoHopper:<model ID> marker.
5. Save a uniquely named backup, then create a fresh project.
6. Configure m/N/s, plane strain and 15-Noded elements explicitly.
7. Initialize contour bounds, create materials and non-overlapping polygons.
8. Select returned objects by type; assign each returned Soil GUID its material GUID.
9. Apply semantic region names. If analysis is present, create contacts/loads, mesh,
   topologically sort phases, set full activation states/dry conditions/supports, then save.
10. Report success only after all commands succeed; issue a transient receipt for analysis
    models and release the port lock.
11. Calculate reacquires the same lock, verifies latest receipt, endpoint, title and filename,
    solves phases parent-first and requires CalculationResult=1 for each before proceeding.
12. Save results and optionally open the final phase in Output. A failed phase stops descendants.
13. Issue a calculated-model receipt only after successful save. A new solve attempt
    invalidates the previous solve receipt; a new build invalidates both through its run ID.
14. Read Results validates receipt, Input file/title and phase result code, calls view,
    then connects to its returned Output port using the same local credentials.
15. Verify Output file/title/length unit and resolve the phase by its semantic name.
    Output phase GUIDs are independent; Identification text may include a name suffix.
16. Read Ux/Uy at validated interior points with getsingleresult and smoothing=False.
    Publish all samples together only after every scalar and the final file check pass.

Returned GUIDs are temporary handles, never durable model identities.
A failed native command stops the sequence. No automatic write retries are allowed.
PLAXIS operations are not transactional; after a failure the current project can be partial.
Backups are recovery artifacts, not an automatic rollback mechanism.

## UI and data lifetime

HTTP work runs off the Rhino UI thread. Progress/completion is dispatched to Rhino and
schedules a Grasshopper solution. Buttons do not continuously submit work.
A fingerprint prevents showing old results for changed inputs. The prototype does not
queue optimization jobs, cancel builds or lock out other applications using PLAXIS.
One Rhino process owns its own port-lock registry.

Custom GH data are transient and regenerated from upstream components when a document
solves. They are not a file persistence format. Password panels still belong to the saved
GH document and must be removed before sharing it.

The Region component triangulates only for display and overlap checking. It transfers
the original polygon contour, not the display triangles, to PLAXIS.
Material colours affect Rhino preview only.

## Protocol and validation boundary

Blowfish-CBC, space padding, Base64 and reply-code checks follow the installed PLAXIS
wrapper. Empty or non-JSON HTTP-success acknowledgements are allowed only for the
environment endpoint. JSON responses still require decryption and reply-code validation.
Build errors include their operation stage; decoding failures also identify the endpoint,
Content-Type and decoding stage without exposing the response body.
Command success is checked in addition to HTTP success. Native command entries wrap
success, extrainfo and returnedobjects in a feedback object. Missing feedback is a
protocol error, not evidence that PLAXIS rejected the command (corrected in 0.2.0.1).

Automated tests use production C# with an isolated encrypted API double and the installed
vendor encryption module. This verifies protocol formatting and client sequencing,
not the actual PLAXIS geometry engine or license availability.
See tests/VALIDATION.md for the remaining live acceptance checks.

## Analysis scope and remaining packages

WP2 implements dry small-deformation gravity initialization and plastic construction.
Standard supports are explicit; arbitrary supports and hydraulic states are not implemented.
The selected construction history must be interpreted before comparing displacement values.
The receipt is an in-memory workflow guard, not a detector of all manual PLAXIS edits.

WP3 implements sampled continuum displacements and chord rotation, not a global maximum
or a rigid-body fit. Sampling coordinates refer to original geometry in the current
small-deformation workflow. Boundary points are rejected to avoid contact-side ambiguity.
The quantity component sums all geometric regions using a material ID, independently of
phase activation. It yields volume, not cost. Results need mesh/domain validation before
they can be used as trustworthy optimization objectives.

Output scalar replies observed in PLAXIS 2025.1 use returnedobjects with type JSON and
a numeric json field. Display text is never parsed as a numerical result. Legacy numeric
returnedvalues are accepted; missing, text, multiple or non-finite values are rejected.
This reader opens/selects Output but does not save, calculate or change model settings.
The shared lock only coordinates this Rhino process; external manual changes to the same
project's contents are not all detectable. Do not edit PLAXIS during a read.

No example model files or optimizer integration are distributed. Start the Input server manually; the result reader can open Output for the selected calculated phase.

## Structural plates (0.5.0.0)

PlateData.cs defines immutable, Rhino-independent PlateMaterialData and PlateData.
PlateComponents.cs creates those definitions and draws the Rhino line preview.
ModelData owns an optional plate list independently of AnalysisData so geometry-only
builds remain possible. ModelBuilder creates plate materials/features before meshing;
AnalysisRunner re-resolves GH_Plate_<ID> staged handles and applies complete phase states.

Plate materials and continuum materials have separate data types; invalid cross-wiring
is rejected in Grasshopper before contacting PLAXIS. Model fingerprints include all plate
geometry, material and phase activation inputs. No Python runtime backend is introduced.
Native commands/properties were checked against the installed PLAXIS 2D 2025.1 command
reference (PlateMat, plate and platemat); protocol doubles are not native acceptance.
