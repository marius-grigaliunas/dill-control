using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// Whether a grown vine may be used: only valid vines are baked or placed.
/// Pure and deterministic, so a seed's re-rolls (<see cref="VineGrower"/>)
/// land on the same vine on every machine. Nothing here runs Jolt; the
/// settle test in <c>vinecheck.gd</c> does, and only flags a kind's ranges.
///
/// Shape: every voxel joined to the ground, nothing but the stalk near the
/// ground, every pod with a pickle's drop and room, inside MagicaVoxel's
/// axes and the body limit. Realism: the stalk tapers, arms shorten and thin
/// upward, the lean is bounded, and the spine (the <see cref="Rod"/> its
/// <see cref="PlantFlex"/> will be) carries its weight off the stalk without
/// kinking or sagging. Leaves are judged by their placements once they are
/// meshes (step 4 of docs/handoffs/vine-tool.md); until then, only by height.
/// </summary>
public static class VineValidator
{
    public sealed class Verdict
    {
        public bool Ok => Reasons.Count == 0;
        /// <summary>Short codes, one per failed check: what the rejection report counts.</summary>
        public readonly List<string> Reasons = new();
        /// <summary>One line per failed check, in words.</summary>
        public readonly List<string> Details = new();

        public double LeanDeg, WideningShare, ArmLengthGrowth, ArmThicknessGrowth, KinkDeg, SagM;
        public int KinkJoint = -1;

        public void Fail(string reason, string detail)
        {
            Reasons.Add(reason);
            Details.Add(detail);
        }
    }

    /// <summary>The spine as <see cref="PlantFlex"/> builds it, read from the
    /// bake's flex stats by name (in the editor they are a placeholder).</summary>
    public struct Spine
    {
        public int Segments;
        public float PeriodSeconds, Damping, Taper, Weakening, MaxLeanDegrees, MassKg, Gravity;

        public static Spine Of(Resource flex) => new()
        {
            Segments = Read(flex, "Segments", 12),
            PeriodSeconds = Read(flex, "PeriodSeconds", 6.0f),
            Damping = Read(flex, "Damping", 0.12f),
            Taper = Read(flex, "Taper", 1.0f),
            Weakening = Read(flex, "Weakening", 2.0f),
            MaxLeanDegrees = Read(flex, "MaxLeanDegrees", 12.0f),
            MassKg = Read(flex, "MassKg", 6000.0f),
            Gravity = 9.8f,
        };

        private static T Read<[MustBeVariant] T>(Resource flex, string name, T fallback)
        {
            if (flex == null)
                return fallback;
            Variant v = flex.Get(name);
            return v.VariantType == Variant.Type.Nil ? fallback : v.As<T>();
        }
    }

