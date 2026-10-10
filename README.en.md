# IVAN Plugin Center · Rhino 7/8 Plugin Suite

[简体中文](README.md) | **English** | [日本語](README.ja.md) | [繁體中文](README.zh-TW.md)

A .NET plugin suite for **Rhino 7 / Rhino 8**: **7 parametric modeling plugins** plus a glassmorphism-style **Plugin Center** (installer / launcher).
Every plugin is "one command + one parameter panel + live preview + headless self-test", with **no third-party dependencies** (only RhinoCommon / WinForms).

> UI and interaction follow the in-repo *Design Consistency Spec*: unified glass cards, semantic controls
> (slider + numeric box linked), pick buttons that turn **green when a valid target is picked / red when missing or invalid**,
> three motion tiers (150/200/300 ms), and one linear icon family.

## Download / Install

| Version | Download | Notes |
|---|---|---|
| **v1.1.0** | [**IVAN-CENTER.exe**](https://github.com/IvanXxxxyyuffff/ivan-rhino-plugins/releases/download/v1.1.0/IVAN-CENTER.exe) | Windows x64 installer (2.9 MB, md5 `4a7932421e68dfc91d64618c63f9f648`) |

1. **Close Rhino first**, then run the installer → installs to `%LOCALAPPDATA%\IVAN\plugins` (registers 7 plugins + writes the toolbar)
2. Open Rhino: 7 buttons appear on the toolbar — click one to open its panel
3. Or build from source (see below)

> Rhino itself is not bundled; Rhino 7 or Rhino 8 must be installed. **The plugin UI is currently Simplified Chinese only.**

## Plugins

| # | Plugin | Command | What it does |
|---|--------|---------|--------------|
| 1 | **StripeOnSurface** | `StripeOnSurface` | Generates stripes on surfaces/polysurfaces; ends can be rounded / flush / fully boundary-fitted (inset by edge distance, ends trimmed to the boundary); hard edges can be filleted; width, spacing, angle and edge distance are edited live in real mm |
| 2 | **VapeVolume** | `VapeVolume`, `VapeVolumeWatch` | Computes container/tank capacity and fill volume, displayed live |
| 3 | **HalftoneDots** (Parametric Texture) | `ParametricTexture` | 6 patterns (grid / staggered / hex / concentric rings / spiral / jittered) × 4 shapes (circle / triangle / square / hexagon); rings, spiral and jitter can spread from a picked center; shapes stay undistorted on curved surfaces and tile the whole face |
| 4 | **VoronoiTexture** | `VoronoiTexture` | Voronoi cell relief texture inside a planar boundary: cell centers, cell-wall thickness, gradient object for density; output is either **wireframe** or **mesh**, and "one-click smooth" converts it into a **SubD (subdivision surface)** |
| 5 | **RadialDots** | `RadialDots` | Radially graded dot pattern: 4 arrays × 5 shapes, sized by peak position and falloff; overlapping shapes are boolean-unioned automatically |
| 6 | **MeshFix** | `MeshFix` | One-click fix for "in shaded/rendered mode a complex trimmed surface shows only edges": sets the object's render-mesh maximum aspect ratio from 0 to 6 and rebuilds it. **No panel** — fixes the selection, or scans the whole file when nothing is selected |
| 7 | **DiamondFacet** | `DiamondFacet` | Faceted diamond relief inside a planar/closed-curve boundary: random triangulation + random vertex heights, optional locked boundary; output is either **wireframe only** or **faces**, where each triangle facet becomes its own mesh patch and can be subdivided |
| 8 | **WaterRipple** | `WaterRipple` | Water ripples on a surface / polysurface (treated as one face) / closed planar boundary: three switchable wave modes (**organic, directional bands, concentric rings**) with wavelength, height, wave count, direction, spread and crest shape; **locked boundary** with blend width & smoothness; outputs a mesh, or a **SubD** via one-click smooth (boundary creased so corners stay sharp) |
| 9 | **SurfaceUnify** | `SurfaceUnify` | Turns a complex polysurface (or surface / extrusion / mesh) into **one single open NURBS surface**: the boundary follows the original naked edges exactly, while the interior is fitted by ray-casting along the base-surface normal (grid count, fit strength, smoothness, max snap distance). Inner holes are projected and trimmed. Live preview with max / RMS / boundary deviation readout |

Each plugin ships a self-test command (e.g. `VoronoiSelfTest`, `DiamondFacetSelfTest`) that runs full geometric assertions headlessly.

## Repository layout

```
implementation/
  MODIFIED_FILE/                  plugin sources
    stripe/                       StripeOnSurface + Plugin Center (center: installer / launcher)
    voronoi/  halftone/  radialdots/  meshfix/  diamondfacet/
                                  the other 5 plugins (each with dual-TFM Rhino7 + Rhino8 projects)
    src/VapeVolume/               VapeVolume
    shared/PanelTheme.cs          shared custom controls + motion + icon rendering (byte-identical copies inside each plugin)
    iconmake/                     icon renderer (the whole linear icon family is generated from code, no bitmaps)
    PROJECT-STATE.md              project state / per-round changes / pitfalls (source of truth)
    DESIGN-CONSISTENCY.md         design consistency spec (read before adding a plugin)
    UI-REFACTOR-HANDOFF.md        UI refactor handoff
  *.ps1 / *.py                    one-shot build / install / self-test / audit scripts
index.html / app.js / styles.css / icons/ …   HTML UI prototype (glassmorphism mockup + screenshots)
```

## Build

Requirements: Windows + .NET SDK 7/8 + Rhino 7 or 8 (RhinoCommon comes from NuGet, so Rhino itself is not required to compile).

```bash
# single plugin (dual TFM: Rhino 7 = net48, Rhino 8 = net7.0-windows)
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino8/VoronoiTexture.csproj -c Release
dotnet build implementation/MODIFIED_FILE/voronoi/Rhino7/VoronoiTexture.csproj -c Release

# all plugins + Plugin Center
python -X utf8 implementation/native-gate.py build-panels
```

Artifacts land in each plugin's `out/rhino7|rhino8/` (the `.rhp` can be dragged into Rhino, or installed through the Plugin Center).

## Install / Self-test

```bash
pwsh -File implementation/refresh-payload-and-center.ps1   # collect payload + build the Plugin Center exe
pwsh -File implementation/install-and-verify.ps1           # silent install + assertions (registry / toolbar / payload byte-identical)
pwsh -File implementation/run-native-selftests.ps1         # run all 7 self-tests (hidden window, no popups)
pwsh -File implementation/run-native-selftests.ps1 -Only Voronoi   # single plugin
```

Install location: `%LOCALAPPDATA%\IVAN\plugins\<Plugin>\rh8\<Plugin>.rhp` (Rhino 8) / `rh7\` (Rhino 7).

## Documentation

- [`PROJECT-STATE.md`](implementation/MODIFIED_FILE/PROJECT-STATE.md) — project state, per-plugin behaviour contracts, round-by-round changes and **pitfalls** (geometry APIs, RhinoCommon traps, verification workflow)
- [`DESIGN-CONSISTENCY.md`](implementation/MODIFIED_FILE/DESIGN-CONSISTENCY.md) — pixel-level layout, semantic controls, red/green pick-button rules, icon family, self-test format
- [`UI-REFACTOR-HANDOFF.md`](implementation/MODIFIED_FILE/UI-REFACTOR-HANDOFF.md) — UI refactor handoff

## License

MIT — see [LICENSE](LICENSE).
