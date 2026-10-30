# 0028 Melee is a stroke, not a clip

*13 October 2026. The same reason as the walking rules in 0046.*

## Context

A first-person swing has to fit more than one weapon, and it has to stop on
whatever it actually hit. A clip cannot do either. The contact distance
changes with the target, and a hit and a miss are different motions: one stops
and bounces, the other follows through. Text-to-motion and video-to-motion
tools export a full body over several seconds, which is the wrong space for a
swing faked in the camera, and they need a rig before any of it can be tuned.

## Decision

**A stroke is data.** `MeleeStroke` holds four poses in viewmodel space
(cocked, contact, follow-through, recover-from), the time of each segment and
its easing. `Heft` on `MeleeStats` scales the times, the head's lag and the
bounce. The head and the two grips are markers in the weapon's wrapper, so
one slam lands with a short blade and with a long hammer.

**The hit is a ray, then a fatter sweep** if the ray misses, which is how
Half-Life 2 decides a crowbar. `ResolveAt` says when the hurt lands, from the
click to the contact. The drop itself is aimed from the start of the strike:
the contact pose slides, up to `ContactSlide`, until the head marker meets
the hit.

**A hit and a miss are different motions.** A hit holds the viewmodel still
for a few tens of milliseconds of real time, then bounces on the viewmodel
spring and punches the camera down through the existing `Fired` signal. A new
`Impacted` signal, re-emitted by the loadout, shakes the camera. A miss
follows through and takes longer to come back. The head lags the grip on the
way up. During the drop the spring is tightened, so the head arrives with the
grip and finishes settling while the viewmodel is held.

**What it hits is a blast the game already has.** A hitbox takes the swing's
damage plus a smaller splash. The world or a voxel body becomes
`Blast.ApplyDamage` with `NoCrater`, and the crowd hears it the same way a
mech's slash does.

**The machete's horizontal swing stays on fire.** Alt-fire plays the shared
overhead slam. The sledgehammer is slot 9, a graybox, and it stays a test
weapon until the slice arsenal is decided. The hands are two rigid forearms
from shoulders just off the bottom of the view to the grip markers. No
skeleton and no clip, as with the mechs.

## What was measured

`lvl_horde_flats` `slam`, 1920x1080, vsync off, once the machine had cooled.
The ground slam was mean 13.49 ms, worst 14.55, 0 of 48 frames over 16.7, GPU
mean 11.30. The pillar slam was mean 10.81, worst 14.35, none over, GPU mean
8.75. The miss was mean 8.92, worst 9.09, 0 of 74 over, GPU mean 6.79. The
frames show the head in a hole carved in the pillar and in a block, a decal
on the ground, eleven pickled out of the ring, and a swing at the sky with
no blast. An earlier run on a hot machine read 30-50 ms while the horde's
flow worker was still up; that is the machine, not the stroke. A stroke is
one ray and one sweep. The screenshot phases are not in these numbers.

`slam quiet` recorded with `--write-movie --fixed-fps 10` to `perflogs/slam`:
200 frames, GPU mean 14.09 ms a frame while the frames were being written.

## Cost

The horizontal swing is still its own code until this resource has been the
one in use. The contact slide is clamped, so a very steep look down at the
floor leaves the head short of the decal rather than dragging the weapon off
the screen. The sledge is boxes, and it is not part of the slice arsenal.
