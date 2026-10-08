using Godot;
using DillControl.Weapons;

namespace DillControl.Vehicles;

/// <summary>
/// The Swampy-specific half of the hoverbike: everything a
/// <see cref="Vehicles.FlightRig"/> itself has no business knowing about --
/// boarding and the arsenal. Flight tuning (thrust, speed, hover hold) lives
/// on that rig's own <see cref="Vehicles.FlightRigStats"/> instead.
///
/// None of the three weapons is a WeaponBase, and that stays true as the bike
/// grows them: WeaponBase assumes a hand and a ViewModel, and a hull-mounted
/// pod has neither. They still route through the same
/// Hitbox / IDamageable / DamageInfo and Blast paths every hand weapon uses,
/// so nothing downstream has a branch for who fired.
/// </summary>
[GlobalClass]
public partial class HoverBikeStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "HoverBike";

    /// <summary>
    /// How close to the bike you have to be for the board prompt to take.
    /// The boarding area's own shape decides where you can stand; this is the
    /// distance from it that counts as reaching it.
    /// </summary>
    [ExportGroup("Boarding")]
    [Export(PropertyHint.Range, "0.5,20,0.1,suffix:m")]
    public float BoardRange { get; set; } = 4.5f;

    [ExportGroup("Cannon")]
    /// <summary>
    /// The beam is a rate, not a magazine: what a pilot controls is how long
    /// something stays under the line, exactly as the Marsh Torch's cone is.
    /// Comfortably lethal per second, and deliberately so -- VoxelBuilding's
    /// carve radius scales with the damage a hit deals, so this is also how
    /// fast the bike eats through a wall it is parked in front of.
    ///
    /// Raised from 1100 after playtesting reported it as underpowered. Still a
    /// rate rather than a per-shot number, so the tuning knob stays the same
    /// one it always was.
    /// </summary>
    [Export(PropertyHint.Range, "10,5000,10,suffix:dps")]
    public float CannonDamagePerSecond { get; set; } = 3000.0f;

    [Export(PropertyHint.Range, "10,2000,1,suffix:m")]
    public float CannonRange { get; set; } = 600.0f;

    /// <summary>
    /// How often the beam resolves a hit. Damage per tick is
    /// <see cref="CannonDamagePerSecond"/> times the elapsed slice, so what a
    /// target takes does not change with frame rate or with this number --
    /// it only trades how smoothly damage arrives against how often the ray
    /// is cast. Same arrangement, and the same reasoning, as
    /// FlamethrowerStats.TickInterval.
    /// </summary>
    [Export(PropertyHint.Range, "0.02,0.5,0.01,suffix:s")]
    public float CannonTickInterval { get; set; } = 0.05f;

    /// <summary>
    /// The beam burns everything it passes through, not only the first thing
    /// it meets: every horde agent within this distance of the line, from the
    /// muzzle to the first wall, takes the full rate. Asked for as "the bike's
    /// weapons seem lacking at this scale" -- a laser that stopped at the
    /// first of twenty-five thousand pickles was killing twenty a second into
    /// a crowd it could see thousands of. Walls, buildings, Mother Brine's
    /// shell and the compound still stop it and still take the rate as
    /// before. 0 is the old single-ray beam.
    /// </summary>
    [Export(PropertyHint.Range, "0,6,0.1,suffix:m")]
    public float LaserPierceRadius { get; set; } = 5.0f;

    /// <summary>How far down the beam the pierce reaches the horde. The cost
    /// is a walk of the horde's spatial hash a cell at a time along the
    /// line, so this bounds it.</summary>
    [Export(PropertyHint.Range, "10,600,5,suffix:m")]
    public float LaserPierceRange { get; set; } = 600.0f;

    /// <summary>Width of the beam's white-hot core where it lands, in metres.
    /// Purely what it looks like: the beam tests a single ray down its centre,
    /// and burns the horde within <see cref="LaserPierceRadius"/> of it. It
    /// tapers toward the muzzle so the rider is not looking down a tube of
    /// light -- the flamethrower's cone already made that mistake once.</summary>
    [Export(PropertyHint.Range, "0.01,3,0.01,suffix:m")]
    public float CannonBeamWidth { get; set; } = 1.2f;

    /// <summary>The glow sheath round the core, as a multiple of its width.
    /// Additive, so it reads as heat without hiding what the beam is on.</summary>
    [Export(PropertyHint.Range, "1,10,0.1")]
    public float LaserGlowScale { get; set; } = 3.0f;

    /// <summary>
    /// Where the beam lands it vaporizes: the full explosion effect -- the
    /// toon fireball, the comic star, the puffs, sparks and a scorch with
    /// glowing embers -- at most once per this interval. Asked for after the
    /// minigun's rounds read as "juicy" and the laser as "a burden": what made
    /// the difference was never damage, it was that every round the gun fires
    /// goes off as a blast you can see and the beam went off as nothing.
    /// Purely drawn: the beam's damage, melt and fire are untouched by it.
    /// ExplosionEffect's own live budgets bound the cost of a held beam.
    /// </summary>
    [Export(PropertyHint.Range, "0.03,1,0.01,suffix:s")]
    public float LaserEffectInterval { get; set; } = 0.1f;

    /// <summary>Radius the vaporize burst is drawn at.</summary>
    [Export(PropertyHint.Range, "0.5,20,0.1,suffix:m")]
    public float LaserEffectRadius { get; set; } = 6.0f;

    /// <summary>Radius of the column of vapor boiling up where the beam lands
    /// (ToonFlame.Vapor). Emitted in world space, so a sweep leaves a wall of
    /// it behind.</summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:m")]
    public float LaserVaporRadius { get; set; } = 1.8f;

    /// <summary>
    /// Camera trauma held for as long as the trigger is, so the seat rumbles
    /// under the beam the way it shakes under a hit -- squared when read, as
    /// every trauma here is, so 0.45 is a tremor and not a blur. The beam has
    /// to feel like it costs the bike something to hold.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float LaserShakeTrauma { get; set; } = 0.45f;

    /// <summary>Push back along the beam while firing, in m/s every second.
    /// Small enough that hover hold and a little throttle cancel it; there so
    /// letting go of the trigger is felt as well as seen.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m/s²")]
    public float LaserRecoil { get; set; } = 3.0f;

    /// <summary>How long the char the beam leaves stays on the ground. Long,
    /// so a sweep leaves a burned trail behind it.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")]
    public float LaserScorchSeconds { get; set; } = 14.0f;

    /// <summary>How long the burned path the beam drags across the ground
    /// lasts (ScorchTrail), dissolving from its edges over all of it. 0 for
    /// none.</summary>
    [Export(PropertyHint.Range, "0,120,1,suffix:s")]
    public float LaserTrailSeconds { get; set; } = 25.0f;

    /// <summary>Width of the burned path.</summary>
    [Export(PropertyHint.Range, "0.2,8,0.1,suffix:m")]
    public float LaserTrailWidth { get; set; } = 1.8f;

    /// <summary>How long a fresh stretch of the path glows.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:s")]
    public float LaserTrailEmberSeconds { get; set; } = 2.5f;

    /// <summary>Most marks in the path at once: one draw however many. Past
    /// it the oldest stretch is reused.</summary>
    [Export(PropertyHint.Range, "16,4000,1")]
    public int LaserTrailMarks { get; set; } = 800;

    /// <summary>
    /// What the beam removes from a destructible body each tick: a sphere of
    /// this radius, bored half a radius into the surface, regardless of the
    /// damage number. Asked for as "it is supposed to melt anything in the
    /// way" -- a concentrated orbital beam. At twenty ticks a second, 2.6 m
    /// cuts through a wall about as fast as the rider can sweep across it.
    /// Armour does not slow it (VoxelBuilding.ArmourMeltShare is 1).
    /// 0 falls back to the ordinary damage-scaled carve.
    /// </summary>
    [Export(PropertyHint.Range, "0,6,0.05,suffix:m")]
    public float LaserCarveRadius { get; set; } = 2.6f;

    /// <summary>Reach of the light cast where the beam lands. One omni light
    /// with no shadow, alive only while the trigger is held.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")]
    public float LaserLightRange { get; set; } = 24.0f;

    /// <summary>
    /// Reuses RocketStats and the existing Rocket, which already integrates
    /// itself and sweeps one ray per physics frame -- exactly what something
    /// leaving a vehicle at cruise speed needs, and the reason a rocket is not
    /// a RigidBody3D anywhere in this project.
    /// </summary>
    [ExportGroup("Rocket Pod")]
    [Export]
    public RocketStats PodOrdnance { get; set; }

    [Export(PropertyHint.Range, "0.05,5,0.01,suffix:s")]
    public float PodFireInterval { get; set; } = 0.35f;

    /// <summary>Rockets per boarding. 0 for unlimited.</summary>
    [Export(PropertyHint.Range, "0,200,1")]
    public int PodReserve { get; set; } = 80;

    /// <summary>
    /// Reuses GrenadeStats and the existing Grenade, which is a RigidBody3D
    /// that already takes a launch velocity -- so a released bomb inherits the
    /// bike's own motion for free and falls along the arc you were flying.
    /// </summary>
    [ExportGroup("Bomb Bay")]
    [Export]
    public GrenadeStats BombOrdnance { get; set; }

    [Export(PropertyHint.Range, "0.1,5,0.05,suffix:s")]
    public float BombInterval { get; set; } = 0.35f;

    /// <summary>Bombs per boarding. 0 for unlimited.</summary>
    [Export(PropertyHint.Range, "0,200,1")]
    public int BombReserve { get; set; } = 48;

    /// <summary>Extra downward speed on release, so a bomb clears the hull
    /// rather than scraping along underneath it.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:m/s")]
    public float BombReleaseSpeed { get; set; } = 4.0f;

    /// <summary>
    /// A fourth, added weapon rather than a change to the cannon. The cannon
    /// stays a single continuous beam -- see HoverBikeSystems.TickCannon's own
    /// decision comment for why that is deliberate and not a placeholder -- and
    /// this is a genuinely different weapon: rapid hitscan shots with spread
    /// and a magazine that runs dry and refills after a quiet spell, the same
    /// shape every hand-held HitscanWeapon already has. It reuses that same
    /// Hitbox / IDamageable / DamageInfo path, so it hurts the horde and lights
    /// the hitmarker with no branch anywhere for who fired it, exactly like the
    /// beam and the pod already do.
    /// </summary>
    [ExportGroup("Minigun")]
    [Export(PropertyHint.Range, "1,60,0.5,suffix:dmg")]
    public float MinigunDamagePerShot { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "0.02,0.5,0.01,suffix:s")]
    public float MinigunFireInterval { get; set; } = 0.07f;

    [Export(PropertyHint.Range, "10,1000,1,suffix:m")]
    public float MinigunRange { get; set; } = 220.0f;

    /// <summary>Half-angle of the spread cone. 0 is a laser; the pellets-style
    /// per-shot roll WeaponStats already documents is the same idea here.</summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:°")]
    public float MinigunSpreadDegrees { get; set; } = 1.6f;

    /// <summary>Rounds per boarding. 0 for unlimited.</summary>
    [Export(PropertyHint.Range, "0,3000,10")]
    public int MinigunReserve { get; set; } = 480;

    /// <summary>Seconds without firing before the reserve starts refilling --
    /// the same "quiet spell" rule FlameWeapon's fuel uses, so tapping the
    /// trigger does not behave like holding it.</summary>
    [Export(PropertyHint.Range, "0.2,5,0.1,suffix:s")]
    public float MinigunRefillDelay { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "1,600,1,suffix:rounds/s")]
    public float MinigunRefillPerSecond { get; set; } = 90.0f;

    /// <summary>
    /// Every round goes off where it lands. The blast is the rocket pod's own
    /// warhead scaled down rather than a second set of numbers, so retuning
    /// the pod retunes the rounds with it: damage is
    /// <see cref="PodOrdnance"/>.Damage times this fraction. At 0.15 a round
    /// is 39 against 30 agent health, so anything inside the inner radius
    /// dies to one round and a pack thins under a sweep -- raised from a
    /// tenth, which needed two rounds on the same pickle and against twenty-
    /// five thousand read as the bike "lacking at this scale".
    /// </summary>
    [ExportGroup("Minigun Explosive Rounds")]
    [Export]
    public bool MinigunExplosive { get; set; } = true;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinigunBlastDamageFraction { get; set; } = 0.15f;

    /// <summary>Kept well under the pod's 18 m on purpose: fourteen rounds a
    /// second at that radius would be an airstrike on a trigger. 4.5 m with a
    /// 2 m killing core is a crowd-sized hole per round.</summary>
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")]
    public float MinigunBlastRadius { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "0,10,0.1,suffix:m")]
    public float MinigunBlastInnerRadius { get; set; } = 2.0f;

    [Export(PropertyHint.Range, "0,20,0.1")]
    public float MinigunBlastImpulse { get; set; } = 3.0f;

    /// <summary>
    /// Shortest gap between two drawn explosions. Each is a light, three
    /// emitters and a decal; at the minigun's fire rate one per round would
    /// keep a dozen lights alive for a picture no different from three.
    /// Rounds inside the gap still deal their full blast and fall back to the
    /// ordinary impact flash. Same argument as ImpactEffect.MaxLive.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float MinigunExplosionEffectInterval { get; set; } = 0.15f;

    /// <summary>How long a round's scorch stays. The effect node lives as long
    /// as its scorch, so the pod's eighteen seconds here would keep a hundred
    /// of them alive through one long burst.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:s")]
    public float MinigunScorchSeconds { get; set; } = 3.0f;

    /// <summary>
    /// Radius the drawn explosion is sized to -- its dust collar, flash and
    /// ring -- independent of the 3 m damage radius. A round is small; the
    /// dirt it throws up is what makes a burst read from the saddle.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,15,0.1,suffix:m")]
    public float MinigunEffectRadius { get; set; } = 5.0f;

    // ---------------- Ground ----------------

    /// <summary>
    /// The gun gouges the ground. One crater per drawn explosion rather than
    /// per round -- fourteen terrain rebuilds a second is not affordable, and
    /// the round between two drawn ones is not visible anyway -- dug as deep
    /// as all the rounds since the last one added together, so sustained fire
    /// on one spot digs a pit and a sweep digs a trench. The radius has to
    /// clear the terrain's ~5 m vertex spacing or a dig moves one vertex and
    /// reads as nothing.
    /// </summary>
    [ExportGroup("Ground")]
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float MinigunCraterRadius { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0,2,0.01,suffix:m")]
    public float MinigunCraterDepthPerRound { get; set; } = 0.12f;

    /// <summary>Deepest a single gun dig goes, however long the gap was.</summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:m")]
    public float MinigunCraterMaxDepth { get; set; } = 1.0f;

    /// <summary>
    /// The laser melts the ground it holds on: a shallow pit deepening at this
    /// rate for as long as the beam stays within <see cref="LaserMeltRadius"/>
    /// of where it started. Molten, so it throws no rubble.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.05,suffix:m/s")]
    public float LaserMeltDepthPerSecond { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float LaserMeltRadius { get; set; } = 10.0f;

    /// <summary>How often the melt is written to the terrain. Each write is a
    /// chunk rebuild, so this is the cost dial.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.01,suffix:s")]
    public float LaserMeltInterval { get; set; } = 0.25f;

    /// <summary>
    /// Seconds of fire left where the beam touches the ground, at most once
    /// per <see cref="LaserIgniteInterval"/>. Burns whatever walks through it
    /// through the same fire zone an incendiary round leaves.
    /// </summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")]
    public float LaserFireSeconds { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:s")]
    public float LaserIgniteInterval { get; set; } = 0.5f;
}
