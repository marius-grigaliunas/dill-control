# Pitch

*The game is **Dill Control**. The prototype was called Swampy, and the document
before that WET WORK.*

**Status, 8 October 2026.** The description of the game. The prototype that
proved it is the lab repository, `fps-swampy`; this repository is the fresh
project that ports its proven systems and leaves the rest behind. How is in
`plan.md`.

---

## The game in one breath

You are a contract exterminator. A mining company wants a planet's minerals,
and the planet is infested with pickles: tens of thousands of them. You fly
down from orbit on your hoverbike, pick your landing, swing over the horde on a grappling line, rain explosives on it, tear
through its nests on foot and on a hoverbike, bring down the giant that rules
it, and leave the ground clear for the drills. The company sends the invoice.

**The ground belongs to the horde. The air belongs to you.**

## The focus

Three things, and everything else serves them:

1. **Clean grappling movement.** The line has to feel precise, readable and
   aimable: zip, swing, orbit and release, with the crosshair always yours.
   Polish here comes before any new feature.
2. **Fast vehicles.** The hoverbike, and whatever follows it, should be quick,
   responsive and a thrill to throw across a planet. Speed is the point;
   sluggishness is a bug.
3. **Destroying the planet and everything standing on it.** The ground
   craters, towers collapse, spires snap, jars crack, and ruins come down as
   dust. Every structure on a planet should be something you can take apart.

## Pillars

1. **A sea of enemies, not a squad.** Twenty-five thousand pickles hunting you
   at once, on a laptop with integrated graphics, at 60 fps. The crowd is the
   spectacle and the pressure. It is never a checklist of individual targets.
2. **Movement is the exhilaration.** The grapple is the core verb: zip, swing,
   orbit a giant, let go at the top of the arc and fire. The vehicle is the
   second gear, for speed, distance and heavy ordnance. Standing still on the
   ground is the mistake.
3. **Destruction is the punctuation.** Wrecking a compound also wrecks your
   route through it: an anchor you blew up is an anchor you no longer have.
4. **Bright, comic and played straight.** Cel shading with ink outlines. The
   enemies are pickles, the company is cheerful, and the paperwork is itemised.
   Nobody in the fiction thinks any of it is strange.

## How a mission plays

One planet per mission. The shape so far:

1. **Arrive from orbit.** You pick the contract at the computer in your own
   ship, and after one loading screen the mission opens in space, on the
   hoverbike beside the ship, with the whole planet in front of you. Every
   contract is a different planet. You fly down to it with no further load
   and no cut. On the way in you read it: where the nests are, where the horde
   masses, where the rearm point is. Then you choose where to land and what to
   hit first. The prototype already gets most of the way there: you can climb
   out of the atmosphere, see the planet whole, and fly back down to marked
   targets.
2. **Breach the compound.** It stands on open ground and is built from tall
   things: rock spires, spitter towers, ruins and the armoured hive jar. Those
   are your anchors. Swing between them while the anti-air tries to swat you
   down, then crack the jar with heavy ordnance and finish its core through the
   breach.
3. **The giant.** A colossal pickle you fight from the line: orbit her, strip
   her armour, burst her weak points and get out from under the slam.
4. **Clear the planet.** The horde hunts you the whole time and comes to you,
   not to an objective. You keep moving, keep killing, and push the population
   down until the company's threshold is met.

A rearm pod lands well away from the fight, so retreating to resupply and
coming back is part of the rhythm.

## The pickles

The roster takes its cue from the brief the project actually received: *the
enemies should be pickles.* Every attack is telegraphed, so dodging is a skill
and getting hit is never a coin flip.

| Pickle | Role |
| --- | --- |
| **Standard** | The body of the crowd. Not too small, not too big; it bites. |
| **Cornichon** | Small and fast. Dashes in and nips, and it is the ammunition of the thrower. |
| **Thrower** | Lobs live cornichons at you on a high, visible arc. They land and join the hunt. |
| **Spitter** | Spits brine that leads your movement and wets the ground where it lands. |
| **Tank** | Big, soaks damage, and a hit throws you. |
| **Mother Brine** | The giant. A voxel-armoured colossus that hops, slams, topples, rolls, erupts geysers and sheds cornichons. |

The anti-air comes from the ground: spitters, throwers and the compound's
towers, all slow enough to read and all killable where they stand. The flying
blades that were tried and cut taught this rule: **nothing should chase the
player through the air faster than they can answer it.**

## Tools

**On foot:** a grappling line on the middle mouse button that keeps your hands
free. Tap to zip, hold to swing, hook the giant to orbit her, and get a moment
of float at the top of every release. A jetpack hover and gas dashes cover the
gaps.

**The hoverbike is your Seamoth.** What the Seamoth is to Subnautica, the bike
is to this game: your signature machine, the thing that takes you from orbit
to the surface and across it, and the thing you come back to. It carries a
gun, a rocket pod and a bomb bay, and it is fast.

**Its laser is an orbital power beam brought down to bike scale.** Think of
RimWorld's orbital power beam, concentrated into one line: it melts whatever
is in its way. Pickles, armour, towers and the ground itself all give way, and
it leaves molten pits and fires behind it. It should be the most satisfying
thing in the game to hold down.

