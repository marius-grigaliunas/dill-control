# Handoff: walking a mech and playing its animations in the Lab tab

*25 October 2026.* Read `CLAUDE.md`, then `docs/decisions/0046` (mechs
broken, mending, moves from Blender) and `0047` (the asset lab). Work only
inside `C:\Projects\dill-control`; the project's rules hold (one system at
a time, numbers in `.tres`, the editor changed through the bridge, commits
by pathspec of your own files). The user interviewed for this on
25 October; their answers are below and are settled.

## What the user wants

In the **Lab tab** (`addons/asset_lab`), with no Test and no physics:

1. **Walk a mech, as a treadmill.** The mech walks on the spot; WASD sets
   its speed and direction (Shift sprint); the camera stays free (orbit as
   now). Today nothing walks in the tab: the user pressed W and nothing
   happened.
2. **A play button for its animations**, covering:
   - every **Blender move** (`resources/vehicles/moves/*.tres`), picked from
     a list;
   - the **whole combo** as it flows in the game (`MechSlashStats.Combo`,
     stroke into stroke, `ChainAfterHit`, `ChainBlend`);
   - the **walk cycle** (the treadmill above, at a chosen speed);
   - the **broken gaits** (limp, hop, crawl, sit).
3. **Inspecting** a playing animation: play / pause / loop; a **timeline
   scrub** frame by frame with the move's markers (`hit`, `shake`) drawn on
   it; **speed** (0.1x to 1x); **onion skin or trails** (ghost poses, or
   the fists' paths through a swing).
4. **Broken gaits by clicking parts**: click a leg or arm in the view to
   take it off, click again to put it back; the stance follows
   (`MechWalker.CurrentStance`).
5. **Editing: timing in Godot, poses in Blender.** Speed, blend and marker
   times (`hit`, `shake`) are edited in the Lab and saved to the move's
   `.tres`, **and written back to Blender**: the add-on
   (`tools/blender/dill_mech`) reads them into the action (markers, and the
   keys rescaled for a speed change) so a re-export never loses them.
   Poses stay in Blender.

## What exists to build on

- `MechWalker` (`scripts/vehicles/`) holds the gait (`UpdateGait`,
  `UpdateBody`, `PoseLegs`, `PoseArms`), stances from what is left
  (`UpdateStance`, `MechStepper`/`MechCrutch`), and move playback
  (`PlayMove`, `PlayingMove`, `MoveTime`, `MoveEvent`, `MoveArms`).
  It touches physics only in `Move()` (`MoveAndSlide`), `Ground()` (a ray)
  and `GetGravity()`.
- **The catch:** the walker is not `[Tool]`, so in the editor its scene
  instance has a placeholder script and none of its code runs; the Lab's
  preview shows the mech at rest only. The likely way: `[Tool]` on
  `MechWalker` with every callback guarded (`Engine.IsEditorHint() &&
  !LabMode` returns at once, so it does nothing in edited scenes), and a
  lab mode where the ground is the plane y = 0, `Move()` does not call
  `MoveAndSlide`, and on a treadmill the body stays put while planted feet
  slide back at the walk's speed. Its siblings (`VoxelBody`,
  `MechCompanion`, `MechGunner`, `MechSlasher`, `MechCare`) stay
  placeholders in the tab; the walker already treats a missing
  `VoxelBody` as every part alive. Clicking a part off in the tab must go
  through the walker (mark the leg dead, hide the part, `UpdateStance`),
  not voxels.
- The Lab tab: `AssetLabPanel` (layout, folding sections), `LabPreview`
  (the view, damage map overlay, `Capture`), `AssetLabCommands` (bridge
  commands; add the new controls there too so Claude can drive them:
  play, pause, scrub, speed, walk, remove part). Panels rebuild after a
  code reload; keep handlers reload-safe (`docs/decisions/0043`).
- Moves: `MechMove` (`Fps`, `Frames`, `Parts`, `Positions`, `Rotations`,
  `EventNames`, `EventTimes`, `Sample`, `EventTime`). The add-on writes
  them (`export.py`); `fighter_moves.blend` holds the fighter's actions
  (`slam_r`, `slam_l`, `uppercut_r`, `backhand_l`, `smash`).
- The live mirror (`lab_pose`) already poses the preview from Blender.

## Suggested order, a commit each

1. Walker lab mode and the treadmill in the tab (WASD, speed, direction).
   *Built 25 October.* `MechWalker` is `[Tool]` and does nothing in the
   editor unless `LabMode` is set before it enters the tree; the tab steps
   it with `LabStep` (`LabPreview._Process`, `TimeScale` ready for the
   transport). On the treadmill the body stays put and planted feet slide
   back by `BeltTravel`, which also moves the view's tiled ground. Keys:
   click the view, WASD, Q/E turn, Shift sprint; a speed slider under the
   bar. Bridge: `lab_walk` (forward, right, turn, sprint, seconds).
   `MechTuning`, `MechMove`, `MechSlashStats` are `[Tool]` too (a
   resource script must be, to be read typed in the editor). The walker's
   `Voxels` export is a plain `Node` now (in the editor `VoxelBody` is a
   placeholder and a typed export threw); code reads `VoxelParts`.
   `mechcheck.gd` passes, `bench.gd ... mechs` runs as before, and
   `lvl_horde_flats` opens in the editor with no errors or changes.
2. Clicking parts off and on, the stance following.
   *Built 25 October.* A click (not a drag) on a part in the view marches
   the camera ray through each visible part's cells (`LabPreview.PartAt`)
   and `MechWalker.LabToggleLimb` takes the whole leg or the arm off, or
   puts it back: hidden, marked off for `PartAlive`, `UpdateStance`. The
   core and head are not limbs and stay. The walk bar shows the stance and
   the last click. Bridge: `lab_walk` `toggle` (by part name) and `click`
   (clicks where a part is drawn, through the same picking). Seen: hop on
   a crutch arm, crawl on both arms, sit, and back to walking.
3. The transport: move list, combo, play/pause/loop, speed, the timeline
   with markers, scrubbing.
4. Onion skin or fist trails.
5. Timing edits saved to the `.tres`, and the add-on reading them back into
   Blender (markers, key rescale for speed).

Check each in the editor through the bridge and on screen; a bench is not
needed for editor-only work, but the walker change must leave the game
untouched (`mechcheck.gd`, and `bench.gd ... mechs` once).

## Known on 25 October

- The renderer is Vulkan now (`0048`); D3D12 hung at start.
- Building while a lab test plays, or while a `lab_test` call waits, has
  cost the editor's bridge before: wait for the editor to reload a build
  (it reloads when its window gets focus) before testing.
- One reload logged 176 `managed_callable ... nullptr` errors and
  "reloading succeeded after failures"; later reloads logged none. Watch
  for it when adding handlers to the tab.
