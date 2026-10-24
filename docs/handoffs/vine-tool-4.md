# Handoff: the Vines tool, part 4 — the fling, the tool's Good/Bad, the records; and the game's path of destruction

*24 October 2026.* Read `CLAUDE.md` first. The earlier handoffs hold the
history: `vine-tool.md` (the task, its settled decisions and build order),
`vine-tool-2.md` (the first nest played), `vine-tool-3.md` (leaves, then
step 7 pods and step 8 the tool, with their status sections at the top). This
file is where things stand now and what to do next. **Work only inside
`C:\Projects\dill-control`.** The rules are the same: one system at a time,
numbers in `.tres`, bench on the target machine, short commits of your own
files only (never `docs/project_board.json`, `engine/` or `.vscode/`).

## Status, 24 October 2026 (later)

- **Report 3 (Good/Bad) is done** (c6df224). The Vine tab has Good/Bad and
  a note under its preview. A click on a range-lab card opens that very
  roll in the Vine tab: its settings, attempt 0. It's marked "as the range
  lab rolled it" when that isn't the vine the seed grows from the kind's
  file, and then it bakes with a `c` name. Both write one record
  (`VineLogs.JudgeVine`, with `changed`). Log keys now keep their order
  (Godot's JSON sorted them). The Nest tab's verdict sits under Play.
  `rangelab-check` and `reload-check` cover the Vine tab. Both passed in a
  real editor, run with every `.vox.import` snapshotted and compared after:
  none changed.
- **Report 2 (the fling): guarded, not solved** (d9b139e). Ruled out by
  reading: a respawn (nothing in the lab calls `RespawnPlayer`), the
  under-ground rescue (it only lifts along the player's own up), the bike
  (capped every tick at 78 m/s x 1.8, and a CharacterBody), and shoves
  collected while riding (the stowed player is off every layer, and the
  crowd checks `IsStowed`). "17801 PICKLED" on the HUD is kills.
  The player falls at 1.75 g, so orbital speed is about 146 m/s; anything
  near it reaches kilometres. Two live causes: shoves summed in one tick
  (they had no limit; HE is 44 m/s each), now capped at
  `MaxShoveSpeed` 50; or a vine section's platform velocity
  (`VoxelPart.BendSections` gives a near section `(offset - was) / dt`, and
  Godot adds it to the player leaving it). The `[fling]` guard prints and
  logs (PerfLog event) a player over 70 m/s or moved over 20 m between
  ticks, with the shoves, the platform's speed and what it touched. Next
  time it happens, the console or the `U` summary says which.
  `flingcheck.gd` holds the cap and the guard.
- **Report 1 (path of destruction): waiting on the design talk** with the
  user; questions asked.

## Where it stands

| Step (vine-tool.md build order) | State |
|---|---|
| 1-6. Generator, in-memory vines, valid-only, mesh leaves, colour, the nest | done |
| 7. Pods and hanging pickles (decision 0034) | done |
| 8. The main-screen tool (Nest tab, range lab, vine panel, Play, Good/Bad/Save) | done; the user played with it and called it good |
| 9. Decision records (rewrite 0033, write new ones) | **not started** |

The user's verdict after playing: **"The tool is good."** Their two new
reports, below, come first.

## The user's two reports from playing (do these first)

### 1. "Add more path of destruction" (the whole game, not the generator)

Playing on the hoverbike and lasering everything, the user sees vines
collapse, but then most of the debris disappears and nothing is left to show
where they went. In their words: **"the player should be able to see what he
did: destroyed vine nest, burning everywhere, mountains of debris. Now,
nothing."**

**This is a gameplay goal for the whole game, not a vine-generator or tool
task.** Everything the player destroys (vines and nests, buildings, mechs,
the ground, pickles) should leave lasting, visible destruction behind: wreck,
fire, debris piled up, and it stays. The vine nest is where it showed, and
the vine debris below is the first place it fails, but the work belongs to
the destruction systems (`VoxelDebris`, `VoxelChunk`, `RubblePile`, the
dust, fire and scorch effects, the planet's craters), and it should be
planned as such: a design talk with the user, likely a decision record (it
revisits 0016's rubble and 0022's recovering destruction), then built system
by system. It may deserve its own handoff once scoped.

What makes it vanish today, all measured or read this session:
- **Vine pieces crumble after `CrumbleAfterSeconds` (20 s)**
  (`voxel_destruction_default.tres` via the class default in
  `VoxelDestructionStats`; the bake loop in `VoxelDebris`). This session
  added it, to stop vine pieces being baked into blocky heaps. It now works
  against the user's wish.
- **Past the loose-piece cap a piece becomes dust before it is made**
  (`MaxChunks`, `HasRoom`, `cap-dusted` events). A laser sweep through a
  nest makes many pieces at once; most go straight to dust.
- **Vines leave no rubble** (`LeavesRubble` off in
  `voxel_destruction_vine.tres`). This session turned it off because the
  heaps were wrong, not because heaps are: baking re-cut a fallen stalk on a
  grid square to the ground and left blocks floating in the air (the user's
  earlier screenshot), and crushed vine was laid down as beige core cubes.
  **The user wants mountains of debris**, so vines need heaps again, done
  right: what fell lying as it fell (logs, crowns, arms, leaves), piling up
  where vines came down, on the ground and never in the air.
- **Vine dust is off** (`DustScale = 0` in `voxel_destruction_vine.tres`):
  shots and crushes on a vine raise nothing. Only `PlantFlex.FallDust` (the
  big dark clouds when a vine falls) is on.
- **Torn leaves** puff after `LooseSeconds` (8 s); **falling dead pickles**
  puff after `FallingSeconds` (5 s) and are capped at 32.
- A felled vine now snaps into a few logs (`SnapMinVoxels` 4,000,
  `SnapReachM` 5 m) instead of shattering at every bounce. That was a fix and
  should stay.

What it takes, from the user's three words (ask about the details and the
look; show screenshots as you go):
- **Destroyed vine nest**: stumps, felled vines lying where they fell,
  stripped arms, torn leaves on the ground, empty pods. All of it stays.
- **Burning everywhere**: fire on what was hit and lasered, and on the
  ground. Fire exists already (`FireField`, `FireZone`, `FireStats`,
  `ToonFlame`, the burner weapon, `fx_scorch_trail`): read what they do,
  and whether vines and leaves can catch and keep burning, then smoulder and
  leave scorch.
- **Mountains of debris**: heaps of what fell, piling up where vines came
  down.

Parts that may serve it:
- **Fallen logs stay** as sleeping bodies, or, once settled, become static
  (no body, no physics cost). They could be merged into one static mesh a
  nest, so hundreds cost a few draws. Small bits may still crumble.
- **Stumps stay** standing where vines were cut (they already do; check
  they survive the cap and the crumble).
- **Scorch on the ground** along the laser's line and under explosions:
  decals or a ground-texture paint. A scorch trail shader already exists
  (`shaders/fx_scorch_trail.gdshader`): check what it does now.
- **Craters stay** (the planet is deformable: `PlanetTerrain.Crater`).
- **Corpses**: dead pickles lying a while longer, capped and instanced like
  the horde rather than a node each.
- **Torn leaves** lie on the ground instead of puffing (they're on Litter and
  rest already).

Whatever is chosen has to hold the frame: every phase of a 25,000 nest is
already over budget (below). Persistent debris must be cheap to draw and free
to simulate once at rest. Measure with `bench.gd ... flex <vine> 12 rec`
(records a rocket-felled vine; read the summary's events) and with a laser
sweep. A bench mode that sweeps the bike's laser through the nest doesn't
exist yet; `lasercut` does it to a tower on the flats and is the model.

### 2. "20 mins in, I get flown away to the other side of the planet"

After 18 minutes of play (colony awake, 29,703 dropped, 13,940 of 20,408
pods left), the user was suddenly far from the nest, on bare ground, with no
vines in sight. Cause **unknown**. Nothing has been looked at yet.
Hypotheses, to check in order:
- **A blast or physics push**: the player standing on, or riding the bike
  into, a falling log or a stack of sleeping pieces, which then wake and
  shove it. Logs are big bodies now (this session made them lie whole). The
  player's mask includes Prop; loose chunks may be on it.
- **The bike**: being flung out of it, or its collision with debris at
  speed. `HoverBikeSystems`, `LimitThrow`.
- **The gravity gun or manipulator**: holding or throwing a big piece.
- **The planet's under-ground rescue** (vine-tool-2, fix 5) putting the player
  "back on the surface" somewhere wrong. Check where it puts them, and
  whether it ever fires far from the player's last place.
- **A respawn after being pickled**: the HUD's top counter read "17801
  PICKLED". Find out what that counter counts (kills, or times the player was
  downed: `PlayerHealth.TimesPickled`), and where a downed player comes back.
  `LevelRoot` makes the player at `Spawns/PlayerSpawn`.

Repro: play the 25,000 nest from the tool with the bike, laser for a long
time, and record with `U`. The summary's events and the player's position
column will show the jump. Or add a guard that logs (`PerfLog.Event`) any
frame where the player moves more than, say, 200 m.

### 3. "Where is Good, Bad?" (the tool)

The user looked for Good/Bad on the **Vine** tab (one vine: kind, seed,
preview, report, Bake) and found none. Today:
- **Nest tab**: Good/Bad, a note, Save and the Saved list are in a
  "Verdict" section at the bottom of the left column, under the measures;
  it has to be scrolled to, and the user didn't find it.
- **Range lab**: Good/Bad on every thumbnail card.
- **Vine tab** (`VinePanel`, also the dock): none.

The user's answer: **the Vine tab must have Good/Bad, because that's where
you actually see the vine.** And: **pressing a vine in the range lab opens it
in the Vine tab**, that kind and seed, to look at it there and judge it.

To do:
- **Clicking a range-lab card opens that vine in the Vine tab**: switch to
  the tab, select the kind, set the seed. The card's vine is its seed's own
  roll (attempt 0, no re-roll), while the Vine tab grows a seed through
  `VineGrower` (re-rolling a failed roll). The Vine tab must show the
  same vine the card did: load the card's settings
  (`VinePanel`'s by-hand settings path, `VineGrower.FromSettings`), or give
  it a way to show attempt 0. Today a click on a card only marks its values
  on the spread bars.
- **Add Good/Bad (and a note) to the Vine tab**, logging to
  `tools/vine-logs/vine_verdicts.jsonl` in the same record shape as the range
  lab's (`RangeLabTab.Judge`: kind, seed, attempt, generator, valid,
  reasons, every setting, measures). If the settings were changed by hand,
  log them as they are and mark it (`changed: true`), and add that field to
  the log's `about` line.
- **Make the Nest tab's verdict visible without scrolling**: put
  Good/Bad/note/Save under Generate and Play, or right after the play's
  results, so they show as soon as play stops.
- Check both in a real editor, including after a rebuild (`reload-check`).

## Waiting on the user

- **Distant shadows.** Sun shadows stop at 140 m in `lvl_nest_lab` (and
  `lvl_contract`), and vines further than 60 m (their coarse copies,
  `CoarseShadows = false` in `voxel_destruction_vine.tres`) cast none. The
  user called the shadowless trees "sad". Measured paired at 25,000: shadows
  to 400 m in four splits plus coarse copies casting cost about 0 on the
  ground but **+1.5-2 ms GPU in the canopy (+250 draws)**. Asked: on, a
  cheaper middle (say 250 m, two splits), or off. No answer yet.
- **The kinds' new looks** (this session): weeping droops like a willow,
  fruiting is stout with short level arms, heart is the tallest with a gentle
  spiral and flat arms. The user hasn't judged them in play yet. See
  `perflogs/kinds/vines.png` on the laptop.
- **Pickles falling on the player** when the colony wakes hurt them once
  each, like a thrown cornichon (`SpawnFlung(..., lobbed: true)`). Asked
  whether it's fair; no answer.
- **"Infinite ammo"** tops weapons up twice a second; magazines still reload.
  Ask whether they want no reloads at all.

## What this session did (commits fdc22bf..de11d01, 26 of them)

Step 7, pods (details in `vine-tool-3.md`'s status):
- `PodField` (bulk pods, anchored to a voxel, bent on the GPU),
  `pickle_hang` (instanced hanging pickles by ground cells), `PodHitProxy`
  (shooting them), the wake into the horde (`SpawnOrigin.Pod`), regrowth (only
  for pickles that died, never past the pods), `ColonyMission` and
  `ColonyHud` (destroy every pod with at most 20 % alive; the bonus for none),
  `podcheck.gd` (19 checks), `bench.gd ... colony pop=25000`.
- A destroyed pod's pickle falls into the horde (or dead); it never vanishes.

Step 8, the tool (`addons/vine_gen`, the "Vines" main screen):
- Nest tab: plan, preview on a flat disc, metrics, Play through
  `NestPlay` (`user://nest_play.json`), playtest results (`NestPlaytest`),
  Good/Bad to `tools/vine-logs/nest_verdicts.jsonl`, Save as `NestPreset`,
  Saved list. Checkboxes for infinite ammo and a parked hoverbike.
- Range lab: N seeds of a kind as they roll, thumbnails, Good/Bad to
  `vine_verdicts.jsonl`, spreads, edit and save ranges.
- Vine tab: `VinePanel`, also in the dock.
- **It survives a rebuild now.** The editor doesn't carry a [Tool]'s C#
  signal handlers over a reload, so every button went dead after the user's
  first rebuild. The plugin tears its panel and dock down and rebuilds them
  on the first frame after a reload (`VineGenPlugin`), and
  `--editor -- reload-check` proves it. Godot's JSON replaced
  System.Text.Json in editor code (its type caches blocked the unload). The
  user's editor had to be restarted once.

Fixes found along the way:
- `Rod`'s calibration cache was written from every nest worker at once and
  could fail a load (now concurrent).
- A voxel part's up was world +Y everywhere; on the planet it's now
  `WorldUp` (dust, topple and hinges went the wrong way 60° round the planet).
- Vines leave no rubble, don't crush through, snap into logs, crumble after
  20 s (see report 1: the crumble is now too eager).
- A felled vine falls in big dark dust (`FallDust*` in `flex_vine.tres`).
- The colony HUD: goals top right sized to their words; the colony's state a
  debug line bottom left.
- Distinct vine kinds (above); baked vines re-baked, golden vines
  re-recorded; `vinecheck.gd -- rejections` prints example failures.

## Measured

25,000 nest, plugged in (`colony` bench, cool machine):

| Phase | Mean | GPU |
|---|---|---|
| Calm, under the canopy | 14.7 ms | 12.8 |
| Calm, in the canopy | 14.6 ms | 12.8 |
| Waking | 15.7-16.3 ms | 13.7-14.1 |
| In the fight | 16.9 ms | 14.7 |

The `nest` bench on a warm machine reads 17-21 ms and swings by up to 5 ms
between runs with nothing changed: always pair. The fight's cost is the
horde's own update at 25,000 (about 2 ms CPU). The pods cost about 0.5 ms
CPU and under 1 ms GPU. Load at 25,000 is about 22 s; managed memory about
1.06 GB, engine 0.65 GB.

Vine validity on a seed's own roll (60 seeds): fruiting 93 %, heart 100 %,
weeping 95 %, leafy 53 %. 25,000 is 117 vines, 25,062 pods, 393 m out
(weeping 51, fruiting 43, heart 23).

## Known and not yet done

- **The `reload-check` reset every `.vox` file's import settings.** At
  11:03 on 24 October a headless editor running `--editor -- reload-check`
  reloaded the project's C# under itself; the voxel importer
  (`addons/vox_importer`, C# too) was reloading when the editor reimported
  the `.vox` files, and every `art/voxels/*.vox.import` was rewritten with
  the default options: `voxel/size` 0.1 instead of 0.5 (0.04 and 0.11 for two
  mechs), and new uids. Vines, trees, towers and mechs imported at a fifth of
  their size. The `.import` files were restored from git
  (`git checkout -- art/voxels/*.vox.import`) and everything reimported the
  same day with the user's editor closed: a fruiting vine imports 70.5 m
  tall again, the heart 110.5 m. **Before running `reload-check` again**,
  make it safe: run
  it on a copy of the project, or snapshot `art/voxels/*.vox.import` first
  and restore them after. And find out why a reload makes the editor
  reimport with defaults: it may also happen in the user's own editor on a
  rebuild while a `.vox` is being reimported.

- **A rifle round on bare ground near the nest doesn't wake the colony**;
  nothing broadcasts a ground hit. A new `EventBus` signal needs a decision
  record (rule 3). Hits on pods, vines and leaves and any blast do wake it.
- **Nearest-neighbour spacing goes down to 45 m**, under the 55-70 m the user
  set; `NestKit.Jitter` (0.22) pulls slots in.
- **Whether the colony ever goes back to sleep**, and **whether a hanging
  pickle's kind should match what it drops as** (all hang as Standard),
  are undecided. Ask.
- A felled vine on the planet can lean on its neighbours and fall very slowly
  (tilt 91° after 17 s in the `nest` bench's crater phase).
- Every phase of a 25,000 nest is over the 16.7 ms budget. Levers from
  earlier sessions: a nest-wide leaf MultiMesh per shape (draws from above
  about 900), shadows, the leaf count.
- The Nest tab's preview is a flat disc, not the planet (its classes are
  placeholders in the editor). The user asked about it once; the played nest
  is on the real planet.

## Step 9, the decision records

Unchanged from `vine-tool-3.md`: rewrite 0033 (load at 25,000 is about
22 s now; saved nests are `NestPreset` seeds, not baked vines), and write
records for leaves as meshes (with walkable stems and holes), nest sizing
(the clump), the 1,260 m planet, the Structure and Litter layers, the
deterministic re-roll gate, the tool, and now also: pods in bulk with the
GPU-bent pickles; the colony's wake; vines leaving no rubble (an exception
to 0016); the tool's reload rebuild.

## How to work here

The lessons in the earlier handoffs hold (check the power; no foreground
`sleep`, cool the laptop with `timeout 45 tail -f /dev/null`; no engine
calls on nest workers; measure a nest by loading it headless). Added this
session:
- **Python heredocs turn `\n` inside C# strings into real newlines.** Write
  edit scripts to a file with raw strings (`r"""..."""`), or use the Edit
  tool, for any line with `\n`.
- **Test editor code in a real editor, including a reload.** The headless
  and fresh-editor checks passed while every button was dead after a
  rebuild. `--editor -- nesttab-check=<png>`, `rangelab-check=<prefix>` and
  `reload-check` cover the tool. The editor draws only when something
  changes: a SubViewport inside a hidden container never renders, and a
  capture must wait for `Engine.GetFramesDrawn()` to advance, with
  `QueueRedraw` on the base control to make it.
- **Don't run windowed benches while the user is using the machine.** Ask
  first; they take the screen for minutes.
- **Headless "tool play"**: write `user://nest_play.json` and load
  `lvl_nest_lab` from a SceneTree script, as the Play button does. That's
  how the request, bike and result were tested.
- **The flex bench's `rec`** records a rocket-felled vine; read
  `perflogs/perf_*_summary.txt` events (`landed-break`, `cap-dusted`,
  `baked`). That's how the shattering was found (97 breaks a fall).
- **`vinecheck.gd -- rejections N`** now prints example failures with counts
  (blocked, too low, crowded): the way to tune a kind.
- To change a kind: edit its `.tres`, check `rejections`, re-bake each baked
  vine (`vinebake.gd -- <kind> <seed>`), delete
  `.godot/imported/prp_vine_*.vox-*`, `--editor --quit`,
  `vinecheck.gd -- record`, then `vinecheck.gd`, and check a 25,000 plan
  isn't short (`[nest] ... short` in a headless load).
