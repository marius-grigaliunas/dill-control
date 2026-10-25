# 0047 The asset lab: one place to look at, tune and break any asset, in the editor and in play

*25 October 2026. Agreed with the user; proposed, being built. Replaces the
mech lab of 0046 (its step 3 keeps the gaits; this is where they are
watched).*

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
  asset, as the Nest tab's Play does. The lab stands it on flat ground, a
  slope or rubble; time pauses, steps a frame and runs slow; any real
  weapon fires from the camera; precise tools take a bite here, set off a
  blast of a radius, cut along a plane, break a named part away; a mech is
  driven, and its gait and balance are drawn. What it costs is read from
  the frame profiler while it runs.
- **Claude drives both.** The tab's actions are editor bridge commands
  (`Bridge.Register`, 0044), so they are MCP tools. The running lab listens
  on 127.0.0.1 for the same JSON, and the editor's `lab_*` commands forward
  to it: one endpoint, the editor. A screenshot of either comes back as a
  PNG. The game listens only in `lvl_asset_lab`, never in a contract.
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
- The preview in the tab is the asset at rest; anything that needs physics
  is judged in Test.
