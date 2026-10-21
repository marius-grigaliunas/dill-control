using System;
using System.Collections.Generic;
using DillControl.Core;

namespace DillControl.Foliage;

/// <summary>
/// Grows the vine a seed means. A seed whose roll fails <see cref="VineValidator"/>
/// is rolled again from the seed and an attempt number, so the same seed is
/// always the same valid vine, everywhere: attempt 0 is the seed itself, so a
/// seed that grows a valid vine keeps it. Every bake, every in-memory vine and
/// the dock grow through here. Pure; any thread.
/// </summary>
public static class VineGrower
{
    public sealed class Rejection
    {
        public int Attempt;
        public int Seed;
        public List<string> Reasons;
    }

    public sealed class Grown
    {
        /// <summary>The seed asked for: the vine's name.</summary>
        public int Seed;
        /// <summary>Which attempt grew it, 0 for the seed itself.</summary>
        public int Attempt;
        public VineGenerator.Result Vine;
        public VineValidator.Verdict Verdict;
        public readonly List<Rejection> Rejected = new();
        public bool Ok => Verdict != null && Verdict.Ok;
    }

    /// <summary>The seed attempt <paramref name="attempt"/> rolls from.</summary>
    public static int AttemptSeed(int seed, int attempt) =>
        attempt == 0 ? seed : (int)(SeededRandom.Hash((uint)seed, "vine:attempt:" + attempt) % 10_000_000UL);

    /// <summary>The first valid vine from <paramref name="seed"/>; if none in
    /// <see cref="VineBakeStats.MaxAttempts"/>, the last tried, not
    /// <see cref="Grown.Ok"/>.</summary>
    public static Grown Grow(VineKind kind, int seed, VineBakeStats stats)
    {
        double vs = Math.Round((double)stats.VoxelSize, 6);
        VineGenerator.Limits limits = stats.Limits();
        var grown = new Grown { Seed = seed };
        int attempts = Math.Max(1, stats.MaxAttempts);
        for (int a = 0; a < attempts; a++)
        {
            int rolled = AttemptSeed(seed, a);
            VineGenerator.Result vine = VineGenerator.Generate(kind.Roll(rolled, vs), limits);
            VineValidator.Verdict verdict = VineValidator.Check(vine, stats, full: false);
            grown.Attempt = a;
            grown.Vine = vine;
            grown.Verdict = verdict;
            if (verdict.Ok)
                return grown;
            grown.Rejected.Add(new Rejection { Attempt = a, Seed = rolled, Reasons = verdict.Reasons });
        }
        return grown;
    }

    /// <summary>Settings set by hand are not re-rolled: grown and judged once.</summary>
    public static Grown FromSettings(VineParams settings, VineBakeStats stats)
    {
        VineGenerator.Result vine = VineGenerator.Generate(settings, stats.Limits());
        return new Grown
        {
            Seed = (int)vine.Params.Seed,
            Vine = vine,
            Verdict = VineValidator.Check(vine, stats),
        };
    }
}
