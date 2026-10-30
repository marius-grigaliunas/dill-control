# 0050 Ground rules: only the infestation is cucumber, everything has a cause, the grapple and jetpack must feel amazing

*23 October 2026. Decided, not built. Reframes the fiction; folds in the tone of
the former 0029 and 0030 (deleted). Changes no code identifier.*

## Context

The pitch's joke was pickles everywhere: the pickle-jar health, *PICKLED!*, the
brine-named weapons, a company that talks in brine. The jar and *PICKLED!* are retired for being the wrong way round (the player looked like a pickle).
The rest of the fiction still leaned on the same pun, and a game that is
cucumber-themed in every system has no straight side for the cucumbers to be
funny against.

## Decision

- **Only the infestation is cucumber-themed.** The enemies are a parasitic
  plant: cucumbers grown giant from the seeds in the company's terraforming kit
  (0051), not literal pickles. The player, weapons, health, death, menu, ship and
  weather are plain industrial sci-fi. Anything overly pickle-themed is stripped:
  the pickle-jar health, *PICKLED!*, brine-named weapons (0052 replaces them).
  Brine stays what the cucumbers bleed (below).
- **Everything has a cause and a history**, as in real life, even when the
  player is never told. The planet has a mineral because the company wants it;
  the cucumbers are there because the company planted them; the nest has a queen
  because every nest does. A design that needs a thing to exist without a cause
  is a design to be reworked.
- **The grapple and the jetpack must feel amazing.** Polish on them comes
  before any new feature (the pitch's first focus), and the slice is judged on
  them first.
- **These rules go at the top of every design doc.**
- **The cucumbers are innocent and the player is not one of them.** They are a
  lifeform that wants to grow and never strikes first (the wake is 0041). Their
  faces are **scared and angry**, on the near tier only, as a palette or texture
  swap in the vertex-animated body; the far tier stays a few-sided column. Brine,
  not blood, is theirs alone. Whimpers and wails exist, drowned out in the fight
  and heard in the quiet before the alarm and at close kills; `AudioDirector`
  (0007) caps and pools them. The player's health is a plain bar under a shield.
  The look stays cartoon: cel shading and ink.
- **Code is not renamed.** `pickle_hang`, `PodField`, `SpawnOrigin.Pod`, `nme_`
  prefixes, bench modes and the older records keep "pickle". The fiction says
  cucumber; the codebase does not churn for it.

## Costs and open ends

- Older records (0034, 0041 and others) say "pickle" throughout and
  are not rewritten. Read them as the same creature.
- Faces, sound and the invoice's words are where "cucumber" is felt; none of
  that is built.
- Weapon names are not final (0052).
- The near-tier expression needs a per-agent input or a few baked face variants;
  which is cheaper is benched. Whimpers need recordings and a voice budget.
