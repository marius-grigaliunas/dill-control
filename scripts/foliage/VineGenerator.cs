using System;
using System.Collections.Generic;
using System.Globalization;
using DillControl.Core;

namespace DillControl.Foliage;

/// <summary>
/// A giant pickle vine as voxels, in MagicaVoxel space: X and Y across, Z up.
/// The same settings grow the same voxels on every machine: rolls go through
/// <see cref="SeededRandom"/> streams and the trigonometry is fdlibm's
/// (<c>scripts/debug/vinecheck.gd</c> holds it to its recorded golden vines).
///
/// The whole shape is one object: Godot turns each MagicaVoxel object into
/// its own destructible part, and a static part is held by the bottom layer
/// of its own grid. Pods are markers, not voxels: the fruit is instanced in
/// the game. Pure data in and out, nothing of Godot's, so it runs anywhere.
/// See docs/decisions/0029, 0032, 0033.
/// </summary>
public static class VineGenerator
{
    public const int Shell = 1, Knuckle = 2, Core = 3, Leaf = 4, Petiole = 5, Tendril = 6;

    // How far a bridge reaches through empty cells to join a loose part.
    public const int BridgeReach = 24;

    private const double Tau = Math.PI * 2;
    private const int Off = 4096;
    private const long Span = 8192;

    /// <summary>What a vine is checked against: the defaults are the measured
    /// ones, a <see cref="VineBakeStats"/> overrides them.</summary>
    public sealed class Limits
    {
        // prp_tree_tall_a, the vine's budget to be judged against (docs/decisions/0024).
        public int TreeVoxels = 30352;
        // XYZI stores each coordinate in a byte.
        public int AxisLimit = 256;
        // A hanging pickle needs this much clear air below its pod (AgentKindStats.Height).
        public double PickleHeightM = 1.8;
        // Closer than this and two hanging pickles overlap.
        public double PodSpacingM = 0.9;
        // VoxelDestructionStats.SectionEdge of the vine: a body per occupied section.
        public int SectionEdge = 64;
    }

    public struct Voxel
    {
        public int X, Y, Z, C;
    }

    public struct Marker
    {
        public string Name;
        public int X, Y, Z;
    }

    public sealed class Ring
    {
        public int Index;
        public double Arc, Radius, MidRadius, Angle0;
        internal Sample Sample;
    }

    public sealed class Counts
    {
        public int Shell, Knuckle, Core, Leaf, Tendril, Bridged, Stranded, Total, Markers, PodsBlocked, PodsGrounded;
        public double PodSpacingM;
        // Bodies at the vine's section edge.
        public int Bodies;
    }

    public sealed class Result
    {
        public bool Ok;
        public string Error;
        public List<string> Warnings = new();
        public VineParams Params;
        public double VoxelSize;
        public List<Voxel> Voxels = new();
        public List<Marker> Markers = new();
        public List<Ring> Rings = new();
        public Counts Counts = new();
        public int BoundsX, BoundsY, BoundsZ;
        public double FootX, FootY;
    }

