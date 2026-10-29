# lvl_tutorial

The onboarding level (0055): a handbuilt nest on a sculpted ground, entered by a
pod drop seen from the rider's own eyes. Everything below is a node in
`scenes/levels/lvl_tutorial.tscn`; open it and move it.

```
                         north (-Z)
          rim of hills ~ 330 m round, 16 m high
     ridge W                              ridge E
          Foliage: 25 leafy, 1 heart (-5,-45), 1 fruiting (60,-45)
                   one nest, 300 m across, 1,115 pods
                                |
                   Props/Hooks: three shrubs, z = 165-205
                                |
                   Spawns/LandingSite   (0, 250)  flat field
```

| Node | What it is |
|---|---|
| `Geometry/Terrain` | the sculpted ground; sculpt it with the terrain brush |
| `Foliage/Vine*` | the nest; paint more with the Vines brush, delete or move any |
| `Props/Hooks` | the first hooks, no pods |
| `Props/PodField` | the colony's pods, filled at load by `Spawns/Nest` |
| `Spawns/LandingSite` | where the pod comes down; its -Z is the view out of the hatch |
| `Spawns/PlayerSpawn` | where the player is first made, then carried up by the pod |
| `Spawns/Landing` | `PodLanding`: the drop (rules in `drop_rules_tutorial.tres`) |
| `Spawns/Nest` | `PlacedNest`: registers the vines' pods, empties the horde |
| `Spawns/Mission` | `ColonyMission`: destroy every pod, at most a fifth alive |
| `Spawns/Guide` | `IntroGuide`: the prompts, `guide_tutorial.tres` |

Flow: look out of the pod while it falls, step out, walk to the hooks, tap the
grapple on a vine, let go, swing, climb 25 m, fire, shoot a pod (the colony wakes),
hold the line, clear the pods. The prompt text is a resource; edit it without a
rebuild.
