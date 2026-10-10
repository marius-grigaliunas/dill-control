using System.Collections.Generic;
using Godot;
using DillControl.Core;

namespace DillControl.Mission;

/// <summary>
/// Turns a seed into a contract, and a session's seed into the contracts on
/// offer. Pure: no nodes, no randomness but the seed's
/// (<c>docs/decisions/0018</c>). Each pick takes its own stream, so adding a
/// pool never reshuffles the others.
/// </summary>
public static class ContractGenerator
{
    public static ContractSpec Generate(ContractPools pools, ulong seed)
    {
        var archetype = Pick(pools.Archetypes, pools.ArchetypeWeights, seed, "archetype");
        return new ContractSpec
        {
            Seed = seed,
            Designation = Designation(seed),
            Archetype = archetype,
            Planet = archetype?.Roll(seed),
            Biome = Pick(pools.Biomes, pools.BiomeWeights, seed, "biome pick"),
            Kit = Pick(pools.Kits, pools.KitWeights, seed, "kit"),
            Tier = Pick(pools.Tiers, pools.TierWeights, seed, "tier"),
        };
    }

    /// <summary>The contracts on offer for a session: each its own seed, made from the session's.</summary>
    public static List<ContractSpec> Offers(ContractPools pools, ulong sessionSeed, int count)
    {
        var offers = new List<ContractSpec>(count);
        for (int i = 0; i < count; i++)
        {
            offers.Add(Generate(pools, SeededRandom.Hash(sessionSeed, $"offer {i}")));
        }
        return offers;
    }

    private static T Pick<[MustBeVariant] T>(Godot.Collections.Array<T> pool, float[] weights, ulong seed, string stage)
        where T : GodotObject
    {
        if (pool == null || pool.Count == 0)
        {
            return null;
        }

        SeededRandom random = SeededRandom.Stream(seed, stage);
        if (weights != null && weights.Length == pool.Count)
        {
            int index = random.PickWeighted(weights);
            return index >= 0 ? pool[index] : null;
        }
        return pool[random.RangeInt(0, pool.Count - 1)];
    }

    private const string Letters = "ABCDEFGHJKLMNPRSTVWXZ";

    /// <summary>Two letters and four digits, from the seed: a survey catalogue's name, not a place's.</summary>
    public static string Designation(ulong seed)
    {
        SeededRandom random = SeededRandom.Stream(seed, "designation");
        char a = Letters[random.RangeInt(0, Letters.Length - 1)];
        char b = Letters[random.RangeInt(0, Letters.Length - 1)];
        return System.FormattableString.Invariant($"{a}{b}-{random.RangeInt(1000, 9999)}");
    }
}