    internal struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    private static V3 Add(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static V3 Sub(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static V3 Scale(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    private static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static V3 Cross(V3 a, V3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Len(V3 a) => VineMath.Hypot(a.X, a.Y, a.Z);

    private static V3 Norm(V3 a)
    {
        double l = Len(a);
        return l < 1e-8 ? new V3(0, 0, 1) : Scale(a, 1 / l);
    }

    private static V3 RotateAround(V3 v, V3 axis, double ang)
    {
        double c = VineMath.Cos(ang);
        double s = VineMath.Sin(ang);
        double k = Dot(axis, v);
        V3 cr = Cross(axis, v);
        return new V3(
            v.X * c + cr.X * s + axis.X * k * (1 - c),
            v.Y * c + cr.Y * s + axis.Y * k * (1 - c),
            v.Z * c + cr.Z * s + axis.Z * k * (1 - c));
    }

    // The wobble comes in over at least three times its own size, so the foot
    // leans into it instead of kinking sideways.
    private static double FadeIn(double t, double span)
    {
        double f = Math.Min(1, t / span);
        return f * f * (3 - 2 * f);
    }

    private static long KeyOf(int x, int y, int z) => (x + Off) + (y + Off) * Span + (z + Off) * Span * Span;

    internal sealed class Sample
    {
        public double X, Y, Z, Arc, Radius, U, H;
        public V3 T, N, B;
        public V3 P => new(X, Y, Z);
    }

    private sealed class Samples
    {
        public List<Sample> List;
        public double Total;
        public List<Ring> Rings;
    }

    private sealed class Cell
    {
        public int X, Y, Z, C;
        public double D2, Radius, Arc;
    }

    /// <summary>A map that keeps its insertion order, as a JavaScript Map
    /// does: the sweep's ties and the bridges' order depend on it.</summary>
    private sealed class Cells
    {
        private readonly Dictionary<long, int> _index = new();
        public readonly List<long> Keys = new();
        public readonly List<Cell> Values = new();
        public bool Overflow;

        public int Count => Values.Count;
        public bool Has(long key) => _index.ContainsKey(key);
        public Cell Get(long key) => _index.TryGetValue(key, out int i) ? Values[i] : null;

        public void Set(long key, Cell cell)
        {
            if (_index.TryGetValue(key, out int i))
            {
                Values[i] = cell;
                return;
            }
            _index[key] = Values.Count;
            Keys.Add(key);
            Values.Add(cell);
        }
    }

    private sealed class Arm
    {
        public int Ring, Slot;
        public List<Sample> Samples;
        public V3 Out;
    }

    private struct Pod
    {
        public V3 End, Out;
    }

    private static Func<double, double> Noise1D(SeededRandom rng)
    {
        var table = new float[256];
        for (int i = 0; i < 256; i++)
            table[i] = (float)(rng.NextDouble() * 2 - 1);
        return x =>
        {
            double i = Math.Floor(x);
            double f = x - i;
            double u = f * f * (3 - 2 * f);
            double a = table[(int)i & 255];
            double b = table[((int)i + 1) & 255];
            return a + (b - a) * u;
        };
    }

    private static Result EmptyResult(VineParams p, string error) => new()
    {
        Ok = false,
        Error = error,
        Params = p,
        VoxelSize = p.VoxelSize,
    };

    public static Result Generate(VineParams raw, Limits limits = null)
    {
        limits ??= new Limits();
        VineParams p = raw.Clamped();
        double vs = p.VoxelSize;
        double heightV = p.HeightM / vs;
        double baseV = p.BaseRadiusM / vs;
        double tipV = p.TipRadiusM / vs;
        // Past this the fill is a multi-second scan. 256 is checked after the
        // shape exists, so a vine a little over the limit can still be looked at.
        if (heightV > 420 || baseV > 32)
        {
            return EmptyResult(p,
                "Too many voxels to build at this size. Raise voxel size or lower the height. An 80 m vine fits one model at 0.5 m per voxel; MagicaVoxel stops at 256 on each axis.");
        }

        Samples samples = BuildSamples(p, heightV, baseV, tipV);
        Cells cells = FillStalk(samples, p.ShellVoxels, p.KnuckleWidth);
        if (cells.Overflow)
            return EmptyResult(p, "The vine left the range the generator can place. Lower the wobble, lean, or helix.");
        List<Arm> arms = AddArms(cells, samples, p);
        if (cells.Overflow)
            return EmptyResult(p, "The vine left the range the generator can place. Lower the arm length, wobble, lean, or helix.");
        List<Pod> pods = HangPods(cells, samples, arms, p);
        AddLeaves(cells, samples, arms, p);
        if (p.Tendrils && p.TendrilCount > 0)
            AddTendrils(cells, samples, arms, p);

        return Pack(cells, samples, pods, p, limits);
    }

    private static Samples BuildSamples(VineParams p, double heightV, double baseV, double tipV)
    {
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:centerline");
        Func<double, double> noiseX = Noise1D(rng);
        Func<double, double> noiseY = Noise1D(rng);
        double lean = VineMath.Tan(p.LeanDeg * Math.PI / 180);
        double radius = p.HelixTurns == 0 ? 0 : p.TrellisRadiusM / p.VoxelSize;
        double wobble = p.WobbleM / p.VoxelSize;
        int steps = (int)Math.Max(16, Math.Ceiling(heightV * 4));
        double fadeSpan = Math.Min(0.6, Math.Max(0.07, wobble * 3 / heightV));
        var raw = new V3[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double z = t * heightV;
            double fade = FadeIn(t, fadeSpan);
            double theta = p.HelixTurns * Tau * t;
            double x = radius * (VineMath.Cos(theta) - 1);
            double y = radius * VineMath.Sin(theta);
            x += fade * wobble * noiseX(t * p.WobbleFrequency);
            y += fade * wobble * noiseY(t * p.WobbleFrequency + 19);
            x += z * lean;
            raw[i] = new V3(x, y, z);
        }
        Nod(raw, p);

        (List<(V3 P, double Arc)> pts, double total) = Resample(raw, 1);
        var tangents = new V3[pts.Count];
        for (int i = 0; i < pts.Count; i++)
        {
            V3 a = pts[Math.Max(0, i - 1)].P;
            V3 b = pts[Math.Min(pts.Count - 1, i + 1)].P;
            tangents[i] = Norm(Sub(b, a));
        }

        var normal = new V3(1, 0, 0);
        normal = Sub(normal, Scale(tangents[0], Dot(normal, tangents[0])));
        if (Len(normal) < 1e-6)
            normal = new V3(0, 1, 0);
        normal = Norm(normal);

        double bare = Math.Min(total * 0.8, p.BareStalkM / p.VoxelSize);
        List<Ring> rings = PlaceRings((int)p.RingCount, bare, total, SeededRandom.Stream((uint)p.Seed, "vine:rings"), p.Irregularity);
        var list = new List<Sample>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            if (i > 0)
            {
                V3 axis = Cross(tangents[i - 1], tangents[i]);
                double s = Len(axis);
                double c = Math.Max(-1, Math.Min(1, Dot(tangents[i - 1], tangents[i])));
                if (s > 1e-6)
                    normal = RotateAround(normal, Scale(axis, 1 / s), VineMath.Atan2(s, c));
                normal = Sub(normal, Scale(tangents[i], Dot(normal, tangents[i])));
                normal = Len(normal) < 1e-6 ? new V3(1, 0, 0) : Norm(normal);
            }
            V3 T = tangents[i];
            V3 N = normal;
            V3 B = Norm(Cross(T, N));
            double arc = pts[i].Arc;
            list.Add(new Sample
            {
                X = pts[i].P.X, Y = pts[i].P.Y, Z = pts[i].P.Z,
                Arc = arc,
                Radius = RadiusAt(arc, total, baseV, tipV, rings, p.KnuckleBulge, p.KnuckleWidth),
                T = T, N = N, B = B,
            });
        }

        for (int i = 0; i < rings.Count; i++)
        {
            rings[i].Sample = NearestSample(list, rings[i].Arc);
            rings[i].Radius = RadiusAt(rings[i].Arc, total, baseV, tipV, rings, p.KnuckleBulge, p.KnuckleWidth);
            double midArc = i < rings.Count - 1
                ? (rings[i].Arc + rings[i + 1].Arc) * 0.5
                : (rings.Count == 1 ? total * 0.5 : (rings[i].Arc + rings[i - 1].Arc) * 0.5);
            rings[i].MidRadius = RadiusAt(midArc, total, baseV, tipV, rings, p.KnuckleBulge, p.KnuckleWidth);
            rings[i].Angle0 = (i * p.PodTwistDeg) % 360;
        }

        return new Samples { List = list, Total = total, Rings = rings };
    }

    private static (List<(V3 P, double Arc)>, double) Resample(IReadOnlyList<V3> raw, double spacing)
    {
        var seg = new double[raw.Count - 1];
        double total = 0;
        for (int i = 1; i < raw.Count; i++)
        {
            seg[i - 1] = Len(Sub(raw[i], raw[i - 1]));
            total += seg[i - 1];
        }
        if (total < 1)
            return (new List<(V3, double)> { (raw[0], 0) }, total);
        var points = new List<(V3, double)> { (raw[0], 0) };
        double @base = 0;
        int si = 0;
        for (double target = spacing; target < total - spacing * 0.25; target += spacing)
        {
            while (si < seg.Length - 1 && @base + seg[si] < target)
            {
                @base += seg[si];
                si++;
            }
            double span = seg[si] != 0 ? seg[si] : 1;
            double t = Math.Max(0, Math.Min(1, (target - @base) / span));
            V3 a = raw[si];
            V3 b = raw[si + 1];
            points.Add((new V3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t), target));
        }
        points.Add((raw[raw.Count - 1], total));
        return (points, total);
    }

    // The top quarter bends over, a little more each step, like a shoot heavy
    // with its own growth. Each step keeps its length, so the height is arc.
    private static void Nod(V3[] raw, VineParams p)
    {
        if (p.TipNodDeg <= 0)
            return;
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:nod");
        double heading = rng.NextDouble() * Tau;
        var axis = new V3(-VineMath.Sin(heading), VineMath.Cos(heading), 0);
        double max = p.TipNodDeg * Math.PI / 180;
        const double from = 0.72;
        V3 prevOld = raw[0];
        for (int i = 1; i < raw.Length; i++)
        {
            V3 old = raw[i];
            double t = (double)i / (raw.Length - 1);
            double u = Math.Max(0, (t - from) / (1 - from));
            V3 step = Sub(old, prevOld);
            V3 bent = u > 0 ? RotateAround(step, axis, max * u * u * (3 - 2 * u)) : step;
            raw[i] = Add(raw[i - 1], bent);
            prevOld = old;
        }
    }

    // Rings share the stalk above the bare foot. Irregularity moves each by up
    // to a third of the spacing, so rings never cross and pods keep their order.
    private static List<Ring> PlaceRings(int count, double bare, double total, SeededRandom rng, double irregularity)
    {
        var rings = new List<Ring>(count);
        for (int i = 0; i < count; i++)
        {
            double jitter = (rng.NextDouble() - 0.5) * 0.66 * irregularity;
            rings.Add(new Ring { Index = i, Arc = bare + ((i + 0.5 + jitter) / count) * (total - bare) });
        }
        return rings;
    }

    private static double RadiusAt(double arc, double total, double baseV, double tipV, List<Ring> rings, double bulge, double width)
    {
        double s = total > 0 ? arc / total : 0;
        double taper = baseV + (tipV - baseV) * s;
        double extra = 0;
        foreach (Ring ring in rings)
        {
            double d = Math.Abs(arc - ring.Arc);
            if (d >= width)
                continue;
            double u = 1 - d / width;
            double smooth = u * u * (3 - 2 * u);
            double bump = taper * bulge * smooth;
            if (bump > extra)
                extra = bump;
        }
        return taper + extra;
    }

    private static Sample NearestSample(List<Sample> samples, double arc)
    {
        int lo = 0;
        int hi = samples.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (samples[mid].Arc < arc)
                lo = mid + 1;
            else
                hi = mid;
        }
        Sample b = samples[lo];
        Sample a = samples[Math.Max(0, lo - 1)];
        return Math.Abs(a.Arc - arc) <= Math.Abs(b.Arc - arc) ? a : b;
    }

    private static Cells FillStalk(Samples samples, double shell, double knuckleWidth)
    {
        List<Ring> rings = samples.Rings;
        Cells cells = Sweep(samples.List);
        if (cells.Overflow)
            return cells;

        foreach (Cell cell in cells.Values)
        {
            double dist = Math.Sqrt(cell.D2);
            double inner = cell.Radius - shell;
            if (inner >= 0.5 && dist <= inner)
                cell.C = Core;
            else if (NearRing(rings, cell.Arc, knuckleWidth * 0.5))
                // The band is the fat middle of the bulge, not its whole fade.
                cell.C = Knuckle;
            else
                cell.C = Shell;
        }
        return cells;
    }

    // Every cell within a sample's radius of the centreline, nearest sample wins.
    private static Cells Sweep(List<Sample> samples)
    {
        var cells = new Cells();
        for (int i = 0; i < samples.Count; i++)
        {
            Sample s = samples[i];
            double pad = Math.Ceiling(s.Radius) + 1;
            int x0 = (int)Math.Floor(s.X - pad);
            int x1 = (int)Math.Floor(s.X + pad);
            int y0 = (int)Math.Floor(s.Y - pad);
            int y1 = (int)Math.Floor(s.Y + pad);
            int z0 = (int)Math.Floor(s.Z - pad);
            int z1 = (int)Math.Floor(s.Z + pad);
            bool cap = i == 0 || i == samples.Count - 1;
            double r2 = s.Radius * s.Radius;
            for (int z = z0; z <= z1; z++)
            {
                if (z < 0)
                    continue;
                double dz = (z + 0.5) - s.Z;
                for (int y = y0; y <= y1; y++)
                {
                    double dy = (y + 0.5) - s.Y;
                    for (int x = x0; x <= x1; x++)
                    {
                        double dx = (x + 0.5) - s.X;
                        double along = dx * s.T.X + dy * s.T.Y + dz * s.T.Z;
                        if (!cap && along * along > 0.64)
                            continue;
                        double px = dx - along * s.T.X;
                        double py = dy - along * s.T.Y;
                        double pz = dz - along * s.T.Z;
                        double d2 = px * px + py * py + pz * pz;
                        if (d2 > r2)
                            continue;
                        if (x <= -Off || x >= Off || y <= -Off || y >= Off || z >= Off)
                        {
                            cells.Overflow = true;
                            return cells;
                        }
                        long key = KeyOf(x, y, z);
                        Cell prev = cells.Get(key);
                        if (prev != null && prev.D2 <= d2)
                            continue;
                        cells.Set(key, new Cell { X = x, Y = y, Z = z, D2 = d2, Radius = s.Radius, Arc = s.Arc });
                    }
                }
            }
        }
        return cells;
    }

    // A tube along points, with the frames and arc the sweep needs.
    private static List<Sample> TubeSamples(List<V3> raw, Func<double, double> radiusOf)
    {
        (List<(V3 P, double Arc)> pts, double total) = Resample(raw, 1);
        var list = new List<Sample>(pts.Count);
        for (int i = 0; i < pts.Count; i++)
        {
            V3 a = pts[Math.Max(0, i - 1)].P;
            V3 b = pts[Math.Min(pts.Count - 1, i + 1)].P;
            double u = total > 0 ? pts[i].Arc / total : 0;
            list.Add(new Sample
            {
                X = pts[i].P.X, Y = pts[i].P.Y, Z = pts[i].P.Z,
                Arc = pts[i].Arc,
                U = u,
                T = Norm(Sub(b, a)),
                Radius = radiusOf(u),
            });
        }
        return list;
    }

    // Arms grow out of the knuckles between the pod slots' spiral, rise, then
    // bow under their own fruit. Each is solid like the stalk, rooted at the
    // stalk's axis so it can never come loose from it.
    private static List<Arm> AddArms(Cells cells, Samples samples, VineParams p)
    {
        var arms = new List<Arm>();
        if (p.ArmsPerRing <= 0)
            return arms;
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:arms");
        double vs = p.VoxelSize;
        double twist = p.PodTwistDeg * Math.PI / 180;
        double slot = Tau / p.ArmsPerRing;
        double irr = p.Irregularity;
        List<Ring> rings = samples.Rings;
        for (int i = 0; i < rings.Count; i++)
        {
            Ring ring = rings[i];
            Sample sample = ring.Sample;
            for (int a = 0; a < p.ArmsPerRing; a++)
            {
                double angle = i * twist + a * slot + (rng.NextDouble() - 0.5) * 0.9 * irr;
                double lengthV = (p.ArmLengthM / vs) * (1 + (rng.NextDouble() - 0.5) * 0.7 * irr);
                double rise = (p.ArmRiseDeg + (rng.NextDouble() - 0.5) * 30 * irr) * Math.PI / 180;
                double sag = p.ArmSag * (1 + (rng.NextDouble() - 0.5) * 0.6 * irr);
                double swing = (rng.NextDouble() - 0.5) * 0.6 * irr;
                V3 outward = DirectionAt(sample, angle);
                outward = Norm(new V3(outward.X, outward.Y, 0));
                var across = new V3(-outward.Y, outward.X, 0);
                V3 root = sample.P;
                int steps = (int)Math.Max(8, Math.Ceiling(lengthV * 2));
                var raw = new List<V3>(steps + 1);
                for (int s = 0; s <= steps; s++)
                {
                    double u = (double)s / steps;
                    double reach = sample.Radius * 0.5 + lengthV * VineMath.Cos(rise) * u * (1 - 0.18 * u);
                    double lift = lengthV * (VineMath.Sin(rise) * u - sag * u * u * 0.6);
                    raw.Add(Add(root, Add(Scale(outward, reach), Add(Scale(across, swing * lengthV * u * u), new V3(0, 0, lift)))));
                }
                double r0 = Math.Max(1, ring.MidRadius * p.ArmThickness);
                List<Sample> arm = TubeSamples(raw, u => Math.Max(0.75, r0 * (1 - 0.55 * u)));
                Cells own = Sweep(arm);
                if (own.Overflow)
                {
                    cells.Overflow = true;
                    return arms;
                }
                for (int k = 0; k < own.Count; k++)
                {
                    long key = own.Keys[k];
                    if (cells.Has(key))
                        continue;
                    Cell cell = own.Values[k];
                    double inner = cell.Radius - p.ShellVoxels;
                    cell.C = inner >= 0.5 && Math.Sqrt(cell.D2) <= inner ? Core : Shell;
                    cells.Set(key, cell);
                }
                // Pods are spaced by how far out they hang, not along the arm:
                // where the tip turns down, even steps along it would stack them.
                double reachSum = 0;
                arm[0].H = 0;
                for (int k = 1; k < arm.Count; k++)
                {
                    reachSum += VineMath.Hypot(arm[k].X - arm[k - 1].X, arm[k].Y - arm[k - 1].Y);
                    arm[k].H = reachSum;
                }
                for (int k = 0; k < arm.Count; k++)
                    arm[k].H = reachSum > 0 ? arm[k].H / reachSum : 0;
                arms.Add(new Arm { Ring = i, Slot = a, Samples = arm, Out = outward });
            }
        }
        return arms;
    }

    private static Sample SampleAtH(List<Sample> tube, double value)
    {
        Sample best = tube[0];
        for (int i = 1; i < tube.Count; i++)
        {
            if (Math.Abs(tube[i].H - value) < Math.Abs(best.H - value))
                best = tube[i];
        }
        return best;
    }

    private static Sample SampleAtU(List<Sample> tube, double value)
    {
        Sample best = tube[0];
        for (int i = 1; i < tube.Count; i++)
        {
            if (Math.Abs(tube[i].U - value) < Math.Abs(best.U - value))
                best = tube[i];
        }
        return best;
    }

    // Where each pickle hangs from: the pod is the top of the fruit, at the
    // bottom of its stem. Stems are drawn now; the pods are found once every
    // other voxel is down, so a leaf cannot sit on one.
    private static List<Pod> HangPods(Cells cells, Samples samples, List<Arm> arms, VineParams p)
    {
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:pods");
        List<Ring> rings = samples.Rings;
        double twist = p.PodTwistDeg * Math.PI / 180;
        double slot = Tau / p.PodsPerRing;
        double stem = p.PedicelM / p.VoxelSize;
        int podsPerRing = (int)p.PodsPerRing;
        var pods = new List<Pod>();
        for (int i = 0; i < rings.Count; i++)
        {
            Sample sample = rings[i].Sample;
            if (arms.Count > 0)
            {
                List<Arm> mine = arms.FindAll(arm => arm.Ring == i);
                for (int a = 0; a < mine.Count; a++)
                {
                    int share = podsPerRing / mine.Count + (a < podsPerRing % mine.Count ? 1 : 0);
                    for (int j = 0; j < share; j++)
                    {
                        double gap = share == 1 ? 0.3 : 0.6 / (share - 1);
                        double u = share == 1 ? 0.75 : 0.38 + gap * j;
                        Sample at = SampleAtH(mine[a].Samples, Math.Min(1, u + (rng.NextDouble() - 0.5) * gap * 0.5 * p.Irregularity));
                        var top = new V3(at.X, at.Y, at.Z - at.Radius);
                        var end = new V3(top.X, top.Y, top.Z - stem);
                        if (stem > 0)
                            PlotLine(cells, at.P, end, Petiole);
                        pods.Add(new Pod { End = end, Out = mine[a].Out });
                    }
                }
            }
            else
            {
                for (int s = 0; s < podsPerRing; s++)
                {
                    double angle = i * twist + s * slot + (rng.NextDouble() - 0.5) * 0.5 * p.Irregularity;
                    V3 dir = DirectionAt(sample, angle);
                    V3 surface = Add(sample.P, Scale(dir, sample.Radius + 1));
                    V3 end = Add(surface, new V3(dir.X * Math.Min(2, stem * 0.3), dir.Y * Math.Min(2, stem * 0.3), -stem));
                    if (stem > 0)
                        PlotLine(cells, Add(sample.P, Scale(dir, sample.Radius - 1)), end, Petiole);
                    pods.Add(new Pod { End = end, Out = dir });
                }
            }
        }
        return pods;
    }

    private static bool NearRing(List<Ring> rings, double arc, double width)
    {
        foreach (Ring ring in rings)
        {
            if (Math.Abs(arc - ring.Arc) <= width)
                return true;
        }
        return false;
    }

    private static readonly (double A, double Amp, double W)[] Lobes =
    {
        (0, 1, 0.4), (0.62, 0.78, 0.32), (-0.62, 0.78, 0.32), (1.22, 0.5, 0.28), (-1.22, 0.5, 0.28),
    };

    // Five lobes, longest along the midrib, with a notch where the petiole joins.
    private static double LobeRadius(double theta)
    {
        double r = 0.14;
        foreach ((double a, double amp, double w) in Lobes)
        {
            double d = theta - a;
            if (d > Math.PI)
                d -= Tau;
            if (d < -Math.PI)
                d += Tau;
            double g = VineMath.Exp(-(d * d) / (2 * w * w));
            double v = amp * g;
            if (v > r)
                r = v;
        }
        double back = Math.Max(0, -VineMath.Cos(theta));
        return r * (1 - 0.5 * back * back);
    }

    private static void Paint(Cells cells, int x, int y, int z, int colour, bool overwrite)
    {
        if (z < 0 || x <= -Off || x >= Off || y <= -Off || y >= Off || z >= Off)
            return;
        long key = KeyOf(x, y, z);
        Cell prev = cells.Get(key);
        if (prev == null)
        {
            cells.Set(key, new Cell { X = x, Y = y, Z = z, C = colour });
            return;
        }
        if (overwrite && (prev.C == Petiole || prev.C == Tendril))
            prev.C = colour;
    }

    private static void PlotLine(Cells cells, V3 a, V3 b, int colour)
    {
        int steps = (int)Math.Max(1, Math.Ceiling(VineMath.Hypot(b.X - a.X, b.Y - a.Y, b.Z - a.Z) * 2));
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            Paint(cells,
                (int)Math.Floor(a.X + (b.X - a.X) * t),
                (int)Math.Floor(a.Y + (b.Y - a.Y) * t),
                (int)Math.Floor(a.Z + (b.Z - a.Z) * t),
                colour, false);
        }
    }

