# 0042 Vines leave no rubble: their pieces are never heaped, they snap into logs, and crumble after a while

*24 October 2026. Built. An exception to 0016 and 0022 for vines. To be
revisited by the path-of-destruction record (`docs/handoffs/vine-tool-4.md`,
report 1), which wants logs and litter to stay.*

## Context

0016 heaps crushed material and bakes resting pieces into the heap. A
vine is not a building. Baked into a heap, a fallen stalk was re-cut on a
grid square to the ground and left blocks floating in the air; crushed vine
was laid down as beige core cubes; and a felled vine shattered at every
bounce (97 breaks a fall in one recording).

## Decision

**Nothing of a vine is heaped or baked into a heap.**

- `LeavesRubble` is off in `voxel_destruction_vine.tres`, and its pieces
  carry that, as they carry `DustScale`.
- A felled vine does not crush through. It falls in big dark dust
  (`FallDust*` in `flex_vine.tres`) and snaps into a few logs where it lands
  (`SnapMinVoxels` 4,000, `SnapReachM` 5 m) instead of shattering.
- Its pieces crumble after `CrumbleAfterSeconds` (20 s).
- A crater under a vine's foot unroots it (`PlantFlex.Unroot`).

## What it costs

- Played, most of a felled nest is gone 20 s later. The user wants the
  opposite: "the player should be able to see what he did". The
  path-of-destruction work keeps logs, stumps and litter for the whole
  contract and lets only small bits fade; the snapping into logs stays.
