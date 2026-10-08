# 0014 Buildings fall whole and break where they land

*8 October 2026. Replaces "What falls fractures" in 0013; the load check,
heavy rubble, the cap and armour stand.*

## Context

0013 fractured anything big that came loose into storey-sized blocks the
moment it came loose. A tower cut through at its base fell as thirty-six
blocks drifting apart: "it shouldn't just crumble all of it until the top, it
should stay intact but collapse down under its own weight." And a tower cut
on one side only should not fall straight off: "the other side should bend
and break" under the weight on it.

## Decision

**What comes loose of a building falls as one body.** No fracture up front,
for an island cut free or a load failure alike, and no random spin. A failed
layer takes its storey with it (`FailureDepth`): the columns buckle over a
storey, and what is above drops through the gap.

**It crushes through.** A heavy piece (over `FractureVolume`) landing end-on at
over `CrushThroughSpeed` does not stop: a slab `CrushDepth` deep across the
contact (`CrushWidth` past the contacts' spread) is crushed to dust out of it
and out of the building under it, their collision is re-boxed at once, and it
carries on with `CrushKeep` of its speed. A tower dropped one storey comes
down storey by storey, whole above the crush, until it is shorter than
`CrushStopHeight`; then it lands like any piece and crumbles. A piece lying on
its side (shorter along the contact than half its length) does not crush
through: landing hard, it snaps across its length into `SnapLength` segments.

**A one-sided failure hangs on a hinge.** When a layer fails more than
`HingeOffCentre` off-centre and something of it is left, the piece above gets
a `HingeJoint3D` to the standing building at the support's centre, about the
horizontal axis away from the side that went. Gravity tips it over; dust puffs
at the hinge grow as it goes. It tears free at `HingeBreakAngle` or after
`HingeHoldSeconds` (propped on its own rubble it would hang there), then
topples off the stump and snaps where it lands. Velocity at the start is the
same rigid rotation about the pivot as before, scaled by how far off-centre.

Why a joint and not contact alone: with the support kept, a cut that leaves
the centre of mass over what remains is statically stable, and nothing would
lean. The failure is the columns yielding, which a hinge that tears models
and a rigid contact does not.

## Measured

`cut` and `cutside`, new bench modes that cut `Tower11` with a 1 m slit 2 m up
(clean through, and 60 % of its width from -X), on a machine in use, so these
are behaviour, not budget:

- `cut`: one body, tilt 0.0° throughout, 10-11 m/s, 4 m slabs; on the ground
  in about 7 s; the last 16 m crumbles to 80-odd pieces.
- `cutside`: hinged, 7° at 0.5 s, tears at 10° near 1 s, rolls off the stump
  edge (15° to 90° over 6 s), snaps into 19 pieces on landing.
- Quiet phases during either: 1-4 frames of about 45 over 16.7 ms, worst
  30-55 ms; the quiet phase before the cut, 4 of 200, worst 35 ms.
- `collapse` (rockets and a strike), paired with `03e1494` in turn: both blow
  the budget while it comes down; the strike's scatter makes single runs vary
  from 16 to 42 ms mean in the landing phase on the same code.

To do: `cut`, `cutside`, `collapse`, `wreck` paired against `03e1494` on a
quiet machine.

## Costs

- The crushed slabs go to dust, not rubble: a pancaked tower leaves only
  the rubble of its last storeys. A pile would cost the cap.
- A heavy piece's collision is coarse (up to 4 m boxes at `MaxChunkBoxes`), so
  a crush may move its collision less than its mesh until the next one.
- Heavy pieces report contacts (`ContactMonitor`, eight).