    private static V3 DirectionAt(Sample sample, double angle) =>
        Norm(Add(Scale(sample.N, VineMath.Cos(angle)), Scale(sample.B, VineMath.Sin(angle))));

    private static void AddLeaves(Cells cells, Samples samples, List<Arm> arms, VineParams p)
    {
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:leaves");
        double lengthV = p.LeafSizeM / p.VoxelSize;
        double droop = p.LeafDroopDeg * Math.PI / 180;
        double tilt = p.LeafTiltDeg * Math.PI / 180;
        // Midway between whatever leaves the stalk: arms when there are any, else pods.
        double slot = Tau / (arms.Count > 0 ? p.ArmsPerRing : p.PodsPerRing);
        double twist = p.PodTwistDeg * Math.PI / 180;
        List<Ring> rings = samples.Rings;
        for (int i = 0; i < rings.Count && p.LeavesPerRing > 0; i++)
        {
            Sample sample = rings[i].Sample;
            for (int leaf = 0; leaf < p.LeavesPerRing; leaf++)
            {
                double angle = i * twist + (leaf + 0.5) * slot + (rng.NextDouble() - 0.5) * 0.3;
                V3 outward = DirectionAt(sample, angle);
                V3 origin = Add(sample.P, Scale(outward, sample.Radius));
                DrawLeaf(cells, origin, outward, lengthV, droop, tilt);
            }
        }
        foreach (Arm arm in arms)
        {
            List<Sample> tube = arm.Samples;
            V3 armOut = arm.Out;
            var across = new V3(-armOut.Y, armOut.X, 0);
            // Along the arm, sides alternating, leaning out and up a little so
            // the blade sits beside the fruit, not on it.
            for (int k = 0; k < p.LeavesPerArm; k++)
            {
                int side = k % 2 == 0 ? 1 : -1;
                Sample at = SampleAtH(tube, 0.22 + 0.7 * (k + 0.5) / p.LeavesPerArm + (rng.NextDouble() - 0.5) * 0.08);
                V3 outward = Norm(Add(Add(Scale(across, side), Scale(armOut, 0.45)), new V3(0, 0, 0.25)));
                DrawLeaf(cells, Add(at.P, Scale(outward, at.Radius)), outward, lengthV * (0.75 + rng.NextDouble() * 0.25), droop, tilt);
            }
            if (!p.ArmTipLeaf)
                continue;
            Sample tip = tube[tube.Count - 1];
            V3 ahead = Norm(new V3(tip.T.X, tip.T.Y, Math.Max(tip.T.Z, -0.2)));
            DrawLeaf(cells, tip.P, ahead, lengthV * 0.7, droop * 0.6, tilt);
        }
    }

