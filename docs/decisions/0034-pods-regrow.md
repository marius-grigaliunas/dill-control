# 0034 Pods regrow their pickles: the threat lasts until the vines are destroyed

*21 October 2026. Decided; the numbers are still to be tuned. Every pickle
starts on a pod; there are no ground workers.*

## Context

The colony hangs on vines. A pickle killed is gone, so a player can win
by shooting pickles from a distance, and the vines are scenery. A vine that
holds hundreds of pickles should be the thing the player has to deal with.

## Decision

**Every pickle grows on a pod, and an emptied pod grows a new one. Killing
pickles thins the colony; only destroying pods, or whole vines, ends it.**

- **One pod per pickle.** A nest has as many pods as its contract's population,
  from 1,000 to 25,000, and every pod starts holding a pickle. The nest's size
  follows from that; the planet is the same size for every nest.
- **Workers come from pods.** A pickle on the ground has dropped from its pod,
  which is then empty.
- **An empty pod regrows its pickle** after a while, for as long as the pod
  is intact and its vine stands. The population never grows past the pods.
- **Destroying a pod ends it for good.** So does felling its vine, which
  takes every pod on the vine with it. Pods are things that can be shot, not
  markers.
- **Its numbers live in a resource:**
  - the regrowth time;
  - how many pods may regrow at once;
  - whether a damaged vine regrows slower.

  They are tuned in play.

- **What wins the contract.** Both of these must hold:
  - **every pod destroyed**, which is the hard rule;
  - **at most 20 % of the starting pickles still alive** (from 100, no more
    than 20 remain).

  Killing them all, 100 %, earns a bonus. A colony with no pods cannot
  regrow, so the remainder is a mop-up, not a moving target.

- **Regrowth starts with the fight.** A pod regrows only once it is empty,
  and a calm colony's pods are all full. Nothing regrows until the colony is
  aggravated (0041) and its pickles drop to fight, or are killed on their
  pods. How fast regrowth runs is a tuning number, not a rule.

## What it costs

- Pods become objects with state (full, empty, regrowing, destroyed), up to
  25,000 of them, which has to stay cheap. They are simulated in bulk, never
  one node each.
- A contract cannot be won by distance shooting alone. That is the intent:
  it pushes the player into the destruction.
