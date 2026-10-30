# Pitch

*The game is **Dill Control**. The prototype was called Swampy, and the document
before that WET WORK.*

**Status, 29 October 2026.** The description of the game, rewritten to the
locked design (`docs/decisions/0050`-`0054`). Where a built record carries a
number (0033-0049), the pitch uses it. The prototype that proved the game is the
lab repository, `fps-swampy`; this repository ports its proven systems. How is in
`plan.md`.

**Ground rules** (0050). Only the infestation is cucumber-themed: the enemies
are a parasitic plant, cucumbers grown giant from the seeds in the company's
terraforming kit. Everything else, the player, weapons, health, death, menu,
ship and weather, is plain industrial sci-fi. Everything has a cause and a
history, even when the player is never told. The grapple and the jetpack must
feel amazing.

---

## The game in one breath

You are a contract exterminator. A mining company hires you to clear a small
planet of the cucumber horde so it can be drilled, one planet per contract, and
the company sends an itemised invoice. You drop in a landing pod, swing over the
horde on a grappling line, rain explosives on it, tear through its vine nest and
bring down the queen at its heart.

**The ground belongs to the horde. The air belongs to you.**

The company planted a terraforming and oxygen kit to make mining easier. The kit
carried cucumber seeds. The company caused the infestation and now pays you to
clean it up; nobody says so.

## The focus

Three things, and everything else serves them:

1. **Precise, aimable grappling.** The line has to feel precise, readable and
   aimable: zip, swing, orbit and release, with the crosshair always yours.
   Polish here comes before any new feature.
2. **Fast, responsive vehicles.** The hoverbike, and whatever follows it, should
   be quick and a thrill to throw across a planet. Sluggish is a bug.
3. **Destroying the planet and everything on it.** The ground craters, towers
   collapse, vines fall, and ruins come down as dust. Everything on a planet
   should be something you can take apart.

## Pillars

1. **A sea of enemies, not a squad.** Twenty-five thousand cucumbers at once, on
   a laptop with integrated graphics, at 60 fps. The crowd is the spectacle and
   the pressure, never a checklist of targets.
2. **Movement is the exhilaration.** The grapple is the core verb: zip, swing,
   orbit, let go at the top of the arc and fire. Standing still is the mistake.
3. **Destruction is punctuation.** Blowing an anchor removes your route.
4. **Bright and comic, played straight.** Cel shading with ink outlines: a
   cartoon monster simulator, industrial sci-fi in low-poly voxel. The unease
   comes from the cucumbers' scared and angry faces and brine instead of blood.
   Horror visuals are rejected.

## The world

Planets are small: nest planets are pinned at 1,260 m in radius (0037), and air
and weather on a planet that size are accepted. Each holds a valuable mineral
(unnamed). Every drillable planet gets a small mining town, built after the
planet was declared clean. A surviving pod grows underground, the miners drill
into it, and the horde comes up the shafts.

**The nest is vines, not a compound.** The trees are giant trellised cucumber
vines, up to about 120 m tall, each holding as many pods as its kind rolls (the generator decides; only the total, one pod per cucumber, matters). A nest is one
clump (0038): vines 55-70 m apart inside a hard 400 m radius, dense at the centre
and thinning outward, so open ground suits the bike and dense ground suits the
grapple. 25,000 cucumbers is 117 vines reaching 393 m. Vines come from an offline
procedural generator that bakes a curated library; a nest is grown at load from a
seed (0033).

**Every cucumber starts on a pod** (0034): one pod per cucumber, 1,000 to 25,000.
The nest is one hive mind. **It never strikes first:** harm to any part of it,
never nearness, wakes all of it (0041), and over 30 s every cucumber drops into
the horde. Vines regrow pods, so pods or whole vines have to be destroyed, which
gives each contract a built-in clock that rewards destruction. Any pod can regrow
cucumbers and any cucumber can become a queen. **Every nest has a queen.**

## How a contract plays

A contract runs 20 to 30 minutes. The contract screen shows a rough sketch of the
planet and nest, and you pick the drop point, free to scout far out or drop
straight onto the nest, with no mandatory setup phase.

1. **Arrive by pod.** Every arrival starts inside the landing pod with a
   handcrafted descent through the window; the door opens onto vines and
   cucumbers. Landing in a town smashes the pod through skyscrapers (0054).
