# GeoHopper component graphics

Complete set for the 15 current components, using the approved outline style.
Each category has exactly one accent colour and one pale tint of that colour.
Dark outlines and white details are shared neutral colours.

## Files

- `svg/`: 15 editable vector originals, each with a 24 by 24 viewBox.
- `png/24/`: transparent 24 by 24 PNGs for the Grasshopper Icon bitmap property.
- `png/48/` and `png/96/`: larger transparent exports for previews and other uses.
- `overview.png`: all components grouped by category, with enlarged and actual-size samples.
- `manifest.json`: component class, category, palette and file mapping.
- `render.ps1`: rebuilds PNG exports and overview from the SVG originals.

## Category colours

| Category | Accent | Components |
| --- | --- | --- |
| Analysis | `#3977BA` | Phase, Mesh Settings, Calculate |
| Connection | `#178BA4` | GeoHopper Connection |
| Geometry | `#168579` | Region, Interface |
| Loads | `#C87820` | Line Load |
| Material | `#A06439` | Material - Linear Elastic, Material - Mohr-Coulomb |
| Model | `#B64C78` | Model, Build Model |
| Results | `#8059B1` | Result Point, Read Results, Chord Rotation, Material Quantity |

The requested label is **Material**. The current shared MaterialComponent constructor uses **Materials** in the plugin. The manifest preserves both labels so integration can match the current code without renaming a category.

## Editing and export

Edit the SVG originals, then run `./render.ps1` in PowerShell on Windows.
The renderer uses System.Drawing with fourfold supersampling and supports the SVG primitives used here: line, polygon, polyline, circle and rectangle. It validates the category palette and dimensions before export. When changing a category colour, update its palette entry and every SVG in that category together.

The full set replaces the earlier five-icon concept set for future integration.
No plugin source or installed binary is changed by creating or rendering these assets.
Since 0.4.0.1, build.ps1 embeds the PNGs from `png/24/` using the manifest's componentClass
mapping. ComponentIcons supplies these bitmaps to the existing Icon overrides. Run the
normal build/publish workflow after graphics changes; rendering alone does not update the plugin.
The larger files are supplementary; the locally installed Grasshopper API documents a 24 by 24 bitmap for the Icon override.

## Symbol notes

- Phase shows linked construction stages; it does not depict a particular stage count.
- Material icons use a spring and grains/shear plane to distinguish the constitutive models.
- Model shows several regions inside a domain; Build Model adds a transfer action.
- Chord Rotation compares chord directions schematically; the shared drawing point does not imply a fixed physical pivot.
- Material Quantity depicts an extruded section, matching section area and volume output.

The overview is a graphics review sheet, not a screenshot of the running plugin.
