using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// A kind of vine: every setting a range, rolled per seed, each from its own
/// named stream, so widening one range never reshuffles the others. A kind's
/// pool is a handful of seeds; each baked vine is one of them
/// (docs/decisions/0018, 0033). A range is (least, most); a pinned setting
/// has both the same.
/// </summary>
[GlobalClass, Tool]
public partial class VineKind : Resource
{
    public const string Folder = "res://resources/foliage";
    public const string Prefix = "vine_kind_";

    /// <summary>The kind's name: part of each pool seed and of each baked file's name.</summary>
    [Export] public string KindName { get; set; } = "Vine";

    /// <summary>How often the nest draws this kind, against the others.</summary>
    [ExportGroup("Nest")]
    [Export(PropertyHint.Range, "0,10,0.1")] public float Weight { get; set; } = 1.0f;
    /// <summary>Where in the nest it grows, a share of the nest's radius: 0 the heart, 1 the edge.</summary>
    [Export] public Vector2 Zone { get; set; } = new(0, 1);
    /// <summary>How many seeds the kind's pool holds.</summary>
    [Export(PropertyHint.Range, "1,8,1")] public int PoolSize { get; set; } = 4;

    [ExportGroup("Vine")]
    [Export] public Vector2 HeightM { get; set; } = new(80, 80);
    [Export] public Vector2 RingCount { get; set; } = new(10, 10);
    [Export] public Vector2 BareStalkM { get; set; } = new(24, 24);
    [Export] public Vector2 WobbleM { get; set; } = new(2, 2);
    [Export] public Vector2 WobbleFrequency { get; set; } = new(1.6f, 1.6f);
    [Export] public Vector2 LeanDeg { get; set; } = new(7, 7);
    [Export] public Vector2 HelixTurns { get; set; } = new(0, 0);
    [Export] public Vector2 TrellisRadiusM { get; set; } = new(4, 4);
    [Export] public Vector2 TipNodDeg { get; set; } = new(30, 30);
    [Export] public Vector2 Irregularity { get; set; } = new(0.6f, 0.6f);

    [ExportGroup("Stalk")]
    [Export] public Vector2 BaseRadiusM { get; set; } = new(2.8f, 2.8f);
    [Export] public Vector2 TipRadiusM { get; set; } = new(1, 1);
    [Export] public Vector2 ShellVoxels { get; set; } = new(2, 2);
    [Export] public Vector2 KnuckleBulge { get; set; } = new(0.4f, 0.4f);
    [Export] public Vector2 KnuckleWidth { get; set; } = new(3, 3);

    [ExportGroup("Arms")]
    [Export] public Vector2 ArmsPerRing { get; set; } = new(4, 4);
    [Export] public Vector2 ArmLengthM { get; set; } = new(18, 18);
    [Export] public Vector2 ArmRiseDeg { get; set; } = new(40, 40);
    [Export] public Vector2 ArmSag { get; set; } = new(1.4f, 1.4f);
    [Export] public Vector2 ArmThickness { get; set; } = new(0.4f, 0.4f);
    /// <summary>How much shorter the top ring's arms are than the bottom's, a share.</summary>
    [Export] public Vector2 ArmTaper { get; set; } = new(0, 0);
    /// <summary>Chance a vine of this kind has a leaf at each arm's tip.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float ArmTipLeafChance { get; set; } = 1.0f;

    [ExportGroup("Pods")]
    [Export] public Vector2 PodsPerRing { get; set; } = new(16, 16);
    [Export] public Vector2 PodTwistDeg { get; set; } = new(40, 40);
    [Export] public Vector2 PedicelM { get; set; } = new(2, 2);

    [ExportGroup("Leaves")]
    [Export] public Vector2 LeavesPerRing { get; set; } = new(0, 0);
    [Export] public Vector2 LeavesPerArm { get; set; } = new(0, 0);
    /// <summary>Across the blade: 6-14 m, so a leaf is something to land on.</summary>
    [Export] public Vector2 LeafSizeM { get; set; } = new(9, 9);
    /// <summary>How far a blade may be from level: platforms, not ramps.</summary>
    [Export] public Vector2 LeafTiltDeg { get; set; } = new(6, 6);
    [Export] public Vector2 LeafStemM { get; set; } = new(3, 3);
    [Export] public Vector2 LeafStemRiseDeg { get; set; } = new(30, 30);

