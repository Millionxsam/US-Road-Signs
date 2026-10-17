# US Road Signs (Cities: Skylines II mod)

730 freely placeable, MUTCD-style American road signs (365 standard signs, each with a flashing variant). Each sign is a complete assembly:
printed face on a plate with real thickness, bolts, and a galvanized post (or twin posts,
a Type III barricade, a portable stand, or an in-street flexible base).

## Install (no build needed)

1. Close the game.
2. Copy the folder `dist\USRoadSigns` into
   `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Mods\`
   so you end up with `...\Mods\USRoadSigns\USRoadSigns.dll`.
3. Start the game. Local mods in that folder load automatically (check **Options > US Road Signs**
   to confirm it loaded).
4. In a city, open the toolbar menu **US Road Signs** (next to the other service menus) and
   pick a category: Regulatory, Warning, Guide, Highway, Services, School & Pedestrian,
   Construction, Other. Each one also has a **(Flashing)** category next to it.

## Flashing signs

Every category has a **(Flashing)** copy next to it. Each flashing sign has LEDs built into the
border, like LED-enhanced signs (MUTCD 2A.07). The LEDs are real game lights: they flash together
about 55 times a minute, day and night. Their color follows the sign's color scheme:

| LEDs | Signs |
|---|---|
| Amber | Yellow and fluorescent yellow-green warning, school and pedestrian signs |
| Red | Regulatory signs: stop, yield, wrong way, speed limits, turn and lane-use signs, restricted-area, government and military signs |
| Orange | Orange work-zone signs, plus the Type II/III barricades and white closure signs in the Construction category |
| White | Blue service, green guide and brown recreational signs, parking signs, END signs, the ROAD CLOSED barricade, object markers, the crossbuck, the in-street crosswalk sign and the push-button sign |

The LED colors, brightness, flash rate, spacing and size are set under `flashing` in
`data/catalog.json`. A category or sign can force a color with `"flashingColor"`.
Where the LEDs would cover lettering or symbols on a small sign, the face sets `"flashingPadding"`
(inches): the flashing version uses a larger plate with the same artwork centered inside.

## Placing signs

- Signs place anywhere, with no road needed. Rotate them while placing as with any prop.
- Use the object tool's **line / curve** modes to drop rows of copies.
- Vanilla CS2 has no vertical offset for props. Use *Move It* to raise or lower a sign, and
  *Anarchy* if the game refuses placement because a sign overlaps road geometry.

## Options

**Options > US Road Signs**: post style for standard signs (square perforated tube,
U-channel, or round pipe) and mounting bolts on/off. Restart the game after changing these.

## Rebuilding / adding signs

Requires the .NET SDK and Windows (the texture tool uses WPF and the Bahnschrift font).

```
powershell -ExecutionPolicy Bypass -File .\build.ps1            # builds dist\USRoadSigns
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install   # also copies it into the Mods folder
```

To add a sign, you only edit data:

1. `data/faces/*.json`: describe the plate (shape, size in inches, colors, border) and its
   elements (text with FHWA series, symbols, arrows, rectangles, polygons, shields).
2. `data/signs/*.json`: an entry with `id`, `name`, `category`, `panels` (faces stacked top to
   bottom) and `support` (`post`, `u-channel`, `round-pipe`, `twin-square`, `twin-ibeam`,
   `portable-stand`, `barricade`, `flex-post`, with optional `mountHeight`/`spacing` in inches).
3. New pictograms go in `data/symbols/symbols.json` as SVG path data.

## Layout

| Path | Purpose |
|---|---|
| `data/` | Catalog: menu, categories, colors, faces, signs, symbols |
| `src/Shared/Catalog` | Catalog model and loader (shared by mod and tool) |
| `src/Shared/Geometry` | Plate shapes, posts/brackets/bolts mesh generation |
| `src/USRoadSigns` | The mod: entry point, settings, localization |
| `src/USRoadSigns/Runtime` | Texture/surface/mesh asset creation, prefab + menu registration |
| `tools/SignForge` | Renders face textures, material textures and menu icons from `data/` |
| `build.ps1` | One-step build into `dist/USRoadSigns` |
