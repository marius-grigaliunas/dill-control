namespace DillControl.Weapons;

/// <summary>
/// What the player's earned upgrades do to their weapons, as multipliers on
/// top of each weapon's own stats.
///
/// Session state, not data: the stats resources stay exactly as authored,
/// and an upgrade is a scale applied at the moment a number is used. Writing
/// earned upgrades into the resources instead would save a run's progress
/// into the .tres the moment anything saved it, and ship it as the weapon's
/// balance. Pushed into every slot by
/// <see cref="WeaponLoadout.ApplyBonus"/>; <see cref="None"/> is no upgrade.
/// </summary>
public sealed class PackageBonus
{
    public static readonly PackageBonus None = new();

    /// <summary>Every gun's damage, direct and splash.</summary>
    public float DamageScale { get; init; } = 1.0f;

    /// <summary>Every reload's duration.</summary>
    public float ReloadScale { get; init; } = 1.0f;

    public float MeleeDamageScale { get; init; } = 1.0f;

    public float MeleeReachScale { get; init; } = 1.0f;

    public float MeleeArcScale { get; init; } = 1.0f;

    /// <summary>Targets one swing may cut, on top of the weapon's own limit.</summary>
    public int MeleeExtraTargets { get; init; }

    /// <summary>
    /// Radius of the brine wave a swing throws ahead of itself, reaching every
    /// agent in it the way a fire zone does. Zero is no wave.
    /// </summary>
    public float MeleeWaveRadius { get; init; }
}
