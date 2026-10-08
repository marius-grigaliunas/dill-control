# Model descriptions

*8 October 2026.* Every model the game needs, what it shows, and enough of a
brief to start modelling it. The game is in `pitch.md`; how a model gets into
the game is in `art/_source/README.md`. Strike an entry through when its wrapper
scene is in the game.

**Pipeline** says which tool: **Blender** (`.glb`, UV-mapped onto
`palette.png`, origin at the feet, at the grip for a weapon, at the hinge for
anything that turns) or **Voxel** (MagicaVoxel `.vox` into `art/voxels/`, split
into named parts, hinges marked with palette index 255; every voxel model is
destructible). Sizes are the game's own numbers: the enemy data in
`resources/enemies/`, or the lab's where the thing is not built here yet.
**When** is the milestone in `plan.md` that needs it.

Style for everything: bright, comic and played straight. Cel shading with ink
outlines, so silhouettes matter more than surface detail, and flat colour
blocks read better than small greebles.

## Where to start

1. **The pipeline test crate** (below). It is a ten-minute model and it
   settles the last two unverified lines in `CLAUDE.md`.
2. **The standard pickle.** There are 25,000 of it on screen; its triangle
   count and silhouette decide more about the game's look than anything else.
3. **The Brine Ripper** viewmodel: the gun on screen most of the time.
4. **The rock spire:** the first thing you swing from.

---

## 0. Pipeline

| Model | File | Pipeline | Size | When |
|---|---|---|---|---|
| Test crate | `prp_test_crate` | Blender | 1 m cube | M0 |

**Test crate.** A wooden or metal supply crate with a clearly marked front
(a stencilled arrow or company logo on the `-Y` face) and a few palette
colours. It exists to prove facing, scale and the palette mapping; see "The
first test asset" in `art/_source/README.md`. Keep it afterwards as set
dressing.

---

## 1. The pickles

All Blender, one mesh each, no rig. They are animated in the vertex shader by
bending along their length (waddle, wobble, squash, stretch, flop), so each
needs **evenly spaced edge loops along its height** and no parts that stick out
sideways far enough to tear when bent. Origin at the bottom, standing upright,
front at `-Y`. Triangle budget: **start near 200** for the standard and let the
horde bench say; the others can go higher since there are fewer of them.
Faces, if any, are painted swatches, not geometry.

| Model | File | Size (tall x wide) | Role |
|---|---|---|---|
| Standard pickle | `nme_standard` | 1.8 m x 0.7 m | The body of the crowd; bites |
| Cornichon | `nme_cornichon` | 0.9 m x 0.4 m | Small, fast, dashes and nips; the thrower's ammunition |
| Thrower | `nme_thrower` | 1.9 m x 0.9 m | Lobs live cornichons on a high arc |
| Spitter | `nme_spitter` | 1.9 m x 0.7 m | Spits brine that leads your movement |
| Tank | `nme_tank` | 3.0 m x 1.8 m | Soaks damage; a hit throws you |
| Far pickle | `nme_far` | 1.8 m | The distant tier for every kind |

**Standard pickle.** The classic dill pickle standing on end: slightly curved,
bumpy (warts as a few raised verts, not separate geometry), round ends. A
mouth, either a painted dark swatch or a shallow cut, so the bite reads. It
must read as "pickle" from 50 m as a silhouette alone.

**Cornichon.** A small, tight, very warty gherkin, leaner and more curved than
the standard, built to look quick. Must read as a different kind from the
standard at a glance, even when tumbling through the air after being thrown.

**Thrower.** A fat, wide-bodied pickle with a big open mouth or a sling-like
top: whatever reads as "this one launches things". Wider at the top than the
standard so the throw's wind-up (a backward bend in the shader) shows clearly.

**Spitter.** Thin and tall with a pronounced snout or nozzle-like mouth at the
top and a swollen brine sac below it. A green-yellow glowing swatch at the
mouth helps the player pick it out of a crowd, since it is the one that shoots.

**Tank.** A huge, thick, old pickle, darker and heavier, possibly a dill pickle
in a crust of salt or a cracked jar shard worn as armour. Low and broad. It
has to look like it hurts.

**Far pickle.** A plain 6-sided column with rounded ends, under 30 triangles.
Used past the near distance for every kind, scaled per kind, so its only job is
the right colour and proportion.

### Mother Brine

| Model | File | Pipeline | Size | When |
|---|---|---|---|---|
| Mother Brine, body | `nme_mother_brine` | Blender | 34 m tall, 12.4 m across | M5 |
| Mother Brine, armour | `vxl_mother_brine_armour` | Voxel | fits the body | M5 |