2. **Do the job.** Three contract types:
   - **Nest clearing:** full violence, and the only type with a boss, the queen.
   - **Town defense and evacuation:** the horde plus the timer is the wall. A
     procedural town, sheltered colonists (no walking people), mine shafts as
     spawn holes, turrets, security mechs and fuel tanks, with the infestation
     stage varying from an intact town to a nest grown through it.
   - **Gear retrieval:** a dead contractor's gear, not his body, for lower pay
     and replayed for time, with the gear at the hive's centre. Its challenge is
     still being designed (0051).
3. **Win.** For a nest: the queen dead, every pod destroyed and at most a fifth
   of the cucumbers alive (0034); all of them for a bonus.
4. **Leave.** Extraction mirrors arrival: a pod drops, and the ground falls away
   through the window.

Rejected: escort missions, planet modifiers as the source of variety, caves for
now, walking colonists.

## The cucumbers

Every attack is telegraphed, so dodging is a skill and getting hit is never a
coin flip.

| Cucumber | Role |
| --- | --- |
| **Standard** | The body of the crowd. It bites. |
| **Cornichon** | Small and fast. Dashes in and nips, and is also thrown as ammunition. |
| **Spitter** | Spits brine that leads your movement. |
| **Tank** | Big, soaks damage, throws cornichons and punches; a hit throws you. The Thrower is folded into it. |
| **The queen** | A stationary pod at the nest's centre; the only boss. |

The queen takes rockets, the laser, the pole saw, the minigun and, slowly, the
.50; rifle rounds barely mark her. Feeder vines heal her until cut and Tanks and
Spitters guard her. Her attacks are a screech shockwave, sticky seed spit and
tearing down the vines around her (0051). Mother Brine, the giant rigid voxel
body, is a later roaming boss, cut from the slice. Mole cucumbers belong to the
full game.

The rule from the prototype holds: **nothing chases the player through the air
faster than they can answer it.** Anti-air comes from the ground.

Targeting is the same everywhere: one flow field toward the contract's anchor,
plus a player aggro radius that allows baiting. The director varies the radius
to ramp or steer, always with a telegraph such as a screech.

## Tools

**On foot:** a grappling line on the middle mouse button: tap to zip, hold to
swing, orbit a giant, and float on release. A jetpack gives hover and gas
dashes. The mouse wheel switches weapons; the right mouse button aims on every
weapon.

**The hoverbike** is a fast scout for getting from A to B. Its gun, rocket pod,
bomb bay and laser all run on limited ammo, which limits its power. It is a special item, not how a contract begins
(0052). Its laser, RimWorld's orbital power beam at bike scale, is the one you
hold down.

**Five core weapons** (0052), with the grapple on top of all five:

- **Assault rifle:** the heavy, punchy workhorse. Piercing rounds, then an
  underbarrel grenade launcher.
- **Bazooka:** a straight rocket from one tube. A four-rocket volley, then
  bigger rockets.
- **Pole saw:** company-issue melee with reach, a chainsaw blade on a long pole.
  No ammo, always available. Longer reach, then a spin attack.
- **Laser cannon:** a sweeping, deliberately imprecise beam on a cooldown of about
  5 s, the main vine-cutting tool; also a gravity-gun mode that pulls and punts.
  Shorter cooldown, then a wider beam.
- **Sniper:** a .50 long rifle, "the air belongs to you". Explosive rounds, then
  an airstrike.

**Specials** are found: reusable ones are kept and refilled (the minigun, a fire
rocket launcher); consumable super weapons have a few charges (the nuke
designator, which marks a spot you must fly clear of). Five slots with free
choice, and a consumables bar for grenades, medkits, shield generators and ammo.

## Progression, health and death

- **Six tiers a weapon:** ammo, ammo, mod, mod, secondary ability, and its
  evolution. Tiers persist.
- **A shop on the ship** sells upgrades for invoice money. Inside a contract the
  company still drops physical pickups with loaned support: fighter and tank
  mechs, and the laser hoverbike at the top tier.
- **A regenerating shield over health.** Ammo and medkits are ordered on the
  invoice.
- **Death is cloning:** three clones a contract. Losing all three fails the
  contract; you wake on the ship unpaid, keep your upgrades and can replay it
  or any unlocked contract. Nothing carried is lost on death (0053).

## The ship and the menu

The main menu is a live 3D view out of the ship's window: the planet rotating
slowly, ship ambience, a periodic beep. Its buttons are Play, Options, Wishlist
on Steam, Join Discord, Send feedback, Credits and Quit. Between contracts you
are in the ship cabin. A printer spits out the itemised invoice (fee minus ammo,
clones, damage and rentals) and stamps it PAID. Your purchase arrives in a crate
through a hatch, and the next planet appears in the window.

