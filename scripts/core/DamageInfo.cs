using Godot;

namespace DillControl.Core;

/// <summary>
/// One damage event, from the thing that caused it to the thing that took it.
///
/// This is a <see cref="RefCounted"/> rather than a struct so it can travel
/// through Godot signals as a single argument. That keeps
/// <see cref="EventBus.DamageDealt"/> stable: month 2 adds fields here instead
/// of widening the signal signature and touching every listener.
/// </summary>
[GlobalClass]
public partial class DamageInfo : RefCounted
{
    /// <summary>Final damage after hitbox multiplier and range falloff.</summary>
    public float Amount { get; set; }

    /// <summary>Damage before any multiplier was applied. Debug/UI only.</summary>
    public float BaseAmount { get; set; }

    /// <summary>Multiplier the hitbox contributed. 1.0 when hit outside a hitbox.</summary>
    public float Multiplier { get; set; } = 1.0f;

    /// <summary>True when the hitbox that took this hit is flagged as a weak point.</summary>
    public bool IsWeakPoint { get; set; }

    /// <summary>
    /// True for area damage. A splash hit was not aimed at the thing it hurt,
    /// so the HUD can mark it differently and weak-point framing does not apply.
    /// </summary>
    public bool IsSplash { get; set; }

    /// <summary>From a detonation (<see cref="Blast.Settings.Explosive"/>):
    /// strong enough to shake something big, where a round or a blade only
    /// bites it.</summary>
    public bool IsExplosive { get; set; }

    /// <summary>World-space point of impact.</summary>
    public Vector3 Position { get; set; }

    /// <summary>Surface normal at the impact point.</summary>
    public Vector3 Normal { get; set; }

    /// <summary>Direction the damage travelled in, normalized.</summary>
    public Vector3 Direction { get; set; }

    /// <summary>Metres between the source and the impact point.</summary>
    public float Distance { get; set; }

    /// <summary>Who caused it. May be null for world damage.</summary>
    public Node3D Source { get; set; }

    /// <summary>
    /// How much of the target was left AFTER this hit landed, from 0 to 1, or
    /// <see cref="UnknownHealth"/> when the target does not keep a health pool
    /// anyone can express that way.
    ///
    /// Filled in by whatever applied the damage, because that is the only thing
    /// that knows -- a HealthComponent reads its own pool, the horde reads one
    /// agent out of a flat array with no node at all, and a VoxelBuilding's
    /// health is literally its remaining voxel count. Every one of those
    /// answers the same question and none of them could be asked it through a
    /// common interface without inventing hit points for something that has
    /// none.
    ///
    /// This is the field-on-DamageInfo extension the class comment above
    /// promises: the bus signature is unchanged and no existing listener had to
    /// be touched.
    /// </summary>
    public float HealthFraction { get; set; } = UnknownHealth;

    /// <summary>
    /// The world point the health readout belongs above -- the target's own
    /// position, not the impact point, so a bar does not crawl around a body as
    /// it is shot. Left at zero when unknown, and then the impact point is used.
    /// </summary>
    public Vector3 TargetPosition { get; set; }

    /// <summary>Sentinel for "this target has no health to report".</summary>
    public const float UnknownHealth = -1.0f;

    /// <summary>True when <see cref="HealthFraction"/> was actually filled in.</summary>
    public bool HasHealth => HealthFraction >= 0.0f;

    /// <summary>
    /// True when this hit was the one that killed its target. Filled in by
    /// whatever applied the damage, same as <see cref="HealthFraction"/> and
    /// for the same reason: only that code knows. Exists because
    /// <see cref="EventBus.DamageDealt"/> otherwise reports every hit
    /// identically, and against the horde specifically a kill is nearly
    /// invisible -- RespawnKilledAgents refills the slot immediately so the
    /// population never visibly shrinks, so a listener needs to be told a
    /// kill happened rather than inferring it from the population changing.
    /// </summary>
    public bool Killed { get; set; }

    /// <summary>
    /// How many targets this report covers the deaths of, when one report
    /// stands for many: the horde announces a whole blast as one DamageDealt
    /// with the total. Zero or one for an ordinary hit. Read by the hit-stop,
    /// which wants to know a crowd died, not that something did.
    /// </summary>
    public int KillCount { get; set; }

    /// <summary>
    /// Damage that arrives continuously -- a flame's wash, a fire zone, the
    /// bike's piercing beam -- rather than as one event. The hit-stop ignores
    /// it: a held beam into a crowd kills ten a tick, and freezing time for
    /// each of those reads as the game stuttering, not as a punch.
    /// </summary>
    public bool IsSustained { get; set; }

    /// <summary>
    /// A melt rather than a hit: a destructible body removes a sphere of this
    /// radius whatever <see cref="Amount"/> says, and armour slows it rather
    /// than stopping it. Zero, the default, is every ordinary hit, whose carve
    /// scales with damage. Set by the bike's laser, which is meant to melt
    /// anything in its way -- a damage number high enough to do that through
    /// the carve formula would also one-shot everything with a health pool.
    /// </summary>
    public float MeltRadius { get; set; }
}