**The body.** The colossal pickle who rules the planet: one rigid body that
squashes and stretches, hops, slams, topples and rolls. No limbs, no rig. An
ancient, enormous, slightly menacing pickle with a face that sells "boss".
Needs more edge loops than the horde, since she bends more and is seen up
close. Mark **six warts** (about 1.3 m each) and a **core** as separate,
named objects: they become hitboxes and weak points that burst.

**The armour.** Voxel plates strapped or grown over the body, which the
player strips off from the grapple. Think brine crust, salt deposits or
salvaged jar glass. Leave the warts uncovered or behind thin plates. Split the
plates into named objects so each can be shot away on its own.

---

## 2. Player weapons

Blender, first-person viewmodels, **origin at the grip**, barrel along `-Y`.
Seen large and close, so they can carry more detail than anything else, but
still in flat palette colours. No hands yet: model a gun that looks right
floating at the bottom right of the screen; hands come in M7. The tone is
company-issue pest-control gear: sprayers, tanks, hazard stripes, brine and
vinegar canisters, a corporate logo.

| Slot | Model | File | Is | What it shows |
|---|---|---|---|---|
| 1 | Brine Ripper | `wpn_brine_ripper` | Rifle | A long automatic rifle, magazine of 50, a brine tank or glowing green rounds visible |
| 2 | Bayou Machete | `wpn_bayou_machete` | Melee | A big, notched swamp machete, taped grip, a little brine on the blade |
| 3 | Gherkin Volley | `wpn_gherkin_volley` | Rocket launcher | A shoulder tube or multi-barrel pod firing pickle-shaped rockets |
| 4 | Strike Designator | `wpn_strike_designator` | Calls airstrikes | A chunky handheld laser pointer or tablet with an antenna and a big button |
| 5 | Dill Hammer | `wpn_dill_hammer` | Shotgun | A heavy, short pump or drum shotgun; reads as brutal at close range |
| 6 | Brine Burner | `wpn_brine_burner` | Flamethrower | A sprayer nozzle on a hose to a fuel canister; pilot light at the tip |
| 7 | Vinegar Lance | `wpn_vinegar_lance` | Piercing beam | A long, precise rail-like gun with coils and a glowing chamber; it shoots through everything |
| 8 | Gravity Manipulator | `wpn_gravity_manipulator` | Picks up pickles | A gripper or prongs around an emitter dish; mark the hold point with an empty named `Muzzle` |
| G | Cluster Jar | `wpn_cluster_jar` | Grenade | A pickle jar with a fuse in the lid. Also the thrown world model, so keep it under 300 triangles |
| MMB | Grapple launcher | `wpn_grapple` | Grappling line | A wrist or forearm launcher (the line leaves your hands free) and a separate claw hook head that flies out |

The **Bayou Boomstick** has stats (`resources/weapons/wpn_bayou_boomstick.tres`)
but no slot; model it only if it comes back into the loadout.

Give every gun an empty named `Muzzle` at the barrel tip, where shots, tracers
and flashes start.

| Model | File | Pipeline | When |
|---|---|---|---|
| First-person hands and forearms | `ply_hands` | Blender, **rigged** | M7 |

**Hands.** The exterminator's gloved hands and sleeves: thick work gloves,
company uniform cuffs. The only rigged model in the game. Wait for the weapons
to settle first.

---

## 3. Projectiles and ordnance

Blender, small and low-poly (under 150 triangles), seen in flight for a moment.
Origin at the centre, nose along `-Y`.

| Model | File | What it shows |
|---|---|---|
| Rocket | `fx_rocket` | A pickle-shaped rocket with fins, for the Gherkin Volley |
| Bike pod rocket | `fx_pod_rocket` | A slimmer rocket for the hoverbike's pod |
| Bike bomb | `fx_bike_bomb` | A small finned bomb dropped from the bike's bay |
| Strike bomb | `fx_strike_bomb` | A 500 lb aircraft bomb, company stencils on the side |
| Heavy HE bomb | `fx_heavy_bomb` | The same, larger: the 1000 lb |
| Jar bomblet | `fx_jar_bomblet` | A mini jar, the cluster's submunition |
| Brine glob | `fx_brine_glob` | The spitter's shot: a wobbly blob, glowing green-yellow |

---

## 4. Vehicles

| Model | File | Pipeline | Size | When |
|---|---|---|---|---|
| Hoverbike | `shp_hoverbike` | Blender | about 3 m long | M6 |
| Quadruped Tank | `mch_quadruped_tank` | Voxel, bought | 5.6 m | Done |
| Field Fighter | `mch_field_fighter` | Voxel, bought | 6.5 m | Done |
| Mobile Storage bot | `mch_mobile_storage` | Voxel, bought | 1.8 m | Done |

**Hoverbike.** The player's signature machine, this game's Seamoth: it takes
you from orbit to the ground and you keep coming back to it. Fast, sleek,
a little heroic, a little company-issue. Currently built from primitives
(`scenes/vehicles/shp_hoverbike.tscn`); the model replaces them. Rigid, no
rig; the moving parts are **separate named objects with origins at their
hinges**:

