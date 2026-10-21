using System;
using System.Collections.Generic;

namespace DillControl.Foliage;

/// <summary>
/// One vine's settings: a vine is these numbers and its seed. Rolled from a
/// <see cref="VineKind"/>'s ranges, grown by <see cref="VineGenerator"/>.
/// </summary>
public sealed class VineParams
{
    // The first seed whose vine at these settings gives every pod its drop.
    public double Seed = 3;
    public double HeightM = 80;
    public double VoxelSize = 0.5;
    public double RingCount = 10;
    public double BareStalkM = 24;
    public double WobbleM = 2;
    public double WobbleFrequency = 1.6;
    public double LeanDeg = 7;
    public double HelixTurns = 0;
    public double TrellisRadiusM = 4;
    public double TipNodDeg = 30;
    public double Irregularity = 0.6;
    public double BaseRadiusM = 2.8;
    public double TipRadiusM = 1;
    public double ShellVoxels = 2;
    public double KnuckleBulge = 0.4;
    public double KnuckleWidth = 3;
    public double ArmsPerRing = 4;
    public double ArmLengthM = 18;
    public double ArmRiseDeg = 40;
    public double ArmSag = 1.4;
    public double ArmThickness = 0.4;
    /// <summary>How much shorter the top ring's arms are than the bottom's, a share.</summary>
    public double ArmTaper = 0;
    public bool ArmTipLeaf = true;
    public double PodsPerRing = 16;
    public double PodTwistDeg = 40;
    public double PedicelM = 2;
    public double LeavesPerRing = 0;
    public double LeavesPerArm = 0;
    public double LeafSizeM = 5;
    public double LeafTiltDeg = 28;
    public double LeafDroopDeg = 34;
    public bool Tendrils = true;
    public double TendrilCount = 14;
    public double TendrilLengthM = 6;
    public double TendrilCurl = 2.5;

    public VineParams Copy() => (VineParams)MemberwiseClone();

    /// <summary>One setting: its range, step and default, and where it lives
    /// on <see cref="VineParams"/> and on a <see cref="VineKind"/>.</summary>
    public sealed class Field
    {
        public string Key;
        public double Min, Max, Step, Fallback;
        public bool Integer, Bool, Global;
        public Func<VineParams, double> Get;
        public Action<VineParams, double> Set;
    }

    private static Field F(string key, double min, double max, double step, double fallback,
        Func<VineParams, double> get, Action<VineParams, double> set, bool integer = false, bool global = false) =>
        new() { Key = key, Min = min, Max = max, Step = step, Fallback = fallback, Integer = integer, Global = global, Get = get, Set = set };

    private static Field B(string key, bool fallback, Func<VineParams, bool> get, Action<VineParams, bool> set) =>
        new() { Key = key, Bool = true, Min = 0, Max = 1, Fallback = fallback ? 1 : 0, Get = p => get(p) ? 1 : 0, Set = (p, v) => set(p, v != 0) };

