# Handoff: mechs that fight on broken, mend themselves, and take their moves from Blender

*25 October 2026.* Read `CLAUDE.md` first, then `docs/decisions/0046`
(the decision this handoff carries out), then 0010 (how mechs walk today),
0011, 0015 and 0027 (how voxels break and what the importer bakes). **Work
only inside `C:\Projects\dill-control`.** The project's rules hold: one system
at a time, numbers in `.tres`, bench on the target machine, the editor
changed through the bridge, short commits of your own files only, by
pathspec (`git commit -- <paths>`). Never commit `docs/project_board.json`,
`engine/` or `.vscode/`; the user keeps those staged.

Nothing in this file is built yet. 0046 is committed as *proposed*
(`b9fac85`). Build in the order below, a commit each.

---

## 1. Where this came from

The user wants the three ally mechs to look and fight better. In
conversation:

- **The procedural walk "looks like shit"**, and the user cannot tune it in
  Godot. The loop was: Claude tweaks numbers, the user builds and runs,
  says no, repeat. Nothing animates in the editor: `MechWalker` has no
  `[Tool]` and runs only in the game.
- **They want fighting moves**: hand smashes, more than today's one
  shoulder-arc slam.
- **They tried rigging the model in an auto-rigger (ActionForge)**, and it
  smeared: the arms stretched into long blades, because a skinned mesh bends
  voxels between bones. The model in `fighter.blend` is one merged mesh,
  which is why.
- **They prefer Blender for animation**: IK, timeline, scrub, graph editor,
  "most assets are probably ready for Blender". Godot should only play the
  finished thing. They asked for Blender tools that let them edit, import,
  export, redo and see the result at once without a rebuild.
- **Hard requirements they added**, in their words, summarised:
  1. Mechs stay voxel-destructible, as they are now. (They first said
     "hollow, layered, glowing inside", then said that was only an example of
     bullet holes and lost parts, which already exist. **No remodel.**)
  2. While the mech is alive, the animation must carry on whatever it has
     lost: one leg, no legs, one arm, no arms, any combination. The balance
     point moves with what is left.
  3. The importer builds a destruction map, so the game does lookups rather
     than analysis.
  4. **The overarching goal: less runtime cost, with quality assets and
     look.**
  5. Mechs are mainly **support vehicles**. Their AI asks for support or
     replacement parts and repairs itself.

## 2. The settled design (0046), point by point

**Death.** A mech dies only when its core is damaged enough or its head is
cut off or destroyed. Every limb can be lost; it fights on, and lost limbs
can be repaired or delivered, the mech crawling to them if it must.
- Core = the walker's `Core` export (today the `Body` part). Threshold
  today: `MechTuning.BodyDeathFraction`, default 0.45, on
  `VoxelPart.Health01` (solid / initial cells).
- **Built (step 2).** The fighter's head is its own part, split by
  `tools/vox-tools/split_part.py` (the visor block, z 16-18 of `Body`,
  pivot at the neck). The user: the tank's `Turret` is its head, and it
  must be extra hard to shoot off. Both heads are `Hardened` (0046).
  `SpareLegs` is gone: legs lost leave a mech walking on what it has, until
  step 3 gives it gaits for that.

**The damage map, baked at import.** Beside what `VoxModelBuilder` already
writes per part as node meta (`VoxelMeta.Cells`, `Dims`, `Origin`, the chunk
bake `Chunk*` from `VoxChunker`, read by `VoxelChunkGraph`). Per part:
- voxel count and mass, and centre of mass;
- the voxels that hold it to its parent at the hinge (its attachment);
- its contacts: soles, fists, knees, elbows: where it can bear weight;
- a copy of its original cells, for repair.

Runtime: a hit removes cells, subtracts mass, checks the attachment.
Nothing that scales with voxels runs in the frame (0015). The existing
carving, chunks and dust are unchanged. No layers, no interiors.

**Gait from what is left.** Balance = centre of the remaining parts' baked
mass, over the support of whatever still touches the ground.
- all legs: walk (as now);
- fewer legs: limp or hop, a free arm reaching down as a crutch;
- no legs: haul itself on its fists;
- no limbs: turn on the spot, still shooting.

