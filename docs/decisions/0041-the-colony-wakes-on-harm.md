# 0041 The colony wakes on harm, never on nearness: every pickle drops into the horde over 30 s

*24 October 2026. Built; decided with the user. Settles 0029's wake for
pods (0034, 0040).*

## Context

0029 proposed that the colony never strikes first and that harm anywhere
alerts all of it. With every pickle on a pod (0034), what counts as harm,
and what happens to a pickle shot where it hangs, had to be chosen.

## Decision

**Any harm to the colony wakes it; getting close does not. Woken, every
pickle drops from its pod into the horde over `WakeSeconds` (30 s), nearest
the harm first.**

- **Harm** is a pickle hit on its pod, a hit on a vine or leaf
  (`VoxelBody.Damaged`), or a blast inside the nest or within
  `AlarmMarginM` (40 m) of it.
- **A pickle shot on its pod drops dead.** It does not fall and fight.
- A dropped pickle enters `HordeManager` as `SpawnOrigin.Pod`, lobbed so
  the fall does not kill it. The horde has no agents of its own in a nest
  level; it is the colony.
- In an awake colony a regrown pickle hangs 5 s (`AwakeHangSeconds`), then
  drops.

## What it costs

- A rifle round on bare ground near the nest does not wake it: nothing
  broadcasts a ground hit, and a new `EventBus` signal needs a record of its
  own (0006).
- Whether the colony ever goes back to sleep is undecided; today it does
  not.
- Pickles falling on the player at the wake hurt once each, like a thrown
  cornichon. Whether that is fair is not yet judged.
