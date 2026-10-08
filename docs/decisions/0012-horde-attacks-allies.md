# 0012 The horde attacks the allies

*8 October 2026*

## Context

Allies that cannot be hurt by pickles are turrets with legs, and guarding means
nothing if nothing is guarded against. The horde was written to hunt one
target, the player: every behaviour measured distance to the player, and every
hit landed on the player.

## Decision

**Allies are targets through an interface and a group.** Anything that joins
`IHordeTarget.GroupName` (`scripts/core/IHordeTarget.cs`) is attacked: an
upright body (base, radius, height) and a `TakeHordeHit`. The director reads
the group once a frame, up to eight, and hands the manager a snapshot before
its tick. The horde never names a mech; a mech never names the horde. A group,
not a bus signal, because it is a list the horde reads, not an event.

- **Steering.** A pickle within `AllyAggroRange` of an ally, and nearer it than
  the player, leaves the flow and goes straight for the ally. The rest of the
  crowd keeps coming for the player.
- **Strikes and dashes** go at whichever is nearest. A strike on an ally is
  raised as the same event with the ally's place in the snapshot in its
  magnitude, and drained through its own budget (`AllyStrikesPerSecond`), so a
  crowd on a mech cannot spend the player's.
- **Spits and throws** aim at the nearest thing in reach. Brine fuses against
  the allies as well as the player.
- **A bite is a hit like any other.** It goes into the voxels along the line
  from the pickle's mouth to the body, scaled by the mech's `HordeDamageScale`
  (armour). A crowd round the legs chews the legs; a leg chewed through falls
  off (0011).
- **Riding, the player is still hunted.** The mech carries the stowed player at
  its seat, so the crowd comes for the mech; a stowed player takes no damage.

The mechs answer: their feet stomp the pickles they land on (a horde-only blast
on the bus), and with a threat within `GuardRadius` of the player a mech takes
a stand between the two, never past `LeashRange`.

## Measured

`bench.gd ... siege` (25,000, crowd brought onto the player and three allies):
12-14 ms mean, GPU 8-11 ms; about a thousand strikes and two hundred spits on
the allies in forty seconds. The storage bot went to 48 %; the field fighter
lost a shin and fell inside twenty seconds at the default armour, so its
`HordeDamageScale` is 0.2.
