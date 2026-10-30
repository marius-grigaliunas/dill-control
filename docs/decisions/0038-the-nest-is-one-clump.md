# 0038 The nest is one clump sized by its population: 55-70 m apart, 400 m at most, more pods a vine before more room

*24 October 2026. Built; decided with the user. Builds on 0034 and 0037.*

## Context

One pod per pickle (0034) means a 25,000 nest needs 25,000 pods. The first
nest put 144 vines 85-110 m apart and reached 706 m from its heart: a third
of a hemisphere of the planet, not a nest. Played, it read as vines spread
over the world. It also came with the drop-test compound, a city that was
only ever a destruction test.

The levers were more pods a vine, closer spacing, a hard radius, or a mix.
At 20 m apart a nest is unplayable; at 100 m it swings well but slowly; the
grapple reaches 140 m.

## Decision

**A mix: vines 55-70 m apart inside a hard 400 m radius, and when the
population does not fit, more pods a vine before more room.** No compound
or city goes with a nest.

- `NestLayout` plans a heart and rings of slots out from it at the kit's
  spacing, jittered, none in an avenue (2-4, 60 m wide), none past
  `MaxRadiusM`. Measured along the ground.
- It adds vines, inner rings first, a kind by weight in its zone, until
  their pods cover the population (clamped 1,000-25,000).
- Out of slots, it plans again weighing kinds by the square of their pods,
  then the fourth and eighth power, as far as the population needs. It
  reports what is still short.
- Fruiting and weeping vines roll 16-20 pods a ring for it.
- Each vine rolls a size jitter (0.88-1.12).
- `NestPlacer`, a site chooser of the planet's, lays the heart at the
  flattest of 16 candidates and stands each vine on a levelled pad (6 m,
  blended over 8 m).
- Numbers: `resources/mission/nest_kit_default.tres`.

25,000 is 117 vines reaching 393 m, 25,062 pods (weeping 51, fruiting 43,
heart 23).

## What it costs

- A large nest is mostly the high-pod kinds; the light ones thin out.
- More vines are in view from above in a clump (about 860-900 draws).
- The jitter (0.22) pulls some neighbours to 45 m, under the 55 m set.
