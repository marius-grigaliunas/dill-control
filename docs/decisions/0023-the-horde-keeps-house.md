# 0023 The horde keeps to its nest until provoked, and roams it

*Its aggro by distance is superseded by 0029 (proposed): the pickles never strike first.*

*10 October 2026. Builds on 0008 and 0020. Changes the pitch's "the horde
hunts you the whole time".*

## Context

Seen in play: the pickles streamed across the planet in lines and gathered in
one spot rather than staying round the nest (for now, the compound's towers).

The horde had two modes. Hunting, every agent on the planet followed the flow
field to the ground under the player, wherever that was: in orbit, on the far
side. At home, every agent followed the field to the garrison's own cell. In
both modes the field leads to one cell, and the only thing that stopped them was
the density cap, so they piled up there. `bench garrison` with roaming off: the
fullest flow cell held 457 agents at arrival and 2,412 after 40 s.

## Decision

- **Aggro and a leash** (`HordeStats.AggroRange`, `LeashRange`,
  `ProvokeSeconds`). The horde comes for the player once they are within
  `AggroRange` of the garrison, or for `ProvokeSeconds` after they kill any
  of it. It goes home once they are past `LeashRange`. Between the two it keeps
  on, so it does not flip at the edge. `AggroRange` 0 is the old behaviour,
  and is the shared stats' default: the flats benches still hunt.
- **A garrison roams** (`RoamRadius`, `RoamSpeedScale`, `RoamPeriod`). At
  home, an agent within the radius walks at a third of its speed to a spot of
  its own in the disc. The spot is a hash of the agent and the period, so
  there is no state per agent. It waits there, and picks another every period,
  staggered so the crowd never turns at once. One period in four it stands.
  It stands too when its spot or its next step is a blocked cell: agents have
  no collision, and the flow field was the only thing keeping them out of
  buildings. Outside the radius the field brings it home.
- **A contract's tier turns it on**: aggro 250 m, leash 600 m, roam 150 m, and
  the garrison seeded over the same disc.

## Measured

`bench garrison` (contract level, player waiting in orbit 2 km out, 12,000
agents), 40 s:

| | Within the disc | Mean distance | Fullest cell |
|---|---|---|---|
| Roaming off | (10 m disc) 13% to 39% | 20 to 13 m | 457 to 2,412 |
| Roaming 150 m | 99% to 100% | 101 to 87 m | 45 to 81 |

Frame times unchanged (mean 9.5-9.9 ms).

## Cost

The pitch's "the horde hunts you the whole time" is now a choice per tier,
not the rule. A roaming crowd drifts slowly inward (mean 101 to 87 m over
40 s), likely agents standing at blocked cells next to buildings. There is still
one garrison: seeding and roaming round each nest is the next step (0020).
