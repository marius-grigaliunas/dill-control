# Flight rig

A six-degrees-of-freedom floating flight controller with no dependency on the
game around it. Two files, one namespace (`Vehicles`), no scenes, no
autoloads, no project settings beyond the input actions you already have.

## Dropping it into another project

1. Copy this folder anywhere under the project's script root.
2. Give a `CharacterBody3D` the `FlightRig.cs` script, a collision shape, and
   a `Camera3D` wired into the `Camera` export.
3. Assign a `FlightRigStats` resource to `Stats` -- either
   `flightrig_stats_default.tres` in this folder, or a new one made in the
   inspector (`[GlobalClass]`, so it appears in the resource picker).
4. Either add the input actions `FlightRig` expects by default
   (`move_forward`, `move_back`, `move_left`, `move_right`, `jump`, `crouch`,
   `sprint`, `ship_roll_left`, `ship_roll_right`, `ship_mode_toggle`, and for
   a stick `look_left`, `look_right`, `look_up`, `look_down`) or point its
   `[Export]` action-name fields at whatever your project already has.
5. Call `SetActive(true)` when something should be flying it, `false` when
   not. Nothing else runs `_PhysicsProcess`'s thrust or `_UnhandledInput`'s
   look and mode-toggle handling while inactive, and gravity still applies
   either way so a parked rig falls and stays where it lands.

That is the whole integration. `Mode` (`Hover`/`Cruise`), `HoverHolding`, and
the inherited `Velocity` are the rig's entire public surface for a game to
read; everything else -- boarding, a HUD, a weapon, a prompt -- is a separate
script that holds an `[Export]` reference to this node, the same way every
other cross-node reference in a Godot scene works.

## The boundary

`FlightRig` never names a player, a level, a weapon or a boarding prompt. The
moment this class knows what a hoverbike's cannon is, it stops being droppable
and starts being one game's ship controller. `DillControl.Ships.HoverBikeSystems`
is that game-specific layer in this project -- it is the file to read for how
a host wires boarding, a cannon, and a flight HUD against this rig without
either side knowing about the other beyond an `[Export]` NodePath.

## Hover Hold

Hover mode holds a target height above whatever is directly below, rather
than leaving vertical to raw thrust. This is deliberately *not* the kind of
flight assist a "no auto-levelling" rule would rule out -- it never touches
orientation, only one axis of position, and only in Hover mode, and only
while a ground ray actually finds something within `HoverHoldMaxRange`. Losing
the ground (climbing above range, or drifting into open space) falls back to
plain `HoverVerticalThrust` control with no special-casing needed on the
caller's side.

## Gravity Compensation

`GravityCompensation` is the share of local gravity the drive cancels while
the rig is active. At the default of 1.0 the vehicle goes where its nose
points and level flight costs nothing.

It is the same category of assist as the hover hold and rules out the same
objection: it never touches the hull's basis, it is one term in the
translational integration, and landing is still aiming the belly at the ground
and killing your speed. It applies *only while active*, so a parked rig, or
one whose pilot got off, still falls.

Set it to 0 for a rig that has to fight its own weight. Be aware what that
costs: against a well of strength `g`, holding a heading then requires either
a permanently held climb key or a permanent nose-up trim of
`asin(g / MainThrust)`, and releasing everything sinks at `g / LinearDamping`.
At `g = 9.8` and the old 22 m/s² cruise thrust that was 26° of trim and an
11.5 m/s sink, which reads as a broken flight model rather than a demanding
one.

## Parking

Disarming does not zero `Velocity` -- a rig disarmed in mid-air should arc, not
stop dead in the sky -- but an inactive rig sheds speed at `ParkedDamping`
rather than `LinearDamping`, and stops outright once it is slower than
`ParkedRestSpeed` and touching something.

Both exist because floating motion mode has no floor and therefore no friction.
Without them a rig left at cruise speed coasts the same `speed / LinearDamping`
metres a throttled-off flight does, then skates along the ground on whatever
tangential velocity the slide response left it -- which is a vehicle drifting
out of reach of whoever just parked it.

## Tuning notes

`thrust / LinearDamping` is the speed a single-axis burn actually settles at,
and it is not the speed cap -- the cap only binds if the equilibrium exceeds
it. If a mode's cruise speed is higher than its own `thrust / LinearDamping`,
that number is decorative and no pilot will ever see it. Keep the two in step.

The vertical thrust values must exceed `gravity * (1 - GravityCompensation)`
in both modes, or holding climb quietly loses to gravity and reads as broken
rather than weak. At the default compensation that threshold is zero. This
module does not know what gravity it will run under and cannot check this for
you -- tune against your own project's gravity, not against this file's
defaults, which were tuned against a 9.8 m/s² well.