## The opening, and the slice

On Play, short, deliberately unclear cloning sequences, shown only once and never
explained. The player has no memory, mirroring a new player, so the amnesia and
the tutorial are the same thing. Then control inside the landing pod, watching
the descent through the window. The cloning flashes replay at the first death in
contract 1 (0054).

The vertical slice is onboarding (`lvl_tutorial`, 0055) and three contracts,
about an hour: **nest clearing, loot grab on a storm planet, colony rescue**
(0051). The full game opens with a fixed run of three or four contracts that
teach each type, then a curated campaign, then a procedural contract board as
the endless mode.

## The story

Parked, not decided: the vertical slice comes first. A story spine of debt, a
show-don't-tell theme, two endings and a recurring rival are written down as an
idea in [`ideas/story.md`](ideas/story.md). Nothing in this pitch depends on it.

## Technical foundation, already proven

All measured on the target machine: an 11th-gen Core i5 with Iris Xe at
1920x1080 native, 60 fps, Godot 4.7 C# with Jolt physics.

- **The horde:** 25,000 agents on flat arrays, with tiered simulation, a density
  cap, a view cull, borrowed colliders for hitscan, and blasts and beams that
  reach the whole crowd. Fifty thousand is one key away.
- **Voxel destruction:** buildings meshed in 16-voxel sections under a
  per-frame budget, a chunk graph for what falls (0027), armour thresholds, and
  dust clouds instead of rubble.
- **The planet:** a cube-sphere with point gravity, streamed terrain and craters
  that reshape both the ground and the horde's routing.
- **The nest:** vines that bend (0032), pods in bulk (0040), a nest grown at load
  (0033), and the tools to make and judge them (0043, 0044, 0047).
- **Mechs** that fight on broken and mend themselves (0046).
- **The look:** a depth-edge ink pass and toon lighting, cheap enough for 25,000
  cucumbers.
- **The instruments:** `bench.gd` and the perf recorder, so every number above is
  taken unattended rather than by eye.

---

## How we got here

The game was found by building and playing, not by writing. This is the path,
so that nobody walks it a second time.

