# 0030 Proposed: the pickles are innocent, and the player is not one of them

*18 October 2026. A direction for tone, art, sound and the HUD. Not yet built.
Builds on pillar four of the pitch ("bright, comic and played straight") and
0029.*

## Context

The pitch is comic, but the joke is about the company. The theme underneath
is that the player is secretly the monster. The way to carry that is a
question of tone, not of a horror palette. Some of the prototype also mixed up
who is who: the player's health is a jar sloshing with brine, and running dry
is *PICKLED!* That makes the player look like a pickle.

## Decision

- **The look stays cartoon:** cel shading, ink, the palette, unchanged.
- **The pickles are an innocent lifeform** that wants to grow without
  violence (0029: they never strike first). The player is the exterminator
  sent to wipe them out so the planet can be drilled. Nobody in the fiction
  finds this strange; the company memo stays cheerful.
- **Faces are scared and desperate, not angry**, as the pickles flee, fight
  back and die. They are seen up close and in machete kills, so the
  expression lives on the near tier only, as a palette or texture swap in the
  vertex-animated pickle. The far tier stays a few-sided column.
- **Brine, not blood.** Brine is the pickles' alone.
- **Whimpers and wails exist.** In the fighting they are drowned out, and
  that is fine. They are heard in the quiet before the alarm, at close kills
  and as the fighting dies down. Twenty-five thousand pickles are a great many
  voices, so `AudioDirector` (0007) caps and pools them.
- **The player has a health bar and a shield**, the pitch's "regenerating
  shield over health" drawn plainly. The brine-jar health and *PICKLED!* are
  retired.

## Costs and open ends

- The pickle shader needs an expression input per near-tier agent, or a few
  baked face variants. Which is cheaper is benched.
- What the player's health and shield look like, and what replaces
  *PICKLED!* on death (cloning, 0026), is not designed yet.
- Whimpers need recordings and a voice budget. The cap is set by ear, then
  checked with the recorder for hitches.
