using Godot;

namespace DillControl.Core;

/// <summary>
/// A blast's geometry, carryable through <see cref="EventBus.AreaBlast"/>.
///
/// A <see cref="RefCounted"/> rather than <see cref="Blast.Settings"/> itself
/// for the same reason <see cref="DamageInfo"/> is one and not a struct:
/// Godot signals need a Variant-compatible argument. Only the fields a blast
/// listener needs to resolve its own falloff are carried -- not the impulse
/// fields, which nothing outside <see cref="Blast"/> currently needs.
/// </summary>
[GlobalClass]
public partial class BlastInfo : RefCounted
{
    public Vector3 Centre { get; set; }
    public float InnerRadius { get; set; }
    public float Radius { get; set; }
    public float Damage { get; set; }
    public float MinMultiplier { get; set; } = 1.0f;

    /// <summary>Local up at the blast, for the shove a listener may want to
    /// give a surviving victim. On a planet this is not world up.</summary>
    public Vector3 Up { get; set; }

    /// <summary>Who caused it. May be null for world damage.</summary>
    public Node3D Source { get; set; }

    /// <summary>See <see cref="Blast.Settings.SourceRid"/>. Listeners that run
    /// their own cover check exclude it too.</summary>
    public Rid SourceRid { get; set; }

    // ---------------- What this blast does besides hurt things ----------------
    //
    // Four fields, all zero by default, which is what every explosive in the
    // project was before ordnance types existed. They ride the existing fourth
    // bus signal rather than earning a fifth: BlastInfo is a RefCounted for
    // exactly this reason -- fields can be added to it without widening a
    // signal signature or touching a single listener.

    /// <summary>Crater radius to dig at the centre. Zero leaves the ground alone.</summary>
    public float CraterRadius { get; set; }

    /// <summary>How deep that crater goes.</summary>
    public float CraterDepth { get; set; }

    /// <summary>
    /// Coolant to pour in at the centre, in wetness units. The company's
    /// cheapest round is the water that made the alligators, sold back as a
    /// weapon -- and because craters hold water, a round that digs and a round
    /// that floods are the same system from two ends.
    /// </summary>
    public float CoolantAmount { get; set; }

    /// <summary>Seconds of burning denial zone left behind.</summary>
    public float FireSeconds { get; set; }

    /// <summary>
    /// True for damage resolved on a tick through the blast path -- a fire
    /// zone burning -- rather than a detonation. Such a "blast" hurts what is
    /// in it and nothing else: no crater, no thrown ground, no camera shake.
    /// Found when the generic crater dug a fresh four-metre pit on every tick
    /// of every fire zone; the thrown ground and the shake had been doing the
    /// same thing, less visibly, since fire zones existed.
    /// </summary>
    public bool Sustained { get; set; }

    /// <summary>See <see cref="Blast.Settings.NoCrater"/>.</summary>
    public bool NoCrater { get; set; }

    /// <summary>
    /// A blast along a segment rather than round a point: from
    /// <see cref="Centre"/> to <see cref="LineEnd"/>, <see cref="Radius"/>
    /// wide, with falloff measured from the segment. The bike's laser is the
    /// first: a beam that burns every agent it passes through rather than the
    /// first one it meets. The caller has already ended the segment at the
    /// first wall, so whoever listens does not check line of sight again.
    /// </summary>
    public bool IsLine { get; set; }

    public Vector3 LineEnd { get; set; }

    public float MultiplierAt(float distance) => Blast.FalloffMultiplier(distance, InnerRadius, Radius, MinMultiplier);

    public static BlastInfo From(in Blast.Settings blast) => new()
    {
        Centre = blast.Centre,
        InnerRadius = blast.InnerRadius,
        Radius = blast.Radius,
        Damage = blast.Damage,
        MinMultiplier = blast.MinMultiplier,
        Up = blast.Up,
        SourceRid = blast.SourceRid,
        Source = blast.Source,
        CraterRadius = blast.CraterRadius,
        CraterDepth = blast.CraterDepth,
        CoolantAmount = blast.CoolantAmount,
        FireSeconds = blast.FireSeconds,
        Sustained = blast.Sustained,
        NoCrater = blast.NoCrater,
    };
}
