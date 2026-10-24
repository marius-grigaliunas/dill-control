# Decisions

One file per decision that had more than one defensible answer: what the
situation was, what was decided, what was measured, and what it costs. One page
at most. `CLAUDE.md` holds the rule and points here for the why.

A decision that is later reversed is not rewritten. Its file gets a
`Superseded by NNNN` line at the top, and the new one says what changed.

Number them in order, `NNNN-short-title.md`.

| # | Decision |
|---|---|
| [0001](0001-a-new-repository.md) | A new repository for the game; the prototype stays as the lab |
| [0002](0002-performance-target.md) | 60 fps at 1080p native on Iris Xe, and how it is measured |
| [0003](0003-jolt-physics.md) | Jolt physics, set explicitly |
| [0004](0004-authored-over-generated.md) | Authored over generated |
| [0005](0005-art-pipeline.md) | Blender, glTF binary, one palette, shader-animated pickles; voxel art beside it, animated by code |
| [0006](0006-event-bus.md) | The event bus, and why each of its signals is on it |
| [0007](0007-audio-director.md) | `AudioDirector` as a second autoload |
| [0008](0008-horde-ground.md) | The horde stands on an interface, and the flats come first |
| [0009](0009-voxel-models.md) | MagicaVoxel `.vox` imported directly: parts as nodes, colour per vertex, emission |
| [0010](0010-procedural-mech.md) | Mechs walk by code: stepping feet, our own two-bone IK, brains apart from walkers |
| [0011](0011-voxel-destruction.md) | Every voxel model breaks the same way: carve, structure, physics chunks, dust |
| [0012](0012-horde-attacks-allies.md) | The horde attacks the allies: targets by group, bites into voxels, guard and stomp |
| [0013](0013-buildings-carry-weight.md) | Buildings carry their own weight: load check, storey fracture, heavy rubble that crushes; armour as data |
| [0014](0014-buildings-fall-whole.md) | Buildings fall whole and break where they land: crush-through, a hinge that tears, snapping on landing |
| [0015](0015-destruction-off-the-frame.md) | Destruction off the frame: voxel work on low-priority workers, a body per section, pooled chunks, first times at load, the game above normal priority |
| [0016](0016-rubble-stays.md) | Rubble stays, and nothing blinks: crushed storeys heap up, pieces replace what they came from under dust, resting rubble joins the heap |
| [0017](0017-the-planet.md) | The planet: ported at the world origin, a far shell and chunk levels, sites where authored things meet generated ground, the horde's ground from a provider |
| [0018](0018-seeded-planets.md) | A contract is a seed; planets roll inside an authored archetype's ranges, biomes are authored presets the seed shifts; ranges capped by the bench |
| [0019](0019-system-and-arrival.md) | The system and the arrival: the ship waits in orbit, the bike starts ridden with its nose on the compound, zero-g past the well, other bodies as lit discs in the sky, the air and fog thin with height |
| [0020](0020-compound-from-clusters.md) | The compound is a seeded layout of authored clusters: the seed picks the flattest site and where each cluster stands; the kit and its scenes decide what; the horde from a tier |
| [0021](0021-session-and-loading.md) | `Session`, a third autoload, carries the contract (state only); the contract level builds itself in stages behind its own loading screen and lets the player in once frames settle |
| [0022](0022-destruction-that-recovers.md) | Destruction the machine recovers from: the cap decides before a body is made, heaps merge and have holes, only grounded pieces bake, resting pieces re-boxed finely, a throw-speed cap, buildings tell the horde |
| [0023](0023-the-horde-keeps-house.md) | The horde keeps to its nest until provoked (aggro range, kills, a leash) and roams it rather than piling into one cell; on per contract tier |
| [0024](0024-trees-hold-by-connection.md) | Trees hold by connection, not weight: `voxel_destruction_tree.tres` turns 0013's load check off; a stats resource reaches only how its own model splits |
| [0025](0025-gamepad.md) | Gamepad and joystick on the same actions as the keys: one layout, stick look as data, the east button crouches in play and goes back only while a menu is open |
| [0026](0026-drop-and-extract-trial.md) | On trial: swing first; a contract is drop by pod, cull half and crack the jar, extract on foot; three clones, no bike; a 1 km arena of its own, dressed by hand |
| [0027](0027-chunk-graph.md) | Buildings fall by a chunk graph baked at import: a cut chunk splits along the cut, pieces break and crush by chunks, overloaded edges give way in stages, the gravity gun tears chunks out |
| [0028](0028-melee-strokes.md) | Melee is a stroke, not a clip: four poses, a ray then a sweep, a hit stops and a miss follows through |
| [0029](0029-the-vine-hive.md) | Proposed: the colony grows on vines, 95% hanging and 5% workers; the pickles never strike first, harm anywhere alerts the whole colony, and it comes down over about 30 s |
| [0030](0030-innocent-pickles.md) | Proposed: the pickles are innocent and the player is not one: cartoon look, scared faces, brine is theirs, whimpers capped and pooled, health and shield for the player |
| [0031](0031-a-contracts-arc.md) | Proposed: scout first or drop in, both valid; random one-off drops within a contract (mechs, vehicles, weapons) up to the bike; persistent progression unchanged; both lasers |
| [0032](0032-plants-bend.md) | Proposed: vines and trees bend: a spine of springs built from their voxels, drawn in the shader, collision moved along and carrying the player; wind, hits and the grapple push it; cut weak, it gives way and falls over |
| [0033](0033-vines-baked-in-the-editor.md) | Decided, rewritten: a nest is generated at load (25 s at 25,000; baking would save at most 10 s); saved nests are seeds; nothing generates mid-mission; single vines are still baked for set pieces |
| [0034](0034-pods-regrow.md) | Decided: every pickle grows on a pod (one pod per pickle, 1,000-25,000); an emptied pod regrows while it and its vine stand; a contract is won with every pod destroyed and at most 20 % of the pickles alive, all of them killed for a bonus |
| [0035](0035-leaves-are-meshes.md) | A vine's leaves are meshes bent by the stalk's shader, a thin box each on its section's body, walkable stems, holes where hit, torn off whole; tendrils gone |
| [0036](0036-only-valid-vines.md) | Only valid vines: a failed roll is rolled again from the seed and an attempt number, so a seed is one valid vine; Jolt never decides a roll; rejection rates per kind |
| [0037](0037-one-planet-size.md) | One planet size for nests: the rock archetype pinned at 1,260 m |
| [0038](0038-the-nest-is-one-clump.md) | The nest is one clump sized by its population: 55-70 m apart, 400 m at most, high-pod kinds before more room; no compound or city |
| [0039](0039-structure-and-litter-layers.md) | Structure (layer 9) for static voxel bodies, never Prop; Litter (layer 10) for torn leaves, which never push the player |
| [0040](0040-pods-in-bulk.md) | Pods in flat arrays, anchored to a voxel; hanging pickles instanced and bent on the GPU; shot through a pool of proxies |
| [0041](0041-the-colony-wakes-on-harm.md) | The colony wakes on harm, never nearness; a pickle shot on its pod drops dead; every pickle drops into the horde over 30 s |
| [0042](0042-vines-leave-no-rubble.md) | Vines leave no rubble (an exception to 0016): never heaped, snap into logs, crumble after 20 s; to be revisited by the path of destruction |
| [0043](0043-the-vines-tool.md) | The Vines tool: a main screen that generates a nest, shows it, plays it and logs Good/Bad as training data; rebuilt after every reload |
| [0044](0044-the-editor-bridge.md) | The editor bridge: Claude works in the running editor on the real scene (localhost, token, every change one undo); terrain is sculpted with our own brushes, not generated; tools register commands |
| [0045](0045-shallow-water.md) | Water is shallow (wade, splash, the horde slower and routing round) and made by flooding a hollow to a level: a WaterBody of wet cells and a saved surface |
