using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Everything tunable about a <see cref="FlameWeapon"/>.
///
/// Deliberately not a <see cref="WeaponStats"/>. A continuous cone has no
/// magazine, no rate of fire, no per-shot spread and no recoil per shot, and
/// bolting "damage per second" onto a resource built around "damage per shot"
/// would leave two thirds of it meaningless on this weapon and a reader unable
/// to tell which third applied.
/// </summary>
[GlobalClass]
public partial class FlamethrowerStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Marsh Torch";

    // ---------------- Damage ----------------

    /// <summary>
    /// Damage per second to everything in the cone at once. It is a rate
    /// rather than a per-hit number because the trigger is held rather than
    /// pulled -- what a player controls here is how long they keep something
    /// inside the cone.
    /// </summary>
    [ExportGroup("Damage")]
    [Export(PropertyHint.Range, "1,1000,1")]
    public float DamagePerSecond { get; set; } = 150.0f;

    [Export(PropertyHint.Range, "2,40,0.5,suffix:m")]
    public float Range { get; set; } = 14.0f;

    /// <summary>Half-angle of the cone.</summary>
    [Export(PropertyHint.Range, "3,60,0.5,suffix:°")]
    public float ConeDegrees { get; set; } = 26.0f;

    /// <summary>
    /// How often damage is resolved. Damage per tick is
    /// <see cref="DamagePerSecond"/> times the elapsed slice, so the rate a
    /// target actually takes is independent of this -- it only trades
    /// smoothness against how often the shape query runs.
    /// </summary>
    [Export(PropertyHint.Range, "0.02,0.5,0.01,suffix:s")]
    public float TickInterval { get; set; } = 0.1f;

    /// <summary>Beyond this the flame starts thinning toward <see cref="FalloffMinMultiplier"/>.</summary>
    [Export(PropertyHint.Range, "0.5,40,0.5,suffix:m")]
    public float FalloffStart { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float FalloffMinMultiplier { get; set; } = 0.4f;

    /// <summary>
    /// Distinct colliders considered per tick. Bounded so a trigger held inside
    /// a crowd cannot cost an unbounded number of line-of-sight rays per tick.
    /// Every agent wears two hitboxes, so this is half as many agents.
    /// </summary>
    [Export(PropertyHint.Range, "4,96,1")]
    public int MaxTargetsPerTick { get; set; } = 48;

    /// <summary>
    /// Spheres of sustained blast strung down the cone each tick, so the flame
    /// reaches every agent inside it rather than only the ones wearing a
    /// borrowed collider -- the cone's own query can find the horde's nearest
    /// few dozen and no more, which in a crowd of thousands is a hose that
    /// passes through most of what it is pointed at. Sustained, so it digs,
    /// throws and shakes nothing, the way a fire zone's tick does. Zero, the
    /// default, is the plain cone. Where neighbouring spheres overlap an agent
    /// takes both; the flame is a little hotter in its middle, which reads as
    /// fire.
    /// </summary>
    [Export(PropertyHint.Range, "0,6,1")]
    public int HordeWashSpheres { get; set; } = 0;

    // ---------------- Fuel ----------------

    /// <summary>Seconds of continuous fire from full.</summary>
    [ExportGroup("Fuel")]
    [Export(PropertyHint.Range, "1,60,0.5,suffix:s")]
    public float FuelSeconds { get; set; } = 6.0f;

    /// <summary>Seconds of fuel restored per second of not firing.</summary>
    [Export(PropertyHint.Range, "0,20,0.1")]
    public float FuelRefillPerSecond { get; set; } = 1.2f;

    /// <summary>
    /// Quiet time before refilling starts, so tapping the trigger is not the
    /// same as holding it and a long burn actually costs something.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:s")]
    public float RefillDelay { get; set; } = 1.2f;

    // ---------------- Camera kick ----------------

    /// <summary>
    /// Per tick rather than per shot, and small: a flamethrower should push the
    /// view rather than punch it.
    /// </summary>
    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,2,0.005,suffix:°")]
    public float KickPitchPerTick { get; set; } = 0.06f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 30.0f;

    [ExportGroup("Viewmodel")]
    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ViewModelRecoverSpeed { get; set; } = 14.0f;

    /// <summary>Multiplier on the flame cone's length while firing.</summary>
    [Export(PropertyHint.Range, "0.1,2,0.01")]
    public float FlameVisualScale { get; set; } = 1.0f;

    /// <summary>Damage falloff with distance down the cone.</summary>
    public float FalloffMultiplierAt(float distance)
    {
        if (distance <= FalloffStart)
        {
            return 1.0f;
        }

        if (distance >= Range || Range <= FalloffStart)
        {
            return FalloffMinMultiplier;
        }

        float t = (distance - FalloffStart) / (Range - FalloffStart);
        return Mathf.Lerp(1.0f, FalloffMinMultiplier, t);
    }
}
