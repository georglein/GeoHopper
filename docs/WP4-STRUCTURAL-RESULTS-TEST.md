# WP4 - Structural results acceptance

Use a private copy of the successful WP3 excavation. No geometry or phase changes are
required. Reopen Rhino after installing 0.8.0.0. Build and Calculate again so the new
session has a valid calculated-model receipt. Keep units in metres and the Input server
running. Do not edit PLAXIS during a read.

## 1. Left wall

Add Plate Results (GeoHopper / Results). Connect Calculate's Calculated model receipt
and the same Connection. Set Phase = Excavate2 and Structure ID = LeftWall. Connect a
Button to Read. Press it after Calculate reports success. Output opens automatically.
Wait for Success = True before reading another component.

Inspect Parts, X, Y, Ux, Uy, N, Q and M in panels. All lists share row indices. X/Y and
displacements are metres; N and Q are N/m; M is N m/m. Duplicate coordinates are kept
where native elements/parts share nodes; do not sum or average them automatically.
Compare several rows at matching coordinates and the same phase against PLAXIS Output.
Match native units (N versus kN), local force conventions and nodal reporting settings.

## 2. Deformed wall

Connect Plate Results.Results to Plate Deformation Preview.Results. Start with factor
100. Connect Original and Deformed curves to separate Custom Preview components and
choose grey/purple colours. Disable competing default previews for clarity.
A factor of zero must put both curves on top of each other. The factor changes display
only. Curves are in Rhino document units; numerical outputs remain metres.

Repeat for RightWall with a second Plate Results and preview pair. Read sequentially.
For a phase comparison, use another reader set to Excavate1 and colour its preview
differently. Always label phases; do not mix nodal rows from different reads.

## 3. Strut force

Add Anchor Results. Connect the same Calculated model and Connection, Phase = Excavate2,
Structure ID = Strut1, and a separate Read button. Press after other reads finish.
Anchor force [N] is the native force per single strut. Force per width [N/m] divides by
spacing from the model. Two endpoint rows are not two separate struts.
Compare both native coordinates and forces with PLAXIS Output. Convert N to kN by
1000 only if the native table displays kN. Verify signs and spacing before interpreting.

## 4. Invalidation and regression

Change Phase to Excavate1 for Strut1: it is inactive, so the reader must reject the
request, not return zeros or old forces. Restore Excavate2 and read again.
Change a model parameter (for example anchor EA), rebuild, calculate, then read again.
Old results must disappear when their upstream receipt changes. Native values should
match the new run; no particular monotonic response is assumed.
Keep one copy of the former continuum Read Results workflow and check it still works.

## Acceptance record

Report any complete Error output and whether it occurred while resolving the source,
listing its parts or reading a named result field. The automated fixtures do not replace
this native acceptance, and no live model was modified during WP4 implementation.
Keep GH/PLAXIS examples outside the source repository. No public example is distributed.
