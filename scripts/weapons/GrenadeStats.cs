using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Everything tunable about a thrown explosive: how it is carried, how it
/// flies, how long it waits, and what the blast does.
///
/// Like <see cref="WeaponStats"/>, the [ExportGroup]s are not cosmetic -- the
/// F4 tuning panel turns them into its section headings, and area damage is
/// exactly the kind of thing that has to be retunable without a rebuild.
/// </summary>
[GlobalClass]
public partial class GrenadeStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Frag Grenade";

    [ExportGroup("Carry")]
    [Export(PropertyHint.Range, "0,20,1")]
    public int CarriedCount { get; set; } = 4;

    /// <summary>Dead time after a throw before another can start.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.05,suffix:s")]
    public float ThrowCooldown { get; set; } = 0.75f;

    /// <summary>How long the weapon stays down after the arm comes through.</summary>
    [Export(PropertyHint.Range, "0.05,1.5,0.01,suffix:s")]
    public float ThrowRecovery { get; set; } = 0.35f;

    [ExportGroup("Throw")]
    [Export(PropertyHint.Range, "1,40,0.1,suffix:m/s")]
    public float ThrowSpeed { get; set; } = 18.0f;

    /// <summary>
    /// Upward share added to the aim direction. Without it a level throw is a
    /// flat line and the arc is unreadable.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ThrowUpBias { get; set; } = 0.22f;

    /// <summary>Spin imparted on release. Purely visual, but a grenade that
    /// does not tumble reads as a thrown box.</summary>
    [Export(PropertyHint.Range, "0,30,0.1,suffix:rad/s")]
    public float ThrowSpin { get; set; } = 9.0f;

    [ExportGroup("Fuse")]
    [Export(PropertyHint.Range, "0.3,10,0.05,suffix:s")]
    public float FuseTime { get; set; } = 2.6f;

    /// <summary>
    /// Whether cooking past the fuse detonates in hand. True on purpose: a cook
    /// with no penalty is not a decision, and the whole reason to hold the
    /// button is that holding it too long is a mistake.
    /// </summary>
    [Export]
    public bool AllowCookOff { get; set; } = true;

    [ExportGroup("Flight")]
    /// <summary>
    /// Matched to MovementTuning.GravityScale, not left at 1. Jolt gives a
    /// RigidBody3D unscaled gravity, so a default-scale grenade floats next to
    /// a player who falls nearly twice as fast, and the arc reads as belonging
    /// to a different world.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,4,0.05")]
    public float GravityScale { get; set; } = 1.75f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Bounce { get; set; } = 0.34f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Friction { get; set; } = 0.62f;

    [Export(PropertyHint.Range, "0,10,0.05")]
    public float AngularDamp { get; set; } = 1.2f;

    /// <summary>Safety net: a grenade that somehow never detonates still goes
    /// away rather than accumulating in the level.</summary>
    [Export(PropertyHint.Range, "1,60,0.5,suffix:s")]
    public float MaxLifetime { get; set; } = 20.0f;

    [ExportGroup("Blast")]
    [Export(PropertyHint.Range, "1,500,0.5")]
    public float Damage { get; set; } = 210.0f;

    /// <summary>Inside this radius the blast does full damage.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1,suffix:m")]
    public float InnerRadius { get; set; } = 3.0f;

    /// <summary>Outside this radius the blast does nothing at all.</summary>
    [Export(PropertyHint.Range, "0.5,40,0.1,suffix:m")]
    public float BlastRadius { get; set; } = 10.0f;

    /// <summary>Damage share left at the very edge of the blast.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinMultiplier { get; set; } = 0.30f;

    /// <summary>Shove applied at the centre of the blast, in metres per second.</summary>
    [Export(PropertyHint.Range, "0,40,0.1,suffix:m/s")]
    public float Impulse { get; set; } = 19.0f;

    /// <summary>How much of the upward share a blast shove carries. Purely up
    /// throws you straight into the air; purely outward slides you along.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ImpulseUpBias { get; set; } = 0.45f;

    /// <summary>Scales the shove the thrower takes from their own grenade.</summary>
    [Export(PropertyHint.Range, "0,2,0.05")]
    public float SelfImpulseScale { get; set; } = 1.0f;

    /// <summary>
    /// Multiplies the impulse handed to loose rigid bodies. Above one because
    /// rubble flying is the cheapest and most legible signal that a blast was
    /// powerful, and debris is not something the blast has to be fair to.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.1")]
    public float DebrisImpulseScale { get; set; } = 2.6f;

    /// <summary>
    /// Crater dug where this goes off. Zero -- the hand grenade's value --
    /// leaves it to the level's generic blast crater; the bike's bomb names
    /// its own, because a bomb is the thing a bomb bay is for.
    /// </summary>
    [ExportGroup("Terrain Effect")]
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float CraterRadius { get; set; }

    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float CraterDepth { get; set; } = 2.6f;

    // ---------------- Cluster ----------------

    [ExportGroup("Cluster")]
    /// <summary>
    /// Bomblets thrown out when this grenade goes off, each its own small
    /// grenade on its own short fuse. Zero, the default, is a plain frag.
    ///
    /// Against a horde a single blast clears a circle and the crowd closes
    /// over it; a ring of bomblets landing a moment later and a few metres
    /// out clears the crowd that was about to close. Each is a full Grenade
    /// -- the same body, blast and effect -- so nothing new is simulated.
    /// </summary>
    [Export(PropertyHint.Range, "0,16,1")]
    public int BombletCount { get; set; } = 0;

    /// <summary>
    /// What each bomblet is. Its own BombletCount is ignored: bomblets never
    /// split again, so a cluster cannot grow geometrically by accident.
    /// </summary>
    [Export]
    public GrenadeStats Bomblet { get; set; }

    /// <summary>Outward speed of a bomblet, in m/s, before the up bias.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float BombletSpeed { get; set; } = 9.0f;

    /// <summary>Upward share of a bomblet's throw, so they arc rather than skid.</summary>
    [Export(PropertyHint.Range, "0,2,0.05")]
    public float BombletUpBias { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.05,3,0.05,suffix:s")]
    public float BombletFuseMin { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0.05,3,0.05,suffix:s")]
    public float BombletFuseMax { get; set; } = 0.85f;

    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,10,0.05,suffix:°")]
    public float ThrowKickPitchDegrees { get; set; } = 1.1f;

    [Export(PropertyHint.Range, "0,10,0.05,suffix:°")]
    public float ThrowKickYawDegrees { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 18.0f;

    /// <summary>
    /// Damage share at a given distance from the blast centre. Full inside
    /// <see cref="InnerRadius"/>, lerping to <see cref="MinMultiplier"/> at
    /// <see cref="BlastRadius"/>, and zero beyond it.
    /// </summary>
    public float BlastMultiplierAt(float distance)
    {
        if (distance <= InnerRadius)
        {
            return 1.0f;
        }
        if (distance >= BlastRadius || BlastRadius <= InnerRadius)
        {
            return distance >= BlastRadius ? 0.0f : 1.0f;
        }

        float t = (distance - InnerRadius) / (BlastRadius - InnerRadius);
        return Mathf.Lerp(1.0f, MinMultiplier, t);
    }
}
