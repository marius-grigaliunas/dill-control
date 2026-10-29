# lvl_nest_intro: the onboarding level, on a nest

Replaces `lvl_drop_test` as the first contract a player meets
(`docs/decisions/0049`). The same flow, hook, swing, go high, graduate, on a
planet with a 1,000-pickle vine nest and nothing else built.

The nest is `resources/mission/nests/nest_intro_1789143419_1000.tres`: the
Vines tool's Nest tab's preset format, kit `nest_kit_intro.tres` (heart,
fruiting and leafy vines; 300 m at most), seed 1789143419, 1,000 pickles. It
grows 22 vines, 206 m out: one heart vine in the middle, one fruiting vine and
twenty leafy ones of 16-120 m, 1,037 pods. A leafy vine holds about 30 pods,
so there are many anchors at every height, which a 1,000 nest of the default
kit (five vines, 74 m out) does not have. It grows the same on every machine.

| Lesson | Where | What teaches it |
|---|---|---|
| Walk | The player's spawn, at the mouth of an avenue | The first prompt; the nest ahead. |
| 1. Hook | Three shrub vines (`ShrubWest`, `ShrubMiddle`, `ShrubEast`) 18-43 m from the spawn | 9-12 m, close and low, impossible to miss. |
| 2. Swing | The nest's outer ring | Leafy vines 55-70 m apart: a swing from one to the next. |
| 3. Go high | The heart vine, 104-112 m | Chain upward. |
| 4. Graduate | The pods | Shoot one and the colony wakes; destroy every pod. |

The jar (`JarSite`, 65 m past the nest's edge, 40 degrees round from the way
in) stays as something to take apart. It is on `voxel_destruction_jar.tres`, not
the mech stats: no armour, 0.008 m³ a point of damage against the mechs'
0.00025, so a rifle bites and a rocket opens it in a few.

## Prompts

`IntroGuide` (under Spawns) shows `guide_nest_intro.tres`'s steps one after
another, each until the player does what it asks (hooked, let go, 25 m up,
fired, hit the jar, woke the colony, destroyed a pod). A control is named
`{grapple}` and shows the player's own key. The jar step gives up after 25 s;
the rest wait.

## Placing things round a nest

The ground is generated and the nest's place is chosen at load, so a landmark
cannot be placed by coordinates. A `NestLandmark` (a `PlanetSite`) is given a
bearing from the avenue the player comes in by and metres past the nest's edge;
`NestPlacer` lays it there, the planet stands it on the ground. It must also be
in the planet's `Sites` and the placer's `Landmarks`. Each logs
`[nest] landmark <name>: N m from the spawn`.
