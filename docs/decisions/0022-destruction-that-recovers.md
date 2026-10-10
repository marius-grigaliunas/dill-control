# 0022 Destruction the target machine recovers from, and no invisible walls

*10 October 2026. Builds on 0015 and 0016.*

## Context

Seen in play: destruction caused large hitches, and once a lot was destroyed
the game stayed slow. Blocks nobody could see stopped the player and the
pickles.

Measured with `bench.gd collapse` on the target machine, plugged in:

- The loose-piece cap (120) was full while 295 more pieces waited in the queue.
  Each was made as a body (1-2 ms) and dissolved or baked the same tick to
  make room for the next.
- Pieces that found no heap in reach each started a heap of their own: 40
  heaps after one collapse.
- Stacked blasts threw pieces at 80-120 m/s, to 600-900 m up, where they
  never came to rest.
- Landing: mean 21.5 ms, worst 59 ms, 103 frames of 140 over budget.

The invisible blocks had four causes:

- A loose piece is boxed coarsely (at most 24 boxes, up to 8 voxels a box,
  4 m on a building), each box the size of whatever voxels are left in it. A
  hole shot through a fallen piece kept its box, and a piece at rest stayed
  boxed that way.
- A heap's collision is a heightfield, so each column is solid from the
  heap's base up to its highest voxel. A piece baked in where it lay on a
  ledge became an invisible pillar under it. Bare ground in a heap was a floor
  at the base, which floated wherever the ground dipped.
- A section hit again while it waited for its split's pieces stayed marked in
  flight for good. It was never re-meshed or re-boxed again.
- The horde stamps what stands where into its flow field once, when it is
  built. A building that came down stayed a wall the pickles walked round.

## Decision

- **The cap decides before a body is made.** At the cap, with nothing lying
  still to give up, a queued piece no bigger than the smallest moving one
  becomes dust, and its rubble share goes on the heap. It is never made.
- **A heap made by a bake has room for its neighbours**: `RubbleReach` and
  `RubbleHeight`, but at most 48 voxels each way, so a mech's fine debris does
  not get a grid of millions of cells.
- **Only a piece lying on the ground or on a heap is baked**: one ray down
  from its middle has to meet a static body that is not a building. A piece
  that would lose more than a tenth of its voxels outside the heap's grid is
  not baked. Refused pieces stay pieces until they move.
- **Heap tiles have holes**: a vertex is NaN (Jolt leaves out every quad
  touching it) where neither its column nor any neighbouring column has
  rubble. The heap's edge still slopes down to the ground.
- **A piece at rest is re-boxed finely**: once it has lain still a moment and
  was not baked, the finest step within `RestingChunkBoxes` (256) is worked out
  on a worker, and its sections are re-boxed on the workers.
- **Loose pieces go no faster than `MaxThrowSpeed` (45 m/s) up or sideways.**
  Falling is not limited. The gravity gun throws at 42.
- **Buildings tell the horde** (`EventBus.ObstaclesChanged`, decision 0006).
  The `HordeDirector` asks physics again for the cells in reach, one region a
  frame. Loose pieces and heaps are not obstacles.
- A crush re-boxes the sections it touched side by side (`Parallel.For`),
  then hands them to physics in order.

## Measured

`bench.gd collapse`, plugged in, baseline then change:

| Phase | Mean | Worst | Over 16.7 ms |
|---|---|---|---|
| coming down | 13.9 to 13.2 ms | 37 to 26 ms | 6 to 3 |
| landing | 21.5 to 17.7 ms | 59 to 32 ms | 103/140 to 78/170 |

`cut quiet 40`: the pieces drain from 102 to 14 in 25 s. Of those, 13 cannot
be baked and stay pieces. The GPU returns to 7.7 ms and draws to 263 (220
before the cut). 1 frame of 4182 is over 25 ms.

## Cost

A piece queued past the cap is dust rather than something seen falling. A
piece resting on a ledge or another piece stays a body (frozen after
`SettleSeconds`) rather than joining a heap. A heap's edge loses up to one
voxel of collision where a lone column stands on bare ground. Settled pieces do
not wake when what they rest on goes, and hang in the air until a blast
reaches them.
