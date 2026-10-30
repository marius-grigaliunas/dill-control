// The board's starting content, as data: the vertical slice as the design now stands.
//
// Kept apart from store.js because that file is the board's machinery and this is
// a list of jobs. It is read once, on the first run in a browser with no board in
// it, and never consulted again; after that the board is whatever you made of it.
//
// Open decisions holds every open question and parked idea; Backlog what is
// decided and not built (docs/decisions/0050-0054, docs/plan.md); Done what the
// built records (0002-0049) already carry. The live board is docs/project_board.json.
(function (PB) {
  "use strict";

  PB.SEED = {
    title: "Dill Control — vertical slice",

    milestones: [
      { key: "slice", name: "Vertical slice", due: "" },
    ],

    labels: [
      {
            "key": "open",
            "name": "open question",
            "color": "#c08a1e"
      },
      {
            "key": "parked",
            "name": "parked",
            "color": "#8b9892"
      },
      {
            "key": "story",
            "name": "story idea",
            "color": "#9a5bb0"
      },
      {
            "key": "later",
            "name": "backlog, later",
            "color": "#7a6f8f"
      },
      {
            "key": "open1",
            "name": "opening",
            "color": "#3f7fbf"
      },
      {
            "key": "c1",
            "name": "contract 1",
            "color": "#5f9a3a"
      },
      {
            "key": "c2",
            "name": "contract 2",
            "color": "#2f8f8a"
      },
      {
            "key": "c3",
            "name": "contract 3",
            "color": "#b4522f"
      },
      {
            "key": "weapons",
            "name": "weapons",
            "color": "#c43d31"
      },
      {
            "key": "cabin",
            "name": "ship and shop",
            "color": "#6f8f7a"
      },
      {
            "key": "built",
            "name": "built",
            "color": "#00a86b"
      }
],

    columns: [
      {
            "key": "open",
            "name": "Open decisions",
            "wip": 0,
            "done": false
      },
      {
            "key": "backlog",
            "name": "Backlog",
            "wip": 0,
            "done": false
      },
      {
            "key": "verify",
            "name": "Verify",
            "wip": 0,
            "done": false
      },
      {
            "key": "done",
            "name": "Done",
            "wip": 0,
            "done": true
      }
],

    cards: [
      {
            "col": "open",
            "type": "design",
            "priority": 1,
            "points": null,
            "labels": [
                  "open",
                  "c2"
            ],
            "title": "Contract 2: what is the challenge?",
            "body": "The gear run lands on a storm planet and retrieves lost gear from the hive's centre, but once the bike is found the player can fly straight to space, so there is no tension. What must the player do to get the gear out, and what stops the bike being an exit? `decisions/0051`."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open",
                  "weapons"
            ],
            "title": "What unlocks persistent tiers in the full game?",
            "body": "Tiers persist across the campaign (0052); what earns them is not settled."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open",
                  "weapons"
            ],
            "title": "Special-weapon charge counts",
            "body": "How many charges a consumable super weapon (nuke designator, orbital laser, atomic grenade, missiles) is found with."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "Kill thresholds for later company drops",
            "body": "The bike drops at 20,000 kills in contract 3; other thresholds and the order of drops are open."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "Do company drops draw the horde?",
            "body": "Whether a package landing attracts cucumbers."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "Scouting tools",
            "body": "What the player scouts a nest with before dropping, if anything."
      },
      {
            "col": "open",
            "type": "polish",
            "priority": 3,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "Hit-stop tuning",
            "body": "The juice around kills and big blasts; design for it from the start."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "The mineral's name",
            "body": "Each planet holds a valuable mineral; it is still unnamed."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open",
                  "story"
            ],
            "title": "How are upgrades bought while in debt?",
            "body": "Unsolved once the parked debt story is built: the player always owes the company, yet the shop sells upgrades. `docs/ideas/story.md`."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open",
                  "story"
            ],
            "title": "The endings",
            "body": "Accept the company's renewal and get a mech, or turn on the company by unleashing the horde on it. A direction only. `docs/ideas/story.md`."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open",
                  "weapons"
            ],
            "title": "Bike ammo counts and laser cooldown",
            "body": "The bike keeps its gun, rocket pod, bomb bay and laser, each on limited ammo. The numbers for its stats resource are not set (the laser fires 10 s then cools in contract 2)."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "open"
            ],
            "title": "Does the colony ever go back to sleep?",
            "body": "Today it does not (0041). Also open: a rifle round on bare ground near the nest wakes nothing, and a new `EventBus` signal would need a record."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "parked",
                  "weapons"
            ],
            "title": "Rocket balance",
            "body": "One bazooka load killing 7,000-8,000 may be a lack of pressure on the player rather than a kill-count problem. Rockets stay plentiful, because they are the fun."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 2,
            "points": null,
            "labels": [
                  "parked"
            ],
            "title": "Fear-scatter after blasts",
            "body": "Scared cucumbers scattering after big blasts so the player must herd them back. Wanted, not implemented."
      },
      {
            "col": "open",
            "type": "perf",
            "priority": 2,
            "points": null,
            "labels": [
                  "parked"
            ],
            "title": "Separable body pieces on kills",
            "body": "Pieces flying off with stylised splatter. A cucumber is one shader-drawn capsule with no separable parts; measure the cost on Iris Xe first."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "parked"
            ],
            "title": "Early pacing: Cornichons first",
            "body": "Thrown Cornichons arrive before the bigger types, so early fights feel like mowing down small fry. Revisit."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "parked",
                  "story"
            ],
            "title": "Tanks defending a damaged vine",
            "body": "Tanks rushing to guard a hit vine in a tight radius. Under 0041 harm wakes everything, so a local guard has nothing to add yet."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "parked",
                  "story"
            ],
            "title": "Debt as the story spine",
            "body": "The player owes the company; deaths and failures add to it with a cap; the balance prints on every invoice. Parked until the slice is played. `docs/ideas/story.md`."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "parked",
                  "story"
            ],
            "title": "Show, don't tell: cold invoice wording",
            "body": "Invoice words grow colder across the campaign, from specimens to units to biomass. No young cucumbers; no corpse-carrying."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "parked",
                  "story"
            ],
            "title": "The recurring rival contractor",
            "body": "A scripted boss with one visible counter per return (flares, cutting vines), who bails out instead of dying and takes your paycheck. Stay apart from the Nemesis system."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Root hearts that wither nest sections",
            "body": "Killing a root heart withers a whole section of the nest."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Mid-mission company bonus jobs",
            "body": "Optional jobs offered during a contract."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Mother Brine as a roaming boss",
            "body": "Climbing out of the main shaft as a town-contract climax. Cut from the slice."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "A buried queen and caves",
            "body": "A queen too big to leave her cave, setting up a follow-up nest contract. Mole cucumbers and the underground queen are full-game."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Player-called airstrike with a handheld designator",
            "body": "An orbital cannon or airstrike the player calls."
      },
      {
            "col": "open",
            "type": "audio",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Cucumber sounds scaled to size",
            "body": "Bigger cucumbers sound bigger."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Magic weapons",
            "body": "A staff that works as a spell conduit. Kept out of the slice."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later"
            ],
            "title": "Detailed ballistics and wound channels",
            "body": "To be checked against the brine-not-blood tone."
      },
      {
            "col": "open",
            "type": "design",
            "priority": 3,
            "points": null,
            "labels": [
                  "later",
                  "story"
            ],
            "title": "A small cast of recurring rival characters",
            "body": "Beyond the first rival."
      },
      {
            "col": "backlog",
            "type": "tech",
            "priority": 2,
            "points": 3,
            "labels": [
                  "weapons"
            ],
            "title": "Remove the prototype weapons outside the five",
            "body": "Noted, not started (decided: do not change the weapons yet). `wpn_bayou_boomstick`, `_machete`, `_brine_burner`, `_brine_ripper`, `_dill_hammer`, `_frag_grenade`, `_gherkin_volley`, `_gravity_manipulator`, `_grenade_thrower`, `_sledge`, `_strike_designator`, `_vinegar_lance` are in the player's code and the `arsenal` and `slam` benches. Removal edits the player and those benches. Replaced by 0052's five."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 0,
            "points": 13,
            "labels": [
                  "weapons"
            ],
            "title": "Five core weapons, tiers 1 and 2",
            "body": "Assault rifle (piercing, underbarrel launcher), bazooka (four-rocket volley, bigger rockets), pole saw (reach, spin; no ammo), laser cannon (shorter cooldown, wider beam; gravity-gun mode), sniper (.50, explosive rounds, airstrike). `decisions/0052`."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 2,
            "points": 8,
            "labels": [
                  "weapons"
            ],
            "title": "Viewmodels for the five weapons",
            "body": "Assault rifle with underbarrel launcher, bazooka, pole saw, laser cannon, .50 sniper."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "weapons"
            ],
            "title": "Weapon wheel and aim on every weapon",
            "body": "Mouse wheel switches weapons; right mouse button aims on every one; five slots with free choice."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "weapons"
            ],
            "title": "Specials and the consumables bar",
            "body": "Reusable specials (minigun, fire rocket launcher) and consumable super weapons (nuke designator, orbital laser, atomic grenade, missiles); a separate bar for grenades, medkits, shield generators, ammo."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "weapons",
                  "c2"
            ],
            "title": "Nuke designator",
            "body": "Marks a spot; the player flies clear before it lands. One-off prompt that fades if ignored. Needs the nuke blast."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "weapons",
                  "c1"
            ],
            "title": "Minigun special (the gift at 50%)",
            "body": "Armour-piercing minigun, given by the company at 50% of the nest destroyed in contract 1."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 13,
            "labels": [
                  "cabin",
                  "weapons"
            ],
            "title": "Six-tier upgrades and the shop",
            "body": "Ammo, ammo, mod, mod, secondary ability, its evolution. The slice shows both tiers of every weapon but pays for two purchases in total, and logs every purchase."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "cabin"
            ],
            "title": "Log every shop purchase",
            "body": "To learn which upgrades people want."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "cabin"
            ],
            "title": "Ship cabin hub",
            "body": "Walkable cabin, a window onto the next planet, ambience. Failure wakes the player here, unpaid."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "cabin"
            ],
            "title": "Invoice printer",
            "body": "Prints the itemised invoice (fee minus ammo, clones, damage, rentals) and stamps it PAID."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "cabin"
            ],
            "title": "Crate hatch",
            "body": "The purchase arrives in a crate through a hatch."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "cabin"
            ],
            "title": "Main menu as a live window on the ship",
            "body": "The planet rotating slowly, ambience, a periodic beep. Play, Options, Wishlist on Steam, Join Discord, Send feedback, Credits, Quit."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 2,
            "labels": [
                  "cabin"
            ],
            "title": "End of the slice: empty contract screen",
            "body": "Ends on an empty contract screen with a wishlist-on-Steam line; any contract can be replayed after."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "open1"
            ],
            "title": "Opening sequence: unclear cloning flashes",
            "body": "Short, deliberately unclear, shown once and never explained. `decisions/0054`."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "open1",
                  "c1"
            ],
            "title": "Landing pod and the descent through the window",
            "body": "Control inside the pod, look around, watch stars, sun, atmosphere and clouds; the door opens onto the vines. Every contract arrival. Landing in a town smashes the pod through skyscrapers."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "open1",
                  "c1"
            ],
            "title": "Replay the cloning flashes at the first death",
            "body": "At the first death in contract 1 the lab flashes replay as the clone respawns."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "c1"
            ],
            "title": "Free drop point on the contract screen",
            "body": "A rough sketch of the planet and nest; scout far out or drop straight onto the nest; no mandatory setup phase."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "c1"
            ],
            "title": "Extraction by pod",
            "body": "Call extraction, a pod drops, the ground falls away through the window."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 0,
            "points": 13,
            "labels": [
                  "c1"
            ],
            "title": "The queen",
            "body": "A stationary pod at the nest's centre: rind that rifle rounds barely mark, weak spots that open after she attacks, feeder vines that heal her until cut, a guard of Tanks and Spitters. Tuned for 2-3 minutes at tier 1, about 25 bazooka hits."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "c1"
            ],
            "title": "Queen attacks: screech, seed spit, tearing down vines",
            "body": "Screech knocks you off the grapple and calls the horde; sticky seed spit slows you; tearing down vines around her in a growing radius pushes you from minigun to sniper range. Each telegraphed."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 3,
            "labels": [
                  "c1"
            ],
            "title": "Contract 1 win and HUD",
            "body": "Queen dead, every pod destroyed, at most 20% of cucumbers alive; the HUD marks the last pods."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "c1"
            ],
            "title": "Contract 1 light tips",
            "body": "Grapple first, then movement and the weapon wheel."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "c1"
            ],
            "title": "Tank throws and punches",
            "body": "The Thrower is folded into the Tank."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "c1"
            ],
            "title": "Targeting: aggro radius and the director",
            "body": "One flow field to the contract's anchor plus a player aggro radius that allows baiting; the director varies the radius with a telegraph such as a screech."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "c1"
            ],
            "title": "Death: three clones, failure pays nothing",
            "body": "Three clones a contract, nothing lost on death; losing all three fails the contract and you wake on the ship unpaid, keeping upgrades. `decisions/0053`."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 8,
            "labels": [
                  "c2"
            ],
            "title": "Storm planet and weather",
            "body": "A second biome in storm season: fog, lightning, dead calm, a tornado that rips up vines. Weather is mood."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "c2"
            ],
            "title": "Contract 2: fixed landing glitch",
            "body": "The storm scrambles targeting; the player always lands at one fixed spot at the nest's edge and it must read as intended."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "c2"
            ],
            "title": "Contract 2: crash pod and beacon",
            "body": "The dead contractor's pod high in the tallest vine at the hive's centre, holding the nuke designator; optional call-in earns the \"piece of shit\" achievement."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "c2"
            ],
            "title": "Contract 2: tornado climax",
            "body": "The storm peaks, a tornado rips up vines; cucumbers flung at you, telegraphed and shootable mid-air."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "c2",
                  "c3"
            ],
            "title": "Hoverbike as a special item with limited ammo",
            "body": "Gun, rocket pod, bomb bay and laser on limited ammo. The prize of contract 2; a company drop at 20,000 kills in contract 3."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 0,
            "points": 13,
            "labels": [
                  "c3"
            ],
            "title": "Contract 3: procedural town",
            "body": "One big pad, skyscrapers, a main shaft and side shafts at the edges as spawn holes (no drill rig), 5-6 turrets. About 5,000 cucumbers at landing, ramping toward 24,000 with refill."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 5,
            "labels": [
                  "c3"
            ],
            "title": "Contract 3: shafts and sealing",
            "body": "Sealing a side shaft with the bazooka or a gravity-gunned fuel tank slows the refill."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 1,
            "points": 8,
            "labels": [
                  "c3"
            ],
            "title": "Contract 3: turrets and roof relays",
            "body": "Power relays on skyscraper roofs: grapple up, flip the switch, hold through the spin-up. A collapsed tower cuts that turret's power."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 0,
            "points": 13,
            "labels": [
                  "c3"
            ],
            "title": "Contract 3: shuttles and four targets",
            "body": "A shuttle every 4 minutes, five in all: 1-3 ore, 4 takes 80% of the people, 5 the last 20%. Four automatic corner miniguns and a rocket launcher while landed. Three ore sites and the shelter are defended at once."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "c3"
            ],
            "title": "Contract 3: mech drop and the ending choice",
            "body": "A mech drops midway; the end is a choice between boarding shuttle 5 at once and defending the last 20% until they board."
      },
      {
            "col": "backlog",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "c1",
                  "c3"
            ],
            "title": "Company drops inside a contract",
            "body": "Physical pickups with loaned support: fighter and tank mechs, and the laser hoverbike at the top tier."
      },
      {
            "col": "backlog",
            "type": "polish",
            "priority": 2,
            "points": 3,
            "labels": [],
            "title": "Plain health bar and shield; strip pickle-themed names",
            "body": "Replace the jar HUD, *PICKLED!* and the brine-named weapons. `decisions/0050`."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 2,
            "points": 5,
            "labels": [],
            "title": "Cucumber faces: scared and angry",
            "body": "Near-tier expression via a per-agent input or a few baked variants; benched. Brine, not blood."
      },
      {
            "col": "backlog",
            "type": "audio",
            "priority": 3,
            "points": 5,
            "labels": [],
            "title": "Whimpers and wails",
            "body": "Heard in the quiet before the alarm and at close kills; `AudioDirector` caps and pools them."
      },
      {
            "col": "backlog",
            "type": "tooling",
            "priority": 3,
            "points": 2,
            "labels": [],
            "title": "Verify Blender .glb facing and scale",
            "body": "Take a test asset through the pipeline and replace the two \"not yet verified\" lines in CLAUDE.md."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 1,
            "points": 13,
            "labels": [
                  "c1"
            ],
            "title": "Model the queen",
            "body": "Rind plates, weak-spot hatches (emissive), seed-spit mouth, feeder roots; voxel and destructible."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 2,
            "points": 8,
            "labels": [
                  "open1",
                  "cabin"
            ],
            "title": "Model the landing pod interior and ship cabin",
            "body": "Pod seat, window and door; the cabin with invoice printer and crate hatch; cloning-lab glimpses."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 2,
            "points": 13,
            "labels": [
                  "c3"
            ],
            "title": "Model the armed shuttle and colony set",
            "body": "Shuttle with corner miniguns; skyscrapers, the armoured shelter, ore storage, shafts, turrets, relays, fuel tanks, security mechs."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 2,
            "points": 8,
            "labels": [
                  "c2"
            ],
            "title": "Model the crash pod, storm planet and nuke blast",
            "body": "The contractor's crash pod, the storm biome and tornado, and the staged planet-scale blast."
      },
      {
            "col": "backlog",
            "type": "art",
            "priority": 3,
            "points": 13,
            "labels": [],
            "title": "First-person hands",
            "body": "The one rigged thing; late on purpose."
      },
      {
            "col": "verify",
            "type": "perf",
            "priority": 0,
            "points": null,
            "labels": [],
            "title": "60 fps at 1080p on Iris Xe",
            "body": "The standing requirement (0002). Re-measure on a rested machine with `bench.gd` after each system lands."
      },
      {
            "col": "done",
            "type": "tooling",
            "priority": 2,
            "points": 3,
            "labels": [
                  "built"
            ],
            "title": "Repository, Godot project, Jolt and Git LFS",
            "body": "Godot 4.7.2 mono, `net8.0`, Jolt set explicitly, LFS from the first commit (0003)."
      },
      {
            "col": "done",
            "type": "tooling",
            "priority": 2,
            "points": 3,
            "labels": [
                  "built"
            ],
            "title": "Perf instruments",
            "body": "`FrameProfiler`, `PhysicsStepProbe`, `PerfRecorder` (`U`) and `bench.gd` (0002)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "Player and the grapple",
            "body": "Zip, swing, orbit and release float; jetpack hover and dashes."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "The horde at 25,000",
            "body": "Flat arrays, simulation tiers, density cap and view cull; blasts and beams reach the crowd. Allies are attacked and bitten (0012)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "Voxel destruction",
            "body": "Every voxel model breaks the same way (0011); off the frame (0015); rubble stays (0016, 0022); chunk graph and building collapse (0027); trees hold by connection (0024)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "MagicaVoxel importer",
            "body": "`.vox` imported directly: parts, colour per vertex, emission, markers (0009)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "The planet",
            "body": "Cube-sphere at the origin, streaming, craters, sites (0017); seeded planets and biomes (0018); the session and loading screen (0021); pinned at 1,260 m (0037)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "Vulkan renderer",
            "body": "As fast idle and faster under the horde than D3D12 (0048)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "Vine nest grown at load",
            "body": "Seeded, 25 s at 25,000; saved nests are presets (0033); only valid vines (0036); one clump, 55-70 m apart (0038)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "Plants bend; leaves are meshes",
            "body": "Spine of springs, collision that follows, wind and the grapple push (0032, 0035); vines leave no rubble (0042)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "Pods in bulk and regrowth",
            "body": "One pod per cucumber in flat arrays, drawn instanced and bent on the GPU (0040); regrow until destroyed; the win rule (0034)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "built"
            ],
            "title": "The colony wakes on harm",
            "body": "Never on nearness; every cucumber drops into the horde over 30 s (0041)."
      },
      {
            "col": "done",
            "type": "tech",
            "priority": 2,
            "points": 3,
            "labels": [
                  "built"
            ],
            "title": "Structure and Litter physics layers",
            "body": "Static voxel bodies off Prop (0039)."
      },
      {
            "col": "done",
            "type": "tooling",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "The Vines tool",
            "body": "Nest tab, range lab, Good/Bad training logs (0043)."
      },
      {
            "col": "done",
            "type": "tooling",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "The editor bridge",
            "body": "Claude works in the running editor, every change one undo; own terrain brushes (0044)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "built"
            ],
            "title": "Shallow water",
            "body": "Wade, splash, the horde slower (0045)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 13,
            "labels": [
                  "built"
            ],
            "title": "Mechs fight on broken",
            "body": "Procedural gait, only core or head kills, mend themselves, Blender moves baked to data (0046)."
      },
      {
            "col": "done",
            "type": "tooling",
            "priority": 2,
            "points": 8,
            "labels": [
                  "built"
            ],
            "title": "The asset lab",
            "body": "One place to look at, tune and break any asset, in the editor and in play (0047)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "built"
            ],
            "title": "Melee strokes and the sledge",
            "body": "Pose strokes, not clips (0028)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 3,
            "labels": [
                  "built"
            ],
            "title": "Gamepad",
            "body": "One layout on the same actions (0025)."
      },
      {
            "col": "done",
            "type": "tech",
            "priority": 2,
            "points": 3,
            "labels": [
                  "built"
            ],
            "title": "EventBus and AudioDirector",
            "body": "Autoloads (0006, 0007)."
      },
      {
            "col": "done",
            "type": "feature",
            "priority": 2,
            "points": 5,
            "labels": [
                  "built"
            ],
            "title": "Onboarding level",
            "body": "`lvl_nest_intro` on a saved 1,000-cucumber nest, with landmarks and prompts (0049)."
      }
],
  };
})(window.PB = window.PB || {});
