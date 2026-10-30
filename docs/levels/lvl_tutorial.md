# lvl_tutorial

The onboarding level (0055): the first contract, on the planet, entered by a pod
drop seen from the rider's own eyes. Everything below is a node in
`scenes/levels/lvl_tutorial.tscn`; open it and move it. The planet's ground is
built at load, so nodes stand where they were placed only roughly and are set on
the ground then.

```
   (world: planet at the origin, radius 1,260 m, +Y is the nest's side)

   LandingSite ----- HookSite ------------- NestSite
   pod comes down    2 trees, 3 shrubs      16 vines, 1,015 pods
   -66, 1260, 0      19, 1262, 0            262, 1234, 0     (about 330 m from the landing)
```

| Node | What it is |
|---|---|
| `Geometry/Planet`, `SystemBuilder` | the planet, built behind the loading screen; pinned to seed 1 |
| `Props/NestSite/Nest` | the baked nest `prp_nest_intro_1789143419_1000`; move, delete or add vines |
| `Props/HookSite/Hooks` | trees and shrubs to grapple on the way in; no pods |
| `Props/PlacedNest`, `PodField` | hand the nest's pods to the colony; the colony's pods |
| `Spawns/LandingSite` | where the pod comes down; its -Z is the view out of the hatch |
| `Spawns/Landing` | `PodLanding`: every body's drop (`drop_rules_tutorial.tres`) |
| `Spawns/Clones` | `CloneBank`: three bodies, then the contract is void (`clone_rules_default.tres`) |
| `Spawns/Extraction` | `PodExtraction`: after the clear, a pod lands at a random flat place; board it to finish |
| `Spawns/Mission` | `ColonyMission`: destroy every pod, at most a fifth alive |
| `Spawns/Guide` | `IntroGuide`: the prompts, `guide_tutorial.tres` |
| `Spawns/Horde` | the colony's cucumbers; none until it wakes |

Flow: look out of the pod while it falls, step out, walk to the hooks, grapple,
let go, swing, climb, fire, shoot a pod (the colony wakes), hold the line, clear the
pods, then run to the pod that lands somewhere new and ride it up. Lose a body and the next is printed and dropped the same way.

Photographs of it from the rider's eyes: `scripts/debug/tutorialshots.gd` (the
drop), `cloneshots.gd` (clones), `wakeshots.gd` (the colony waking), `extractshots.gd` (the way home).
