# 0051 The slice: onboarding and three contracts, nest clearing, loot grab, colony rescue

*22-23 October 2026. Decided, not built. Replaces the slice table of
`docs/plan.md` and `docs/notes.md`. Builds on 0034, 0037, 0038, 0041 and 0049,
and on 0050 for the fiction.*

## Context

The plan's slice was one mission: orbit, bike, compound, Mother Brine. an
earlier trial paused that, and 0034-0041 replaced the compound with a vine nest, and 0049 built
the onboarding level. What the slice is, in contracts, was never written down.

## Decision

**The slice is onboarding (`lvl_nest_intro`, 0049) and three contracts in order,
about an hour in all: nest clearing, loot grab, colony rescue.** Each has
scripted beats on procedural terrain with one locked, tuned seed for the demo. A
contract runs 20-30 minutes. After the third the game ends on an empty contract
screen with a wishlist-on-Steam line; no one is locked out, and any contract can
be replayed. The opening sequence (0054) comes before contract 1.

**Enemies:** Standard, Cornichon (small, fast, also thrown), Spitter (leading
shots), Tank (which also throws and punches, so the Thrower is folded into it)
and a stationary queen. Cut from the slice: the Thrower as a kind, mole
cucumbers, the underground queen, Mother Brine (a later roaming boss).

**Loadout:** all five core weapons in fixed slots from the drop (0052).
Consumables are shield, medkits and ammo. Specials: the minigun (reusable) and
the nuke designator (consumable). **Shop:** both tiers of every weapon are
shown, but the slice pays for only **two purchases in total**, to leave players
wanting more; every purchase is logged to learn which upgrades people want.
Contract 1 carries light tips (grapple first, then movement and the weapon
wheel); the nuke gets a one-off prompt that fades if ignored.

**Targeting** is the same in every contract: one flow field toward the
contract's anchor, plus a player aggro radius that allows baiting. The director
varies the radius to ramp or steer, always with a telegraph (a screech before
they turn). Contract 3 needs four targets.

### Contract 1: nest clearing

Free choice of drop, by pod (0054), no bike. It builds on 0049's
`lvl_nest_intro` and 0034's win, and adds what that level leaves out: the pod
drop, clones, the queen, the minigun gift and extraction. It teaches, in order:
shooting and the shield against workers; the grapple and the laser at the first
vines; clones at the first death (0053, 0054). The briefing says only "clear the
nest".

- **At 50% of the nest destroyed** the company gifts the armour-piercing
  minigun: the first special, and a hint that something is coming.
- **The queen** is a surprise and teaches how bosses work: a stationary pod at
  the nest's centre. Rifle rounds barely mark her rind; she takes rockets, the
  laser, the pole saw, the minigun and, slowly, the .50. Feeder vines heal her
  until cut; Tanks and Spitters guard her. Her weak spots open only after she
  attacks. Telegraphed attacks: a screech shockwave that knocks you off the
  grapple and calls the horde; sticky seed spit that slows you; tearing down vines
  around her in a growing radius, which removes anchors and pushes you from
  minigun range out to sniper range. Tuned for tier 1 at 2-3 minutes, roughly
  25 bazooka hits.
- **The win:** the queen dead, 100% of pods and 80% of cucumbers (0034's rule;
  loose ones die off without pods), the HUD marking the last pods. Then you call
  extraction, a pod drops, and you ride the rocket up, watching the ground fall
  away through the window.

### Contract 2: gear run (needs more work)

The contract that introduces the gear run: land on a planet and retrieve lost
gear from the centre of the hive. The planet suffers extreme weather, a different
planet in storm season (planets have seasons of many and few storms): fog,
lightning, tornadoes, dead calm. The cucumbers are the same; the weather is
mood and pressure, not a rule of its own. The map is shown and the player picks a
spot, but the storm scrambles the ship's targeting and he always lands at one
fixed spot at the nest's edge: a glitch that must read as intended.

The cause: the last contractor was sent in heavy, with a nuke designator and a
laser bike; the storm threw his drop into the nest and he died. The company wants
the gear, not the body: this is the gear-retrieval contract type, for lower pay.

- **It is not a stealth contract.** The hive is the built one (0041): harm wakes
  all of it, nearness does not. There is no harm threshold, no worker count and
  no dormant mode. Every cucumber is on a vine (0034); none is on the ground.
- **His crash pod** sits at the centre of the hive, high in the tallest vine
  under a beacon, and holds the nuke designator. It has an optional call-in
  prompt that fades if ignored; using it earns the "piece of shit" achievement.
- **A second tracker** leads to the bike, thrown clear in the crash. Its laser
  fires freely for 10 s, then cools down.
- **The weather climax:** the storm peaks and a tornado rips up vines, with
  cucumbers flung at you (telegraphed and shootable mid-air) and lightning
  everywhere. The storm grounds every ship.
- Destroying the nest is optional.

**Open: the challenge.** As written, once the bike is found the player can fly it
straight up to space, which makes the run an errand with no tension. What the
player must do to get the gear out, and what stops the bike being an exit, is not
designed. The pieces above are the setting, not yet a contract. Until it is
reworked, the slice's second contract is a placeholder for the gear-run type.

### Contract 3: colony rescue

The cause: miners drilled into an underground pod and the horde comes up the
shafts. The player has full drop control and can smash the pod through
skyscrapers. The town is Spore-style: one big pad; skyscrapers (the terraformed
patch is small, so they build up, and because they look and collapse well); the
main shaft at the centre (**no drill rig**) and side shafts at the edges as spawn
holes; 5-6 turrets.

- **The horde:** about 5,000 on the ground at landing, ramping toward a cap of
  about 24,000 with refill. Sealing a side shaft with the bazooka or a
  gravity-gunned fuel tank slows the refill.
- **Defense is movement:** emergencies across town. Turret power relays sit on
  skyscraper roofs; grapple up, flip the switch and hold through the spin-up. A
  collapsed tower cuts that turret's power.
- **Four targets at once:** three ore storage sites and the human shelter
  (heavy armoured metal, indestructible, a landing pad on top).
- **Shuttles:** one lands every 4 minutes, five in all. 1-3 take only ore, 4
  takes 80% of the people, 5 the last 20%. Each has four automatic corner
  miniguns and a rocket launcher that mow down everything in a radius while it is
  landed: a cinematic moment of awe before the horde floods back. A site drops
  off the target list once its shuttle leaves.
- **A mech drops midway.** The hoverbike is **a company drop at 20,000 kills,
  not a rental** (0052); used to bait groups away.
- The town crumbles over time; dying is expected, and clones cover it (0053).
- **The end is a choice:** board shuttle 5 at once and leave the last 20%
  running across the pad, or defend them until they board. About 22 minutes.

## Costs and open ends

- **Contract 2 is unfinished** (see its open question); the built wake (0041)
  stands for it, and no new cucumber state is needed.
- Contract 3 is a town, not a nest: no pods, no queen, a horde fed by shafts.
  0038's "no compound or city goes with a nest" still holds; this is the other
  case.
- Special-weapon charge counts, the kill thresholds for later drops, whether a
  drop draws the horde, scouting tools, hit-stop tuning and the mineral's name
  are open.
- Nothing here is built except what 0034, 0041 and 0049 already are.
