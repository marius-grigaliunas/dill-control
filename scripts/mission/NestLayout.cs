using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DillControl.Core;
using DillControl.Foliage;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// A nest planned from a kit, a seed and a population: pure, so the same
/// seed is the same nest, and it can run on worker threads while the planet
/// generates.
///
/// - Slots: a heart, then rings out from it at the kit's spacing, each slot
///   jittered, none in an avenue, none past the kit's radius. Measured along
///   the ground, in the nest's own plane (<see cref="NestPlacer"/> lays it on
///   the planet).
/// - Vines: slot by slot, inner rings first, a kind picked by weight among
///   those whose zone the slot is in, grown valid (<see cref="VineGrower"/>)
///   on all cores a batch at a time, until their pods cover the population.
/// - If the slots run out first, the plan is made again with kinds weighed
///   by their pods and zones ignored: more pods a vine, never closer vines.
/// - Then every vine's model is built, on all cores.
/// </summary>
public sealed class NestLayout
{
    public sealed class Vine
    {
        public int Index;
        public VineKind Kind;
        public int Seed;
        public float Scale;
        /// <summary>From the heart along the ground, and its bearing.</summary>
        public float RadiusM;
        public float Bearing;
        public VineGrower.Grown Grown;
        public VineFactory.Data Data;
        /// <summary>Which way it was built leaning, in its model's space, level:
        /// it is turned so this points at the heart.</summary>
        public Vector3 Lean;
        public int Pods => Grown?.Vine?.Counts.Markers ?? 0;
    }

    public readonly List<Vine> Vines = new();
    public int Population;
    public int Pods;
    public int Slots;
    public float RadiusM;
    public float[] Avenues = Array.Empty<float>();
    /// <summary>The kit's weights did not fit the population inside its radius.</summary>
    public bool Shifted;
    /// <summary>Pickles without a pod even so.</summary>
    public int Short;
    public double GrowSeconds;
    public double ModelSeconds;

    private readonly struct Slot
    {
        public readonly float Radius, Bearing;
        public Slot(float radius, float bearing)
        {
            Radius = radius;
            Bearing = bearing;
        }
    }

    public static NestLayout Plan(NestKit kit, VineGrower.Context context, ulong seed, int population)
    {
        var clock = Stopwatch.StartNew();
        List<VineKind> kinds = kit.Kinds.Where(k => k != null).ToList();
        var grown = new Dictionary<(VineKind, int, float), VineGrower.Grown>();
        var layout = new NestLayout { Population = population };
        List<Slot> slots = MakeSlots(kit, seed, layout);
        layout.Slots = slots.Count;
        if (kinds.Count == 0)
        {
            return layout;
        }

        // How many pods a kind's vine has, roughly: a few of each grown.
        var meanPods = new Dictionary<VineKind, double>();
        foreach (VineKind kind in kinds)
        {
            var sample = Enumerable.Range(0, 3).Select(i => (int)(SeededRandom.Hash(seed, $"nest:sample:{kind.KindName}:{i}") % 10_000_000UL)).ToArray();
            var pods = new int[sample.Length];
            Parallel.For(0, sample.Length, i => pods[i] = VineGrower.Grow(kind, sample[i], context).Vine.Counts.Markers);
            meanPods[kind] = Math.Max(1.0, pods.Average());
        }

        Fill(layout, kit, context, seed, population, slots, kinds, meanPods, grown, shift: false);
        if (layout.Pods < population)
        {
            layout.Vines.Clear();
            layout.Shifted = true;
            Fill(layout, kit, context, seed, population, slots, kinds, meanPods, grown, shift: true);
        }
        layout.Short = Math.Max(0, population - layout.Pods);
        layout.RadiusM = layout.Vines.Count > 0 ? layout.Vines.Max(v => v.RadiusM) : 0.0f;
        layout.GrowSeconds = clock.Elapsed.TotalSeconds;
        return layout;
    }

    /// <summary>Every vine's model, on all cores.</summary>
    public void BuildModels(VineBakeStats stats)
    {
        var clock = Stopwatch.StartNew();
        Parallel.ForEach(Vines, vine =>
        {
            vine.Data = VineFactory.BuildData(vine.Grown, vine.Kind, stats, $"nest_{vine.Index}_{vine.Kind.Slug}_{vine.Seed}");
        });
        ModelSeconds = clock.Elapsed.TotalSeconds;
    }

    private static List<Slot> MakeSlots(NestKit kit, ulong seed, NestLayout layout)
    {
        SeededRandom avenues = SeededRandom.Stream(seed, "nest:avenues");
        int count = avenues.RangeInt(kit.Avenues.X, kit.Avenues.Y);
        float phase = avenues.Range(0.0f, Mathf.Tau);
        layout.Avenues = new float[count];
        for (int i = 0; i < count; i++)
        {
            layout.Avenues[i] = phase + (Mathf.Tau * i / count) + avenues.Range(-0.2f, 0.2f);
        }

        SeededRandom rings = SeededRandom.Stream(seed, "nest:rings");
        var slots = new List<Slot> { new(0.0f, 0.0f) };
        float radius = 0.0f;
        while (true)
        {
            float spacing = rings.Range(kit.SpacingM.X, kit.SpacingM.Y);
            radius += spacing;
            if (radius > kit.MaxRadiusM)
            {
                break;
            }
            int n = Math.Max(1, (int)Math.Floor(Mathf.Tau * radius / spacing));
            float ringPhase = rings.Range(0.0f, Mathf.Tau);
            // Taken in an order that spreads them round the ring: a ring the
            // population ends part way round is not a crescent.
            var order = Enumerable.Range(0, n).OrderBy(j => (j * 0.6180339887) % 1.0).ToList();
            var ring = new Slot[n];
            for (int j = 0; j < n; j++)
            {
                float bearing = ringPhase + (Mathf.Tau * j / n) + (rings.Range(-1.0f, 1.0f) * kit.Jitter * spacing / radius);
                float r = radius + (rings.Range(-1.0f, 1.0f) * kit.Jitter * spacing);
                ring[j] = new Slot(r, bearing);
            }
            foreach (int j in order)
            {
                if (!InAvenue(layout.Avenues, ring[j], kit.AvenueWidthM))
                {
                    slots.Add(ring[j]);
                }
            }
        }
        return slots;
    }