**It started as WET WORK** (see the lab's `docs/summary.md`): a drilling contractor on a
swamp planet, coolant spreading from the drill, an alligator horde storming it,
and a roguelike economy around the greed of the throttle. It was coherent on
paper. In play it fell apart, and the playtest put it plainly: **the premise and
the mechanics were pulling in different directions.** A swamp spreading in real
time was expensive to simulate and hard to make beautiful. Standing on the
ground shooting ten capsules out of three thousand storming a rectangle felt
pointless. The only thing that was consistently fun was the hoverbike, and the
single best moment was flying it into a nest and destroying it.

**So the premise was rebuilt around what was fun.** The drill, the swamp and
the defend-the-objective loop were dropped. Some rules came out of that turn:
*the horde needs a narrative reason to follow you, and if it follows you, it
must be able to answer your vehicle.* That gave the contract exterminator, sent
by the mining company to clear a planet so it can drill, which is a premise
that is cohesive with every mechanic in it. The tone stayed comic, because the
look was always going to be cel shaded. Then came the most important design
input of the whole project: **the enemies should be pickles.** It set the tone,
the names, the colours and the jokes, and every later decision got easier.

**The giant came from wanting something huge and terrifying,** a single enemy
that can end you in a second. Pickles have no limbs, which turned out to be a
gift: Mother Brine is one rigid voxel body that squashes and stretches, with no
animation rig at all. The direction set at the same time was just as clear: *it
should not become Attack on Titan.* The fantasy of anchoring to a giant was
right; the risk was swinging controls that become impossible to aim. So the
grapple was built to keep aiming normal: zips that pull in a straight line,
orbits that circle on rails, and a float at the top of every release.

**Death was settled early and firmly:** cloning, three tries, and then the
company stops paying for you. It is felt, and it is funny.

**Then came the honest fork.** Two prototypes were both fun: swinging through a
forest with a giant in it, and destroying a nest from the hoverbike. The bike
won the argument on paper, because open terrain is cheap content for a solo
developer and hand-built swing levels are not. Then the playtest overruled the
argument. **Swinging, even janky, was more exhilarating than flying, and going
through the nest on foot with the grapple combined both into the best thing
yet.** The bike's destruction read as rage; the swing read as joy. Moving fast
through the air while the world comes apart underneath was the game.

That also dissolved the problem that had blocked the grapple from the start,
that it needs tall things to hang from. **The compound is the tall things.**
Spires, towers, ruins and the jar are both the targets and the anchors.

**The last input was scale:** a hundred thousand pickles racing at you, some
spitting, some throwing smaller pickles, some waiting for you to touch down,
while you destroy them from the air with weapons and explosions that feel
juicy. The honest arithmetic said the current approach topped out near ten
thousand. The horde was then rebuilt with simulation tiers, a crowd density
cap, cache-friendly passes and a view cull, and **it now ships at 25,000 on the
target laptop,** at 60 fps when first measured. The flying slicers were cut on the way, as
frustrating rather than challenging, and the threat in the air now comes from
the ground.

**And the bike earned its place for good.** Losing the argument to the swing
did not make it a side vehicle. Its role was set by two references: it is this
game's Seamoth, the machine that is yours, and its laser is RimWorld's orbital
power beam concentrated into a single line that melts whatever is in its way.
Climbing out to space and seeing the whole planet with its targets marked then
suggested the opening: a commercial mission should begin in orbit, with the
player looking down at the planet, deciding where to land and what to do, and
flying straight down to do it.

**The verdict, in the project's own words:** this is not the final form of the
game. *That game does not exist yet; this one does.* The premise is loved; what
it needs now is content. That content goes into a clean project, built on the
systems this prototype proved.

### The October changes

Between 12 and 29 October the design moved on, and most of it is recorded in
0032-0054. Playing said the same thing three times, that swinging through things
as they come apart is the best part, so the bike stopped being the answer to
everything. The compound of spires, towers and an armoured jar became a
vine nest that wakes only when harmed (0034-0041), and the giant became a queen.
The pickle pun was pulled back to the infestation alone and the enemies became
cucumbers grown from the company's own seeds (0050). The arrival from orbit gave
way to the landing pod (0054), random kill-based upgrades to a shop on the ship
(0052), and the one mission to three contracts (0051). A worry that one planet,
one nest and one contract type gets boring raised a pivot to a story-driven
game; the answer was no pivot, and the story is parked as an idea
(`ideas/story.md`).

### What was learned, kept as rules

- **Find the fun by playing, then write it down.** Every premise written first
  had to be thrown away.
- **Content must be cheap for one person.** Prefer generated ground with a few
  hand-authored set pieces (compounds, giants) over hand-built levels.
- **Enemies threaten the space you want to be in.** Telegraphed, readable, and
  answerable from where you are.
- **No multiplayer.** Netcode, synchronising voxel destruction and
  non-deterministic physics are not a solo project's fight. Co-op stays a
  "maybe, decided early" at most.
- **Performance is a requirement.** 60 fps at 1080p on Iris Xe, measured, not
  felt.

## Open questions

- **What unlocks persistent tiers** in the full game.
- **Still to settle:** special-weapon charge counts, kill thresholds for later
  drops, whether drops draw the horde, scouting tools, hit-stop tuning, the
  mineral's name, how upgrades are bought while in debt (once the story is
  unparked) and the endings.
- **Parked until measured or played:** rocket balance (one bazooka load killing
  7,000-8,000 may be too little pressure on the player rather than a kill-count
  problem); fear-scatter behaviour after blasts, with scared cucumbers scattering
  so the player must herd them back; separable body pieces on kills, which
  cannot be judged until measured on Iris Xe because a cucumber is one
  shader-drawn capsule; early pacing, where thrown Cornichons arrive before the
  bigger types and make fights feel like mowing down small fry.
- **Parked ideas:** Tanks rushing to defend a damaged vine within a tight radius
  (0041 wakes the whole colony on any harm, so a local guard has nothing to add
  yet); contract 2's challenge, since a found bike can simply fly to space
  (0051).
- **Backlog, later:** root hearts that wither nest sections; mid-mission company
  bonus jobs; Mother Brine climbing out of the main shaft as a town climax; a
  buried queen too big to leave her cave; caves; a player-called airstrike with a
  handheld designator; cucumbers making sounds scaled to their size; magic
  weapons; detailed ballistics, checked against the brine-not-blood tone; a small
  cast of recurring rival characters.

## Next

See `plan.md`: the slice, its milestones, and what 0033-0049 have already built.
