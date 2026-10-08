// The board's starting content: the vertical slice, milestone by milestone, as data.
//
// Kept apart from store.js because that file is the board's machinery and this is
// a list of jobs. It is read once, on the first run in a browser with no board in
// it, and never consulted again; after that the board is whatever you made of it.
//
// Every card is traceable to docs/plan.md, which says what each milestone is and
// when it counts as done. The milestones are labels rather than board milestones
// because the board has one category, the slice, and labels are what the
// swimlanes and the filter group by.
(function (PB) {
  "use strict";

  PB.SEED = {
    title: "Dill Control — vertical slice",

    milestones: [
      { key: "slice", name: "Vertical slice", due: "" },
    ],

    labels: [
      { key: "m0",    name: "M0 scaffold",       color: "#8b9892" },
      { key: "m1",    name: "M1 feel",           color: "#3f7fbf" },
      { key: "m2",    name: "M2 wreck",          color: "#b4522f" },
      { key: "m3",    name: "M3 pickles",        color: "#5f9a3a" },
      { key: "m4",    name: "M4 planet",         color: "#2f8f8a" },
      { key: "m5",    name: "M5 Mother Brine",   color: "#9a5bb0" },
      { key: "m6",    name: "M6 bike, arrival",  color: "#c08a1e" },
      { key: "m7",    name: "M7 mission",        color: "#c43d31" },
      { key: "port",  name: "port from the lab", color: "#6f8f7a" },
      { key: "bench", name: "needs a bench",     color: "#7a6f8f" },
    ],

    columns: [
      { key: "backlog", name: "Backlog",     wip: 0, done: false },
      { key: "next",    name: "Next",        wip: 6, done: false },
      { key: "doing",   name: "In progress", wip: 3, done: false },
      { key: "verify",  name: "Verify",      wip: 0, done: false },
      { key: "done",    name: "Done",        wip: 0, done: true },
    ],

    cards: [

      // ================= M0: scaffold =================

      {
        col: "done", type: "tooling", priority: 0, points: 2,
        labels: ["m0"],
        title: "Repository, Godot project and Git LFS",
        body: "Godot 4.7.2 mono, `net8.0`, Jolt set explicitly, a 1920x1080 window, the "
          + "optimiser on in Debug. LFS tracks art and audio from the first commit, "
          + "because art is why this repository exists.",
      },
      {
        col: "done", type: "tooling", priority: 0, points: 3,
        labels: ["m0", "port"],
        title: "Perf instruments ported",
        body: "`FrameProfiler`, `PhysicsStepProbe`, `PerfRecorder` (`U`) and `bench.gd`, "
          + "cut down to what an empty project needs. Bench modes arrive with the systems "
          + "they measure rather than being ported in bulk.",
      },
      {
        col: "done", type: "tooling", priority: 1, points: 1,
        labels: ["m0"],
        title: "Project board seeded from the plan",
        body: "This board. Connect it to the project folder so `docs/project_board.json` "
          + "is the copy git keeps.",
      },
      {
        col: "verify", type: "perf", priority: 0, points: 1,
        labels: ["m0", "bench"],
        title: "Bench the empty pipeline level on a quiet machine",
        body: "`bench.gd` on `lvl_pipeline_test.tscn`, with the laptop idle for a while "
          + "first. This is the floor every later milestone is measured against, so it "
          + "goes into `docs/decisions/0002-performance-target.md`.",
        checklist: [
          "Machine idle, nothing else running",
          "Bench run, worst frame and gpu mean recorded",
          "Number written into the performance decision",
        ],
      },
      {
        col: "doing", type: "art", priority: 0, points: 3,
        labels: ["m0"],
        title: "Blender to Godot round trip: one test asset",
        body: "One small model taken through the whole pipeline in "
          + "`art/_source/README.md`: palette UVs, `.glb` export, the toon palette "
          + "material, a wrapper `.tscn`, placed in `lvl_pipeline_test.tscn` and inked.\n\n"
          + "This is how the facing, scale and material-mapping conventions get "
          + "**verified** rather than assumed. Write what you find into CLAUDE.md's "
          + "verified environment table.",
        checklist: [
          "Model faces Blender -Y, origin at the feet, transforms applied",
          "UVs on the palette swatches",
          "Exported to art/models/ as .glb",
          "Material mapped to mat_palette_toon.tres in the import dialog",
          "Wrapped in scenes/props/prp_*.tscn",
          "Facing and scale confirmed in Godot, written into CLAUDE.md",
        ],
      },
      {
        col: "next", type: "art", priority: 1, points: 2,
        labels: ["m0"],
        title: "Paint the real palette",
        body: "`art/textures/palette.png` is a generated starter: rows of flat swatches "
          + "for pickle greens, brine, rock, metal, company livery and effects. Repaint it "
          + "in whatever you paint in, keeping the 16 px grid so UVs stay put. Its source "
          + "file goes in `art/_source/`.",
      },

      // ================= M1: feel =================

      {
        col: "next", type: "tech", priority: 0, points: 8,
        labels: ["m1", "port"],
        title: "Port the player controller and camera rig",
        body: "`PlayerController`, `CameraRig` and `MovementTuning` from the lab. Keep the "
          + "rig's shape: yaw on the body, pitch on the pivot, every procedural motion on "
          + "the offset. Keep `GroundStickSpeed` and `FloorSnapDistance` as consts, and the "
          + "input map's device ids at 16 and 32.",
        checklist: [
          "Walk, sprint, crouch, jump with coyote time and buffering",
          "Headbob, sway and landing punch on the offset",
          "Gravity read from GetGravity(), up derived from it",
          "Player scene, spawned by LevelRoot, with the ink pass on its camera",
        ],
      },
      {
        col: "next", type: "feature", priority: 0, points: 8,
        labels: ["m1", "port"],
        title: "Port the grapple",
        body: "`GrappleHook` and its half of `PlayerController`: tap to zip, hold to "
          + "swing, apex float on release. The orbit and heightfield ray stay delegates, "
          + "unassigned until Mother Brine and the planet exist.\n\n"
          + "The pitch's first focus. Polish it before anything new.",
      },
      {
        col: "next", type: "feature", priority: 1, points: 3,
        labels: ["m1", "port"],
        title: "Port the jetpack hover and gas dash",
        body: "`J` hover, `Shift` tap dash, fuel refilled on the ground. It shares the line "
          + "rather than dropping it.",
      },
      {
        col: "backlog", type: "art", priority: 0, points: 5,
        labels: ["m1"],
        title: "The gym: a hand-built level of things to swing from",
        body: "A dozen tall pillars and blocks, modelled in Blender and laid out by hand. "
          + "Spaced by the grapple's range rather than by eye: a top 60-70 m of rope from "
          + "the floor, a neighbour about 30 m away for a zip.",
      },
      {
        col: "backlog", type: "polish", priority: 0, points: 5,
        labels: ["m1", "bench"],
        title: "Grapple feel pass",
        body: "Zip, swing, release and float until they feel right. Then a `grapple` bench "
          + "mode that throws, reels, swings and lets go, printing the rope's state.",
      },

      // ================= M2: something to wreck =================

      {
        col: "backlog", type: "tech", priority: 0, points: 8,
        labels: ["m2", "port"],
        title: "Port voxel bodies",
        body: "`VoxelBuilding` and `VoxelField`: 16-voxel sections, the millisecond budget, "
          + "greedy meshing, structural collapse, the armour threshold, dust through a "
          + "delegate. The generators (`TowerModels`, `CompoundModels`) stay in the lab. A "
          + "voxel model here is authored and placed, never generated.",
      },
      {
        col: "backlog", type: "tooling", priority: 1, points: 3,
        labels: ["m2", "port"],
        title: "Bring tools/voxel-bench across",
        body: "It writes `VoxelModel` `.tres` files that point at the voxel scripts, so it "
          + "arrives with them. Rename its storage keys, point its writer at the new paths, "
          + "and leave out the planet, horde and flight labs until those systems are here.",
      },
      {
        col: "backlog", type: "art", priority: 1, points: 5,
        labels: ["m2"],
        title: "Author the first spire, tower and jar in voxel-bench",
        body: "Placed by hand in a scene, not seeded. A spire is a swing anchor and a thing "
          + "that comes down when its base is cut.",
      },
      {
        col: "backlog", type: "tech", priority: 0, points: 5,
        labels: ["m2", "port"],
        title: "Port Blast, the damage path and ExplosionEffect",
        body: "`IDamageable`, `Hitbox`, `DamageInfo`, `HealthComponent`, `Blast` with "
          + "`DistinctOverlaps`, the `AreaBlast` bus signal. `ExplosionEffect`'s six-clock "
          + "staging and inked layers come across; its look gets remade.",
      },
      {
        col: "backlog", type: "feature", priority: 1, points: 5,
        labels: ["m2", "port"],
        title: "First two weapons: Brine Ripper and the rocket launcher",
        body: "`WeaponBase`, `WeaponLoadout`, `HitscanWeapon`, `Rocket`. Gun-only viewmodels "
          + "modelled in Blender; the procedural sway and recoil carry them until there are "
          + "hands.",
      },

      // ================= M3: the pickles =================

      {
        col: "backlog", type: "tech", priority: 0, points: 13,
        labels: ["m3", "port", "bench"],
        title: "Port the horde into scripts/horde/",
        body: "`HordeManager` out of the lab's `spike/`, with the kind data from "
          + "`scripts/enemies/`. Tiers, density cap, view cull, hit proxies that keep their "
          + "agents, blasts and lines over the bus. `bench.gd ... horde 25000` comes with it.",
      },
      {
        col: "backlog", type: "art", priority: 0, points: 8,
        labels: ["m3"],
        title: "Model the five pickles",
        body: "Standard, cornichon, thrower, spitter, tank. Enough edge loops along the "
          + "length to bend; start near 200 triangles and let the horde bench decide. A "
          + "silhouette that reads at the horizon matters more than detail up close.",
      },
      {
        col: "backlog", type: "design", priority: 1, points: 5,
        labels: ["m3"],
        title: "Pickle animation in the vertex shader",
        body: "Waddle, wobble, squash before a lunge, stretch in the air, flop on death, all "
          + "from a phase and a state in `INSTANCE_CUSTOM`. No rig, no bake.",
      },
      {
        col: "backlog", type: "feature", priority: 1, points: 5,
        labels: ["m3"],
        title: "Shield and health",
        body: "The pitch's regenerating shield over health, on the existing damage path. "
          + "This is the game, not the sandbox: empty means dead, and dead means a clone.",
      },

      // ================= M4: the planet =================

      {
        col: "backlog", type: "tech", priority: 0, points: 13,
        labels: ["m4", "port", "bench"],
        title: "Port the cube-sphere planet",
        body: "`scripts/planet/` is already free of the spike: the heightfield, streaming, "
          + "craters, the flow field. Point gravity through an `Area3D` on collision "
          + "layer 0.",
      },
      {
        col: "backlog", type: "design", priority: 1, points: 5,
        labels: ["m4"],
        title: "The compound as an authored scene on the planet",
        body: "Spires, towers, ruins and the jar in one `.tscn`, placed at a marker. Its "
          + "crater is dug once through the ordinary crater call.",
      },

      // ================= M5: Mother Brine =================

      {
        col: "backlog", type: "feature", priority: 1, points: 13,
        labels: ["m5", "port"],
        title: "Port Mother Brine",
        body: "Hop, slam, topple, roll, geyser, shed. Armour authored in voxel-bench, warts "
          + "and core as hitboxes, the orbit delegate on the grapple.",
      },

      // ================= M6: the bike and the arrival =================

      {
        col: "backlog", type: "tech", priority: 1, points: 8,
        labels: ["m6", "port"],
        title: "Port the flight rig and the bike's systems",
        body: "`scripts/vehicles/flightrig/` is portable as it stands. Gun, laser and pod "
          + "come across in `HoverBikeSystems`; the bike itself is modelled in Blender.",
      },
      {
        col: "backlog", type: "feature", priority: 1, points: 8,
        labels: ["m6"],
        title: "Arrive from orbit with no cut",
        body: "A distant-planet stand-in with a clean silhouette, handing over to the "
          + "streamed terrain on the way down, and a planet worth reading from up there.",
      },

      // ================= M7: the mission =================

      {
        col: "backlog", type: "design", priority: 1, points: 8,
        labels: ["m7"],
        title: "The mission loop: clear threshold, three clones, the invoice",
        body: "Start in orbit, clear the population to the company's threshold, die and "
          + "come back as a clone up to three times, and leave with an itemised invoice.",
      },
      {
        col: "backlog", type: "art", priority: 2, points: 13,
        labels: ["m7"],
        title: "First-person hands",
        body: "The one rigged, animated thing in the slice. Late on purpose: gun-only "
          + "viewmodels carry the earlier milestones.",
      },
      {
        col: "backlog", type: "audio", priority: 2, points: 5,
        labels: ["m7", "port"],
        title: "Port the sound system and replace the loudest placeholders",
        body: "`SoundEvent`, `SoundBank`, `AudioDirector`. The synthesiser stays as the "
          + "placeholder; recordings go into the bank's slots, starting with what is heard "
          + "most: guns, kills, explosions.",
      },
    ],
  };
})(window.PB = window.PB || {});