Each gait is a procedural rule the walker plans steps with. Any can take a
style loop from Blender (sway, swagger) layered on top. All three mechs:
the biped fighter, the four-legged tank and the four-legged storage bot.

**Repair and resupply, as AI.** A new brain beside `MechCompanion` reads
the damage map and asks for help through **one `EventBus` signal** (a
damaged mech cannot name its repairer; justify it in 0006's way).
- Light damage: calls the mobile storage bot, or goes to it. The bot
  regrows lost cells from the baked copy, outward from the hinge. Repair is
  destruction played backward, on the same `VoxelWorkers`.
- A lost limb: a replacement part is dropped in a pod. The mech walks,
  limps or crawls to it, and it is fitted at its hinge.
- Badly hurt: it falls back behind the player and asks for cover.
- **The storage bot** (`scenes/vehicles/mch_mobile_storage.tscn`, the
  smallest mech) is mainly storage (`MechSupply.cs` hands out ammunition
  within 4 m) but **transforms** to repair, other bots included. *The
  transform is not designed yet; ask the user before modelling it.* If
  every bot is destroyed, a supply drop brings more, at a price.

**Cost.** The price of the material: every restored voxel, part and dropped
bot, priced in `resources/vehicles/mech_repair_default.tres`, entered in
the contract's ledger in `Session` (`scripts/mission/Session.cs`, the third
autoload; state only: offers, accepted contract). Once the fight cools, an
owned mech's bill is paid in the garage screen; a hired mech is billed at
the end of the contract. **There is no money in the game yet.** The ledger
records; the screens come with the economy. Never invent prices in code.

**Moves, posed in Blender, baked to data.**
- A Blender add-on of our own, in `tools/blender/` (`tools/` is
  `.gdignore`d; this is not a Godot addon, so rule 7 does not apply).
  Blender 5.1.
- **Import:** reads a mech's `.vox` with the importer's own rules: one
  object per MagicaVoxel object, origin at its palette-255 pivot voxel,
  `@Name` objects as empties, 0.1 m a voxel, MagicaVoxel `-Y` front to the
  game's `+Z`. Each part fixed whole to one bone (**rigid, never
  skinned**). IK handles on fists and feet. Re-import after a model change
  swaps meshes and keeps bones and actions, matched by name.
- **Author:** a move is a Blender action. Timeline markers are its events
  (`hit`, `sfx:<name>`, `shake`). It names the parts it claims.
- **Export:** bakes each part's transform against its rest pose to a
  `MechMove` `.tres` (`resources/vehicles/moves/<mech>_<move>.tres`). No
  rebuild.
- **Play:** the walker plays a move as an override, relative to the body.
  Unclaimed parts keep walking. A missing part's keys are skipped; a move
  whose key part is gone is mirrored or withheld. When a move claiming legs
  ends, the feet are planted where they are. No IK in the game beyond
  `MechLeg`'s feet. Cost: about ten part transforms a frame.
- **See it at once:** while posing, the add-on mirrors the pose live into
  the Godot editor through the editor bridge; a debug game reloads changed
  move files without a restart.
- The existing slam (`MechSlasher`, `MechSlashStats`: `CockPitch`,
  `StrikePitch`, `CockYaw`, `StrikeYaw`, timings) becomes the first move.
  The user wants two-fisted smashes and more (ground pound, uppercut,
  backhand were floated).

**Two tools.**
1. **A mech lab in the Godot editor**: `addons/mech_lab`, a main screen
   beside Vines (`addons/vine_gen` is the model: `_HasMainScreen`, a live 3D
   preview in its Nest tab, panels rebuilt after a code reload), plus
   bridge commands registered with `Bridge.Register`. It draws the damage
   map (mass, centre of mass, supports, attachments), shoots parts off on
   purpose, shows the balance and gait switching at any speed and
   direction with pause, single-frame steps and slow motion, and plays a
   repair. The walker needs a **lab mode**: Godot runs no physics in the
   editor, and the walker touches physics only at `MoveAndSlide`
   (`MechWalker.cs:478`) and one ground ray (`MechWalker.cs:661`), so the
   lab mode stands the feet on a flat floor. A fifth addon: update
   CLAUDE.md's addon list (rule 7 and the folder map).
