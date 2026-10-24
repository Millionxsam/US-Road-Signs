# US Road Signs (Cities: Skylines II mod)

[Install on Paradox Mods](https://mods.paradoxplaza.com/mods/163735/Windows)

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

| LEDs   | Signs                                                                                                                                                                                             |
| ------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Amber  | Yellow and fluorescent yellow-green warning, school and pedestrian signs                                                                                                                          |
| Red    | Regulatory signs: stop, yield, wrong way, speed limits, turn and lane-use signs, restricted-area, government and military signs                                                                   |
| Orange | Orange work-zone signs, plus the Type II/III barricades and white closure signs in the Construction category                                                                                      |
| White  | Blue service, green guide and brown recreational signs, parking signs, END signs, the ROAD CLOSED barricade, object markers, the crossbuck, the in-street crosswalk sign and the push-button sign |

The LED colors, brightness, flash rate, spacing and size are set under `flashing` in
`data/catalog.json`. A category or sign can force a color with `"flashingColor"`.
Where the LEDs would cover lettering or symbols on a small sign, the face sets `"flashingPadding"`
(inches): the flashing version uses a larger plate with the same artwork centered inside.

## Digital message signs

The **Digital Message Signs** category (first tab of the US Road Signs menu) holds changeable message signs
and arrow boards whose display you set yourself:

| Sign                                    | Structure                                         | Display                       |
| --------------------------------------- | ------------------------------------------------- | ----------------------------- |
| Portable Message Sign (Trailer)         | Solar work-zone trailer, raised mast              | 3 lines x 8 chars (48 x 27)   |
| Compact Message Sign (Trailer)          | Small towable board, about the size of a TV       | 2 lines x 5 chars (32 x 18)   |
| Arrow Board (Trailer)                   | Type C arrow board on a solar trailer             | 28 lamps, 10 patterns         |
| Roadside Message Sign (Pole)            | Single round pole, roof beacons                   | 3 lines x 15 chars (90 x 27)  |
| Overhead Message Sign (Cantilever)      | Pole with a truss arm reaching over the lanes     | 3 lines x 18 chars (108 x 27) |
| Overhead Message Sign (Gantry, 3 Lanes) | Box-truss gantry, 18 m span, walk-in cabinet      | 3 lines x 21 chars (126 x 27) |
| Overhead Message Sign (Gantry, 5 Lanes) | Box-truss gantry, 26 m span, walk-in cabinet      | 3 lines x 21 chars (126 x 27) |

Place one, then click it. The **Message sign** section of the info panel lets you:

- type each line (up to 4 pages that alternate, with an adjustable time per page); a line turns red when it is
  too wide for that sign. `<` `>` `^` `~` draw left, right, up and down arrows;
- pick one of about 60 common messages (only those that fit the selected sign are listed);
- set the alignment, make the text flash, change the LED color (amber, yellow, white, red, green, blue);
- switch the roof beacons on (pole, cantilever and gantry signs);
- on arrow boards, choose the pattern: arrows, sequential arrows and chevrons, caution bar, four corners or off.

Settings are stored per sign in the save game. An unedited sign shows its default message. The LED glow can
be adjusted under **Options > US Road Signs > LED brightness**.

How it works: the structures are ordinary prefabs built in code (`src/Shared/Digital`). Each placed sign gets a
`DigitalSign` component; `DigitalSignRenderSystem` renders the current page from a 5 x 7 LED font into a
texture and draws it in front of the sign's LED window with the game's default lit shader. The info panel
section is `DigitalSignSection` plus the plain JavaScript UI module `src/USRoadSigns/UI/USRoadSigns.mjs`
(no npm build needed).

## Placing signs

- Signs place anywhere, with no road needed. Rotate them while placing as with any prop.
- Use the object tool's **line / curve** modes to drop rows of copies.
- Vanilla CS2 has no vertical offset for props. Use _Move It_ to raise or lower a sign, and
  _Anarchy_ if the game refuses placement because a sign overlaps road geometry.

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

| Path                      | Purpose                                                              |
| ------------------------- | -------------------------------------------------------------------- |
| `data/`                   | Catalog: menu, categories, colors, faces, signs, symbols             |
| `src/Shared/Catalog`      | Catalog model and loader (shared by mod and tool)                    |
| `src/Shared/Geometry`     | Plate shapes, posts/brackets/bolts mesh generation                   |
| `src/USRoadSigns`         | The mod: entry point, settings, localization                         |
| `src/USRoadSigns/Runtime` | Texture/surface/mesh asset creation, prefab + menu registration      |
| `tools/SignForge`         | Renders face textures, material textures and menu icons from `data/` |
| `src/Shared/Digital`      | Digital signs: types, structures, LED font, layout, arrow patterns   |
| `src/USRoadSigns/Digital` | Digital signs: prefabs, per-sign component, renderer, info panel     |
| `src/USRoadSigns/UI`      | UI module for the info panel editor                                  |
| `build.ps1`               | One-step build into `dist/USRoadSigns`                               |
