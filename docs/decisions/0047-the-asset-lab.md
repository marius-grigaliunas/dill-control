# 0047 The asset lab: one place to look at, tune and break any asset, in the editor and in play

*25 October 2026. Agreed with the user; built the same day: the tab, Test,
the lab level and Claude's commands. Replaces the mech lab of 0046 (its step
3 keeps the gaits; this is where they are watched). Built 25-26 October,
from the user's interview: a mech walks in the tab itself, without physics,
on a treadmill (`MechWalker.LabMode`); a limb clicked comes off and the
stance follows; its moves and the combo play on a timeline with markers,
scrubbed, slowed, with fist trails and ghost poses; and their timing (speed,
blend, markers) is edited there and read back into Blender. See
`docs/handoffs/lab-walk-and-moves.md`.*

## Context

0046 planned a mech lab: a main screen to watch a mech's balance and gait
with parts shot off. The user asked for it to be universal instead: test the
destruction, the look and the parameters of any asset that is imported or
made in the game, mechs included, and usable by Claude as the editor bridge
is. Judging an asset today means placing it in a level, running a bench
mode written for it, and reading numbers.

Godot runs no physics in the editor: nothing falls, no piece comes loose,
no mech walks. Destruction can only be tested in a running game.

## Decision

**`addons/asset_lab`, a C# `EditorPlugin` with its own main screen, a tab
beside 2D, 3D and Vines, and a lab level it plays.** The Vines tool is its
model (0043): plan in the tab, play in a lab level, survive a reload.

- **The tab** lists every asset: each wrapper scene under `scenes/props`,
  `scenes/vehicles`, `scenes/enemies` and `scenes/weapons`, and each bare
  `.vox` and `.glb` under `art/`. One is shown in a 3D preview of its own
  (orbit, sun and biome presets, outlines on and off, the asset at the
  sizes the game camera sees it), with what it is and what it costs:
  parts, voxels, chunks, meshes, surfaces, and for a mech its damage map
  drawn over it (each part's mass, the centre of mass, holds, soles and
  tips). Its resources (`VoxelDestructionStats`, `MechTuning`, flex,
  leaves, guns) are edited in an inspector. **Save writes the shared
  `.tres`**, after naming every other asset that uses it.
- **Test** writes `user://lab_play.json` and plays `lvl_asset_lab` with the
  asset, as the Nest tab's Play does (`LabPlay`, taken and deleted by the
  level's `AssetLabDirector`). The asset stands in front of the player on a
  grid floor with a ramp and steps. **WASD drives whatever can move**: a
  vehicle seats the player at once (`IRideable.BoardNow`, the mechs and the
  bike), a walker nobody rides is steered from a chase camera with its own
  brain off, and anything else is walked round on foot. The player's own
  weapons fire as in the game; the lab's tools act at the crosshair: a
  bite (one direct hit), a blast of a radius, a slit cut right through at
  that height, a part broken away. Time pauses, steps a frame and runs
  slow. A corner shows what it costs: frame, physics and GPU ms, draws,
  voxels left of those built, parts lost, loose pieces. Keys are letters
  (rule 6): N board/leave or steer, Y tool, X use it, - = strength, P pause,
  O one frame, T slower, K put it back, L help. Numbers in
  `resources/debug/asset_lab_default.tres`.
- **Walking a mech in the tab** (no physics: its feet stood on a flat
  floor) comes with 0046's gaits; until then a mech walks in Test.
- **Claude drives both.** The tab's actions are editor bridge commands
  (`Bridge.Register`, 0044), so they are MCP tools: `lab_assets`,
  `lab_open`, `lab_view`, `lab_resource`. `lab_test` plays the lab; the
  running lab listens on 127.0.0.1 (`LabListener`: one JSON line in, one
  out, a token in `user://lab_game.json`) and `lab_do` forwards to it:
  status, tools (aimed at the crosshair, a point, or a part by name
  wherever it is now), time, board, leave, reset, drive (input actions
  held for seconds), wait, and a shot of the screen or from a camera of
  Claude's own round the asset, drawn in a viewport of its own so the
  game's cameras are left alone. The game listens only in
  `lvl_asset_lab`, never in a contract. **Never rebuild the code while a
  test plays**: the editor's reload froze the game once.
- **It survives a rebuild**, as the Vines tool does: panels are rebuilt on
  the first frame after the project's code reloads; Godot's JSON, not
  System.Text.Json, in editor code that keeps state.

## What was not chosen

- **Physics in the editor**, stepping a private world under the tab:
  Godot's editor keeps its physics server off, and switching it on would
  move the bodies of the scene being edited too.
- **A lab only in the game**: the user wants it beside the other tabs, and
  looking and tuning need no physics.
- **A copy of a resource per asset on save**: it would split shared tuning
  silently; a per-model destruction `.tres` still needs a reason (0011).

## What it costs

- A fifth addon: CLAUDE.md's rule 7 and the folder map list it.
- Two halves to keep in step: the tab and the lab level read the same
  request file, and the bridge forwards to a game that may not be running.
- The preview in the tab is the asset at rest, or a mech walking and
  playing its moves on a flat floor with no physics; anything that needs
  physics (destruction, real ground, the crowd) is judged in Test.
- `MechWalker` is a tool script, and with it the resources it reads
  (`MechTuning`, `MechMove`, `MechSlashStats`): in a scene being edited it
  must do nothing, and does, unless `LabMode` is set.