2. **The Blender add-on**, for moves and style only.

Plus `scripts/debug/mechcheck.gd` (headless: every mech `.vox` follows the
part rules and bakes a sane map) and a `bench.gd` mode that shoots limbs
off one by one, lets them repair and reports frames.

**Not chosen:** skinning the voxel mesh (smears, costs GPU skinning);
`AnimationPlayer` clips on the parts (the walker writes every part every
frame, and a clip cannot cope with a part shot off); authoring moves in a
Godot timeline (the user works in Blender); a hollow layered remodel.

## 3. Build order, a commit each

1. **The damage map in the importer**, and `mechcheck.gd`. Then force a
   reimport (delete `.godot/imported/*.vox-*`; `--headless --editor
   --quit`). Check the vines still pass `vinecheck.gd`, and the buildings
   `graphcheck.gd`, since they share the importer.
2. **The fighter's head split out of `Body`** and the new death rule (core
   or head). Wire `Head` on the walker. Ask the user about the tank's turret
   first.
3. **The mech lab** with balance and gaits from what is left, all three
   mechs. Bench mode for damage. This is where the walk is also judged
   and improved. *Built: the lab became the asset lab (0047); the gaits
   are in (0046, step 3); `limbs` is the bench. Walking a mech in the tab
   without Test is not built: Test walks it with real physics.*
4. **Repair and resupply AI**: the brain, the bus signal, the storage bot
   regrowing cells, the part pod (`scripts/mission/DropPod.cs` is the
   company's existing pod: falls, lands like a bomb, opens; a starting
   point), the ledger in `Session`, the repair `.tres`.
5. **The Blender add-on**: import and rig first (the user poses the
   fighter), then export, `MechMove` playback, the live mirror and the hot
   reload. Rebuild the slam as a Blender action to prove the path end to end.
6. **New attacks** and the walk's style loop, made with the tools.

Bench every stage on the target machine (60 fps at 1080p on Iris Xe;
`bench.gd`; `mech`, `mechs`, `mechwreck` modes exist in
`lvl_horde_flats`). Promote 0046 from *proposed* when its first stages are
built and measured, and record the numbers in it.

## 4. Facts measured today

| Mech | Scene | `.vox` | Parts (objects) | Voxels | Enclosed |
|---|---|---|---|---|---|
| Field Fighter (biped, arms) | `scenes/vehicles/mch_field_fighter.tscn` | `art/voxels/mch_field_fighter.vox` | `Body`, `ThighL/R`, `ShinL/R`, `FootL/R`, `ArmL/R`; markers `@Seat`, `@Muzzle`, `@Cannon` | 3,642 | 18 % |
| Quadruped Tank | `mch_quadruped_tank.tscn` | `mch_quadruped_tank.vox` | `Body`, `Turret`, `Thigh/Shin/Foot` × `FL FR BL BR`; `@Seat`, `@Muzzle`, `@TurretMuzzle` | 28,415 | 71 % |
| Mobile Storage | `mch_mobile_storage.tscn` | `mch_mobile_storage.vox` | `Body`, `Thigh/Shin/Foot` × `FL FR BL BR` | 17,191 | 71 % |

- All three are in `scenes/levels/lvl_horde_flats.tscn` (also
  `lvl_pipeline_test`, `lvl_planet_test`).
- Numbers: `resources/vehicles/mch_*_tuning.tres` (`MechTuning`),
  `mch_field_fighter_guns.tres`, `_flamer.tres`, `_slash.tres`,
  `mch_quadruped_tank_guns.tres`, `_shell.tres`. Armour:
  `resources/voxel/voxel_destruction_mech.tres`.
