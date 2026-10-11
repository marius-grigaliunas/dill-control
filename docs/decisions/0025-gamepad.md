# 0025 Gamepad and joystick

*11 October 2026*

## Decision

A pad drives the same actions as the keyboard and mouse. Joypad events are
stored with device `-1`, so any pad matches and a replug does not orphan the
binding. Keyboard stays device `16` and mouse device `32`.

The layout is the usual shooter one, in Godot's Xbox-position buttons (a
PlayStation pad is the same positions; the menu prints Cross and Circle when
it can see that pad):

| Pad | Action |
|---|---|
| Left stick | Move. On the ship, climb and dive stay on the face buttons. |
| Right stick | Look. `look_left`, `look_right`, `look_up`, `look_down`. |
| Right trigger | Fire. Left trigger aims, or the ship's rocket pod. |
| A / Cross | Jump, and the ship's climb. |
| B / Circle | Crouch, and the ship's dive. |
| X / Square | Reload. |
| Y / Triangle | Interact: board, dismount, the contract terminal. |
| LB / L1 | Grapple, held. |
| RB / R1 | Grenade, held. On the ship, one press drops a bomb. |
| L3 | Sprint, and the ship's boost. |
| R3 | Jetpack on foot. The ridden mech's camera, when the player is stowed. |
| D-pad up / down | Weapons 1 and 2. On the ship, its two guns. |
| D-pad left / right | Previous and next weapon on foot. Roll, when the ship is being flown. |
| Back / Share | Swap to the previous weapon. The ship's hover/cruise toggle, when flown. |
| Start / Options | Free the cursor, and `ui_cancel` (the same job as Escape). |

Slots 3 to 8 have no button of their own. The d-pad walks them.

## Why the east button is not cancel

Godot's built-in `ui_cancel` is Escape and B. This menu opens on `ui_cancel`
when nothing else has consumed it, so leaving B there would make crouch open
the menu. `ui_cancel` in this project is Escape and Start. B goes back only
while a menu or the contract board is already open.

The same button can sit on two actions when only one of them is live. Roll
and weapon-cycle share the d-pad: the ship applies roll only while it is
flown, and the loadout is not running then. The jetpack and the mech camera
share R3 for the same reason.

## Look

The mouse is a stream of motion events. A stick is a deflection held all
frame, so the player, the mech and the ship poll it. Signs match the mouse:
stick right yaws right, stick up pitches up, and the player's Invert Y flips
the stick too. It is applied on the physics tick, before movement, so the
step moves the way the stick is already facing.

The speeds live next to mouse sensitivity. On foot, 4 rad/s at full
deflection, a curve of 1.5, and 0.45 of that while a weapon is aiming. The
mech is 2.6 rad/s, the ship 2.2. The stick's deadzone is the action's 0.2,
applied by `GetVector` as a circle. The triggers use 0.35, so a trigger that
is only resting does not fire; a mouse click is still a full press.

A rebind replaces bindings of the same kind. Putting crouch on a pad button
leaves Ctrl and C where they are.
