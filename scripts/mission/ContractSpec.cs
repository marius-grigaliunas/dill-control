using DillControl.Horde;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// One contract, resolved: the seed and everything picked and rolled from it.
/// Produced by <see cref="ContractGenerator"/>, never authored; the same seed
/// and pools always give the same contract.
/// </summary>
public sealed class ContractSpec
{
    public ulong Seed { get; init; }

    /// <summary>The planet's catalogue name, made from the seed: "KR-4471".</summary>
    public string Designation { get; init; }

    public PlanetArchetype Archetype { get; init; }

    /// <summary>The planet's shape, rolled inside the archetype.</summary>
    public PlanetSpec Planet { get; init; }

    public PlanetBiome Biome { get; init; }

    public CompoundKit Kit { get; init; }

    public HordeTier Tier { get; init; }

    /// <summary>Small, medium or large, by where the radius fell in the archetype's range.</summary>
    public string SizeClass
    {
        get
        {
            if (Archetype == null || Archetype.RadiusMax <= Archetype.RadiusMin)
            {
                return "standard";
            }
            float t = (Planet.Radius - Archetype.RadiusMin) / (Archetype.RadiusMax - Archetype.RadiusMin);
            return t < 0.34f ? "small" : t < 0.67f ? "medium" : "large";
        }
    }

    /// <summary>One line for a list: "KR-4471, a medium Rust Dunes rock, Infested (25,000)".</summary>
    public string Summary => System.FormattableString.Invariant(
        $"{Designation}: a {SizeClass} {Biome?.DisplayName ?? "unknown"} {Archetype?.DisplayName.ToLowerInvariant() ?? "planet"}, {Tier?.DisplayName ?? "unknown"} ({Tier?.AgentCount ?? 0:N0})");
}