    private static void DrawLeaf(Cells cells, V3 origin, V3 outward, double lengthV, double droop, double tilt)
    {
        V3 midrib = Norm(Add(Scale(outward, VineMath.Cos(droop)), new V3(0, 0, -VineMath.Sin(droop))));
        V3 side = Cross(midrib, new V3(0, 0, 1));
        if (Len(side) < 1e-4)
            side = Cross(midrib, new V3(1, 0, 0));
        side = Norm(side);
        V3 normal = Norm(Cross(side, midrib));
        side = Norm(Add(Scale(side, VineMath.Cos(tilt)), Scale(normal, VineMath.Sin(tilt))));

        double reach = lengthV * 0.72;
        V3 center = Add(origin, Scale(midrib, lengthV * 0.28));
        PlotLine(cells, origin, center, Petiole);

        const double step = 0.7;
        for (double u = -reach; u <= reach; u += step)
        {
            for (double v = -reach; v <= reach; v += step)
            {
                double theta = VineMath.Atan2(v, u);
                double rho = VineMath.Hypot(u, v);
                if (rho > LobeRadius(theta) * reach)
                    continue;
                V3 point = Add(center, Add(Scale(midrib, u), Scale(side, v)));
                Paint(cells, (int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z), Leaf, true);
            }
        }
    }