    /// <summary>The kind's leaf shapes (<see cref="VineGenerator.LeafShapes"/> of
    /// them) roll inside these: lobes each side, teeth round the edge, how deep
    /// the heart's notch is and how cupped the blade (LeafMesh).</summary>
    /// <summary>The kind's own colours and how far each vine's shade strays;
    /// none, the bake's (vine_bake.tres).</summary>
    [ExportGroup("Look")]
    [Export] public VinePalette Palette { get; set; }

    [ExportGroup("Leaf shape")]
    [Export] public Vector2 LeafLobes { get; set; } = new(0, 1);
    [Export] public Vector2 LeafTeeth { get; set; } = new(10, 16);
    [Export] public Vector2 LeafNotch { get; set; } = new(0.15f, 0.3f);
    [Export] public Vector2 LeafCup { get; set; } = new(0.08f, 0.16f);

    /// <summary>The file name's part for this kind: lower case, letters and digits.</summary>
    public string Slug
    {
        get
        {
            var chars = new System.Text.StringBuilder();
            foreach (char c in KindName.ToLowerInvariant())
            {
                if (char.IsAsciiLetterOrDigit(c))
                    chars.Append(c);
            }
            return chars.Length > 0 ? chars.ToString() : "vine";
        }
    }

    /// <summary>The seed of the pool's <paramref name="index"/>th vine.</summary>
    public int PoolSeed(int archetypeSeed, int index) =>
        (int)(SeededRandom.Hash((uint)archetypeSeed, $"vine:pool:{KindName}#{index}") % 10_000_000UL);

    /// <summary>One vine's settings, each inside its range.</summary>
    /// <summary>A kind's ranges and chances, read once on the main thread so
    /// rolls on worker threads never call into the engine (<see cref="VineGrower.Context"/>).</summary>
    public sealed class Ranges
    {
        public readonly Dictionary<string, (double lo, double hi)> Range = new();
        public readonly Dictionary<string, double> Chance = new();
    }

    public Ranges ReadRanges()
    {
        var ranges = new Ranges();
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            if (field.Bool)
                ranges.Chance[field.Key] = ChanceOf(field.Key);
            else
                ranges.Range[field.Key] = RangeOf(field);
        }
        return ranges;
    }

    public VineParams Roll(int seed, double voxelSize) => Roll(seed, voxelSize, null);

    /// <summary>One vine's settings, from <paramref name="ranges"/> when given
    /// (any thread), else read from the kind (main thread).</summary>
    public VineParams Roll(int seed, double voxelSize, Ranges ranges)
    {
        var p = new VineParams { Seed = seed, VoxelSize = voxelSize };
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            double t = SeededRandom.Stream((uint)seed, "vine:roll:" + field.Key).NextDouble();
            if (field.Bool)
            {
                field.Set(p, t < (ranges != null ? ranges.Chance[field.Key] : ChanceOf(field.Key)) ? 1 : 0);
                continue;
            }
            (double lo, double hi) = ranges != null ? ranges.Range[field.Key] : RangeOf(field);
            if (field.Integer)
            {
                field.Set(p, Math.Min(hi, lo + Math.Floor(t * (hi - lo + 1))));
            }
            else
            {
                double v = lo + (hi - lo) * t;
                if (field.Step > 0)
                    v = Math.Min(hi, Math.Max(lo, VineMath.Round(v / field.Step) * field.Step));
                field.Set(p, VineMath.Round(v * 1000) / 1000);
            }
        }
        return p.Clamped();
    }

    /// <summary>A setting's range: inside the field, whole for
    /// integers, least first. A float only carries six or so digits, so each
    /// end is read back as the decimal it was written as.</summary>
    public (double lo, double hi) RangeOf(VineParams.Field field)
    {
        Vector2 pair = Get(Property(field.Key)).AsVector2();
        double lo = Math.Clamp(Math.Round((double)pair.X, 6), field.Min, field.Max);
        double hi = Math.Clamp(Math.Round((double)pair.Y, 6), field.Min, field.Max);
        if (field.Integer)
        {
            lo = VineMath.Round(lo);
            hi = VineMath.Round(hi);
        }
        return hi < lo ? (hi, lo) : (lo, hi);
    }

    public double ChanceOf(string key) =>
        Math.Clamp(Math.Round((double)Get(Property(key) + "Chance").AsSingle(), 6), 0, 1);

    private static string Property(string key) => char.ToUpperInvariant(key[0]) + key.Substring(1);
}
