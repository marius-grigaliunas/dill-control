# 0029 Proposed: the hive grows on vines, and never strikes first

*18 October 2026. A proposal, not yet built or played. Builds on 0023 and
0024. Once built, it supersedes 0023's aggro by distance.*

## Context

0023 keeps the horde at its nest, but a nest is just a spot on the ground, and
the horde comes for the player as soon as they are within `AggroRange`
(250 m on a contract tier, 280 m in the drop test). Every pickle is a walking
agent from the first frame. The pickles read as a garrison waiting to fight.
They should read as a colony that wants to grow and is left alone until
someone attacks it (0030).

## Decision

- **The colony grows on vines.** Trees become trellised pickle vines. Most of
  the population, about 95%, hangs from them as growing pickles: the capsules
  that become the enemies, holding still. About 5% are workers, moving round
  the nest the way 0023's garrison roams.
- **The vines sag with fruit.** The hanging pickles are visible from orbit, on
  the approach and from the landing site, so the size of the threat can be
  read before it wakes. The crowd is still the spectacle; it is asleep.
- **The pickles never attack first.** Coming close does nothing. A player can
  walk or fly among the workers and nothing happens. Proximity no longer
  triggers the horde: `HordeDirector`'s `distance <= Stats.AggroRange`
  (`HordeDirector.cs:481`) goes for colonies.
- **Harm to any part of the colony alerts all of it.** A shot vine, a hanging
  pickle or a worker is enough. There are no sections to isolate: cutting a
  vine first does not keep part of the hive asleep.
- **The hive comes down over about 30 s**, not in one frame. Once alerted, the
  hanging pickles drop and join the chase a share at a time until the whole
  colony is hunting. The time is data (`WakeSeconds` in the horde's stats).
- **A hanging pickle is drawn, not simulated.** It has no path, no movement
  and no flow-field step until it drops.

## Why

- The colony being harmless until attacked is what makes the player the
  aggressor (0030). A horde that charges anyone who comes near would undo it.
- The vines are trees, and trees are grapple anchors. The hive is the thing
  you swing through.
- It is cheap while asleep: at 25,000, about 1,250 workers move before the
  alarm. A 30 s wake turns about 800 agents a second, not 25,000 in one frame.

## Costs and open ends

- **The fully awake hive is the cost to measure**, at the end of the 30 s:
  `siege 25000` already covers it. The asleep state needs its own bench: are
  about 24,000 hanging pickles cheap to draw, as one instanced draw per vine
  or per chunk?
- After the alarm, does the hive still go home past `LeashRange`, and does it
  ever go back to sleep? Not decided.
- In what order the hive comes down (nearest the harm first, or at random
  across the colony) is not decided. Nearest-first reads as the alarm spreading.
- A vine is a voxel model like any other: destructible on the tree stats
  (0024), with hanging pickles as markers or a baked list.
- The pitch's "the horde hunts you the whole time" and 0023's per-tier aggro
  are rewritten when this is built.
