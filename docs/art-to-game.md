# From a model to the game

The one place that says how a 3D model gets into Dill Control: imported,
wrapped, placed, made to break and made to move. The reasoning is in the
decision records named in each step; this is what you do.

| Want | Go to |
|---|---|
| A smooth model: weapon, pickle, organic prop | [A. Blender](#a-blender-glb) |
| A blocky model: mech, robot, building, crate | [B. Voxels](#b-voxels-vox) |
| It breaks when shot | [C. Destructible](#c-destructible) (voxels only) |
| It moves by code | [D. Procedural animation](#d-procedural-animation) |
| It plays keyframed clips | [E. Hand-made animation](#e-hand-made-animation) |
| Both at once | [F. Both](#f-procedural-and-hand-made-together) |
| Then | [G. Place it and bench it](#g-place-it-and-bench-it) |

## Pick the pipeline first

**Only voxel models can be destroyed.** Destruction carves the voxel grid that
the importer stores with the model (0011). A `.glb` has no grid and does not
break. To make a Blender-shaped thing breakable, build it in voxels. A
MagicaVoxel OBJ can be turned back into a `.vox` with
`tools/vox-tools/obj2vox.py`, but an arbitrary Blender mesh cannot.

| | Blender `.glb` | MagicaVoxel `.vox` |
|---|---|---|
| Breaks | no | yes, every one, the same way |
| Colour | UVs on `palette.png`, `mat_palette_toon` | per vertex from its own palette, `mat_voxel_toon` |
| Moves by code | yes, any node | yes, its named parts (`MechWalker` for legs) |
| Keyframed clips | yes, exported actions | no: MagicaVoxel frames are ignored, see E |
| Facing and scale | expected `-Y` to `+Z`, 1 unit = 1 m. **Not yet verified** | verified: `-Y` to `+Z`, size set at import |

Both pipelines follow one rule: **the imported scene is wrapped, never edited**
(CLAUDE.md rule 4). A `.tscn` with the same name instances it and owns every
script, collider and setting, so re-exporting replaces the mesh and leaves your
work in place.

---

## A. Blender (`.glb`)

Reasoning: `docs/decisions/0005`.

### Set up the model

1. **Scale:** Scene Properties, Units: Metric, Unit Scale 1.0. One unit is one
   metre. A standard pickle is about 1.8 m tall; the player's eye is at 1.7 m.
2. **Facing:** the model's front looks along Blender's **-Y**, which is what
   *Front* view (numpad 1) shows. It should arrive in Godot facing `+Z`.
3. **Origin:** at the feet for anything that stands, at the grip for a weapon,
   at the hinge for anything that rotates.
4. **Before export:** Object, Apply, All Transforms. A model with an unapplied
   scale imports at the wrong size and is lit wrongly.

### Colour: the palette

Every model shares one texture, `art/textures/palette.png`: a 256x256 grid of
16x16 px swatches, one colour family per row, light on the left and dark on the
right.

1. Give the model one material named **`palette`**, with `palette.png` as its
   Base Color. Its other Blender settings only affect the preview; Godot
   replaces the material.
2. Select faces, press `U`, choose *Project from View* or *Unwrap*. In the UV
   editor, scale the island right down (`S 0`) and drop it in the middle of a
   swatch. A face's colour is the swatch it sits in.
3. For a gradient across a part, stretch its UVs along a row.

Repainting a swatch recolours every model that uses it. Keep the 16 px grid
when you repaint, or UVs land on the wrong colour.

### Export

*File, Export, glTF 2.0*:

| Setting | Value |
|---|---|
| Format | glTF Binary (`.glb`) |
| Include | Selected Objects (or a collection), not the whole scene |
| Transform | +Y Up (the default) |
| Data, Mesh | Apply Modifiers on; UVs on; Normals on |
| Data, Material | Export |
| Animation | Off, unless the model has actions (see E) |

Save to `art/models/<prefix>_<name>.glb`, for example `art/models/prp_spire_a.glb`,
and keep the `.blend` in `art/_source/` under the same name.

### In Godot

1. Godot imports the `.glb` when its window gets focus. Double-click the file
   to open **Advanced Import Settings**.
2. Under *Materials*, select `palette`, tick *Use External* and point it at
   `res://art/materials/mat_palette_toon.tres`, then reimport. Every model
   mapped this way shares one material, which is what lets thousands of them
   batch.
3. **Wrap it.** Make a new scene whose root is a `Node3D`, or a body if it
   collides. Instance the `.glb` under it and add scripts, hitboxes and
   collision there. Save it as `scenes/<folder>/<same name>.tscn`.
4. **Collision:** add a simple shape in the wrapper, or name a helper mesh in
   Blender with the suffix `-colonly` (exact) or `-convcolonly` (convex,
   cheaper). Godot turns a suffixed mesh into a collider on import.

### The first Blender asset (still open)

Facing and scale have not been verified for Blender. Take something small, such
as a crate or a stand-in pickle, through every step above. Put its wrapper under
`Props/TestAsset` in `scenes/levels/lvl_pipeline_test.tscn`, run the `look`
bench (G) and check `perflogs/look.png`. The model should face the camera (the
camera looks along `-Z`, so a model facing `+Z` faces it), be the size you
modelled and show the swatches you picked. Then replace the two *not yet
verified* lines in `CLAUDE.md`.

---

## B. Voxels (`.vox`)

Reasoning: `docs/decisions/0009` (the importer).

```
 Tablet                 PC, MagicaVoxel             Godot
 ------                 ---------------             -----
 model the shape  --->  split into parts,     --->  reimports on focus
 paint, set glow        name them, mark             set voxel/size
                        hinges and points           wrapper: VoxelBody, scripts,
                        save to art/voxels/          collision; tune the .tres
```

A `.vox` is never exported to anything. Our importer (`addons/vox_importer/`)
reads it directly, so the working file lives in `art/voxels/`, not
`art/_source/`.

### 1. Model it

Use any editor that saves `.vox`. On an Android tablet: **Goxel** (free, has
layers), **Mega Voxels**, or **FunVoxel** (reported limit 64x64x64). You can also
stream MagicaVoxel from the PC. If you sync one folder from the tablet straight
into `art/voxels/` (Syncthing or a cloud folder), a save on the tablet becomes a
reimport in Godot.

- **Scale:** build at whatever voxel count reads well, because size is set at
  import (step 4). Reference heights: player and storage bot 1.8 m, tank 5.6 m,
  field fighter 6.5 m, graybox towers at 0.5 m per voxel.
- **Facing:** the front looks along MagicaVoxel's `-Y` and arrives facing
  Godot's `+Z`. A model that arrives backwards is turned in MagicaVoxel, never
  in the wrapper.
- **Feet on the ground:** leave the model standing on MagicaVoxel's ground
  plane. That plane becomes the game's ground and the wrapper's origin.
- **Colour:** any palette. The colours are written into the mesh, and every
  voxel model shares one material.
- **Light:** select a colour and set its material to **Emit** in the Render tab.
  **Weight** and **Power** set how bright it is. It glows in game at no cost; the
  halo is the level's glow setting. Never add a real light for it: one
  `OmniLight3D` cost about 2 ms of GPU (0009).
- **Palette index 255 is reserved** (the last swatch). Do not paint with it.
- **Voxel count is cost.** Destruction work scales with a model's voxels
  (0015). A building at 0.5 m per voxel breaks as well as a 0.1 m one at a
  fraction of the cost, because carving is sized in metres, not voxels.

### 2. Split it into parts (PC, MagicaVoxel)

Tablet editors do not reliably keep named objects, so do this pass in
MagicaVoxel. **Skip it for a building or prop that does not move: one object is
enough.**

Anything that moves on its own is a separate **object** in the world view
(press Tab).

- **Name every object** in the outliner. The name becomes the node name in
  Godot, and the wrapper finds parts by these names.
- **Mark each hinge** with one voxel in palette index 255 where the part turns.
  It is not drawn, and the part's origin goes there. Put it in the part's own
  empty space, just beyond the part (a thigh's pivot sits one voxel above the
  thigh), so it never leaves a hole. A part without one turns about its centre.
  It is also what a destructible part hangs from (C).
- **Mark points** with a one-voxel object named `@Something`. It draws nothing
  and becomes a `Marker3D` named `Something`.
- Hidden objects and hidden layers are not imported, so keep reference models
  there.

The names a walking mech uses:

| Part | Name | Hinge (index 255 voxel) |
|---|---|---|
| Everything that does not move | `Body` | none |
| Each leg, top to bottom | `ThighFL` `ShinFL` `FootFL` ... | hip, knee, ankle |
| Turret, if any | `Turret` | the ring it turns on |
| Arms, if any | `ArmR` `ArmL` | the shoulder |

Leg suffixes are `FL` `FR` `BL` `BR` for four legs and `L` `R` for two. Left is
the model's own left: MagicaVoxel's `+X` side, seen from the front.

| Marker | What it is |
|---|---|
| `@Seat` | where the rider's eyes go |
| `@Muzzle` | where the main gun's shots leave |
| `@TurretMuzzle`, `@Cannon` | a second gun |

A bought model that arrives as one merged object can be split by region with
the scripts in `tools/vox-tools/`. For your own model, splitting by hand in
MagicaVoxel is quicker.

### 3. Save into `art/voxels/`

Save as `art/voxels/<prefix>_<name>.vox`: `prp_` for props and buildings, `mch_`
for walkers, `vxl_` for anything else. If the model is not ours, add it to
`credits.md` now and keep the download untouched in `art/_source/<Name>/`.

### 4. Size it (Godot)

Select the `.vox`, open the Import dock, set **voxel/size** to the metres one
voxel should be, and reimport. The model's target height is what matters;
height in voxels times voxel size gets you there. The default is 0.1 m.
**voxel/chunk_size** (default 8 voxels) and **voxel/chunk_jitter** (default 2)
bake the chunk graph a building falls by (`docs/decisions/0027`): chunks stay
inside one MagicaVoxel material type, and a full chunk of `chunk_size`³ voxels
heavier than 600 cannot be torn out by the gravity gun. At 0.1 m a chunk is
about 0.8 m; a model on 0.5 m voxels gets 4 m chunks at the same setting.

After changing the importer itself, force a reimport by deleting
`.godot/imported/*.vox-*`, because Godot skips sources that have not changed.

### 5. Wrap it (Godot)

Copy the wrapper nearest to what you are making, point it at the new `.vox` and
re-wire it:

| Like | Copy | Root |
|---|---|---|
| A building or prop that breaks | `scenes/props/prp_graybox_pillar.tscn` | `Node3D` |
| A rideable walker with a turret | `scenes/vehicles/mch_quadruped_tank.tscn` | `CharacterBody3D` (`MechWalker`) |
| A rideable biped with arms | `scenes/vehicles/mch_field_fighter.tscn` | same |
| A follower that carries something | `scenes/vehicles/mch_mobile_storage.tscn` | same |

Save it under the same name in `scenes/props/` or `scenes/vehicles/`.
Collision is the wrapper's job (a few boxes, never the mesh), except on a
static destructible, which makes its own (C).

---

## C. Destructible

Reasoning: `docs/decisions/0011` (one system), `0013` (buildings carry
weight), `0014` (they fall whole), `0015` (the work runs off the frame).

**Every voxel model is destructible in the same way, and you write no damage
code.** What makes a model break is one node in its wrapper: a `Node` named
`Voxels` with `scripts/voxel/VoxelBody.cs`.

| `VoxelBody` export | Set to |
|---|---|
| `Model` | the instanced `.vox` |
| `Stats` | empty for the default (`voxel_destruction_default.tres`); `voxel_destruction_mech.tres` for armoured things; `voxel_destruction_tree.tres` for trees and anything else that hangs off its base |
| `Static` | **on** for a building or prop |
| `Hinged` | for a moving thing: the parts that hang from their pivot voxel |

### A building or prop (static)

```
PrpMyCrate        (Node3D, no collision of its own)
├── Model         (the .vox, instanced)
└── Voxels        (VoxelBody: Model = Model, Static = on)
```

That is the whole wrapper (`prp_graybox_pillar.tscn`). At start the
`VoxelBody` builds collision from the voxels, one body per section, on the
World and Prop layers. The player walks on what is left of it and the grapple
catches it. You get all of this behaviour with nothing else wired:

- a hit takes out a sphere sized in metres from its damage;
- what loses its connection to the ground comes loose as a physics chunk, or as
  dust if it is small;
- a storey that loses most of its support gives way, and what is above falls
  whole, crushes down storey by storey, hangs on a hinge if cut on one side,
  and snaps where it lands;
- chunks crush the horde and settle as rubble that can still be shot.

Do not give the root a collider of its own: it would stay put while the voxels
are blown away.

### A mech or anything with moving parts

The `VoxelBody` sits beside a `MechWalker`, with `Static` off:

1. List every part that should hang from its hinge under **`Hinged`**: legs,
   arms, turret. Parts not listed hold together as their biggest piece.
2. Set the walker's **`Voxels`** to the `VoxelBody` and **`Core`** to the part
   whose loss kills it (usually `Model/Body`). The walker forwards hits to the
   voxels, drops the rest of a leg when a segment goes, and falls apart at
   `BodyDeathFraction` (in its `MechTuning`) or when too few legs are left.
3. Hits land on the `Hull` box and are traced into the voxels by a ray march.
   Animated parts get no colliders of their own.

**Something that moves but is not a walker** (a door, a rolling barrel): a
non-static `VoxelBody` takes hits only through an owner that implements
`IDamageable` and forwards to `Voxels.ApplyDamage(info, this)`, as
`MechWalker.ApplyDamage` does. That owner does not exist yet. Write it as a
small script on the wrapper's root body when the first such prop is needed.

### Tuning how it breaks

Every number is in `resources/voxel/voxel_destruction_default.tres` (carving,
structure, collapse, fracture, loose pieces, budgets), documented field by
field in `VoxelDestructionStats.cs`. Armour (`Armour`, `MinCarveDamage`,
`CarveVolumePerDamage`) is in `voxel_destruction_mech.tres`. Trees use
`voxel_destruction_tree.tres`, which turns the weight check off
(`MinSupportFraction = 0`): a tree's canopy hangs off its trunk, and the
building check cut every tree down at the first hit (0024). **Changing the
default changes every voxel thing in the game, which is intended.** Another
`.tres` breaks "everything breaks the same way" and needs a decision record.

A model's own `.tres` reaches only how that model splits: carving, structure,
collapse, fracture and section size. Loose pieces always use the default, and
so do the per-frame budgets, which are world-wide. Density or settle time in a
model's `.tres` does nothing.

**Anything that hangs off its own base** (a tree, a leaning rock, an arch) fails
the building check before it is ever hit: the check counts a load off the
layer's centre against it, and the first hit anywhere brings it down from
the lowest layer that fails. Give it the tree resource (0024 says how it
fails).

---

## D. Procedural animation

Reasoning: `docs/decisions/0010`. This is the rule for voxel models: a model's
whole animation brief is where it bends.

### A legged walker: `MechWalker`

Nothing to animate. With the parts named and hinged (B2), wire the walker in the
wrapper:

| `MechWalker` export | What goes in it |
|---|---|
| `Tuning` | its own `MechTuning` `.tres` (copy the nearest mech's) |
| `Model` | the instanced `.vox` |
| `Thighs`, `Shins`, `Feet` | the leg parts, in the same order: FL FR BL BR, or R L |
| `Arms` | `ArmR`, `ArmL`, if any |
| `Turret` | the turret part, if any |
| `Mounted` / `MountedOn` | markers that ride a moving part, and that part, pairwise (`Seat` on `Turret`, `Muzzle` on `ArmR`) |
| `Voxels`, `Core` | see C |

Size the `Hull` box to the body. Add a brain: `MechCompanion` (follows, carries
a rider), `MechGunner` (guns, `MechGunStats`) and `MechSlasher` (melee, for
arms). An enemy would be another brain on the same walker.

What you get, with every number in the `MechTuning` `.tres` (no rebuild):

| Motion | Made by |
|---|---|
| Walking in any direction, turning on the spot, stopping | feet that step when the body leaves them behind (`MechWalker`) |
| Knees bending, feet flat on the ground | two-bone IK per leg (`MechLeg`); `KneeBias` sets the bend direction |
| Bob, sink at speed, lean, squash on landing, recoil | a spring on the body |
| Sprint, jump wind-up, jets, legs hanging in the air | the walker |
| Turret tracking, arm swing, arms raised to aim, slash | the walker (`AimArm`, `StrokeArm`) |
| Choosing targets, firing | `MechGunner` |

### Anything else: a spinning radar, a door, a piston

No generic component exists yet. The pattern to follow:

1. Make the moving bit its own named object with a pivot voxel (B2).
2. Add a small script to the wrapper that takes the part as an `[Export]
   Node3D` and sets its `Transform` in `_PhysicsProcess`, starting from the
   rest transform it read in `_Ready`, as `MechWalker` does.
3. Put its numbers (speed, angle, period) in a `[GlobalClass] Resource`
   (CLAUDE.md rule 2).
4. If it is destructible, check that the part still exists before moving it:
   `Voxels.PartOf(part)` is null or `IsEmpty` once it has been shot away.

---

## E. Hand-made animation

### Blender: supported

1. Animate in Blender as actions (object or armature), and export with
   **Animation** on.
2. The `.glb` imports with an `AnimationPlayer` inside it. In the wrapper, hand
   that player to your script as an `[Export]` and play clips by name. Do not
   edit the imported scene.
3. Mind the cost. Skinned, rigged meshes are planned only for the first-person
   hands (M7). Pickles are never clip-animated: thousands of them are drawn as
   one multimesh and bent in the vertex shader (0005). A rigged prop is fine one
   or two at a time; bench it before it appears in numbers.

### Voxels: not part of the pipeline today

MagicaVoxel's animation frames are ignored, and 0005 says that **nothing voxel
is animated by hand**. It can be done: the importer's parts are plain `Node3D`s,
so an `AnimationPlayer` in the wrapper can key `Model/ArmR:rotation` like
any other node. Doing it reverses that rule, though, so write a decision record
first, and keep to F's rule if the model also moves by code.

---

## F. Procedural and hand-made together

**The rule: one writer per node per frame.** `MechWalker` sets every part it
poses from its rest transform in every physics frame. An `AnimationPlayer`
keying the same part is overwritten, or flickers depending on which runs last.
So decide who owns each node:

1. **Split by part (simplest; use it first).** Code owns the parts it moves
   (legs, arms, turret, body bob). Clips own parts the code never touches: a
   hatch, an antenna, a radar dish, a cockpit canopy. Make those separate named
   objects with their own hinges, and leave them out of every list on the
   walker.
2. **Hand-made pose, code-driven playback (for limbs).** When an arm needs an
   authored move (a wave, a reload, a special attack), store the move as data
   (keyed angles in a `Resource`, or a `Curve`) and feed it through the
   walker's override, as the slash does: `StrokeArm(arm, direction, weight)`
   wins over the gait swing while its weight is up, then hands back. The walker
   stays the only writer, and a part that has been shot off simply stops.
3. **Clip as the base, code on top (not built yet).** Set the player's
   `callback_mode_process` to Manual, have the walker call `Advance(delta)`
   first, then add its procedural offsets to the pose the clip left, rather than
   to the rest transform. That is a change to `MechWalker` and its own decision
   record.

For a **rigged Blender model**, the engine already layers code over clips:
`SkeletonModifier3D`s (`LookAtModifier3D`, `TwoBoneIK3D` and others) run after
the `AnimationPlayer` on the same `Skeleton3D`. Animate the clip and let a
modifier aim the head or plant the feet.

---

## G. Place it and bench it

1. **Place it** by instancing the wrapper in a level: props and mechs go under
   `Props`, encounter markers under `Spawns`. Never save the player into a
   level.
2. **Look at it:**
   ```
   "$GODOT" --path . --script scripts/debug/bench.gd -- res://scenes/levels/lvl_pipeline_test.tscn look
   ```
   Check `perflogs/look.png` for facing, size, colour and glow.
3. **Break it**, if it is destructible, in `lvl_horde_flats`: `wreck` (props and
   buildings shot), `collapse`, `cut`, `cutside` (buildings brought down), and
   `mechwreck` (allies shot apart). Read worst frames from `cut quiet` or
   `cutside quiet`, not from phases that take screenshots.
4. **Move it**, if it is a mech: add it to `mechs` (walks, rides, fights, takes
   screenshots) and `mechsab` (what it costs to draw).
5. **The target:** 60 fps at 1080p native on the Iris Xe laptop. Pair each run
   (baseline, change, baseline) and cool the laptop between runs. A new model
   that misses the target is a bug.
6. Commit the `.vox` or `.glb` (Git LFS takes it), the wrapper, its `.tres`
   files and any `credits.md` line together.

---

## Known gaps

- **The Quadruped Tank does not break.** Its wrapper wires `Voxels` to a node
  it never had, so `MechWalker.Voxels` is null and hits do nothing. To fix it,
  add a `Voxels` `VoxelBody` as in the field fighter's wrapper, listing its
  twelve leg parts and `Turret` under `Hinged`.
- Blender facing and scale are not verified (A, *The first Blender asset*).
- There is no `IDamageable` owner for moving voxel props that are not walkers
  (C), and no generic part animator (D). Each will be written with its first
  user.
- No voxel model has hand-made clips yet. That needs a decision record (E).