- Code: `scripts/vehicles/`. `MechWalker.cs` (1,055 lines: gait
  `UpdateGait` at 510, body spring `UpdateBody` at 674, arms `UpdateArms` at
  898, `StrokeArm` at 798, lost parts `OnPartEmptied` at 322, which breaks a
  whole leg away), `MechLeg.cs` (two-bone IK on plain nodes), `MechCompanion`
  (follow, ride), `MechGunner`, `MechSlasher`, `MechSupply`.
- Voxels: `VoxelBody` (`PartOf(node)`, `PartEmptied` event, `ApplyDamage`,
  `Hinged` parts), `VoxelPart` (`Health01`, `IsEmpty`, `Carved` and
  `Emptied` events, `Carve`, `Cut`, `Shatter`, `BreakAway`).
- `art/_source/FieldFighter/fighter.blend` holds one merged mesh `fighter`
  (9,242 verts, 3.8 × 5.9 × 1.6 m, rotated 90° on X), a camera and two
  lights, no armature or actions; `fighter.glb` beside it. Not used by the
  plan: the add-on imports the `.vox`.
- **Step 1 is built** (`VoxDamageMap`, `mechcheck.gd`). A part's parent is
  the part with a voxel nearest its pivot; the core is the one part with no
  pivot. Contacts are a `Sole` (lowest layer at rest) and a `Tip` (far end
  from the pivot: fist, knee, ankle). Mass is voxels times the stats'
  `Density`, not baked. The copy for repair is `vox_cells`, never written.
- **The tank had no `VoxelBody`** (its walker named `Voxels`, the wrapper
  never had the node, so it took no damage). Wired on 25 October with the
  mech armour and every part but `Body` hinged. Paired benches: `mech`
  unchanged; `mechwreck` +0.5-1 ms mean while shooting, now that it breaks.
  `mechwreck` barely touches it: the mechs follow the player out of its
  aim (body 99 % at the end). Step 3's limb bench should pin them.
- The fighter stands on two one-voxel spikes: its centre of mass is 0.08 m
  outside its feet at rest. Only three or more feet are held to balance.
- Blender facing and scale are still listed as unverified in CLAUDE.md;
  the add-on, doing its own `.vox` mapping, settles it. Update CLAUDE.md's
  table when it does.

## 5. Driving Blender

- The user runs **Blender 5.1.1** with Blender Lab's official **MCP**
  add-on (`%APPDATA%\Blender Foundation\Blender\5.1\extensions\lab_blender_org\mcp`),
  listening on `127.0.0.1:9876`.
- Claude Code's `blender` MCP server (in `~/.claude.json`) was fixed on
  25 October to run
  `%LOCALAPPDATA%\Claude-Data\claude_pzs8sxrjxfjjc\Claude Extensions\ant.dir.gh.blender.blender-mcp\.venv\Scripts\blender-mcp.exe`
  (Claude Desktop had moved the extension; backup `~/.claude.json.bak-blender-mcp`).
  It offers 26 tools: `execute_blender_code`, `get_objects_summary`,
  `get_screenshot_of_window_as_image`, `get_screenshot_of_area_as_image`,
  `render_viewport_to_path`, API and manual search, and more. If the tools
  are not in the list, `/mcp` reconnects.
- Without the tools, the socket is direct: send
  `{"type":"execute","code":"<python>","strict_json":false}` followed by a
  NUL byte; the code sets a dict `result`; the reply is JSON ending in NUL.
  Verified by building, rendering and removing a test scene.
- **Never change the user's open file unasked.** Work in a new scene or a
  new `.blend` under `art/_source/<Mech>/` (`art/_source/` is
  `.gdignore`d and in LFS).
- MagicaVoxel has no API: Claude reads and writes `.vox` files only, and the
  user reopens a file to see a change. The `voxel-art` skill builds `.vox`
  models in Python.

## 6. The user, for this work

- Wants to see motion and judge it, not read numbers. Show frames and
  screenshots (`view`, Blender screenshots, `--write-movie`).
- Plain words; no jargon dumps.
- Proposes ideas freely and expects them weighed, not adopted blindly; ask
  when a choice is genuinely theirs (the storage bot's transform; prices).
- Still open: the transform's design.
