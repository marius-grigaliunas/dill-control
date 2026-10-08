# 0004 Authored over generated

*8 October 2026*

## Context

In the lab, where a spire stood was a number in `CompoundStats`, and what it
looked like was code in `CompoundModels.Spire`. Moving it meant changing a
number and relaunching; replacing it with something modelled meant rewriting the
code that made it. That is why the lab could not take art.

## Decision

**If a designer would want to move it, it is a node in a scene, not a number in
a generator.** Set pieces, structures, props, how enemies and weapons look, the
HUD and effects are authored: modelled in Blender or `voxel-bench`, wrapped in a
`.tscn`, placed in the editor.

Generators stay where nobody places things by hand and the result has to respond
at runtime: the planet's heightfield and craters, the horde's agents, the flow
field, destruction itself.

## How a port follows it

When a lab system both simulates and decides where things go (the compound,
Mother Brine's model, the forest), the simulation is ported and the placement
becomes `[Export]`s that take authored content.

## Cost

Content takes modelling time where the lab took a slider. That is the point: a
slice is judged on how it looks, and a slider cannot be art-directed.