- `Chassis`: the body and seat, the rider's eyes about 1 m above the seat.
- `Handlebar`: turns with steering.
- `PodPort`, `PodStarboard`: the rocket pods on each side.
- `Nose` with the gun and the laser emitter (the orbital beam brought down to
  bike scale; it should look like the most dangerous thing on the bike).
- A bomb bay underneath.
- Emissive neon strips along the sides (`NeonPort`, `NeonStarboard`) for the
  underglow.
- Empties for `GunMuzzle`, `LaserMuzzle`, `PodMuzzlePort`, `PodMuzzleStarboard`,
  `BombBay` and `Seat`.

The three mechs are Max Parata's (`credits.md`) and are already in. A new mech
of our own follows the voxel half of `art/_source/README.md`.

---

## 5. The compound

Voxel, **static** (`VoxelBody` with Static ticked), destructible. These are both
the targets and the grapple anchors, so they are tall, have overhangs and
ledges to hook, and their shape must survive being partly shot away. Build them
in pieces of solid voxels; hollow shells collapse too easily. The lab's sizes
are the starting point. Interior colour (what a cut shows) can differ from the
skin.

| Model | File | Size | When |
|---|---|---|---|
| Rock spire, three variants | `prp_spire_a`..`c` | 22-58 m tall, 7-14 m across | M2 |
| Spitter tower | `prp_spitter_tower` | about 24 m tall | M2 |
| Hive jar | `prp_hive_jar` | 15 m tall, 13 m across | M2 |
| Hive jar core | `prp_hive_jar_core` | about 4 m | M2 |
| Ruins, three or four pieces | `prp_ruin_a`.. | 8-25 m | M4 |

**Rock spires.** Tall, crooked fingers of alien rock, the main swing anchors.
Rough, leaning, with knobs and ledges near the top to hook. Three variants so a
ring of them does not look copied: tall and thin, bent, and broken-topped.

**Spitter tower.** The compound's anti-air: a grown or built tower with a
bulbous head (about 3.6 m) at the top that spits at you in the air. Organic
pickle architecture: warty green stalk, brine-stained. The head must read as
the thing to shoot.

**Hive jar.** The heart of the compound: a giant armoured pickle jar, partly
buried, with a heavy lid and metal bands. Heavy ordnance cracks it open. The
outer shell should be visibly armoured; the inside is murky glowing brine.

**Hive jar core.** What you finish through the breach: a glowing, pulsing
organic mass inside the jar. Emissive.

**Ruins.** The remains of whoever was here before the pickles: broken walls,
arches, a toppled pillar, a collapsed gantry. Plenty of edges and arches to
swing through. Make them as separate pieces so they can be arranged.

---

## 6. Props and world

| Model | File | Pipeline | Size | When |
|---|---|---|---|---|
| Gym pillars and blocks | `prp_gym_pillar`, `prp_gym_block` | Voxel | 10-30 m | M1 (graybox in) |
| Rearm pod | `prp_rearm_pod` | Blender | about 3 m | M7 |
| Dill plant clumps | `prp_dill_a`..`c` | Blender | 0.5-3 m | M4 |
| Rocks and boulders | `prp_rock_a`..`d` | Blender | 0.5-4 m | M4 |
| Mining survey beacon | `prp_survey_beacon` | Blender | about 6 m | M7 |
| Distant planet | `prp_planet_far` | Blender | a sphere | M6 |

**Gym pillars and blocks.** For the grapple test level. The graybox voxels
(`prp_graybox_*`) already do the job; replace them only when the gym needs to
look like somewhere.

**Rearm pod.** A company drop pod that lands far from the fight with ammo and
medkits: a stubby capsule with landing legs, a door or hatch that opens (a
separate object hinged at its edge), corporate livery and a beacon light
(emissive). Cheerful and corporate, like everything the company makes.

**Dill plant clumps.** Ground scatter: feathery dill fronds and flower heads,
oversized. Drawn by the thousand, so keep each under 200 triangles and use
crossed flat cards for the fronds rather than modelled leaves.

**Rocks and boulders.** Ground scatter and cover. Chunky, faceted, a few
sizes. Under 100 triangles each.

**Mining survey beacon.** A company marker planted on the planet, the drill
site the company wants cleared. Optional set dressing that sells the premise.

**Distant planet.** The stand-in seen from orbit before the real terrain takes
over (the pitch's open question). A clean low-poly sphere; the look is mostly
a shader. Model only when M6 decides how the hand-over works.

---

## Not models

So nobody models them: explosions, brine splatter, fire, dust, tracers and the
laser beam are effects drawn by shaders and particles (`fx_*` scenes). The
health jar and the HUD are 2D. The ground is generated terrain. The player has
no body in first person.