    private static bool InAvenue(float[] avenues, Slot slot, float width)
    {
        foreach (float a in avenues)
        {
            float d = Mathf.Abs(Mathf.AngleDifference(a, slot.Bearing));
            if (d < Mathf.Pi * 0.5f && Mathf.Sin(d) * slot.Radius < width * 0.5f)
            {
                return true;
            }
        }
        return false;
    }

    private static void Fill(NestLayout layout, NestKit kit, VineGrower.Context context, ulong seed, int population,
        List<Slot> slots, List<VineKind> kinds, Dictionary<VineKind, double> meanPods,
        Dictionary<(VineKind, int, float), VineGrower.Grown> grown, bool shift)
    {
        double most = meanPods.Values.Max();
        float[] weights = kinds.Select(k => (float)(Math.Max(k.Weight, 0.0f) * (shift ? Math.Pow(meanPods[k] / most, 2) : 1.0))).ToArray();

        // How far out the nest will reach, from the kinds' mean pods: a
        // kind's zone is a share of that.
        double perVine = weights.Sum() > 0 ? kinds.Select((k, i) => weights[i] * meanPods[k]).Sum() / weights.Sum() : 1;
        int needed = Math.Min(slots.Count, (int)Math.Ceiling(population / Math.Max(perVine, 1)));
        float reach = Math.Max(1.0f, slots[Math.Max(0, needed - 1)].Radius);

        SeededRandom picks = SeededRandom.Stream(seed, shift ? "nest:kinds:shifted" : "nest:kinds");
        SeededRandom sizes = SeededRandom.Stream(seed, "nest:sizes");
        int pods = 0;
        int next = 0;
        while (next < slots.Count && pods < population)
        {
            // A few a core at a time, grown at once, inner slots first.
            var batch = new List<Vine>();
            while (next < slots.Count && batch.Count < Math.Max(4, System.Environment.ProcessorCount * 2))
            {
                Slot slot = slots[next];
                float zone = Math.Min(1.0f, slot.Radius / reach);
                var allowed = new float[kinds.Count];
                bool any = false;
                for (int k = 0; k < kinds.Count; k++)
                {
                    bool inZone = shift || (zone >= kinds[k].Zone.X - 1e-4f && zone <= kinds[k].Zone.Y + 1e-4f);
                    allowed[k] = inZone ? weights[k] : 0.0f;
                    any |= allowed[k] > 0.0f;
                }
                int pick = picks.PickWeighted(any ? allowed : weights);
                float scale = sizes.Range(kit.SizeJitter.X, kit.SizeJitter.Y);
                batch.Add(new Vine
                {
                    Index = next,
                    Kind = kinds[Math.Max(0, pick)],
                    Seed = (int)(SeededRandom.Hash(seed, "nest:vine:" + next) % 10_000_000UL),
                    Scale = scale,
                    RadiusM = slot.Radius,
                    Bearing = slot.Bearing,
                });
                next++;
            }
            Parallel.ForEach(batch, vine =>
            {
                var key = (vine.Kind, vine.Seed, vine.Scale);
                VineGrower.Grown done;
                lock (grown)
                {
                    grown.TryGetValue(key, out done);
                }
                done ??= VineGrower.Grow(vine.Kind, vine.Seed, context, vine.Scale);
                lock (grown)
                {
                    grown[key] = done;
                }
                vine.Grown = done;
                vine.Lean = LeanOf(done.Vine);
            });
            foreach (Vine vine in batch)
            {
                if (pods >= population)
                {
                    break;
                }
                if (vine.Grown.Ok)
                {
                    layout.Vines.Add(vine);
                    pods += vine.Pods;
                }
            }
        }
        layout.Pods = pods;
    }

    /// <summary>Its top's middle against its foot, level, in model space.</summary>
    private static Vector3 LeanOf(VineGenerator.Result vine)
    {
        if (vine?.Voxels == null || vine.Voxels.Count == 0)
        {
            return Vector3.Zero;
        }
        int from = Math.Max(0, vine.BoundsZ - 12);
        double x = 0, y = 0;
        int n = 0;
        foreach (VineGenerator.Voxel v in vine.Voxels)
        {
            if (v.Z >= from)
            {
                x += v.X + 0.5;
                y += v.Y + 0.5;
                n++;
            }
        }
        if (n == 0)
        {
            return Vector3.Zero;
        }
        Vector3 lean = Voxel.VoxSpace.ToGodot(new Vector3((float)(x / n - vine.FootX), (float)(y / n - vine.FootY), 0.0f));
        return new Vector3(lean.X, 0.0f, lean.Z);
    }
}
