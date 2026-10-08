# 0001 A new repository for the game

*8 October 2026*

## Context

The prototype (`fps-swampy`, now the lab) found the game: swinging through a
compound while it comes apart, 25,000 pickles on Iris Xe at 1080p. It got there
by generating and measuring. It has 52,000 lines of C# and no authored content:
the planet, the forest, the compound, Mother Brine and every structure are
assembled at runtime by `SpikeDirector` from sliders in `SpikeTuning`. There is
no model, texture or recorded sound in it, and its `CLAUDE.md` is a 2,000-line
history of experiments, some reversed and some about features that were cut.

## Decision

A new repository, Dill Control, for the game and its vertical slice. The lab
stays, unchanged in purpose: a no-rules sandbox where ideas get tried, and the
baseline a misbehaving system here is A/B'd against.

Engines are ported one at a time (horde, voxel bodies, planet, grapple, flight
rig, instruments). Content generators are not; that content is authored in
Blender, in `voxel-bench` and in the editor.

## Why not clean up the lab

Deleting down to a slice means unpicking every implicit dependency on
`SpikeDirector`. Starting empty and pulling systems in means each arrives with
only what it needs. And the two have opposite goals, "everything at max, no
rules" against "one mission, authored, shippable", which would otherwise share
one `CLAUDE.md`, one `project.godot` and one set of benches.

## Cost

Every system is ported rather than inherited, so the slice starts slower. A fix
to a shared system is made twice if both repositories need it; the lab is
allowed to fall behind.