    /// <summary>Judges a grown vine. <paramref name="full"/> measures everything;
    /// otherwise the spine, the dearest check, is skipped once anything else has failed.</summary>
    public static Verdict Check(VineGenerator.Result vine, VineBakeStats stats, bool full = true, Spine? spine = null)
    {
        var verdict = new Verdict();
        if (!vine.Ok || vine.Voxels.Count == 0)
        {
            verdict.Fail("failed", vine.Error ?? "Nothing grew.");
            return verdict;
        }
        double vs = vine.VoxelSize;
        VineGenerator.Counts c = vine.Counts;

        // Shape.
        if (c.Stranded > 0)
            verdict.Fail("stranded", $"{c.Stranded} voxels do not reach the ground face to face.");
        if (vine.BoundsX > stats.AxisLimit || vine.BoundsY > stats.AxisLimit || vine.BoundsZ > stats.AxisLimit)
            verdict.Fail("axis", $"{vine.BoundsX}x{vine.BoundsY}x{vine.BoundsZ} voxels: over {stats.AxisLimit} on an axis.");
        if (c.Bodies > stats.BodiesMax)
            verdict.Fail("bodies", $"{c.Bodies} collision bodies at rest, over {stats.BodiesMax}.");
        // The generator keeps only pods a pickle can hang from; too many left
        // out says the shape crowds its own fruit.
        int grownPods = c.Markers + c.PodsLeftOut;
        double lost = grownPods > 0 ? c.PodsLeftOut / (double)grownPods : 0;
        if (lost > stats.MaxPodsLeftOut + 1e-9)
            verdict.Fail("pods-left-out", F($"{c.PodsLeftOut} of {grownPods} pods left out ({c.PodsBlocked} blocked, {c.PodsGrounded} too low, {c.PodsCrowded} crowded), over {stats.MaxPodsLeftOut:P0}."));
        if (c.Markers == 0)
            verdict.Fail("no-pods", "No pod a pickle can hang from.");
        if (c.Markers > 1 && c.PodSpacingM < stats.PodSpacingM - 1e-6)
            verdict.Fail("pod-spacing", F($"Two pods {c.PodSpacingM:0.00} m apart: hanging pickles overlap under {stats.PodSpacingM:0.00} m."));
        // Leaves: kept clear of the stalk, the arms, the stems and each other
        // by the generator, which leaves out any that are not; judged here on
        // what is left. Too many left out says the kind crowds its leaves.
        int lowLeaf = 0, tilted = 0;
        double level = Math.Cos(stats.MaxLeafTiltDeg * Math.PI / 180);
        foreach (VineGenerator.LeafPlace leaf in vine.Leaves)
        {
            double r = leaf.SizeM * 0.5;
            double lowest = Math.Min(leaf.AZ * vs, leaf.CZ * vs - r * Math.Sqrt(Math.Max(0, 1 - leaf.NZ * leaf.NZ)));
            if (lowest < stats.GroundClearanceM)
                lowLeaf++;
            if (leaf.NZ < level)
                tilted++;
        }
        if (lowLeaf > 0)
            verdict.Fail("leaf-low", F($"{lowLeaf} leaves within {stats.GroundClearanceM:0.#} m of the ground."));
        if (tilted > 0)
            verdict.Fail("leaf-tilt", F($"{tilted} leaves tilted more than {stats.MaxLeafTiltDeg:0.#} deg from level: ramps, not platforms."));
        int grownLeaves = c.Leaves + c.LeavesLeftOut;
        double leavesLost = grownLeaves > 0 ? c.LeavesLeftOut / (double)grownLeaves : 0;
        if (leavesLost > stats.MaxLeavesLeftOut + 1e-9)
            verdict.Fail("leaves-left-out", F($"{c.LeavesLeftOut} of {grownLeaves} leaves left out for want of room, over {stats.MaxLeavesLeftOut:P0}."));
        double lowestArm = double.MaxValue;
        foreach (VineGenerator.ArmInfo arm in vine.Arms)
            lowestArm = Math.Min(lowestArm, arm.LowestM);
        if (lowestArm < stats.GroundClearanceM)
            verdict.Fail("arm-low", F($"An arm comes {lowestArm:0.0} m from the ground, under {stats.GroundClearanceM:0.#} m."));

        // Realism: the stalk tapers.
        for (int i = 1; i < vine.Rings.Count; i++)
        {
            double was = vine.Rings[i - 1].Radius;
            verdict.WideningShare = Math.Max(verdict.WideningShare, was > 0 ? vine.Rings[i].Radius / was - 1 : 0);
        }
        if (verdict.WideningShare > stats.StalkWidening + 1e-6)
            verdict.Fail("taper", F($"A knuckle is {verdict.WideningShare:P0} wider than the one below it."));

        // Arms shorten and thin upward, on the line through them all.
        verdict.ArmLengthGrowth = Growth(vine.Arms, a => a.LengthM);
        verdict.ArmThicknessGrowth = Growth(vine.Arms, a => a.RootRadiusM);
        if (verdict.ArmLengthGrowth > stats.ArmGrowth + 1e-6)
            verdict.Fail("arm-growth", F($"Arms get {verdict.ArmLengthGrowth:P0} longer toward the top."));
        if (verdict.ArmThicknessGrowth > stats.ArmGrowth + 1e-6)
            verdict.Fail("arm-thickening", F($"Arms get {verdict.ArmThicknessGrowth:P0} thicker toward the top."));

        // The stalk, followed up from the foot; its lean, and the spine's load.
        Layers layers = Layers.Of(vine);
        double tipX = layers.StalkX[layers.Count - 1] - vine.FootX, tipY = layers.StalkY[layers.Count - 1] - vine.FootY;
        verdict.LeanDeg = Math.Atan2(Math.Sqrt(tipX * tipX + tipY * tipY), Math.Max(1, layers.Count - 1)) * 180 / Math.PI;
        if (verdict.LeanDeg > stats.MaxLeanDeg + 1e-6)
            verdict.Fail("lean", F($"The tip leans {verdict.LeanDeg:0.0} deg from the foot, over {stats.MaxLeanDeg:0.#}."));
        if (!full && !verdict.Ok)
            return verdict;
        Load(vine, layers, spine ?? Spine.Of(stats.Flex), verdict);
        if (double.IsNaN(verdict.KinkDeg) || double.IsNaN(verdict.SagM))
            verdict.Fail("spine", "Its spine would not build: a joint with nothing across it.");
        else
        {
            if (verdict.KinkDeg > stats.MaxKinkDeg + 1e-6)
                verdict.Fail("kink", F($"Joint {verdict.KinkJoint} would bend {verdict.KinkDeg:0.0} deg under the weight it carries off the stalk."));
            if (verdict.SagM > stats.MaxSagM + 1e-6)
                verdict.Fail("sag", F($"Its tip would sag {verdict.SagM:0.0} m under its own weight."));
        }
        return verdict;
    }

    /// <summary>How much bigger the top's arms are than the bottom's, on the
    /// least-squares line through every arm by height: 0.1 is 10 % bigger.</summary>
    private static double Growth(List<VineGenerator.ArmInfo> arms, Func<VineGenerator.ArmInfo, double> size)
    {
        if (arms.Count < 2)
            return 0;
        double mh = 0, ms = 0;
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (VineGenerator.ArmInfo a in arms)
        {
            mh += a.HeightM;
            ms += size(a);
            lo = Math.Min(lo, a.HeightM);
            hi = Math.Max(hi, a.HeightM);
        }
        mh /= arms.Count;
        ms /= arms.Count;
        if (hi - lo < 1e-6)
            return 0;
        double cov = 0, var = 0;
        foreach (VineGenerator.ArmInfo a in arms)
        {
            cov += (a.HeightM - mh) * (size(a) - ms);
            var += (a.HeightM - mh) * (a.HeightM - mh);
        }
        double slope = cov / var;
        double bottom = ms + slope * (lo - mh), top = ms + slope * (hi - mh);
        return bottom > 1e-6 ? top / bottom - 1 : 0;
    }

