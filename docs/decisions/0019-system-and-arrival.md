# 0019 The system and the arrival: a ship waiting in orbit, the bike already ridden, other bodies as sky discs

*10 October 2026. Builds on 0017 and 0018.*

## Context

The flow agreed for the slice: menu, the player's own ship (a walkable hub),
contract chosen at its computer, a loading screen, then the contract's
system. The pitch asks for the arrival itself to be one unbroken flight from
orbit to the ground. The ship in the contract's system is a backdrop: it is
never flown.

## Decision

- **`SolarSystem`** owns everything round the planet, rolled from the
  planet's seed on streams of its own. It readies after the planet and before
  `LevelRoot`, so the ship, bike and spawn marker are placed when the player
  is made.
- **The ship waits at 2.4 radii**, past the gravity well, 18° round from over
  the compound in a direction the seed picks, upright to the planet. The bike
  waits at the ship's `BikeDock`, **nose on the compound itself**: the bike
  flies six ways with no pitch limit, and a nose levelled to the local
  horizon flew a tangent straight out to space. The player is made beside it
  and put on it a deferred call after spawning (the bike hears of the player
  from the same signal).
- **Zero-g past the well** (`SpaceField`, eight radii, priority 0). Without
  it the project's default gravity, straight down in world space, takes over
  the moment the planet's well lets go.
- **Other bodies are discs in the sky shader**: one to three, a direction, an
  angular size and a colour each from `SystemStats`, lit by the sun, kept 35°
  off it. No mesh, no draw call, unreachable: only the contract planet is a
  place (0017).
- **The sky** is the lab's `planet_sky` ported: a ramp from the eye's own up,
  a ground band below the real limb, the cloud deck, stars as the air thins,
  plus the bodies. Up, limb dip and air are pushed as globals every frame
  (smooth) and, throttled, as material uniforms for the radiance cubemap
  (which lights the ground). A biome paints its colours.
- **The air thins from 0.1 to 0.8 radii of altitude.** The authored fog
  thins with it and recedes two metres per metre of height, so the ground
  reads from high up and space is clear.
- **`lvl_contract`** is the level all this lives in: the planet rolled from a
  seed, the compound on a site (graybox until the compound kit), the ship, the
  bike, the horde.

## Measured

`bench.gd -- lvl_contract.tscn descent`: from the ship, cruise flat out at the
compound (140 m/s boosted, about 25 s to the ground), seed 1 (1350 m, 1,014
chunks). The laptop rested 30 s before; not fully cool.

| phase | mean | worst | over 16.7 | gpu |
|---|---|---|---|---|
| at the ship | 12.0 ms | 16.4 ms | 0/334 | 7.3 ms |
| cruise | 11.4 ms | 21.5 ms | 16/527 | 6.7 ms |
| descent | 11.6 ms | 62.1 ms | 3/433 | 7.3 ms |
| descent, late | 12.9 ms | 22.7 ms | 2/389 | 10.4 ms |
| hovering at the ground | 12.2 ms | 17.8 ms | 1/328 | 9.8 ms |

Chunks stream in under the descent within their budget; the one 62 ms frame
is not yet found.

## Costs and open ends

- The 62 ms frame in the descent, and the handful over budget in cruise, need
  a quiet recorded run (`U`) to find.
- The horde hunts the player from the start, so with the player in orbit it
  gathers under the ship's ground point. The mission decides what the crowd
  does before the player lands.
- The cloud deck seen from above, and the far shell's colours from orbit, are
  placeholders like the rest of the sky.
- The flight from orbit takes about 25 s boosted. Whether that is a moment or
  a wait is for playing it to say; the bike's speed is `FlightRigStats`.
