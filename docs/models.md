# Models

Every model the vertical slice needs, what it is made of, and where it stands.
Work from the top of each table down; the order is the milestone order in
`plan.md`. Tick the review column after the model has been seen in the game,
in its level, at its real distance, not just in Blender or MagicaVoxel.

**How to read it**

- **Tool:** *Vox* is MagicaVoxel, imported directly and destructible
  (`docs/decisions/0009`, `0011`). *Blend* is Blender, a `.glb` on the
  palette (`0005`). Anything that is meant to be blown apart is Vox.
- **Parts:** model each part as its own MagicaVoxel object or Blender
  object. A part that moves, breaks off on its own, or carries a hitbox has to
  be separate. Then the **whole** is the assembled scene (`.tscn`) that
  places the parts; it is reviewed as a thing in its own right.
- **Status:** *none* (nothing yet), *graybox* (a stand-in is in the game),
  *bought* (from a pack, needs review against our look), *done*.

Everything that arrives from outside goes into `credits.md` the day it does.

---

## 1. The nest (the compound you breach)

The set piece of the mission (`pitch.md`, "Breach the compound"). It stands
on open ground and is built from tall things that are both targets and grapple
anchors. Today it is the graybox pillars, columns and blocks in
`lvl_horde_flats`.

### The whole

