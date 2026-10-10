# 0021 A session autoload carries the contract; the contract level loads itself behind its own screen

*10 October 2026. Builds on 0018-0020.*

## Context

A contract is picked in the hub and flown in another scene. Something has to
carry it across the scene change, and something has to cover the second or
two the planet, the compound and the horde take to build.

## Decision

- **`Session`, a third autoload, holds state only**: the session's seed, the
  offers made from it (`ContractGenerator.Offers`, each its own seed hashed
  from the session's), and the accepted contract. It raises no signals, so it
  is not a second event bus (0006): a scene reads it when it starts and writes
  it when the player decides. A new seed each session; a test can set one.
- **`ContractGenerator`** turns a seed and the `ContractPools` (archetypes,
  biomes, kits, tiers, each optionally weighted) into a `ContractSpec`, with a
  survey designation ("ZA-7954") made from the seed. Pure, like every roll.
- **No separate loading scene.** The hub changes straight to `lvl_contract`;
  its `LevelRoot` has a `Builder` (`ILevelBuilder`) and shows a
  `LoadingOverlay` over the level until the builder is ready, and only then
  makes the player. Nothing has to hand the contract through a third scene.
- **`SystemBuilder` builds in stages**: the contract resolved (a bench seed,
  a `seed=N` argument, the session's contract, or a fallback seed, so the
  bench and F6 work without the hub); the planet's heights on a background
  task (`PlanetTerrain.BeginBuild`), so the screen keeps drawing; the rest of
  the planet, the compound and the system in one main-thread step
  (`FinishBuild`); the horde's routing; then **30 frames in a row under
  20 ms with no chunk left queued**, or 6 s, before the player is let in. The
  first frame the player sees is already a settled one.
- **The solar system builds when the planet says it has** (its `Built`
  signal), not in its own ready, since the planet now builds after every
  node is ready.

## Measured

Headless, seed 7 (1260 m, 1,014 chunks, Rust Dunes, 12,000 pickles): planet
887 ms of which the heights ran on the background task, horde ground 540 ms,
in after 2.1 s. Recorded at 10 fps (seed 3), the overlay draws throughout.

## Costs and open ends

- `FinishBuild` is one main-thread step of a few hundred milliseconds: the
  screen holds still for it. Splitting chunk creation and the far shell over
  frames would keep the bar moving; not worth it yet.
- The heights' rows run on `Jobs` from the background task. Nothing else may
  use `Jobs` while they do; during a load nothing does, because the horde
  waits for the planet.
- `resources/ui/font_display.tres`, which `PickleStyle` loads, is not in the
  repository; the overlay (like the HUD) falls back to the system comic face
  and logs the miss.
