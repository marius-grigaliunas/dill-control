# 0054 The opening: unclear cloning flashes, then control in the landing pod

*27 October 2026. Decided; the pod drop, descent and placeholder flashes built in `lvl_tutorial` (0055). Replaces the pitch's arrival (a bike beside
the ship, flown down with no cut). Builds on 0051 and 0053.*

## Context

An earlier design began a contract in orbit on the bike; a trial paused that for a drop by pod.
The player knows nothing of the game on first Play, and the story, if there is
one, must not be explained (`docs/ideas/story.md`).

## Decision

- **On Play, short, deliberately unclear cloning sequences.** Shown only this
  once and never explained. The player has no memory, which mirrors a new player
  who knows nothing, so the amnesia and the tutorial are the same thing.
- **Then control, inside the landing pod.** The player can look around, but
  mainly watches the descent onto the planet through the window: stars, sun,
  atmosphere and clouds. It is a handcrafted descent, not a flight.
- **Contract 1 then teaches the mechanics** (0051).
- **The payoff is the first death in contract 1**, which already teaches clones
  (0053): the same lab flashes replay as the player respawns, which is when
  attentive players realise they are a clone and have died before. Hints only,
  never an explanation.
- **Every contract's arrival is the pod.** It starts inside the landing pod with
  the descent through the window; the door opens onto vines and cucumbers.
  Landing in a town smashes the pod through skyscrapers (contract 3). Leaving
  mirrors it: the ground falls away through the window (contract 1's extraction;
  contract 2's exit is the bike).

## Costs and open ends

- The orbit-and-bike arrival, the ship in the sky and the far shell for a clean
  silhouette from space are no longer slice work. The code stays.
- The pod interior, the window view and the cloning glimpses are art that does
  not exist (`docs/models.md`).
- The cloning glimpse is a hint, so how unclear is too unclear is for playing.
