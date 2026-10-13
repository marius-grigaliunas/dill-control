# 0026 On trial: swing first, drop in by pod, extract on foot

*12 October 2026. A trial, not yet the game: the pitch and the plan are
unchanged until it has been played. Builds on 0017-0021 and 0023. Pauses the
pitch's arrival from orbit and the bike as the main way to fight.*

## Context

Three times now, playing has said the same thing: swinging and grappling
through things as they come apart is the best part. The pitch already says
so ("the grapple is the core verb") and the lab's playtest said it before
that. What has been built since works against it:

- **The bike solves everything.** It is fast, armed and safe, so there is no
  reason to be on the line. Grappling is fun as a toy and never needed.
- **Nothing is at stake.** With no objective and no real death, flying
  around destroying things is a sandbox, and it gets boring.
- **On foot, with health on, the horde wins.** Died 7-10 times a run off the
  bike. That is the pitch working ("the ground belongs to the horde"), but
  nothing gives the player a way to live in the air instead.

## Decision (to test)

- **A contract is drop, job, extract**, as in Risk of Rain 2 or Deep Rock
  Galactic. The player comes down in a pod; the job is to **cull half the
  horde and crack the jar**; then the company drops a pod at a far site, and
  the contract ends when the player gets into it.
- **On foot and on the line only.** No bike. Health on.
- **Clones**: three bodies a contract, the first included. Each one comes down
  in a pod at the same landing site as the first. When the third dies, the
  contract is void.
- **The test arena is its own 1 km terrain, not the planet.** The planet's
  ground exists only at runtime, so nothing can be dressed on it by hand.
  `ArenaTerrain` is a tool script: rolling noise, a rim of hills, and
  `TerrainFeature` nodes (hill, mesa, ridge, basin, pad) that are dragged in
  the editor while the ground follows. Up is +Y. The horde stands on it
  through `ArenaGround`, with the planet's slope costs, so a mesa's cliffs
  are walls the horde cannot climb.
- **The jar** is a graybox `.vox` (`prp_jar_block`, 24 m by 40 m at 0.5 m a
  voxel) on the mech armour stats. Its walls are 3 m thick at the foot and
  1.5 m at the shoulder, and its lid is 1 m. Inside is a glowing `Core`, and
  the jar is cracked when half of it is gone. Through the side is a long job;
  down through the lid is the short one, and getting up there is the line's
  job.
- **The planet, the system, the ship and the bike stay**, unchanged, and get
  no further work until the trial has an answer. Where the bike goes
  (a call-in, an upgrade, a boss) is left open.
- Numbers are data: `extraction_rules_drop_test.tres` (kill share, core share,
  clones, pod timings, extraction distance), `tier_drop_test.tres` (10,000
  pickles, the dead stay dead, aggro 280 m), `arena_terrain_drop_test.tres`.

## What the trial should answer

1. Is 1 km enough room to swing, or does it feel like a pen? Played, not
   guessed. The terrain's `Size` is one number.
2. With health on and no bike, does the line keep you alive, or are there
   not enough anchors? The arena is bare until it is dressed with trees and
   props.
3. Is the run to the pod tense or a chore?
4. How long does a contract take?

## Found on the way

**A contract's horde never fell.** `RespawnKilledAgents = false` only
stopped the dead respawning in place. `HordeDirector` then topped the crowd
back up to its base count the next frame, so no kill counted. The manager
now counts the base agents that stay dead (`BaseLost`), and the director
holds the crowd at its base count less those. This also fixes the contract
level's tiers.

## Measured

`bench.gd -- res://scenes/levels/lvl_drop_test.tscn drop` and `extract`,
10,000 pickles. Two runs back to back, laptop not cooled between them, so
read these as a first look, not as pairs.

| Phase | Mean | Worst | Over 16.7 | GPU |
|---|---|---|---|---|
| Landed, looking at the jar | 14.4 ms | 33.7 | 13 / 279 | 12.0 |
| Top of the west mesa | 13.1 ms | 29.7 | 7 / 306 | 10.6 |
| 220 m over the arena | 15.5 ms | 29.3 | 4 / 65 | 13.0 |
| At the nest, horde arriving | 11.2 ms | 37.8 | 17 / 716 | 8.5 |

The ground builds in 0.2-0.46 s (251² samples of 4 m, 100 chunks). The
horde's ground is 27,556 cells of 6 m, built in 0.12-0.25 s.

Standing still on the ground at the nest, the player was pickled in about
16 s.

## Costs and open ends

- **Over budget**, about 5% of frames, with worst frames of 30-38 ms, before
  the arena is even dressed. Not investigated yet. Find it with `gpuab` and
  `U` before dressing adds to it.
- Craters do not dig the arena's ground; that is the planet's.
- From high up, the fog hides the arena and the square's edge shows past the
  rim. That is fine at swinging height and wrong for anything that flies.
- A clone is a new player scene, made while the "printing" screen covers it.
  How much that one frame costs is not measured yet.
- The pitch, the plan and `CLAUDE.md` still describe the orbit-and-bike
  contract. They are rewritten if the trial holds, and this record is then
  superseded by one that says what was learned.
