# Handoff: finishing the main menu

For the next session on the main menu (`scenes/ui/main_menu.tscn`): the planet
seen out of the ship's window, and the menu over it. It replaces
`menu-planet-atmosphere.md` (its steps are done; its measuring method and traps
are carried over below). Read "Traps" before touching anything.

## Where things stand (commit `59f16c1`, 2026-11-01)

The menu is a `Window` (Node3D) holding:

| Node | What | Files |
|---|---|---|
| `Planet` | `PlanetBackdrop`: heightfield → mesh shell (`ShellEdge` 97) + a height **cube map** the colours are cut from per pixel. Radius 6000 m, spins `SpinDegreesPerSecond` 0.1 | `scripts/planet/PlanetBackdrop.cs`, `shaders/planet_backdrop.gdshader`, `art/materials/mat_planet_backdrop.tres` |
| `Clouds` | `PlanetLayer` sphere: ray-marched cel clouds, 150-450 m. Writes stencil 1 | `shaders/planet_clouds.gdshader`, `art/materials/mat_planet_clouds.tres` |
| `CloudMaps` | bakes the weather map (octahedral) → global `planet_cloud_flow`; publishes `planet_noise` (lattice) and `planet_puffs` (packed balls) | `scripts/planet/CloudMaps.cs`, `CloudPuffs.cs`, `NoiseLattice.cs`, `shaders/planet_weather*.gdshader*` |
| `Atmosphere` | `PlanetLayer` sphere, `AboveSurface` 1200: single scattering. Two passes split by the stencil (air over ground/space; air in front of clouds, its overlay made at runtime from `StopShader`) | `shaders/planet_air.gdshaderinc`, `planet_atmosphere.gdshader`, `planet_atmosphere_clouds.gdshader`, `art/materials/mat_planet_atmosphere.tres` |
| `Stars` | sky sphere at the far plane; draws the sun disc, rings, glow and a cross of streaks | `shaders/space_stars.gdshader` |
| `Sun` | DirectionalLight3D; `PlanetLayer` pushes its direction to every layer as `sun_direction`. The user placed it for a sunrise limb | |
| `WindowCamera` | ~2.7 km above the surface | |
| `Menu` | `MenuSystem` (the lab's modular menu, `scripts/ui/menu/`), root screen `resources/ui/menu_main.tres`: **PLAY** and **EXIT** only | `scripts/ui/MainMenu.cs` |

### What was done in the last session

- **Sea:** the white crest flecks are gone.
- **Clouds, extremely puffy** (the user's word; thinning the deck was rejected as
  "flat and sad"): billows are packed round balls (`CloudPuffs`), heaped only
  over cloudy columns (`billow_gate`). Storms and jets ride the drift, so a cloud
  keeps its shape instead of morphing like a timelapse. Drift 0.5 °/s (the user's).
- **Clouds at the limb:** no more lines. Where the sight line slants through the
  deck the march steps are jittered (PCG hash) and the clouds grown a little
  (`horizon_grow` 0.06); where it skims, the steps halve and the balls and finest
  noise fade (`horizon_smooth` 0.8). Lower `horizon_smooth` gives puffier limbs
  and brings lines back around 0.5.
- **Atmosphere:** single scattering (Rayleigh + Mie, Schuler's Chapman in closed
  form, 3-8 samples, count unrounded so no rings). Where a sight line ends is
  computed (planet sphere, cloud deck top), never read from depth (that read
  cost 1.1 ms). Tuned: `dimming` 0.5 (less grey wash), `mie` 8e-4, `mie_g` 0.6,
  `sunset` 10 with `sunset_glow` 1 (the day/night line glows orange instead of
  dull ochre), `shadow_soft` 400 m (the planet's shadow in the air fades; it was a
  hard line across the disc). `bands` 0 (smooth); 3-4 gives cel steps.
- **Editor bridge:** code reloads survive (`CodeUnload`); new `material` command
  sets a material's shader parameters as one undo.

### Known small things

- A fine speckle on a few cloud edges near the limb (the jitter). Accepted.
- Clouds near the day/night line stay blue; they get no sunset colour. Offered
  to the user: tint the cloud shading orange near the terminator.
- The rose haze over the sea near the terminator may read strong; `sunset` and
  `sunset_glow` turn it down.

## What is left, in order

The user's list, with what is known about each. Show the user a screenshot after
every step (`view`, and `bench.gd ... look` for the game camera).

### 1. Land detail and a better look

The user: "the ground is too perfect, too straight, too one coloured; it gets
stale fast." Keep the cel style.

- **Per-pixel normals** from the height cube (four reads around `local_dir`) →
  toon lit/shaded sides on mountains and ridges, so relief reads from orbit.
- **Colour variety:** mottling inside each biome, wet/dry, darker valleys,
  lighter ridges; break the straight bands. The biome colours and `dry_*`
  uniforms are in `planet_backdrop.gdshader`.
- Watch the cost: every extra read is on most of the screen. Bench each step.
- The terrain *shape* stays the generator's for now (see 5).

### 2. Sun lens flare

The streaks are in (`space_stars.gdshader`, `sun_streak_length`); a real lens
flare is not.

**First, the user: the streaks are "way too harsh".** With the sun rising at
the limb they are two hard, bright lines crossing most of the screen. They are
`exp(-|sy| / 0.0025) * exp(-|sx| / sun_streak_length) * 0.8` in the sky shader:
the width (0.0025) is hard-coded, the length is an instance uniform set from the
sun's node, and the strength is a fixed 0.8. Make width and strength settings
too, then shorten, soften and dim them (and consider fading them as the sun
clears the limb) until the user is happy, before adding anything.

In the same screenshot the clouds at the sunrise limb stay dark blue against
the orange air; the cloud dusk tint (see "Known small things") belongs with this
step.

Then the flare itself. Wanted: ghosts along the line from the sun through the screen
centre, drawn only when the sun is on screen and not behind the planet (the sky
shader already hides the sun behind the planet; the flare must too, e.g. test
the sun's ray against the planet sphere, or the clouds' stencil). Cartoon and
flat, matching the cel look, not photographic. A screen-space quad or a
`CanvasLayer` under the menu are both fine; propose before building.

### 3. The largest nest, visible but not clear

Not started. The user asked to see the contract's largest nest on the menu
planet: there, readable as something, but not clear (a hint from orbit, not a
detailed model). Ask the user how it should read (a dark patch, a cluster of
specks, a glow?) and where, before building. Nests are `docs/decisions/0033`,
`0037`, `0038`; the menu planet is a backdrop, not a nest planet, so nothing
from `NestBuilder` should run here.

### 4. The window frame

Only the window and a bit of frame were wanted; there is no frame yet. The menu
is the view out of the ship's window (pitch, "The ship and the menu"; the
cabin hub, `plan.md` step 6, will use the same window). A modelled frame at the
screen's edge, partly in view, toon-inked like the rest. Rule 4 applies: a
`.glb` or `.vox` wrapped in a `.tscn`. Ask the user for the shape (round
porthole, rectangular, bolted?) and whether it comes from Blender or voxels.

### 5. Terrain (later)

The menu planet uses the generator's shape. Sculpting and saved heights are for
later; do not start them in this pass.

### 6. A working menu

The pitch's buttons: **Play, Options, Wishlist on Steam, Join Discord, Send
feedback, Credits, Quit.** Only Play and Exit exist. Entries are data:
`MenuEntry` sub-resources in `resources/ui/menu_main.tres` (`MenuAction`:
Resume, OpenScreen, Back, Toggle, Slider, Custom, Quit). Custom rows come back
through `MainMenu.OnCustom` by `Id`.

- **Options:** an `OpenScreen` to a settings screen; `scripts/ui/GamePauseMenu.cs`
  already builds and applies six settings; share its screen rather than copy it.
- **Wishlist, Discord, Feedback:** `Custom` rows calling `OS.ShellOpen(url)`.
  The URLs do not exist yet: ask the user; keep them in a resource (rule 2),
  not in code.
- **Credits:** a screen from `credits.md`.
- Label "EXIT" today; the pitch says Quit. Ask which.

## Performance: the state and how to measure

Budget: 60 fps at 1920×1080 on this Iris Xe, worst frame included (`0002`).

The machine swung widely all afternoon: the same commit benched 12 ms GPU at one
hour and 22-25 ms at another. The steadiest pair at the end: previous commit GPU
13.99 / mean 17.02 / 115 of 354 frames over 16.7; current 15.35 / 17.06 / 111
over. **The menu is at or over budget and that is not yet settled.** Before
adding land detail, take clean numbers on a cool, idle machine (restart it), and
do the 70 s sustained run. If over: the clouds' march is the biggest cost; the
atmosphere is ~1 ms.

How to measure so it means something:

1. The editor must not be drawing the menu: switch its tab to a light scene
   (`open res://scenes/ui/contract_card.tscn`), switch back after. Check with
   `Get-Counter "\GPU Engine(*engtype_3D)\Utilization Percentage"` that nothing
   else is busy.
2. Cool 50-60 s between runs; plugged in.
3. `bench.gd -- res://scenes/ui/main_menu.tscn idle` for the official line.
4. Sustained: a scratch `SceneTree` script that loads the menu, disables vsync,
   and prints mean/worst, frames over 16.7 and
   `RenderingServer.viewport_get_measured_render_time_gpu` per 10 s window for
   70 s. Accept only if the last windows have no frames over.
5. A/B in one process by swapping `material.shader` at runtime (old, new, old,
   new); separate runs drift with heat. **Never A/B by swapping files on disk
   while the editor is open** (see Traps).

## Traps (all happened)

- **Swapping shader files on disk under an open editor breaks them silently.**
  `git stash` / `pop` of the shaders for paired benches left the editor's clouds
  and atmosphere drawing nothing, with no error. `reload {"save": true}` fixed it.
  After any such swap, reload and `view` before handing back.
- **The editor can write back stale shaders.** After editing `.gdshader` files
  on disk, if the editor has not picked them up, restart with
  `reload {"save": false}` and `grep` the file afterwards. Material `.tres`
  files open in the editor: change them with the bridge `material` command or
  the inspector, not on disk.
- **Globals** (`planet_cloud_flow`, `planet_noise`, `planet_puffs`) live in
  `project.godot` `[shader_globals]`; add new ones with the bridge's
  `shader_global`, never by hand while the editor is open. A shader compiled
  before its global existed turns the planet grey; reload after adding one.
- **`PI` is built into Godot shaders**; redefining it fails.
- **`PlanetNoise` is the terrain's gradient noise**; the shaders' lattice
  texture is `NoiseLattice`. Read a file before writing it.
- **`PlanetBackdrop` duplicates its material** at build; per-planet parameters
  (like `height_map`) go on that copy, never on the shared `.tres`.
- The lattice texture holds exact hashed values only for coordinates in
  −64…63; past that it repeats.
- The editor viewport shows a black crescent at the limb from some cameras;
  editor-only.
- `bench.gd look` takes screenshots: its timings are not valid; use `idle`.
- Commit with a pathspec (`git commit -- <paths>`): the user keeps other work
  staged.