    // Tendrils curl off the stalk, or off the arms' last third when there are arms.
    private static void AddTendrils(Cells cells, Samples samples, List<Arm> arms, VineParams p)
    {
        SeededRandom rng = SeededRandom.Stream((uint)p.Seed, "vine:tendrils");
        List<Ring> rings = samples.Rings;
        double slot = Tau / p.PodsPerRing;
        double twist = p.PodTwistDeg * Math.PI / 180;
        double lengthV = p.TendrilLengthM / p.VoxelSize;
        int usable = Math.Max(1, rings.Count - 1);
        for (int n = 0; n < p.TendrilCount; n++)
        {
            Ring ring = rings[(int)Math.Floor(rng.NextDouble() * usable)];
            Sample sample = ring.Sample;
            V3 outward = DirectionAt(sample, ring.Index * twist + (Math.Floor(rng.NextDouble() * p.PodsPerRing) + 0.5) * slot);
            if (arms.Count > 0)
            {
                Arm arm = arms[(int)Math.Floor(rng.NextDouble() * arms.Count)];
                sample = SampleAtU(arm.Samples, 0.65 + rng.NextDouble() * 0.35);
                outward = Norm(Add(arm.Out, new V3(0, 0, 0.6)));
            }
            V3 side = Norm(Cross(outward, sample.T));
            V3 start = Add(sample.P, Scale(outward, sample.Radius + 0.8));
            int steps = (int)Math.Max(4, Math.Ceiling(lengthV * 3));
            V3 prev = start;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double ang = t * p.TendrilCurl * Tau;
                double curl = 0.35 + t * 2.4;
                V3 point = Add(start, Add(
                    Scale(outward, t * lengthV * 0.55),
                    Add(Scale(side, VineMath.Cos(ang) * curl),
                        Add(Scale(sample.T, VineMath.Sin(ang) * curl), new V3(0, 0, -t * lengthV * 0.18)))));
                PlotLine(cells, prev, point, Tendril);
                prev = point;
            }
        }
    }

    public static string PodName(int index)
    {
        string text = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return "@Pod" + (text.Length < 2 ? "0" + text : text);
    }

    private static Result Pack(Cells cells, Samples samples, List<Pod> pods, VineParams p, Limits limits)
    {
        (int bridged, int stranded) = Connect(cells);

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue, maxZ = 0;
        int shell = 0, knuckle = 0, core = 0, leaf = 0, tendril = 0;
        var voxels = new List<Voxel>(cells.Count);
        foreach (Cell cell in cells.Values)
        {
            voxels.Add(new Voxel { X = cell.X, Y = cell.Y, Z = cell.Z, C = cell.C });
            minX = Math.Min(minX, cell.X);
            minY = Math.Min(minY, cell.Y);
            maxX = Math.Max(maxX, cell.X);
            maxY = Math.Max(maxY, cell.Y);
            maxZ = Math.Max(maxZ, cell.Z);
            if (cell.C == Shell) shell++;
            else if (cell.C == Knuckle) knuckle++;
            else if (cell.C == Core) core++;
            else if (cell.C == Leaf || cell.C == Petiole) leaf++;
            else if (cell.C == Tendril) tendril++;
        }
        if (voxels.Count == 0)
            return EmptyResult(p, "The vine produced no voxels.");

        List<Marker> markers = PlaceMarkers(pods, cells);
        (int blocked, int grounded, double spacingM) = HangCheck(markers, cells, p, limits);
        for (int i = 0; i < voxels.Count; i++)
        {
            Voxel v = voxels[i];
            v.X -= minX;
            v.Y -= minY;
            voxels[i] = v;
        }
        for (int i = 0; i < markers.Count; i++)
        {
            Marker m = markers[i];
            m.X -= minX;
            m.Y -= minY;
            markers[i] = m;
        }
        voxels.Sort((a, b) => a.Z != b.Z ? a.Z - b.Z : a.Y != b.Y ? a.Y - b.Y : a.X != b.X ? a.X - b.X : a.C - b.C);
        markers.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var result = new Result
        {
            Params = p,
            VoxelSize = p.VoxelSize,
            Voxels = voxels,
            Markers = markers,
            BoundsX = maxX - minX + 1,
            BoundsY = maxY - minY + 1,
            BoundsZ = maxZ + 1,
        };
        double footX = 0, footY = 0;
        int footN = 0;
        foreach (Voxel v in voxels)
        {
            if (v.Z != 0)
                continue;
            footX += v.X + 0.5;
            footY += v.Y + 0.5;
            footN++;
        }
        result.FootX = footN > 0 ? footX / footN : result.BoundsX / 2.0;
        result.FootY = footN > 0 ? footY / footN : result.BoundsY / 2.0;

        int edge = Math.Max(4, limits.SectionEdge);
        var sections = new HashSet<long>();
        foreach (Voxel v in voxels)
            sections.Add(v.X / edge + 1024L * (v.Y / edge + 1024L * (v.Z / edge)));

        int total = voxels.Count;
        result.Counts = new Counts
        {
            Shell = shell + knuckle, Knuckle = knuckle, Core = core, Leaf = leaf, Tendril = tendril,
            Bridged = bridged, Stranded = stranded, Total = total, Markers = markers.Count,
            PodsBlocked = blocked, PodsGrounded = grounded, PodSpacingM = spacingM,
            Bodies = sections.Count,
        };

        List<string> warnings = result.Warnings;
        if (total > limits.TreeVoxels)
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{total:N0} voxels. The tall tree that already spikes collision is {limits.TreeVoxels:N0}."));
        if (stranded > 0)
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{stranded} voxels float more than {BridgeReach} voxels clear of the vine. In the game they hold until the first shot anywhere on it, then fall."));
        if (core == 0)
            warnings.Add("No core. The stalk is thinner than twice the shell, so a carve will not reveal anything.");
        if (blocked + grounded > 0)
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{blocked + grounded} of {markers.Count} pods have no {limits.PickleHeightM} m drop below them ({blocked} into the vine, {grounded} into the ground). Lengthen the arms, raise their rise, or shorten the stems."));
        if (markers.Count > 1 && spacingM < limits.PodSpacingM)
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Two pods are {spacingM:F2} m apart; hanging pickles overlap under {limits.PodSpacingM} m. Fewer pods per ring, or more arms."));
        if (result.BoundsX > limits.AxisLimit || result.BoundsY > limits.AxisLimit || result.BoundsZ > limits.AxisLimit)
        {
            result.Error = string.Create(CultureInfo.InvariantCulture, $"The vine is {result.BoundsX} × {result.BoundsY} × {result.BoundsZ} voxels. MagicaVoxel stores each axis in a byte, so a model stops at {limits.AxisLimit}. Raise voxel size, or lower the height, lean, wobble, or leaves.");
        }
        result.Ok = result.Error == null;

        foreach (Ring ring in samples.Rings)
            result.Rings.Add(ring);
        return result;
    }

    private static readonly (int X, int Y, int Z)[] Neigh =
    {
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    };

    // Every voxel joined to the ground face to face. The game holds a part up
    // only by faces (an edge or a corner is not a joint), and checks the whole
    // vine at its first hit. Each part that does not reach the ground is
    // joined to the nearest part that does, along the shortest run of empty
    // cells, in its own colour.
    private static (int bridged, int stranded) Connect(Cells cells)
    {
        var held = new HashSet<long>();
        var queue = new List<Cell>();
        for (int i = 0; i < cells.Count; i++)
        {
            if (cells.Values[i].Z == 0)
            {
                held.Add(cells.Keys[i]);
                queue.Add(cells.Values[i]);
            }
        }

        List<Cell> Flood(List<Cell> from, HashSet<long> into)
        {
            var list = new List<Cell>();
            List<Cell> q = from;
            while (q.Count > 0)
            {
                var next = new List<Cell>();
                foreach (Cell c in q)
                {
                    list.Add(c);
                    foreach ((int dx, int dy, int dz) in Neigh)
                    {
                        long key = KeyOf(c.X + dx, c.Y + dy, c.Z + dz);
                        Cell n = cells.Get(key);
                        if (n != null && into.Add(key))
                            next.Add(n);
                    }
                }
                q = next;
            }
            return list;
        }
        Flood(queue, held);

        int bridged = 0;
        int stranded = 0;
        var seen = new HashSet<long>();
        // Cells added by a bridge are visited too, as a JavaScript Map's
        // iteration visits entries added during it; they are held, so skipped.
        for (int i = 0; i < cells.Count; i++)
        {
            long key = cells.Keys[i];
            Cell cell = cells.Values[i];
            if (held.Contains(key) || seen.Contains(key))
                continue;
            seen.Add(key);
            List<Cell> island = Flood(new List<Cell> { cell }, seen);
            // Out through empty cells, one ring at a time, until a held voxel
            // is beside the front: then back along the way it came.
            var from = new Dictionary<long, long>();
            var front = new List<(int X, int Y, int Z, long K)>();
            foreach (Cell c in island)
            {
                long k = KeyOf(c.X, c.Y, c.Z);
                from[k] = -1;
                front.Add((c.X, c.Y, c.Z, k));
            }
            (int X, int Y, int Z, long K)? end = null;
            for (int step = 0; step <= BridgeReach && front.Count > 0 && end == null; step++)
            {
                var next = new List<(int, int, int, long)>();
                foreach (var c in front)
                {
                    foreach ((int dx, int dy, int dz) in Neigh)
                    {
                        int x = c.X + dx, y = c.Y + dy, z = c.Z + dz;
                        if (z < 0)
                            continue;
                        long k = KeyOf(x, y, z);
                        if (held.Contains(k))
                        {
                            end = c;
                            break;
                        }
                        if (from.ContainsKey(k) || cells.Has(k))
                            continue;
                        from[k] = c.K;
                        next.Add((x, y, z, k));
                    }
                    if (end != null)
                        break;
                }
                front = next;
            }
            if (end == null)
            {
                stranded += island.Count;
                continue;
            }
            int colour = island[0].C;
            for (long k = end.Value.K; k != -1 && !cells.Has(k); k = from[k])
            {
                int z = (int)(k / (Span * Span)) - Off;
                int y = (int)((k % (Span * Span)) / Span) - Off;
                int x = (int)(k % Span) - Off;
                cells.Set(k, new Cell { X = x, Y = y, Z = z, C = colour });
                held.Add(k);
                bridged++;
            }
            foreach (Cell c in island)
                held.Add(KeyOf(c.X, c.Y, c.Z));
        }
        return (bridged, stranded);
    }

    // The first empty cell at or below the stem's end; out from the vine if the
    // fall is blocked for eight cells. A placed pod claims its cell, so two pods
    // never share one.
    private static List<Marker> PlaceMarkers(List<Pod> pods, Cells cells)
    {
        var markers = new List<Marker>(pods.Count);
        var claimed = new HashSet<long>();
        bool Free(int x, int y, int z)
        {
            long key = KeyOf(x, y, z);
            return !cells.Has(key) && !claimed.Contains(key);
        }
        for (int i = 0; i < pods.Count; i++)
        {
            Pod pod = pods[i];
            int x = (int)Math.Floor(pod.End.X);
            int y = (int)Math.Floor(pod.End.Y);
            int z = (int)Math.Max(0, Math.Floor(pod.End.Z));
            bool found = false;
            for (int step = 0; step < 8 && !found; step++)
            {
                int zz = Math.Max(0, z - step);
                if (Free(x, y, zz))
                {
                    z = zz;
                    found = true;
                }
            }
            for (int step = 1; step <= 8 && !found; step++)
            {
                int xx = (int)Math.Floor(pod.End.X + pod.Out.X * step);
                int yy = (int)Math.Floor(pod.End.Y + pod.Out.Y * step);
                if (xx > -Off && xx < Off && yy > -Off && yy < Off && Free(xx, yy, z))
                {
                    x = xx;
                    y = yy;
                    found = true;
                }
            }
            claimed.Add(KeyOf(x, y, z));
            markers.Add(new Marker { Name = PodName(i + 1), X = x, Y = y, Z = z });
        }
        return markers;
    }

    // Can a pickle hang from each pod? Its body runs PickleHeightM straight
    // down; it is blocked by a voxel or another pod in that column, or by the ground.
    private static (int blocked, int grounded, double spacingM) HangCheck(List<Marker> markers, Cells cells, VineParams p, Limits limits)
    {
        int drop = (int)Math.Ceiling(limits.PickleHeightM / p.VoxelSize);
        var pods = new HashSet<long>();
        foreach (Marker m in markers)
            pods.Add(KeyOf(m.X, m.Y, m.Z));
        int blocked = 0;
        int grounded = 0;
        foreach (Marker m in markers)
        {
            for (int z = m.Z - 1; z >= m.Z - drop; z--)
            {
                if (z < 0)
                {
                    grounded++;
                    break;
                }
                long key = KeyOf(m.X, m.Y, z);
                if (cells.Has(key) || pods.Contains(key))
                {
                    blocked++;
                    break;
                }
            }
        }
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < markers.Count; i++)
        {
            for (int j = i + 1; j < markers.Count; j++)
            {
                double d = VineMath.Hypot(markers[i].X - markers[j].X, markers[i].Y - markers[j].Y, markers[i].Z - markers[j].Z);
                if (d < nearest)
                    nearest = d;
            }
        }
        return (blocked, grounded, double.IsFinite(nearest) ? nearest * p.VoxelSize : 0);
    }
}