**The arsenal:** weapons are retuned against the crowd they actually face, so
splash kills and beams pierce. The current set includes the Brine Ripper, the
Gherkin Volley, the Vinegar Lance, the Brine Burner and the Cluster Jar, plus
called airstrikes and a gravity manipulator that can pick up a pickle and throw
it into its friends.

## Health, death and the company

- **A regenerating shield over health.** Kills drop health; ammunition and
  medkits are ordered, and the order goes on your invoice.
- **Death is cloning.** You get three clones per mission, and after the third
  the company stops bringing you back.
- **Progression is a company package.** Every so many kills the corporation
  rewards its most productive exterminator with an upgrade, announced like a
  memo: *Vermin Control Bonus*, *Exterminator of the Week*, *Shareholder
  Gratitude Parcel*. Upgrades arrive on their own and stack, with no menu in the
  middle of a fight, and the machete gains the most. This already works in the
  prototype. Persistent unlocks across missions (weapons, bike parts, ordnance,
  new planets and nest types) follow the Deep Rock Galactic and Helldivers
  model, and are not built yet.

## Technical foundation, already proven

All measured on the target machine: an 11th-gen Core i5 with Iris Xe at
1920x1080 native, 60 fps, Godot 4.7 C# with Jolt physics.

- **The horde:** 25,000 agents on flat arrays, with tiered simulation, a density
  cap, a view cull, borrowed colliders for hitscan, and blasts and beams that
  reach the whole crowd. It held 60 fps when first measured. The latest
  measurement, after the look and HUD work, puts a fight with the whole crowd
  on top of you at 16-17 ms, so about one frame in six is late. About 2 ms of
  that is new and not yet found. Fifty thousand is one key away, at about 69
  fps on a cool machine.
- **Voxel destruction:** buildings meshed in 16-voxel sections under a
  per-frame budget, structural collapse, armour thresholds, and dust clouds
  instead of rubble.
- **The planet:** a cube-sphere with point gravity, streamed terrain and craters
  that reshape both the ground and the horde's routing.
- **The look:** a depth-edge ink pass and toon lighting, cheap enough for
  25,000 pickles.
- **The instruments:** `bench.gd` and the perf recorder, so every number above
  is taken unattended rather than by eye.

## Already in the prototype

Beyond the core, the prototype has grown a good deal of game around it. None of
this needs reinventing in the new project:

- **Destruction everywhere:** every big explosion digs a crater, the bike's
  gun digs trenches, its laser melts pits and starts fires, and fire zones
  burn the crowd. Buildings collapse when cut through, and the compound's
  spires and ruins are destructible voxel rock that regrows when the compound
  does.
- **The arsenal:** a machete with direction-chained swings and a cut that
  hits harder at speed, a shotgun, a splash rifle, a piercing lance, a
  flamethrower, a rocket launcher, the Cluster Jar grenade, a gravity
  manipulator that throws pickles into pickles, and an airstrike designator
  with five ordnance types (autocannon, cluster, heavy HE, incendiary and a
  coolant dump).
- **The bike:** two flight modes, boost, roll, hover hold, a gun of explosive
  rounds, a piercing laser, a rocket pod and a bomb bay, a voxel hull that can
  be redesigned in the editor tool, and obstacle courses to fly.
- **Juice:** hit-stop when one blow kills a crowd, brine spraying from every
  kill, pickles that sound bigger the bigger they are, impact sounds by
  surface (flesh, stone or dirt), blast shake that builds through a salvo, and
  staged explosions drawn as inked cartoon layers.
- **The HUD:** health as a pickle jar sloshing with brine, a kill tally with a
  combo that breaks, tier words from *Killing Spree* to *Extinction Event*, and
  *PICKLED!* when the jar runs dry.
- **A demo shell:** a title screen, a pause menu, graphics presets aimed at
  integrated GPUs, key rebinding with saved settings, a help screen read live
  from the key bindings, first-two-minutes onboarding prompts, and an end
  screen with the run's numbers, a retry and a wishlist button.
- **The world:** a planet with a second body to fly to, a sky that becomes
  space as you climb, ground scatter, a forest built for swinging, and a
  browser tool for building voxel models, previewing destruction and testing
  flight and the horde.

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

- **The orbital approach.** Seen from space, the planet's edge is jagged,
  because the terrain culls its far chunks for performance. An approach that
  opens the game needs a clean silhouette, probably a cheap distant planet
  model that hands over to the real terrain as you descend. Choosing a landing
  site also needs the planet to be worth reading from orbit: visible nests,
  hordes and landmarks.
- **How far the bike goes in combat.** It is the transport and the laser
  platform, but how much of a fight happens from the seat, and how much on
  foot with the line, is for playtesting. Hover mode currently feels sluggish.
- **What a mission's clear threshold is** (80% of the population was the first
  number) and how it reads with a horde that hunts you.
- **What losing all three clones costs** across a campaign: debt, gear or the
  contract.
- **The shape of progression:** how far company packages go inside a mission,
  and what persists between missions.
- **Juice:** hit-stop, brine splatter, chain reactions and layered kill sounds.
  The prototype has the beginnings; the new project should design for it from
  the start.

## Next

See `plan.md`: milestones M0 to M7, from an empty project to one mission played
start to finish at 60 fps.
