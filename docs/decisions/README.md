# Decisions

One file per decision that had more than one defensible answer: what the
situation was, what was decided, what was measured, and what it costs. One page
at most. `CLAUDE.md` holds the rule and points here for the why.

A decision that is overwritten or reversed is deleted, and the new one says what
changed; git keeps the old text. Numbers are not reused. Only what is still true
stays here.

Number them in order, `NNNN-short-title.md`.

| # | Decision |
|---|---|
| [0002](0002-performance-target.md) | 60 fps at 1080p native on Iris Xe, and how it is measured |
| [0003](0003-jolt-physics.md) | Jolt physics, set explicitly |
| [0004](0004-authored-over-generated.md) | Authored over generated |
| [0005](0005-art-pipeline.md) | Blender, glTF binary, one palette, shader-animated pickles; voxel art beside it, animated by code |
| [0006](0006-event-bus.md) | The event bus, and why each of its signals is on it |
| [0007](0007-audio-director.md) | `AudioDirector` as a second autoload |
| [0008](0008-horde-ground.md) | The horde stands on an interface, and the flats come first |
| [0009](0009-voxel-models.md) | MagicaVoxel `.vox` imported directly: parts as nodes, colour per vertex, emission |
| [0011](0011-voxel-destruction.md) | Every voxel model breaks the same way: carve, structure, physics chunks, dust |
| [0012](0012-horde-attacks-allies.md) | The horde attacks the allies: targets by group, bites into voxels, guard and stomp |
| [0015](0015-destruction-off-the-frame.md) | Destruction off the frame: voxel work on low-priority workers, a body per section, pooled chunks, first times at load, the game above normal priority |
| [0016](0016-rubble-stays.md) | Rubble stays, and nothing blinks: crushed storeys heap up, pieces replace what they came from under dust, resting rubble joins the heap |
| [0017](0017-the-planet.md) | The planet: ported at the world origin, a far shell and chunk levels, sites where authored things meet generated ground, the horde's ground from a provider |
| [0018](0018-seeded-planets.md) | A contract is a seed; planets roll inside an authored archetype's ranges, biomes are authored presets the seed shifts; ranges capped by the bench |
| [0021](0021-session-and-loading.md) | `Session`, a third autoload, carries the contract (state only); the contract level builds itself in stages behind its own loading screen and lets the player in once frames settle |
| [0022](0022-destruction-that-recovers.md) | Destruction the machine recovers from: the cap decides before a body is made, heaps merge and have holes, only grounded pieces bake, resting pieces re-boxed finely, a throw-speed cap, buildings tell the horde |
| [0024](0024-trees-hold-by-connection.md) | Trees hold by connection, not weight: `voxel_destruction_tree.tres` turns 0013's load check off; a stats resource reaches only how its own model splits |
| [0025](0025-gamepad.md) | Gamepad and joystick on the same actions as the keys: one layout, stick look as data, the east button crouches in play and goes back only while a menu is open |
| [0027](0027-chunk-graph.md) | Buildings fall by a chunk graph baked at import, carry their own weight, fall whole and crush through (folds in the former 0013, 0014): a cut chunk splits along the cut, pieces break and crush by chunks, overloaded edges give way in stages, the gravity gun tears chunks out |
| [0028](0028-melee-strokes.md) | Melee is a stroke, not a clip: four poses, a ray then a sweep, a hit stops and a miss follows through |
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
| [0046](0046-mechs-fight-on-broken.md) | Mechs walk by code and fight on broken (only core or head kills; the gait comes from what is left), mend themselves as AI at the material's price, and take their moves from Blender, baked to data (proposed) |
| [0047](0047-the-asset-lab.md) | The asset lab: one place to look at, tune and break any asset, in the editor and in play |
| [0048](0048-vulkan-on-windows.md) | Vulkan on Windows, not D3D12 |
| [0049](0049-the-onboarding-level-is-a-nest.md) | The onboarding level is a generated nest: `lvl_nest_intro`, a saved preset, landmarks, prompts; the onboarding level is now `lvl_tutorial` (0055) |
| [0050](0050-ground-rules-and-the-cucumber-reframe.md) | Decided, not built: only the infestation is cucumber-themed (cucumbers grown from the company's seeds), everything has a cause, the grapple and jetpack must feel amazing |
| [0051](0051-the-slice-contracts.md) | Decided, not built: onboarding and three contracts (nest clearing with the queen, loot grab on a storm planet, colony rescue); the slice's enemies, loadout and two-purchase shop |
| [0052](0052-weapons-specials-tiers-and-the-shop.md) | Decided, not built: five core weapons with two tiers, reusable and consumable specials, five slots and a consumables bar, six tiers, a shop on the ship, the bike a special item |
| [0053](0053-death-and-clones.md) | Decided, built in `lvl_tutorial`: three clones a contract (`CloneBank`); failure pays nothing, upgrades kept, nothing lost on death |
| [0054](0054-the-opening-sequence.md) | Decided, built as placeholders in `lvl_tutorial`: unclear cloning flashes, then control in the landing pod; the pod descent is every arrival |
| [0055](0055-the-tutorial-is-handbuilt.md) | The tutorial is handbuilt on a pinned planet: a baked nest, hooks, a first-person pod drop and clones; replaces 0049 for onboarding |
| [0056](0056-a-saved-nest-can-be-baked-to-a-scene.md) | A saved nest stays a preset grown at load; Bake turns one into an authored scene to place and edit by hand (one-way, never overwritten) |