    /// <summary>A vine's voxels by layer: how many, their middle, and the
    /// stalk's middle followed up from the foot (an arm on one side does not
    /// pull it off the stalk).</summary>
    private sealed class Layers
    {
        public int Count;
        public int[] Voxels;
        public double[] SumX, SumY, StalkX, StalkY;

        public static Layers Of(VineGenerator.Result vine)
        {
            int n = vine.BoundsZ;
            var l = new Layers
            {
                Count = n, Voxels = new int[n], SumX = new double[n], SumY = new double[n],
                StalkX = new double[n], StalkY = new double[n],
            };
            // Voxels come sorted by layer.
            var start = new int[n + 1];
            foreach (VineGenerator.Voxel v in vine.Voxels)
            {
                l.Voxels[v.Z]++;
                l.SumX[v.Z] += v.X + 0.5;
                l.SumY[v.Z] += v.Y + 0.5;
            }
            for (int z = 0; z < n; z++)
                start[z + 1] = start[z] + l.Voxels[z];

            double cx = vine.FootX, cy = vine.FootY, reach = double.MaxValue, footReach = double.MaxValue;
            double edge = 2.0 / vine.VoxelSize;
            for (int z = 0; z < n; z++)
            {
                double sx = 0, sy = 0;
                int count = 0;
                for (int i = start[z]; i < start[z + 1]; i++)
                {
                    VineGenerator.Voxel v = vine.Voxels[i];
                    double dx = v.X + 0.5 - cx, dy = v.Y + 0.5 - cy;
                    if (reach < double.MaxValue && dx * dx + dy * dy > reach * reach)
                        continue;
                    sx += v.X + 0.5;
                    sy += v.Y + 0.5;
                    count++;
                }
                if (count > 0)
                {
                    cx = sx / count;
                    cy = sy / count;
                    double r = Math.Sqrt(count / Math.PI);
                    if (footReach == double.MaxValue)
                        footReach = r * 1.5 + edge;
                    reach = Math.Min(r + edge, footReach);
                }
                l.StalkX[z] = cx;
                l.StalkY[z] = cy;
            }
            return l;
        }
    }

    /// <summary>
    /// The spine as its plant will build it, and how much each joint would
    /// bend under the weight above it that hangs off the stalk there (its
    /// moment over the joint's stiffness), summed into a sag at the tip. In
    /// the game the built pose is the rest pose; this is what says a real
    /// plant of that shape could hold it.
    /// </summary>
    private static void Load(VineGenerator.Result vine, Layers layers, Spine spine, Verdict verdict)
    {
        float vs = (float)vine.VoxelSize;
        int segments = Math.Clamp(spine.Segments, 2, Rod.MaxSegments);
        float top = Math.Max(layers.Count * vs, 1.0f);
        var rod = new Rod(segments, top / segments);
        int total = vine.Voxels.Count;
        float massPerVoxel = spine.MassKg / Math.Max(1, total);
        var mass = new float[segments];
        var section = new float[segments];
        RodProfile.From(layers.Voxels, layers.Voxels, vs, segments, rod.SegmentLength, massPerVoxel, mass, section);
        if (section[0] <= 0)
        {
            verdict.KinkDeg = double.NaN;
            return;
        }
        rod.Build(mass, section, spine.PeriodSeconds, spine.Damping, spine.Taper, spine.Weakening,
            Mathf.DegToRad(spine.MaxLeanDegrees), spine.Gravity);

        double sag = 0, worst = 0;
        for (int j = 0; j < segments; j++)
        {
            float k = rod.Stiffness(j);
            int z = Math.Clamp((int)Math.Round(j * rod.SegmentLength / vs), 0, layers.Count - 1);
            double jx = layers.StalkX[z], jy = layers.StalkY[z];
            double mx = 0, my = 0;
            for (int zz = z; zz < layers.Count; zz++)
            {
                mx += layers.SumX[zz] - layers.Voxels[zz] * jx;
                my += layers.SumY[zz] - layers.Voxels[zz] * jy;
            }
            double moment = Math.Sqrt(mx * mx + my * my) * vs * massPerVoxel * spine.Gravity;
            double bend = k > 1e-6f ? moment / k : double.PositiveInfinity;
            if (bend > worst)
            {
                worst = bend;
                verdict.KinkJoint = j;
            }
            sag += bend * (top - j * rod.SegmentLength);
        }
        verdict.KinkDeg = worst * 180 / Math.PI;
        verdict.SagM = sag;
    }

    private static string F(FormattableString s) => FormattableString.Invariant(s);
}
