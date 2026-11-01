# Handoff: menu planet, quick fixes and a scattering atmosphere

For the next session working on the main menu's planet (`scenes/ui/main_menu.tscn`).
Two jobs, in order: **step 1** (two small fixes) and **step 2** (replace the
atmosphere with single scattering, the "Kerbin" look). Then a performance pass.
Read "Traps" before touching anything.

## Where things stand (commit `616bf72`)

The menu is a `Window` (Node3D) holding:

| Node | What | Files |
|---|---|---|
| `Planet` | `PlanetBackdrop`: heightfield → mesh shell (`ShellEdge` 97) + a height **cube map** (`HeightMapEdge` 768, ~10 m a texel) the colours are cut from per pixel | `scripts/planet/PlanetBackdrop.cs`, `shaders/planet_backdrop.gdshader`, `art/materials/mat_planet_backdrop.tres` |
| `Clouds` | `PlanetLayer` sphere, ray-marched cel clouds between `BaseAboveSurface` 150 and `AboveSurface` 450 | `shaders/planet_clouds.gdshader`, `art/materials/mat_planet_clouds.tres` |
| `CloudMaps` | bakes the weather map each frame (octahedral, 768², SubViewport) → global `planet_cloud_flow`; publishes the noise lattice → global `planet_noise` | `scripts/planet/CloudMaps.cs`, `scripts/planet/NoiseLattice.cs`, `shaders/planet_weather.gdshaderinc`, `shaders/planet_weather_bake.gdshader` |
| `Atmosphere` | `PlanetLayer` sphere, `AboveSurface` 2000: a rim glow from the sight line's closest pass to the centre. **This is what step 2 replaces.** | `shaders/planet_atmosphere.gdshader`, `art/materials/mat_planet_atmosphere.tres` (priority 1) |
| `Stars` | sky sphere pushed to the far plane, draws the sun disc | `shaders/space_stars.gdshader` |
| `Sun` | DirectionalLight3D. `PlanetLayer` pushes its direction to each layer as the instance uniform `sun_direction` (toward the sun = light's +Z) | |
| `WindowCamera` | ~2.7 km above the surface, fov 60, planet radius 6000 m | |

`PlanetLayer` sizes its sphere to `planet radius + AboveSurface` every frame and
sets instance uniforms `planet_radius`, `layer_radius`, `base_height` (if
`BaseAboveSurface` > 0) and `sun_direction`. Reuse it for the new atmosphere.

Done earlier: clouds read textures instead of computing the weather and noise per
sample; puffy domed clouds (`dome`, `billow`, `billow_scale`, `crease_shade`);
horizon dither only on skimming rays; planet noise partly from the lattice
texture; sea depth bands fixed to the sea floor (`shelf_depth` 0.22,
`slope_depth` 0.3), no swell and no swirls.

## What the user asked for, in their words

- White spots in deep water look wrong (they are the "crests").
- "The atmosphere ring is visible through the clouds."
- "The clouds still look weird in the horizon."
- "The ground is too perfect, too straight, too one coloured; it gets stale fast."
- "KSP comes to mind: Kerbin looked absolutely stunning even on my older potato
  pc, the clouds were less drastic, but the atmosphere was top notch."
- Keep the cel style, change the look. 60 fps is the requirement (`0002`).

## Step 1: quick fixes

### 1a. Remove the crest blobs

`shaders/planet_backdrop.gdshader`, sea branch: delete the three lines under
`// Crests:` (`fleck`, `crest`, the `mix` with `foam`) and the `crest_share`
uniform, and update the comment in the `// Sea.` block and the file header that
mention crests/flecks. They are a 70 m noise cut at a threshold, so up close one is
a white teardrop. `swell_speed` then only feeds the `drift` vector; if nothing
else uses `drift`, remove both. Not in the `.tres`, so nothing to clean there.

(Optional later: sun glints, a few pixels wide at any distance, only near the
sun's reflection on the lit side. Not now.)

### 1b. Clouds: puffy, not thin (done differently)

Thinning the layer to 60-200 m was tried and rejected: the clouds went flat.
**The user wants them extremely puffy and comfortable**, cartoon cumulus, so the
deck stays 150-450 m. Done instead:

- The billows are packed round balls (`CloudPuffs`, global `planet_puffs`), not
  folded value noise: clouds heap into cauliflower lobes, each shaded like a ball.
- `billow_gate`: balls heap only over cloudy columns, so clear sky has no
  popcorn. `billow_scale` 3.5 (5 read busy), `billow_middle` 0.35, `puff` 0.3,
  `lit_from` 0.7.
- The storms and jets ride the drift (`planet_weather.gdshaderinc`): a cloud
  keeps its shape as the deck turns. Fixed storms tore every cloud drifting
  through them apart in half a minute (the "timelapse" the user saw). Breathing
  is `storm_breath` 0.08 over `storm_breath_seconds` 600.
- `drift_degrees_per_second` is still 2 (the user asked to keep the rotation);
  that is ~210 m/s at the equator, a lap in 3 min. Offer a slower drift if the
  motion still reads fast.
- The thin crust past the limb is mostly the old atmosphere ring; step 2.

Material parameters change through the bridge's `material` command.

## Step 2: the atmosphere (single scattering): done

Built as below, with these differences (2026-11-01):

- **No depth read.** `hint_depth_texture` alone cost 1.1 ms on Iris Xe (twice
  the scattering). Instead the clouds write stencil 1; `planet_atmosphere`
  (stencil != 1) stops each sight line on the planet's mean sphere or where it
  leaves the air; `planet_atmosphere_clouds` (stencil == 1) stops at the cloud
  deck's top. The light is shared in `planet_air.gdshaderinc`. The cloud pass is
  `PlanetLayer`'s material overlay, made at runtime from `StopShader`, its
  settings copied from the air material every frame (`StopAt` = Clouds).
- **Sun transmittance in closed form** (Schuler's Chapman approximation), not
  4 sun samples; above the horizon it reuses the sample's density.
- **3-8 view samples** by the length of air crossed (`step_share` 0.75).
- **`sunset`** lengthens the sun's path for colouring: a 6 km planet's air is
  too thin edge-on to redden sunlight. Warm tint at the terminator limb is
  still subtle; more `mie` makes it stronger.
- Tuned: `Atmosphere.AboveSurface` 1200, `rayleigh_height` 200 with `rayleigh`
  (2.3e-4, 5.4e-4, 1.32e-3) (same column as the 110 m start, wider halo),
  `mie` 1.6e-3, `mie_height` 70, `density` 0.6, `sun_intensity` 13, `sunset` 6.
- `bands` 0 is saved (smooth); 4 shown to the user for comparison. Banding
  steps one amount (light or dimming, the larger) rounded down, both scaled by
  it; stepping them apart made a grey shell in space.
- Bench (paired, editor at 29 % GPU in the background): GPU 12.0-12.7 ms, mean
  14.9-15.7, against the old atmosphere's 11.3 / 14.2 earlier that day. The 70 s
  sustained check is still to do.

The original plan, for reference:


### Goal

The Kerbin look: the middle of the disc clear and saturated; toward the edge
everything (ground, sea, clouds) fades into blue haze; a glowing limb; warm
colours along the day/night line; clouds near the horizon hazed, not stark white.
No ring painted over the clouds. The user wants to see it **smooth (KSP) and
banded (cel)** side by side before choosing, so build a `bands` setting
(0 = smooth).

### Why the current one cannot do it

It colours a sphere by the sight line's closest pass to the centre. It never
knows where the sight line stops (on the planet, on a cloud), so it cannot haze
what is behind the air, and its bright band at the planet's radius is painted
across the clouds that stand out past the limb.

### Design

Keep the `Atmosphere` node (`PlanetLayer`), give it a new shader that, for every
pixel the sphere covers, works out the scattering along the sight line **up to
the first opaque thing**, read from the depth buffer.

Render state (verify each against Godot 4.7.2 before relying on it):

```
shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_test_disabled, depth_draw_never;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
```

- `cull_front`: draw the sphere's far side, so it covers the screen whether the
  camera is outside or inside the atmosphere.
- `depth_test_disabled`: the far side is behind the planet; it must still run.
- Transparent pass (it is, with blending), so the depth texture holds the
  planet, the clouds (opaque, they write depth) and the stars.
- `blend_premul_alpha`: out = src.rgb + dst × (1 − src.a). Write
  `ALBEDO = in_scattered` and `ALPHA = 1 − mean(transmittance)`. One alpha means
  grey extinction; acceptable. True per-channel extinction would need a
  separate pass; not worth it here.

Distance to the opaque surface (Godot 4.3+ uses reversed Z with Vulkan; depth 0
is the far plane):

```
float depth = textureLod(depth_texture, SCREEN_UV, 0.0).r;
vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
view.xyz /= view.w;
float surface = depth < 1e-6 ? 1e30 : length(view.xyz);   // stars sit at the far plane
```

Ray: camera position and direction in the planet's frame (as the cloud shader
does: centre = `MODEL_MATRIX[3].xyz`, camera from `INV_VIEW_MATRIX`, direction
toward `world_position`). Intersect the atmosphere sphere (`layer_radius`):
`[t0, t1]`; clamp `t1 = min(t1, surface)`. If the ray misses, `discard`.

Scattering (the classic one from GPU Gems 2 ch. 16 / Bruneton, simplified):

- Air density at height h above the planet: Rayleigh `exp(-h / H_R)`, Mie
  `exp(-h / H_M)`.
- View samples: `N = 8` along `[t0, t1]` (midpoints). At each: density, optical
  depth from the camera so far (accumulate), and optical depth toward the sun
  (`M = 4` samples to where the sun ray leaves the atmosphere; zero light if the
  sun ray hits the planet: that is the night side and the terminator).
- `T(x) = exp(-(β_R · od_R + β_M · 1.1 · od_M))` per channel.
- In-scatter += `T(view) · T(sun) · (β_R · ρ_R · P_R(μ) + β_M · ρ_M · P_M(μ)) · ds`,
  with μ = dot(ray, sun), `P_R = 3/(16π)(1+μ²)`, `P_M` Henyey-Greenstein,
  g ≈ 0.76.
- Final: `in_scattered = 1 − exp(−exposure · sun_intensity · inscatter)` (a
  simple tonemap; the menu's environment does not tonemap), transmittance from
  the total view optical depth.

Starting numbers (planet radius R = 6000 m). Real Earth values, scaled by how
much smaller the planet is; tune from here, all as material uniforms (rule 2):

| Uniform | Start | Note |
|---|---|---|
| `Atmosphere.AboveSurface` | 800 | top of the air, 13 % of R (Kerbin ~12 %). Today 2000. |
| `rayleigh_height` | 110 m | H_R |
| `mie_height` | 25 m | H_M |
| `rayleigh` (β_R) | (4.2e-4, 9.8e-4, 2.4e-3) /m | Earth's × ~73 |
| `mie` (β_M) | 1.0e-3 /m | Earth's × ~48 |
| `mie_g` | 0.76 | |
| `sun_intensity` | 20 | |
| `exposure` | 1.0 | |
| `density` | 1.0 | one knob over both β, for "thicker/thinner air" |
| `bands` | 0 | 0 smooth; 3–4 steps the in-scatter's brightness into flat tones |

The banded look: take the in-scatter's luminance, quantise it to `bands` steps
with a soft edge (smoothstep a pixel wide via `fwidth`), keep its hue. Same
for ALPHA. Show the user both.

Things to check when it runs:

- Disc centre clear, limb blue, sunset orange where the terminator meets the
  limb, nothing over the night side.
- Clouds at the limb hazed blue; no line across them.
- The stars and the sun disc are dimmed behind the air near the limb (they are
  "surface" at infinity; the ray is cut by `t1` only).
- From the editor camera too (it can go inside the atmosphere).

Clean-up once it works: the old `planet_atmosphere.gdshader` uniforms
(`inner_colour`, `dusk_*`, `rim_depth` …) all go; write the new header comment in
the house style (what it is, why, what pushes the instance uniforms).

### Cost

Most of the screen, 8 × 4 samples with a few `exp` each. Expect 1–2 ms on Iris
Xe. If it is more: replace the 4 sun samples with a transmittance look-up table
(2D: height × sun angle, baked once in C# at load, since the planet does not
change), or drop to 6 view samples. Thinner clouds (1b) pay part of it back.

## After step 2 (not started)

- **Ground**: per-pixel normal from the height cube (four reads around
  `local_dir`) → toon lit/shaded sides on mountains; then colour variety
  (mottling, wet/dry, darker valleys). The haze from step 2 already breaks up
  the flat look a good deal; judge after it.
- **Performance pass** (below).

## Performance: the state and how to measure

Budget: 60 fps at 1920×1080 on this Iris Xe, worst frame included (`0002`).
Last clean numbers, *before* the octahedral weather map, the height cube and the
sea change (none of those measured cleanly yet):

- `bench.gd` idle: mean 14.7 ms, worst 15.2, 0/409 over 16.7, GPU 12.8 (before
  the puffy clouds).
- With puffy clouds, 70 s sustained: GPU 12.6 → 14.5 as the laptop heats, 1 frame
  over 16.7. Too tight.
- Since then: octahedral map (≈ −1 ms), sea swell and swirls gone (−6 noise
  reads per sea pixel), height cube (+1 read). Unverified.

How to measure so it means something:

1. The editor must not be drawing the menu: switch its tab to a light scene
   (`open res://scenes/ui/contract_card.tscn`), and switch back after. Check with
   `Get-Counter "\GPU Engine(*engtype_3D)\Utilization Percentage"` that nothing
   else is busy (Chrome took 25 % once).
2. Cool 50–60 s between runs; plugged in.
3. `bench.gd -- res://scenes/ui/main_menu.tscn idle` for the official line.
4. A sustained run, because the laptop throttles within a minute: a scratch
   `SceneTree` script that loads the menu, disables vsync, and prints mean/worst
   frame, frames over 16.7 and `RenderingServer.viewport_get_measured_render_time_gpu`
   per 10 s window for 70 s. Accept only if the last windows have no frames over.
5. A/B by swapping `material.shader` at runtime in one process (old, new, old,
   new); separate runs drift with heat.

## Traps (all happened)

- **The editor writes back stale shaders.** It loads a shader when it starts and
  does not see disk edits until its window gets focus. A bridge `save`, or
  `reload` with `save: true`, then wrote its old copy over the new file. After
  editing `.gdshader` files on disk, restart it with `reload {"save": false}`,
  and `grep` the file afterwards to be sure. Material `.tres` files open in the
  editor: change them through the bridge or the inspector, not on disk.
- **`PlanetNoise` is the terrain's gradient noise** (`scripts/planet/PlanetNoise.cs`);
  the shaders' lattice texture is `NoiseLattice`. Read a file before writing it.
- **Globals** `planet_cloud_flow` and `planet_noise` live in `project.godot`
  (`[shader_globals]`); add new ones with the bridge's `shader_global` command,
  never by hand while the editor is open.
- **`PlanetBackdrop` duplicates its material** at build; set per-planet shader
  parameters (like `height_map`) on that copy, never on the shared `.tres`.
- The lattice texture holds the exact hashed values only for coordinates in
  −64…63; past that it repeats (fine for moving or unimportant noise).
- The editor viewport shows a black crescent at the planet's limb from some
  cameras; it is editor-only (not in the game at fov 60 or 70) and not from the
  clouds.
