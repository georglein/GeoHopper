# Changelog

## 0.5.0.0 - Excavation WP1: plates (live acceptance pending)

- Add Plate Material and Plate, with isotropic elastic SI properties and line preview.
- Append optional Model.Plates and Phase.Active plates without changing existing GUIDs/ports.
- Create and deduplicate plate materials, assign native plate objects and resolve staged
  plate handles after meshing. Apply complete plate activation snapshots in every phase.
- Reject invalid stiffness, conflicting IDs/materials, overlapping plates and out-of-domain lines.
- Add plate core/transfer regression tests, two icons and an interactive acceptance guide.
- No plate interfaces, excavation-specific changes, anchors or plate result extraction yet.
- Live Grasshopper/PLAXIS transfer and old-document compatibility are pending user acceptance.

## 0.4.0.1 - Component icons

- Embed the supplied transparent 24-pixel icons for all 15 components.
- Resolve icons by component class using graphics/manifest.json at build time.
- Validate icon dimensions/resources during build and asset freshness during publication.
- Presentation only: component GUIDs, ports, categories and analysis behavior are unchanged.

## 0.4.0.0 - Work Package 3: Output results and quantities

- Add Result Point, Read Results, Chord Rotation and Material Quantity components.
- Append Calculated model to Calculate; preserve all existing GUIDs and port positions.
- Resolve Output via the port returned by Input view; verify project path/title, metre
  units and named Output phase rather than reusing Input phase GUIDs.
- Return unsmoothed interpolated Ux/Uy, magnitude and downward-positive settlement at
  strictly interior points of active regions. Failed reads return no partial objectives.
- Compute signed chord orientation change in radians and quantities by material ID.
- Guard reads with current build/solve receipts and the shared port job lock.
- Add numerical and encrypted dual-server tests. Production C# Output access was also
  checked read-only against the user's existing solved wall model.
- No optimizer loop, maximum-field search, rigid-body rotation fit or cost model is included.

## 0.3.0.2 - Staged construction object references

- Resolve soil, interface and load names again after gotostages to obtain SourceFeature
  handles instead of reusing geometry-mode GUIDs for phase activation.
- Report the phase and feature being deactivated in build diagnostics.
- Make the encrypted regression fixture reject geometry handles in staged activation.

## 0.3.0.1 - Current interface material property name

- Replace obsolete InterfaceStrengthMethod with InterfaceStrengthDetermination for
  PLAXIS 2025.1 contact materials; set Manual before Rinter.
- Reject the obsolete name in the encrypted analysis fixture. The revised fixture
  reproduces the failure against 0.3.0.0. Component ports and GUIDs are unchanged.
- Bundled z_legacy_files HTML contains outdated material names; cross-check against
  the vendor's current material-property compatibility table.

## 0.3.0.0 - Work Package 2: dry staged analysis

- Add generic Interface, Line Load, Phase, Mesh Settings and Calculate components.
- Append optional analysis inputs to Model and a transient Built model receipt to Build;
  preserve published GUIDs and original port positions.
- Validate shared contact boundaries, load hosts, activation snapshots, phase parents,
  duplicate features and mesh settings before writing to PLAXIS.
- Create custom contact materials, loads, a mesh and explicit dry gravity/plastic phases.
- Set standard supports explicitly; use full active-ID lists and controlled displacement resets.
- Calculate parent phases before descendants, inspect native phase result codes, stop on
  failure, save results and optionally open the final phase in PLAXIS Output.
- Share the per-port build/solve lock and reject obsolete or mismatched build receipts.
- Add core and encrypted analysis regression tests and an English demo walkthrough.
- Live FE convergence and interactive acceptance remain pending. No result extraction
  or optimizer integration is claimed.

## 0.2.0.3 - Non-porous material unit weight

- Omit gammaSat when creating non-porous linear-elastic materials; the live server
  rejects writes to this read-only property. Preserve gammaUnsat and all mechanical inputs.
- Continue sending distinct unsaturated/saturated weights for drained Mohr-Coulomb soil.
- Add regression checks and model the reported read-only restriction in transfer tests.

## 0.2.0.2 - Environment acknowledgements and actionable diagnostics

- Handle successful empty/non-JSON environment acknowledgements without trying to
  decrypt them, following the installed vendor wrapper's Content-Type handling.
- Keep encrypted JSON parsing and reply-code validation for JSON environment replies
  and all command/data replies. Malformed JSON still stops the build.
- Include the build stage, endpoint and decoding stage in response errors without
  exposing response bodies or credentials.
- Reproduce the plain-text environment failure against 0.2.0.1, then validate the
  correction with empty, plain-text, encrypted and malformed response cases.
- The reported live failure remains subject to verification after plugin reload.

## 0.2.0.1 - Native command feedback fix

- Read command success, diagnostics and returned objects from commands[].feedback,
  matching the installed PLAXIS wrapper. Previously a successful save could be
  reported as rejected because the command envelope was interpreted as feedback.
- Correct the encrypted test server response structure and verify error diagnostics
  and rejection of missing feedback before any project reset.
- Reproduce the failure against the previous binary before testing the correction.

## 0.2.0.0 - Work Package 1: geometry and materials

- Add general linear-elastic and drained Mohr-Coulomb material definitions in SI units.
- Add closed XY polygon regions with automatic material-coloured Rhino previews and area output.
- Validate region topology, shared material identity and non-overlapping model assembly.
- Append transient connection settings to the existing Connection component.
- Add explicit asynchronous Build Model with a project-title guard, pre-build backup,
  fresh project creation, m/N/s configuration, returned-object material assignment and unique run files.
- Preserve existing component/library identities and original connection ports.
- Add core and encrypted transfer tests; document pending live Rhino/PLAXIS acceptance.
- Add an English walkthrough for constructing the gravity-wall example together.
- Meshing, phases, interfaces, calculation and results remain later work packages.

## 0.1.0.0 - GeoHopper development foundation

- Rename the library and component UI to GeoHopper, retaining original component/library GUIDs.
- Translate plugin messages, comments, scripts and documentation into English.
- Add XML documentation for C# types and methods and enforce warning-free compilation.
- Separate candidate builds/tests in artifacts/staging from the loaded release in dist.
- Add checksum-verified dependency restoration, candidate verification and source freshness checks.
- Back up previous releases and reject publishing while Rhino is running.
- Document the SI contract, general component roadmap and interactive example workflow.
- Preserve the direct encrypted read-only PLAXIS connection; no model-writing features added.

## Prototype - confirmed by the user

- Direct C# to PLAXIS Input connection from Grasshopper in Rhino 7.
- Read Project.Title using encrypted HTTP without an external Python process.
- Protocol tests against the installed PLAXIS encryption module.
