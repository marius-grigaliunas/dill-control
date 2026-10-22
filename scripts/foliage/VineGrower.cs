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
    /// <summary>
    /// Everything a grow reads from resources, read once on the main thread:
    /// many vines can then grow at once on worker threads (a nest) without
    /// calling into the engine.
    /// </summary>
    public sealed class Context
    {
        public VineBakeStats Stats;
        public VineGenerator.Limits Limits;
        public VineValidator.Spine Spine;
        public double VoxelSize;
        public readonly Dictionary<VineKind, VineKind.Ranges> Kinds = new();

        public static Context For(VineBakeStats stats, IEnumerable<VineKind> kinds)
        {
            var context = new Context
            {
                Stats = stats,
                Limits = stats.Limits(),
                Spine = VineValidator.Spine.Of(stats.Flex),
                VoxelSize = Math.Round((double)stats.VoxelSize, 6),
            };
            foreach (VineKind kind in kinds)
            {
                if (kind != null && !context.Kinds.ContainsKey(kind))
                    context.Kinds[kind] = kind.ReadRanges();
            }
            return context;
        }
    }

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
    public static Grown Grow(VineKind kind, int seed, VineBakeStats stats) =>
        Grow(kind, seed, Context.For(stats, new[] { kind }));

    /// <summary>The same, from a <see cref="Context"/>: any thread. A
    /// <paramref name="scale"/> other than 1 grows it that much taller (a
    /// nest's size jitter), within the setting's range.</summary>
    public static Grown Grow(VineKind kind, int seed, Context context, float scale = 1.0f)
    {
        VineBakeStats stats = context.Stats;
        context.Kinds.TryGetValue(kind, out VineKind.Ranges ranges);
        var grown = new Grown { Seed = seed };
        int attempts = Math.Max(1, stats.MaxAttempts);
        for (int a = 0; a < attempts; a++)
        {
            int rolled = AttemptSeed(seed, a);
            VineParams p = kind.Roll(rolled, context.VoxelSize, ranges);
            if (scale != 1.0f)
                p.HeightM = Math.Round(p.HeightM * scale, 3);
            VineGenerator.Result vine = VineGenerator.Generate(p, context.Limits);
            VineValidator.Verdict verdict = VineValidator.Check(vine, stats, full: false, context.Spine);
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