    /// <summary>Every setting, in roll order.</summary>
    public static readonly IReadOnlyList<Field> Fields = new[]
    {
        F("seed", 0, 9999999, 1, 3, p => p.Seed, (p, v) => p.Seed = v, integer: true),
        F("heightM", 16, 120, 1, 80, p => p.HeightM, (p, v) => p.HeightM = v),
        F("voxelSize", 0.1, 1, 0.05, 0.5, p => p.VoxelSize, (p, v) => p.VoxelSize = v, global: true),
        F("ringCount", 1, 40, 1, 10, p => p.RingCount, (p, v) => p.RingCount = v, integer: true),
        F("bareStalkM", 0, 60, 0.5, 24, p => p.BareStalkM, (p, v) => p.BareStalkM = v),
        F("wobbleM", 0, 24, 0.5, 2, p => p.WobbleM, (p, v) => p.WobbleM = v),
        F("wobbleFrequency", 0.5, 8, 0.1, 1.6, p => p.WobbleFrequency, (p, v) => p.WobbleFrequency = v),
        F("leanDeg", -45, 45, 1, 7, p => p.LeanDeg, (p, v) => p.LeanDeg = v),
        F("helixTurns", 0, 3, 0.05, 0, p => p.HelixTurns, (p, v) => p.HelixTurns = v),
        F("trellisRadiusM", 0, 16, 0.5, 4, p => p.TrellisRadiusM, (p, v) => p.TrellisRadiusM = v),
        F("tipNodDeg", 0, 150, 1, 30, p => p.TipNodDeg, (p, v) => p.TipNodDeg = v),
        F("irregularity", 0, 1, 0.05, 0.6, p => p.Irregularity, (p, v) => p.Irregularity = v),

        F("baseRadiusM", 0.5, 8, 0.1, 2.8, p => p.BaseRadiusM, (p, v) => p.BaseRadiusM = v),
        F("tipRadiusM", 0.3, 4, 0.1, 1, p => p.TipRadiusM, (p, v) => p.TipRadiusM = v),
        F("shellVoxels", 1, 6, 1, 2, p => p.ShellVoxels, (p, v) => p.ShellVoxels = v, integer: true),
        F("knuckleBulge", 0, 1, 0.05, 0.4, p => p.KnuckleBulge, (p, v) => p.KnuckleBulge = v),
        F("knuckleWidth", 1, 12, 1, 3, p => p.KnuckleWidth, (p, v) => p.KnuckleWidth = v, integer: true),

        F("armsPerRing", 0, 4, 1, 4, p => p.ArmsPerRing, (p, v) => p.ArmsPerRing = v, integer: true),
        F("armLengthM", 2, 24, 0.5, 18, p => p.ArmLengthM, (p, v) => p.ArmLengthM = v),
        F("armRiseDeg", -30, 70, 1, 40, p => p.ArmRiseDeg, (p, v) => p.ArmRiseDeg = v),
        F("armSag", 0, 1.5, 0.05, 1.4, p => p.ArmSag, (p, v) => p.ArmSag = v),
        F("armThickness", 0.15, 0.8, 0.05, 0.4, p => p.ArmThickness, (p, v) => p.ArmThickness = v),
        F("armTaper", 0, 0.8, 0.05, 0, p => p.ArmTaper, (p, v) => p.ArmTaper = v),
        B("armTipLeaf", true, p => p.ArmTipLeaf, (p, v) => p.ArmTipLeaf = v),

        F("podsPerRing", 1, 30, 1, 16, p => p.PodsPerRing, (p, v) => p.PodsPerRing = v, integer: true),
        F("podTwistDeg", 0, 180, 1, 40, p => p.PodTwistDeg, (p, v) => p.PodTwistDeg = v),
        F("pedicelM", 0, 4, 0.25, 2, p => p.PedicelM, (p, v) => p.PedicelM = v),

        F("leavesPerRing", 0, 2, 1, 0, p => p.LeavesPerRing, (p, v) => p.LeavesPerRing = v, integer: true),
        F("leavesPerArm", 0, 6, 1, 0, p => p.LeavesPerArm, (p, v) => p.LeavesPerArm = v, integer: true),
        F("leafSizeM", 1, 24, 0.5, 5, p => p.LeafSizeM, (p, v) => p.LeafSizeM = v),
        F("leafTiltDeg", 0, 80, 1, 28, p => p.LeafTiltDeg, (p, v) => p.LeafTiltDeg = v),
        F("leafDroopDeg", 0, 80, 1, 34, p => p.LeafDroopDeg, (p, v) => p.LeafDroopDeg = v),

        B("tendrils", true, p => p.Tendrils, (p, v) => p.Tendrils = v),
        F("tendrilCount", 0, 40, 1, 14, p => p.TendrilCount, (p, v) => p.TendrilCount = v, integer: true),
        F("tendrilLengthM", 1, 16, 0.5, 6, p => p.TendrilLengthM, (p, v) => p.TendrilLengthM = v),
        F("tendrilCurl", 0, 6, 0.1, 2.5, p => p.TendrilCurl, (p, v) => p.TendrilCurl = v),
    };

    /// <summary>What a kind rolls: everything but the seed and the voxel size.</summary>
    public static readonly IReadOnlyList<Field> RangeFields =
        Array.FindAll((Field[])Fields, f => !f.Global && f.Key != "seed");

    public static Field FieldOf(string key)
    {
        foreach (Field f in Fields)
        {
            if (f.Key == key)
                return f;
        }
        return null;
    }

    /// <summary>Every value inside its field, integers whole.</summary>
    public VineParams Clamped()
    {
        var clamped = Copy();
        foreach (Field f in Fields)
        {
            if (f.Bool)
                continue;
            double n = f.Get(this);
            if (!double.IsFinite(n))
                n = f.Fallback;
            if (f.Integer)
                n = VineMath.Round(n);
            if (n < f.Min)
                n = f.Min;
            if (n > f.Max)
                n = f.Max;
            f.Set(clamped, n);
        }
        return clamped;
    }
}

/// <summary>
/// The generator's maths. The trigonometry is fdlibm's, not .NET's, which
/// differs in the last bit between runtimes and machines: the same seed grows
/// the same vine everywhere.
/// </summary>
public static class VineMath
{
    /// <summary>Halves go up: 2.5 to 3, -2.5 to -2.</summary>
    public static double Round(double x) => Math.Floor(x + 0.5);

    public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

    public static double Hypot(double a, double b, double c) => Math.Sqrt(a * a + b * b + c * c);

    public static double Sin(double x) => Fdlibm.Sin(x);
    public static double Cos(double x) => Fdlibm.Cos(x);
    public static double Tan(double x) => Fdlibm.Tan(x);
    public static double Atan2(double y, double x) => Fdlibm.Atan2(y, x);
    public static double Exp(double x) => Fdlibm.Exp(x);
}