| Model | What it is | Status | Review |
|---|---|---|---|
| `prp_nest_compound.tscn` | The assembled nest: the jar at the centre, a ring of spitter towers, spires and ruins around it at swinging distance (anchors 25 to 60 m apart, inside the grapple's 80 m), anti-air on the outside. Authored as one scene, placed at a marker on the planet (M4). | graybox | |
| Nest layout sketch | A top-down plan before any modelling: where each piece goes, the routes the player swings, sight lines to the jar, where the horde pours out. | none | |

### The hive jar (the objective)

The armoured jar you crack with heavy ordnance and finish through the breach.

| Part | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `vxl_jar_shell` | The jar's glass-and-brine body, in several stacked rings so a breach can open one band at a time. Thick: it takes heavy ordnance. | Vox | none | |
| `vxl_jar_armour_plates` | Bolted plates over the shell, 6 to 8 separate objects so each can be blown off on its own. The first layer you strip. | Vox | none | |
| `vxl_jar_lid` | The lid and its rim; a grapple anchor at the top. | Vox | none | |
| `vxl_jar_core` | The glowing core inside, seen through the breach. Carries the weak-point hitbox. Emissive (MagicaVoxel Emit). | Vox | none | |
| `vxl_jar_plinth` | The base it stands on, with the pipes running in. Ground-anchored, so it holds the jar up until it is cut. | Vox | none | |
| `vxl_jar_vents` | Spawn vents round the plinth where cornichons pour out. 3 to 4, separate. | Vox | none | |
| `vxl_jar_pipes` | Brine pipes and roots from the jar to the towers. Breakable, set dressing. | Vox | none | |
| `prp_hive_jar.tscn` | The whole jar assembled: parts, core hitbox, vents as spawn markers. | | none | |

### Spitter towers (anti-air)

| Part | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `vxl_spitter_base` | Trunk of the tower, ground-anchored, cut to topple it. | Vox | none | |
| `vxl_spitter_stalk` | Upper stalk, a separate part so it can snap and fall on its own. | Vox | none | |
| `vxl_spitter_head` | The mouth that turns and spits, a hinged part (pivot at palette 255). Carries the attack hitbox. | Vox | none | |
| `vxl_spitter_tank` | Brine bladder on its side: a weak point that bursts. | Vox | none | |
| `prp_spitter_tower.tscn` | The whole tower, two heights (25 m and 40 m). | | none | |

### Rock spires and ruins (anchors)

| Part | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `vxl_spire_a/b/c` | Three rock spires, 30 to 60 m, different silhouettes. One object each; they snap under load like the towers. | Vox | graybox (pillars, columns) | |
| `vxl_ruin_wall_a/b` | Broken wall segments, 2 shapes. | Vox | graybox (blocks) | |
| `vxl_ruin_arch` | An arch to swing through. | Vox | none | |
| `vxl_ruin_tower` | A broken tower, half fallen, 30 m. | Vox | none | |
| `vxl_ruin_rubble` | Scatter: loose stones around ruins. Small, breakable. | Vox | none | |

## 2. The pickles (M3)

Blender models on the palette, animated by the vertex shader, no rig
(`plan.md`, "The pickles"). Enough edge loops along the length to bend; start
near 200 triangles and let `bench.gd horde 25000` decide.

| Model | What it is | Parts | Status | Review |
|---|---|---|---|---|
| `nme_standard` | The body of the crowd. Bites. | body; eyes and mouth as palette swatches, not geometry | graybox (capsule) | |
| `nme_cornichon` | Small, fast, the thrower's ammunition. | body | graybox | |
| `nme_thrower` | Lobs cornichons on a high arc. | body, a visible sling or cup on its back | graybox | |
| `nme_spitter` | Spits brine that leads you. | body, a bulging cheek or nozzle | graybox | |
| `nme_tank` | Big, soaks damage, throws you. | body, warts or armour lumps | graybox | |
| `nme_far` | The far tier for all of them: a few-sided column. | one | graybox | |
| Death pose check | Each pickle's flop and squash read from the shader, on the real model. | | none | |

## 3. Mother Brine (M5)

The giant. One rigid voxel body that squashes and stretches, armoured in
voxels you strip from the line.

| Part | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `vxl_brine_body` | The giant pickle body, the part that squashes. | Vox | none | |
| `vxl_brine_armour` | Armour plates over the body, many separate objects so they come off one at a time while you orbit. | Vox | none | |
| `vxl_brine_warts` | Weak-point warts (hitboxes) under the armour. Emissive when exposed. | Vox | none | |
| `vxl_brine_core` | The core the last phase goes for. | Vox | none | |
| `vxl_brine_geyser_vents` | Where she erupts geysers. | Vox | none | |
| `nme_mother_brine.tscn` | The whole giant assembled with its hitboxes and grapple anchors. | | none | |

## 4. The player

| Model | What it is | Parts | Tool | Status | Review |
|---|---|---|---|---|---|
| Hands | First-person arms, the one rigged model (`plan.md`). M7 or earlier if gun-only feels wrong. | upper arm, forearm, hand, fingers; rig | Blend | none | |
| `wpn_bayou_machete` | Machete viewmodel. Grip at origin. | blade, handle | Blend | graybox | |
| `wpn_brine_ripper` | The rifle. | body, magazine (moves on reload), muzzle | Blend | graybox | |
| `wpn_bayou_boomstick` | The shotgun. | body, pump (moves) | Blend | graybox | |
| `wpn_gherkin_volley` | The rocket launcher. | tube, grip, a visible rocket | Blend | graybox | |
| `wpn_cluster_jar` | The grenade: a jar. Also its world model in flight. | jar, lid | Blend | graybox | |
| `wpn_dill_hammer`, `wpn_brine_burner`, `wpn_vinegar_lance`, `wpn_strike_designator`, `wpn_gravity_manipulator` | In the build today, not in the slice's proposed five. Model only if the slice keeps them. | | Blend | graybox | |
| Grapple launcher | The line's launcher on the wrist, and the hook. | launcher, hook, line (shader) | Blend | graybox | |
| Clone pod | Where you come back after a death. | pod, door | Blend | none | |

## 5. The hoverbike (M6)

Your Seamoth: a rigid model with moving parts on node transforms, no rig.
Today it is a voxel hull editable in the lab's tool.

| Part | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `shp_hoverbike_body` | The hull and seat. | Blend or Vox | graybox | |
| `shp_hoverbike_thrusters` | Hover pads or thrusters that tilt with flight. Separate, they move. | | graybox | |
| `shp_hoverbike_laser` | The emitter under the nose: the most satisfying thing in the game should look it. | | graybox | |
| `shp_hoverbike_gun`, `_pod`, `_bombbay` | Gun, rocket pod, bomb bay doors (move). | | graybox | |
| Cockpit view | What you see from the seat: the bike's own dash and nose. | | none | |
| `shp_hoverbike.tscn` | The whole bike, seen from the chase camera and the seat. | | graybox | |

## 6. Allies

Bought from Max Parata's Voxel Robot Collection. Review each against our toon
look and our scale (0.1 m a voxel).

| Model | Status | Review notes |
|---|---|---|
| `mch_quadruped_tank` | bought | Its scene has no `VoxelBody`, so it is not destructible now; check. |
| `mch_field_fighter` | bought | Now armoured and carries a flamer on its gun arm; the flamer has no model of its own (it fires from the arm muzzle). A nozzle part on the arm would sell it. |
| `mch_mobile_storage` | bought | |

## 7. The planet and the arrival (M4, M6)

| Model | What it is | Status | Review |
|---|---|---|---|
| Distant planet | A cheap whole-planet stand-in seen from orbit that hands over to the real terrain (`pitch.md`, open questions). | none | |
| Rearm pod | Lands away from the fight. | pod, legs, opening door, ammo racks | none | |
| Mining drill / company lander | Seen at the end, or on the invoice screen: the company arriving. | none | |
| Ground scatter | Rocks, grass clumps, alien plants, on the palette. Cheap, many. | none | |

## 8. Buildings (the towers)

The graybox skyscrapers in `lvl_horde_flats` (`prp_graybox_tower_a/b/c`,
`tools/vox-tools/make_graybox.py`). Real ones keep the structure the load
check reads (`docs/decisions/0027`): an open lobby of columns, a slab every
storey, a core. Facade style is free.

| Model | What it is | Tool | Status | Review |
|---|---|---|---|---|
| `vxl_tower_*` | 3 to 5 tower designs, 60 to 125 m, one 32-voxel section wide where possible (cost: draws per section). | Vox | graybox | |
| `vxl_tower_crowns` | Rooftop pieces: tanks, masts, signs. Separate so they fall on their own. | Vox | none | |
| Collapse review | Bring each design down in the game and check it reads as a building coming apart. | | | |

## 9. Effects that need a model

Most effects are shaders and particles. These need geometry.

| Model | What it is | Status | Review |
|---|---|---|---|
| Rocket / shell | In flight, for the launcher, the bike pod and the tank. | graybox | |
| Strike aircraft | What drops the designator's ordnance, if it is shown. | none | |
| Brine splash decal shapes | Splat shapes for kills, if a texture rather than a shader. | none | |

## 10. The slice's new assets (29 October 2026)

What `decisions/0051`-`0054` need that nothing above covers. The nest is vines
now (0038), not the compound of section 1, and the roster has no Thrower (the
Tank throws), so sections 1 to 3 are history until the models are redone; they
are kept as written.

| Model | What it is | Contract | Status | Review |
|---|---|---|---|---|
| `nme_queen` | The stationary queen pod at a nest's heart: rind that rifle rounds barely mark, weak spots that open after she attacks, feeder-vine attachments, voxel and destructible. Parts: rind plates, weak-spot hatches (emissive), seed-spit mouth, feeder roots. | 1 | none | |
| Landing pod interior | The pod the player is in at every arrival: seat, window with the descent through it, a door that opens onto the ground, the same view for extraction. | all | none | |
| Cloning lab flashes | The glimpses at the start and at the first death: a tank, a table, lights. Short, unclear. | 1 | none | |
| Ship cabin | The hub between contracts: a window onto the next planet, a menu view, ship ambience. | all | none | |
| Invoice printer | Prints the itemised invoice and stamps it PAID. Moving paper and a stamp. | all | none | |
| Crate hatch | The hatch the purchase arrives through, and the crate. | all | none | |
| Armed shuttle | Lands every 4 minutes at the colony: four corner miniguns and a rocket launcher that fire on their own, a ramp, a landing pad and a take-off. Five of them; ore and people loaded. | 3 | none | |
| Crash pod | The dead contractor's pod high in a vine, with a beacon and the nuke designator inside. | 2 | none | |
| Colony set | Skyscrapers (several designs; section 8's towers may serve), the human shelter (armoured, indestructible, a landing pad on top), ore storage sites, main and side mine shafts, turrets, roof power relays with a switch, fuel tanks, security mechs. | 3 | graybox (towers) | |
| Storm planet | A second biome in storm season: fog, lightning, tornado (a rotating voxel or shader funnel that rips up vines), dead calm. | 2 | none | |
| Nuke blast | The designator's detonation: a staged inked explosion at planet scale. | 2 | none | |
| Core weapon viewmodels | Assault rifle with underbarrel launcher, bazooka (single tube, volley), pole saw, laser cannon (with gravity-gun mode), .50 sniper. Replace the table in section 4. | all | none | |
