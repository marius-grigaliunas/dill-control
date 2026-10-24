# 0040 Pods in bulk: flat arrays, pickles drawn instanced and bent on the GPU, shot through a pool of proxies

*24 October 2026. Built. Carries out 0034.*

## Context

0034 makes every pickle start on a pod, up to 25,000, each with state (full,
empty, regrowing, destroyed). A node per pod, with a mesh and a collider,
would cost more than the nest. The pods also hang from vines that bend
(0032) and break (0011), so they must move and die with the voxel they hang
from.

## Decision

**`PodField` holds a nest's pods in flat arrays. A pod hangs from a voxel
of its vine, and its pickle is drawn instanced and bent by the vine's flex
row on the GPU. No node is made per pod.**

- **Filled at load** by `NestBuilder` from each vine's `@PodNN` markers,
  read from the built model with no nodes; each pod is anchored to the
  nearest solid voxel (`AnchorSearchVoxels`). Destroyed when that voxel,
  or its vine, goes.
- **Drawn** by 150 m ground cells: capsules near, columns far
  (`pickle_hang`, `pickle_hang_far`), each instance carrying its vine's
  flex row in custom data (`FlexField.Track`, `flex_bend_row`), so a
  pickle sways with its arm. Paired at 25,000: under 1 ms of GPU.
- **Shot** through a pool of 48 `PodHitProxy` placed on the hanging
  pickles nearest the aim, moved every few ticks; blasts reach them through
  the bus. A pickle killed on its pod falls as a body (at most 32) and
  puffs.
- **Regrowth** (0034): an emptied pod regrows after 45 s, at most 2,000 at
  once, and only for a pickle that died, so the colony never grows past
  its pods.
- **The contract** (`ColonyMission`, `ColonyRules`, `ColonyHud`): every pod
  destroyed with at most 20 % alive clears it; none alive earns the bonus.
- Numbers: `resources/mission/pod_default.tres`. `podcheck.gd` holds it.

The pods add about 0.5 ms of CPU at 25,000.

## What it costs

- Pods are their own hit and draw path beside the horde's.
- Every hanging pickle draws as Standard; the horde's composition picks its
  kind when it drops. Whether a hanging pickle should show what it drops as
  is undecided.
