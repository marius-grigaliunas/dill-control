using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DillControl.Foliage;
using DillControl.Voxel;
using Godot;

namespace DillControl.Debug;

/// <summary>
/// Checks the vine generator headless: the same seed grows the same voxels
/// and pods as when the golden vines were recorded
/// (<c>scripts/debug/vine_golden.json</c>), its vines pass the generator's
/// own checks, the .vox it writes reads back with the foot on the origin, and
/// every baked vine scene is the wrapper a vine needs. <c>vinecheck.gd</c>
/// runs it; <c>vinecheck.gd -- record</c> re-records the golden vines after a
/// change meant to move them; <c>vinecheck.gd -- dump name</c> writes one
/// golden vine's voxels for a diff.
/// </summary>
[GlobalClass]
public partial class VineCheck : Node
{
    private const string GoldenPath = "res://scripts/debug/vine_golden.json";
    /// <summary>Pool vines recorded per kind.</summary>
    private const int GoldenPool = 3;

    private StringBuilder _report;
    private bool _pass;

    private void Check(string name, bool ok, string detail = "")
    {
        if (!ok)
            _pass = false;
        _report.AppendLine($"  {(ok ? "ok  " : "FAIL")} {name}{(detail.Length > 0 ? "  " + detail : "")}");
    }

    public string Run()
    {
        _report = new StringBuilder();
        _pass = true;
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        Dictionary<string, VineKind> kinds = LoadKinds();
        Check("bake stats and four kinds load", stats != null && kinds.Count == 4,
            $"kinds: {string.Join(", ", kinds.Keys)}");
        if (stats == null)
            return "FAIL\n" + _report;

        _report.AppendLine("Against the golden vines:");
        Golden(kinds, stats);
        _report.AppendLine("The generator's checks:");
        Prototype(kinds, stats);
        _report.AppendLine("Written and read back:");
        Written(stats);
        _report.AppendLine("Baked scenes:");
        BakedScenes(stats);
        _report.AppendLine($"Leaves on their vines, {LeafPool} pool vines a kind grown in memory:");
        LeavesInMemory(kinds, stats);
        _report.AppendLine("Valid vines, 200 seeds a kind:");
        string rejections = Rejections(200);
        Check("every seed of every kind grows a valid vine", rejections.StartsWith("PASS"));
        _report.Append(rejections.Substring(rejections.IndexOf('\n') + 1));
        return (_pass ? "PASS\n" : "FAIL\n") + _report;
    }

    public static Dictionary<string, VineKind> LoadKinds()
    {
        var kinds = new Dictionary<string, VineKind>();
        foreach (string file in DirAccess.GetFilesAt(VineKind.Folder))
        {
            if (!file.StartsWith(VineKind.Prefix) || !file.EndsWith(".tres"))
                continue;
            var kind = GD.Load<VineKind>($"{VineKind.Folder}/{file}");
            if (kind != null)
                kinds[kind.KindName] = kind;
        }
        return kinds;
    }

    private static VineParams ParamsOf(JsonElement json)
    {
        var p = new VineParams();
        foreach (VineParams.Field field in VineParams.Fields)
        {
            // A setting added after the file was recorded grows as its default did.
            if (!json.TryGetProperty(field.Key, out JsonElement v))
            {
                field.Set(p, field.Fallback);
                continue;
            }
            field.Set(p, v.ValueKind switch
            {
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => v.GetDouble(),
            });
        }
        return p;
    }

    // FNV-1a over each voxel as x, y, z (two bytes each, little end) and colour.
    private static uint VoxelHash(List<VineGenerator.Voxel> voxels)
    {
        uint h = 2166136261;
        void Byte(int b)
        {
            h ^= (uint)(b & 255);
            h *= 16777619;
        }
        foreach (VineGenerator.Voxel v in voxels)
        {
            Byte(v.X); Byte(v.X >> 8); Byte(v.Y); Byte(v.Y >> 8); Byte(v.Z); Byte(v.Z >> 8); Byte(v.C);
        }
        return h;
    }

    // FNV-1a over each leaf's numbers to a thousandth.
    private static uint LeafHash(List<VineGenerator.LeafPlace> leaves)
    {
        uint h = 2166136261;
        foreach (VineGenerator.LeafPlace l in leaves)
        {
            foreach (double v in new[] { l.AX, l.AY, l.AZ, l.CX, l.CY, l.CZ, l.MX, l.MY, l.MZ, l.NX, l.NY, l.NZ, l.SizeM, l.Shape, l.Arm })
            {
                long n = (long)Math.Round(v * 1000);
                for (int b = 0; b < 8; b++)
                {
                    h ^= (uint)((n >> (8 * b)) & 255);
                    h *= 16777619;
                }
            }
        }
        return h;
    }

    private void Golden(Dictionary<string, VineKind> kinds, VineBakeStats stats)
    {
        string path = ProjectSettings.GlobalizePath(GoldenPath);
        if (!File.Exists(path))
        {
            Check("golden file", false, $"{path} missing: vinecheck.gd -- record");
            return;
        }
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        int archetypeSeed = doc.RootElement.GetProperty("archetypeSeed").GetInt32();
        int exact = 0, total = 0;
        foreach (JsonElement g in doc.RootElement.GetProperty("vines").EnumerateArray())
        {
            total++;
            string name = g.GetProperty("name").GetString();
            VineParams p = ParamsOf(g.GetProperty("params"));

            // A pool vine: its seed and every rolled setting must be as recorded, exactly.
            int underscore = name.IndexOf('_');
            string kindName = underscore > 0 ? char.ToUpperInvariant(name[0]) + name.Substring(1, underscore - 1) : null;
            if (kindName != null && kinds.TryGetValue(kindName, out VineKind kind))
            {
                int seed = int.Parse(name.Substring(underscore + 1));
                bool inPool = Enumerable.Range(0, GoldenPool).Any(i => kind.PoolSeed(archetypeSeed, i) == seed);
                VineParams rolled = kind.Roll(seed, Math.Round((double)stats.VoxelSize, 6));
                var differ = VineParams.Fields.Where(f => f.Get(rolled) != f.Get(p)).Select(f => $"{f.Key} {f.Get(rolled)} vs {f.Get(p)}").ToList();
                Check($"{name}: pool seed and rolled settings", inPool && differ.Count == 0, string.Join("; ", differ));
            }

            ulong t0 = Time.GetTicksMsec();
            VineGenerator.Result vine = VineGenerator.Generate(p);
            ulong ms = Time.GetTicksMsec() - t0;
            int wantTotal = g.GetProperty("counts").GetProperty("total").GetInt32();
            uint wantHash = g.GetProperty("hash").GetUInt32();
            uint hash = VoxelHash(vine.Voxels);
            bool leavesSame = !g.TryGetProperty("leaves", out JsonElement wantLeaves) || wantLeaves.GetUInt32() == LeafHash(vine.Leaves);

            var markers = g.GetProperty("markers").EnumerateArray().Select(m =>
                $"{m[0].GetString()} {m[1].GetInt32()} {m[2].GetInt32()} {m[3].GetInt32()}").ToList();
            var ours = vine.Markers.Select(m => $"{m.Name} {m.X} {m.Y} {m.Z}").ToList();
            bool markersSame = markers.SequenceEqual(ours);

            if (hash == wantHash && markersSame && !leavesSame)
            {
                Check($"{name}: same leaves", false, $"{vine.Leaves.Count} leaves differ");
                continue;
            }
            if (hash == wantHash && markersSame)
            {
                exact++;
                Check($"{name}: same voxels and pods", true, $"{vine.Counts.Total} voxels, {ours.Count} pods, {ms} ms");
                continue;
            }
            // Not exact: something moved the generator. If it was meant to,
            // re-record; how far off, layer by layer, says where to look (dump).
            int[] layers = g.GetProperty("layers").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var mine = new int[Math.Max(layers.Length, vine.BoundsZ)];
            foreach (VineGenerator.Voxel v in vine.Voxels)
                mine[v.Z]++;
            int off = 0, firstLayer = -1;
            for (int z = 0; z < mine.Length; z++)
            {
                int d = Math.Abs(mine[z] - (z < layers.Length ? layers[z] : 0));
                off += d;
                if (d > 0 && firstLayer < 0)
                    firstLayer = z;
            }
            Check($"{name}: voxels differ", false,
                $"{vine.Counts.Total} vs {wantTotal} voxels, {off} off by layer from z {firstLayer}, pods {(markersSame ? "same" : $"differ ({ours.Count} vs {markers.Count})")}");
        }
        _report.AppendLine($"  {exact} of {total} vines exactly as recorded");
    }

    /// <summary>Writes the golden file: the fixed-setting vines already in it
    /// keep their settings and are regrown, and each kind's first pool vines
    /// are rolled afresh. Returns what was written.</summary>
    public string Record()
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        Dictionary<string, VineKind> kinds = LoadKinds();
        string path = ProjectSettings.GlobalizePath(GoldenPath);
        var presets = new List<(string Name, VineParams Params)>();
        if (File.Exists(path))
        {
            using JsonDocument old = JsonDocument.Parse(File.ReadAllText(path));
            foreach (JsonElement g in old.RootElement.GetProperty("vines").EnumerateArray())
            {
                string name = g.GetProperty("name").GetString();
                if (!name.Contains('_') || !char.IsDigit(name[^1]))
                    presets.Add((name, ParamsOf(g.GetProperty("params"))));
            }
        }
        if (presets.Count == 0)
            presets.Add(("default", new VineParams().Clamped()));

        double vs = Math.Round((double)stats.VoxelSize, 6);
        var all = new List<(string Name, VineParams Params)>(presets);
        foreach (VineKind kind in kinds.Values.OrderBy(k => k.KindName))
        {
            for (int i = 0; i < GoldenPool; i++)
            {
                int seed = kind.PoolSeed(stats.ArchetypeSeed, i);
                all.Add(($"{kind.Slug}_{seed}", kind.Roll(seed, vs)));
            }
        }

        var vines = new Godot.Collections.Array();
        var lines = new StringBuilder();
        foreach ((string name, VineParams p) in all)
        {
            VineGenerator.Result vine = VineGenerator.Generate(p);
            var settings = new Godot.Collections.Dictionary();
            foreach (VineParams.Field field in VineParams.Fields)
            {
                double v = field.Get(p);
                settings[field.Key] = field.Bool ? v != 0 : v;
            }
            var layers = new int[vine.BoundsZ];
            foreach (VineGenerator.Voxel v in vine.Voxels)
                layers[v.Z]++;
            var markers = new Godot.Collections.Array();
            foreach (VineGenerator.Marker m in vine.Markers)
                markers.Add(new Godot.Collections.Array { m.Name, m.X, m.Y, m.Z });
            vines.Add(new Godot.Collections.Dictionary
            {
                ["name"] = name,
                ["params"] = settings,
                ["ok"] = vine.Ok,
                ["counts"] = new Godot.Collections.Dictionary { ["total"] = vine.Counts.Total },
                ["bounds"] = new Godot.Collections.Array { vine.BoundsX, vine.BoundsY, vine.BoundsZ },
                ["hash"] = VoxelHash(vine.Voxels),
                ["leaves"] = LeafHash(vine.Leaves),
                ["layers"] = new Godot.Collections.Array(layers.Select(n => Variant.From(n))),
                ["markers"] = markers,
            });
            lines.AppendLine($"  {name}: {vine.Counts.Total} voxels, {vine.Markers.Count} pods{(vine.Ok ? "" : "  (" + vine.Error + ")")}");
        }
        var data = new Godot.Collections.Dictionary
        {
            ["tool"] = VineBaker.Tool,
            ["version"] = VineBaker.Version,
            ["archetypeSeed"] = stats.ArchetypeSeed,
            ["vines"] = vines,
        };
        File.WriteAllText(path, Json.Stringify(data, "  ", sortKeys: false) + "\n");
        return $"Recorded {vines.Count} golden vines to {GoldenPath}\n{lines}";
    }

    private void Prototype(Dictionary<string, VineKind> kinds, VineBakeStats stats)
    {
        var defaults = new VineParams().Clamped();
        VineGenerator.Result vine = VineGenerator.Generate(defaults);
        int podCount = (int)(defaults.RingCount * defaults.PodsPerRing);
        Check("default vine builds", vine.Ok && vine.Error == null);
        VineValidator.Verdict verdict = VineValidator.Check(VineGenerator.Generate(defaults, stats.Limits()), stats);
        Check("the default vine is valid", verdict.Ok, string.Join(" ", verdict.Details));
        Check("every voxel reaches the ground face to face", Grounded(vine));
        int lost = vine.Counts.PodsLeftOut;
        Check($"{podCount} pods grown, every one kept or left out", vine.Markers.Count + lost == podCount && vine.Counts.Markers == vine.Markers.Count,
            $"{vine.Markers.Count} kept, {lost} left out");
        var names = new HashSet<string>(vine.Markers.Select(m => m.Name));
        Check("marker names are unique @ names", names.Count == vine.Markers.Count && names.All(n => n.StartsWith('@')));
        Check("pods named with no gaps", names.Contains("@Pod01") && names.Contains(VineGenerator.PodName(vine.Markers.Count)));
        var occupied = new HashSet<(int, int, int)>(vine.Voxels.Select(v => (v.X, v.Y, v.Z)));
        Check("markers are not stalk voxels", vine.Markers.All(m => !occupied.Contains((m.X, m.Y, m.Z))));
        Check("no two pods share a cell", vine.Markers.Select(m => (m.X, m.Y, m.Z)).Distinct().Count() == vine.Markers.Count);
        // Counted here afresh, not taken from the generator's own counts.
        int drop = (int)Math.Ceiling(stats.PickleHeightM / vine.VoxelSize);
        var podCells = new HashSet<(int, int, int)>(vine.Markers.Select(m => (m.X, m.Y, m.Z)));
        bool hanging = vine.Markers.All(m => Enumerable.Range(1, drop).All(d => m.Z - d >= 0
            && !occupied.Contains((m.X, m.Y, m.Z - d)) && !podCells.Contains((m.X, m.Y, m.Z - d))));
        Check("every kept pod has a pickle's drop", hanging);
        double closest = double.MaxValue;
        for (int i = 0; i < vine.Markers.Count; i++)
            for (int j = i + 1; j < vine.Markers.Count; j++)
                closest = Math.Min(closest, Math.Sqrt(Math.Pow(vine.Markers[i].X - vine.Markers[j].X, 2)
                    + Math.Pow(vine.Markers[i].Y - vine.Markers[j].Y, 2) + Math.Pow(vine.Markers[i].Z - vine.Markers[j].Z, 2)) * vine.VoxelSize);
        Check("every kept pod has room", closest >= stats.PodSpacingM - 1e-6, $"nearest two {closest:0.00} m");
        Check("pods hang above the bare stalk", vine.Markers.Min(m => m.Z * vine.VoxelSize) > defaults.BareStalkM * 0.5);
        Check("palette indices 0 and 255 unused", vine.Voxels.All(v => v.C >= 1 && v.C <= 6));
        Check("a core sits inside the shell", vine.Counts.Core > 0);
        VineGenerator.Voxel nearest = vine.Voxels.Where(v => v.Z >= 2 && v.Z <= 10)
            .MinBy(v => Math.Pow(v.X + 0.5 - vine.FootX, 2) + Math.Pow(v.Y + 0.5 - vine.FootY, 2) + Math.Pow(v.Z - 4, 2));
        Check("the base interior is core", nearest.C == VineGenerator.Core);
        Check("every knuckle is fatter than the span beside it",
            vine.Rings.Count == (int)defaults.RingCount && vine.Rings.All(r => r.Radius > r.MidRadius));
        Check("pods spiral from ring to ring", vine.Rings[0].Angle0 != vine.Rings[1].Angle0);
        Check("fits one MagicaVoxel model", vine.BoundsX <= 256 && vine.BoundsY <= 256 && vine.BoundsZ <= 256 && vine.BoundsZ > 100);

        VineParams tooFine = defaults.Copy();
        tooFine.VoxelSize = 0.1;
        VineGenerator.Result fine = VineGenerator.Generate(tooFine);
        Check("0.1 m refuses an 80 m vine", !fine.Ok && fine.Voxels.Count == 0);
        VineParams overParams = defaults.Copy();
        overParams.VoxelSize = 0.28;
        overParams.LeanDeg = 0;
        overParams.WobbleM = 0;
        overParams.LeavesPerRing = 0;
        VineGenerator.Result over = VineGenerator.Generate(overParams);
        Check("a model past 256 is refused", !over.Ok && over.BoundsZ > 256);
        VineParams bareParams = defaults.Copy();
        bareParams.ArmsPerRing = 0;
        bareParams.PedicelM = 0;
        VineGenerator.Result bare = VineGenerator.Generate(bareParams);
        Check("no arms puts the pods on the stalk", bare.Ok && bare.Markers.Count + bare.Counts.PodsLeftOut == podCount && bare.Markers.Count > 0);

        if (kinds.Count == 0)
            return;
        double vs = Math.Round((double)stats.VoxelSize, 6);
        bool inside = true;
        foreach (VineKind kind in kinds.Values)
        {
            for (int i = 0; i < kind.PoolSize; i++)
            {
                VineParams rolled = kind.Roll(kind.PoolSeed(stats.ArchetypeSeed, i), vs);
                foreach (VineParams.Field f in VineParams.RangeFields.Where(f => !f.Bool))
                {
                    (double lo, double hi) = kind.RangeOf(f);
                    double v = f.Get(rolled);
                    if (v < lo - 1e-9 || v > hi + 1e-9)
                        inside = false;
                }
            }
        }
        Check("every roll is inside its range", inside);
        VineKind fruiting = kinds.GetValueOrDefault("Fruiting");
        VineKind leafyKind = kinds.GetValueOrDefault("Leafy");
        if (fruiting == null || leafyKind == null)
        {
            Check("Fruiting and Leafy kinds exist", false);
            return;
        }
        int seed0 = fruiting.PoolSeed(stats.ArchetypeSeed, 0);
        VineParams before = fruiting.Roll(seed0, vs);
        var wider = (VineKind)fruiting.Duplicate();
        wider.HeightM = new Vector2(60, 120);
        VineParams after = wider.Roll(seed0, vs);
        bool othersKept = VineParams.Fields.Where(f => f.Key != "heightM").All(f => f.Get(before) == f.Get(after));
        Check("widening one range moves no other setting", othersKept && before.HeightM != after.HeightM);
        var pool = Enumerable.Range(0, fruiting.PoolSize).Select(i => fruiting.Roll(fruiting.PoolSeed(stats.ArchetypeSeed, i), vs))
            .Select(p => string.Join(",", VineParams.Fields.Select(f => f.Get(p)))).ToList();
        Check("a pool's vines differ", pool.Distinct().Count() == pool.Count);

        // Colour (step 5): every vine's shade, and every leaf's on top of it,
        // inside its palette's green band; no two vines the same.
        bool inBand = true;
        var shades = new HashSet<Color>();
        foreach (VineKind kind in kinds.Values)
        {
            VinePalette basePalette = kind.Palette ?? stats.Palette;
            for (int s = 0; s < 100; s++)
            {
                VinePalette shade = basePalette.Shaded(s * 7919 + 1);
                shades.Add(shade.Leaf);
                inBand &= basePalette.Inside(shade.Shell) && basePalette.Inside(shade.Knuckle)
                    && basePalette.Inside(shade.Leaf) && basePalette.Inside(shade.Petiole);
                for (int leaf = 0; leaf < 8; leaf++)
                {
                    Color tint = shade.LeafTint(s, leaf);
                    Color linear = shade.Leaf.SrgbToLinear();
                    inBand &= basePalette.Inside(new Color(linear.R * tint.R, linear.G * tint.G, linear.B * tint.B).LinearToSrgb());
                }
            }
        }
        Check("every vine's shade and leaf tint stays in the green band", inBand);
        Check("vines' shades differ", shades.Count > kinds.Count * 90, $"{shades.Count} leaf shades of {kinds.Count * 100}");
        VineGenerator.Result leafy = VineGenerator.Generate(leafyKind.Roll(leafyKind.PoolSeed(stats.ArchetypeSeed, 0), vs));
        VineGenerator.Result fruity = VineGenerator.Generate(before);
        double LeafArea(VineGenerator.Result v) => v.Leaves.Sum(l => l.SizeM * l.SizeM);
        Check("a leafy vine is mostly leaf, a fruiting one mostly pods",
            LeafArea(leafy) > LeafArea(fruity) * 1.5 && fruity.Counts.Markers > leafy.Counts.Markers * 4,
            $"leaf area {LeafArea(leafy):0} vs {LeafArea(fruity):0} m2, pods {leafy.Counts.Markers} vs {fruity.Counts.Markers}");
        Check("a fruiting vine's collision bodies", fruity.Counts.Bodies <= stats.BodiesMax,
            $"{fruity.Counts.Bodies} at section edge {stats.Limits().SectionEdge}");
    }

    private static bool Grounded(VineGenerator.Result vine)
    {
        var at = new HashSet<(int, int, int)>(vine.Voxels.Select(v => (v.X, v.Y, v.Z)));
        var held = new HashSet<(int, int, int)>();
        var q = new Queue<(int X, int Y, int Z)>();
        foreach (VineGenerator.Voxel v in vine.Voxels.Where(v => v.Z == 0))
        {
            held.Add((v.X, v.Y, v.Z));
            q.Enqueue((v.X, v.Y, v.Z));
        }
        var dirs = new[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach ((int dx, int dy, int dz) in dirs)
            {
                var n = (c.X + dx, c.Y + dy, c.Z + dz);
                if (at.Contains(n) && held.Add(n))
                    q.Enqueue(n);
            }
        }
        return held.Count == vine.Voxels.Count;
    }

    private void Written(VineBakeStats stats)
    {
        VineGenerator.Result vine = VineGenerator.Generate(new VineParams().Clamped(), stats.Limits());
        byte[] bytes = VineBaker.Encode(vine, stats.Palette);
        Check("same vine, same bytes", bytes.SequenceEqual(VineBaker.Encode(VineGenerator.Generate(new VineParams().Clamped(), stats.Limits()), stats.Palette)));
        Check("VOX header and version 150", Encoding.ASCII.GetString(bytes, 0, 4) == "VOX " && BitConverter.ToInt32(bytes, 4) == 150);

        VoxFile file = VoxFile.Load(bytes);
        Check("reads back: the stalk and one marker model", file.Models.Count == 2
            && file.Models[0].Size == new Vector3I(vine.BoundsX, vine.BoundsY, vine.BoundsZ)
            && file.Models[0].Cells.Count(c => c != 0) == vine.Counts.Total);
        VoxFile.TransformNode stalk = file.Transforms.Values.FirstOrDefault(t => t.Name == "Stalk");
        var pods = file.Transforms.Values.Where(t => t.Name.StartsWith("@Pod")).ToList();
        Check("one Stalk and every pod by name", stalk != null && pods.Count == vine.Markers.Count);
        Check("shell and core are different materials", file.MaterialType[VineGenerator.Core] != file.MaterialType[VineGenerator.Shell]);
        int o = 254 * 4;
        int rgba = FindChunk(bytes, "RGBA");
        Check("palette index 255 is empty", rgba > 0 && bytes[rgba + o] == 0 && bytes[rgba + o + 3] == 0);

        // Where the importer puts the foot: a voxel corner p lands at
        // t + p - floor(size / 2), times the voxel size, in Godot's axes.
        Vector3 centre = VoxSpace.Centre(file.Models[0].Size);
        Vector3 foot = stalk.Translation + new Vector3((float)vine.FootX, (float)vine.FootY, 0) - centre;
        Vector3 metres = VoxSpace.ToGodot(foot) * stats.VoxelSize;
        Check("the foot stands on the origin", Mathf.Abs(metres.X) <= stats.VoxelSize * 0.5f + 1e-4f
            && Mathf.Abs(metres.Z) <= stats.VoxelSize * 0.5f + 1e-4f && Mathf.Abs(metres.Y) < 1e-4f, $"{metres}");
        // A marker lands on the centre a stalk voxel in its cell would.
        VineGenerator.Marker first = vine.Markers[0];
        VoxFile.TransformNode pod = pods.First(t => t.Name == first.Name);
        Vector3 podAt = pod.Translation + new Vector3(0.5f, 0.5f, 0.5f);
        Vector3 cellAt = stalk.Translation + new Vector3(first.X + 0.5f, first.Y + 0.5f, first.Z + 0.5f) - centre;
        Check("a pod sits on its cell's centre", podAt.IsEqualApprox(cellAt), $"{podAt} vs {cellAt}");
    }

    /// <summary>Pool vines a kind whose leaves <see cref="LeavesInMemory"/> checks.</summary>
    private const int LeafPool = 6;

    // A nest grows its vines taller or shorter (NestKit.SizeJitter): its extremes, and as baked.
    private static readonly float[] LeafScales = { 1.0f, 0.88f, 1.12f };

    private void LeavesInMemory(Dictionary<string, VineKind> kinds, VineBakeStats stats)
    {
        VineGrower.Context context = VineGrower.Context.For(stats, kinds.Values);
        foreach (VineKind kind in kinds.Values.OrderBy(k => k.KindName))
        {
            for (int i = 0; i < LeafPool; i++)
            {
                int seed = kind.PoolSeed(stats.ArchetypeSeed, i);
                float scale = LeafScales[i % LeafScales.Length];
                VineFactory.Data data = VineFactory.BuildData(VineGrower.Grow(kind, seed, context, scale), kind, stats,
                    $"{VineBaker.NameOf(kind, seed, false)}{(scale != 1.0f ? FormattableString.Invariant($" x{scale}") : "")}");
                if (data.Model == null)
                {
                    Check($"{data.Name}: grows in memory", false, string.Join(" ", data.Grown.Verdict.Details));
                    continue;
                }
                Node3D model = VoxModelBuilder.Instantiate(data.Model, "Model", VoxModelBuilder.MeshInstances(null));
                LeavesOn(data.Name, model, data.Leaves);
                model.Free();
            }
        }
    }

    /// <summary>
    /// Every leaf's stem leaves a solid cell: its attach point, in the
    /// model's space, within a voxel of one of the stalk's cells. Reports how
    /// many do not, and the furthest off in voxels.
    /// </summary>
    private void LeavesOn(string name, Node3D model, VineLeafSet set)
    {
        var stalk = model?.GetNodeOrNull<Node3D>("Stalk");
        if (stalk == null || set == null || !stalk.HasMeta(VoxelMeta.Cells))
        {
            Check($"{name}: leaves on the vine", false, "no stalk cells or no leaves");
            return;
        }
        byte[] cells = stalk.GetMeta(VoxelMeta.Cells).AsByteArray();
        Vector3I dims = stalk.GetMeta(VoxelMeta.Dims).AsVector3I();
        Vector3 origin = stalk.GetMeta(VoxelMeta.Origin).AsVector3();
        float vs = (float)model.GetMeta(VoxelMeta.VoxelSize).AsDouble();
        Transform3D toStalk = stalk.Transform.AffineInverse();
        const int Reach = 8;
        int off = 0;
        float worst = 0.0f;
        int worstLeaf = -1;
        for (int i = 0; i < set.Attach.Length; i++)
        {
            // Model space to the grid's continuous coordinates: a cell spans [c, c + 1).
            Vector3 q = VoxSpace.FromGodot((toStalk * set.Attach[i]) / vs) + origin;
            float nearest = float.MaxValue;
            Vector3I c = new(Mathf.FloorToInt(q.X), Mathf.FloorToInt(q.Y), Mathf.FloorToInt(q.Z));
            for (int z = c.Z - Reach; z <= c.Z + Reach; z++)
            for (int y = c.Y - Reach; y <= c.Y + Reach; y++)
            for (int x = c.X - Reach; x <= c.X + Reach; x++)
            {
                if (x < 0 || y < 0 || z < 0 || x >= dims.X || y >= dims.Y || z >= dims.Z || cells[x + dims.X * (y + dims.Y * z)] == 0)
                    continue;
                // From the point to the cell's box.
                Vector3 d = new(Mathf.Max(0, Mathf.Max(x - q.X, q.X - (x + 1))), Mathf.Max(0, Mathf.Max(y - q.Y, q.Y - (y + 1))), Mathf.Max(0, Mathf.Max(z - q.Z, q.Z - (z + 1))));
                nearest = Mathf.Min(nearest, d.Length());
            }
            if (nearest > 1.0f)
            {
                off++;
                if (nearest > worst)
                {
                    worst = nearest;
                    worstLeaf = i;
                }
            }
        }
        Check($"{name}: every leaf's stem on the vine", off == 0,
            off == 0 ? $"{set.Attach.Length} leaves"
                : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{off} of {set.Attach.Length} more than a voxel off, worst leaf {worstLeaf} {(worst == float.MaxValue ? $"over {Reach}" : $"{worst:F1}")} voxels at {set.Attach[worstLeaf]}"));
    }

    /// <summary>A vine made in memory (<see cref="VineFactory"/>) against its
    /// baked, imported self: the same nodes in the same places, and every
    /// voxel and chunk field the same, so the game can grow what the editor bakes.</summary>
    private void SameInMemory(string name, Node3D baked, VineLeafSet bakedLeaves, VineBakeStats stats)
    {
        var match = System.Text.RegularExpressions.Regex.Match(name, @"^prp_vine_([a-z]+)_(\d+)$");
        VineKind kind = LoadKinds().Values.FirstOrDefault(k => k.Slug == match.Groups[1].Value);
        if (!match.Success || kind == null)
            return;
        long managed0 = GC.GetTotalMemory(true);
        VineFactory.Data data = VineFactory.BuildData(kind, int.Parse(match.Groups[2].Value), stats);
        long managed1 = GC.GetTotalMemory(true);
        if (data.Model == null)
        {
            Check($"{name}: grows in memory", false, string.Join(" ", data.Grown.Verdict.Details));
            return;
        }
        Node3D made = VoxModelBuilder.Instantiate(data.Model, "Model", VoxModelBuilder.MeshInstances(null));
        var differ = new List<string>();
        foreach (string key in new[] { VoxelMeta.VoxelSize, VoxelMeta.Palette, VoxelMeta.Emission })
        {
            if (!GD.VarToBytes(made.GetMeta(key)).SequenceEqual(GD.VarToBytes(baked.GetMeta(key))))
                differ.Add("root " + key);
        }
        var bakedChildren = baked.GetChildren().OfType<Node3D>().ToList();
        var madeChildren = made.GetChildren().OfType<Node3D>().ToList();
        if (bakedChildren.Count != madeChildren.Count)
            differ.Add($"{madeChildren.Count} nodes vs {bakedChildren.Count}");
        for (int i = 0; i < Math.Min(bakedChildren.Count, madeChildren.Count); i++)
        {
            Node3D a = bakedChildren[i], b = madeChildren[i];
            if (a.Name != b.Name || !a.Transform.IsEqualApprox(b.Transform))
                differ.Add($"node {i} {b.Name} vs {a.Name}");
            foreach (StringName key in a.GetMetaList())
            {
                if (!b.HasMeta(key) || !GD.VarToBytes(a.GetMeta(key)).SequenceEqual(GD.VarToBytes(b.GetMeta(key))))
                    differ.Add($"{a.Name} {key}");
            }
        }
        made.Free();
        foreach (string property in new[] { "Attach", "Centre", "Midrib", "Face", "Size", "Shape", "ShapeLobes", "ShapeTeeth", "ShapeNotch", "ShapeCup", "Blade", "Vein", "Stem" })
        {
            if (bakedLeaves == null || !GD.VarToBytes(bakedLeaves.Get(property)).SequenceEqual(GD.VarToBytes(data.Leaves.Get(property))))
                differ.Add("leaves " + property);
        }
        Check($"{name}: made in memory, the same voxels, chunks and leaves", differ.Count == 0,
            differ.Count > 0 ? string.Join(", ", differ.Take(6))
                : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"generate {data.GenerateMs:F0} ms, mesh and chunks {data.ModelMs:F0} ms, {(managed1 - managed0) / 1048576.0:F1} MB"));
    }

    /// <summary>
    /// Grows <paramref name="count"/> seeds of each kind and says how often a
    /// seed's own roll is valid, how many attempts it takes, why rolls fail,
    /// and how each measure spreads over the first rolls: a kind that fails
    /// often has ranges to narrow. A kind that cannot grow a seed within
    /// <see cref="VineBakeStats.MaxAttempts"/> fails the check.
    /// </summary>
    public string Rejections(int count)
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        var text = new StringBuilder();
        bool pass = true;
        foreach (VineKind kind in LoadKinds().Values.OrderBy(k => k.KindName))
        {
            var rng = Core.SeededRandom.Stream(1, "vinecheck:rejections:" + kind.KindName);
            int firstOk = 0, failed = 0, attempts = 0;
            var reasons = new Dictionary<string, int>();
            var lean = new List<double>();
            var kink = new List<double>();
            var sag = new List<double>();
            var widen = new List<double>();
            var armLength = new List<double>();
            var armThick = new List<double>();
            var leaves = new List<double>();
            var leavesOut = new List<double>();
            ulong t0 = Time.GetTicksMsec();
            for (int i = 0; i < count; i++)
            {
                int seed = rng.RangeInt(0, 9_999_999);
                VineGrower.Grown grown = VineGrower.Grow(kind, seed, stats);
                attempts += grown.Attempt + 1;
                if (!grown.Ok)
                    failed++;
                else if (grown.Attempt == 0)
                    firstOk++;
                foreach (VineGrower.Rejection r in grown.Rejected)
                {
                    foreach (string reason in r.Reasons)
                        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                }
                // The seed's own roll, valid or not: how the kind's ranges land.
                VineValidator.Verdict first = grown.Attempt == 0 ? grown.Verdict
                    : VineValidator.Check(VineGenerator.Generate(kind.Roll(seed, Math.Round((double)stats.VoxelSize, 6)), stats.Limits()), stats);
                lean.Add(first.LeanDeg);
                kink.Add(first.KinkDeg);
                sag.Add(first.SagM);
                widen.Add(first.WideningShare);
                armLength.Add(first.ArmLengthGrowth);
                armThick.Add(first.ArmThicknessGrowth);
                leaves.Add(grown.Vine.Counts.Leaves);
                leavesOut.Add(grown.Vine.Counts.LeavesLeftOut);
            }
            int rolls = reasons.Values.Sum();
            string Spread(List<double> v)
            {
                v.Sort();
                double At(double q) => v[Math.Min(v.Count - 1, (int)(q * v.Count))];
                return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{At(0.1):0.##}/{At(0.5):0.##}/{At(0.9):0.##}/{v[^1]:0.##}");
            }
            text.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"  {kind.KindName}: {firstOk * 100.0 / count:0}% valid on the seed's own roll, {attempts / (double)count:0.00} rolls a seed, {failed} seeds with no valid vine, {Time.GetTicksMsec() - t0} ms"));
            if (reasons.Count > 0)
            {
                text.AppendLine("    rejected for: " + string.Join(", ", reasons.OrderByDescending(r => r.Value)
                    .Select(r => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{r.Key} {r.Value * 100.0 / rolls:0}%"))));
            }
            text.AppendLine($"    first rolls p10/p50/p90/max: lean {Spread(lean)} deg, kink {Spread(kink)} deg, sag {Spread(sag)} m, "
                + $"knuckle widening {Spread(widen)}, arm length growth {Spread(armLength)}, arm thickness growth {Spread(armThick)}, leaves {Spread(leaves)} ({Spread(leavesOut)} left out)");
            if (failed > 0)
                pass = false;
        }
        return (pass ? "PASS\n" : "FAIL\n") + text;
    }

    /// <summary>For <c>vinelook.gd leaves</c>: every kind's leaf shapes laid
    /// in a row each, 12 m across and 16 m apart, a stem under each.</summary>
    public Node3D LeafSheet()
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        VinePalette palette = stats.Palette ?? new VinePalette();
        var sheet = new Node3D { Name = "LeafSheet" };
        var material = GD.Load<Material>(VineLeaves.MaterialPath);
        int row = 0;
        foreach (VineKind kind in LoadKinds().Values.OrderBy(k => k.KindName))
        {
            LeafMesh.Shape[] shapes = LeafMesh.ShapesOf(kind);
            for (int s = 0; s < shapes.Length; s++)
            {
                var at = new Vector3((s - 1) * 16.0f, 0.5f, (row - 1.5f) * 16.0f);
                sheet.AddChild(new MeshInstance3D
                {
                    Name = $"{kind.KindName}{s}",
                    Mesh = LeafMesh.Blade(shapes[s], palette.Leaf.SrgbToLinear(), palette.Knuckle.SrgbToLinear()),
                    MaterialOverride = material,
                    Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(6, 6, 6)), at),
                });
                sheet.AddChild(new MeshInstance3D
                {
                    Mesh = LeafMesh.Stem(palette.Petiole.SrgbToLinear()),
                    MaterialOverride = material,
                    Transform = new Transform3D(new Basis(Vector3.Right * 0.35f, Vector3.Up * 0.35f, Vector3.Back * 4.0f), at - new Vector3(0, 0, 8.5f)),
                });
            }
            row++;
        }
        return sheet;
    }

    /// <summary>The baked vine scenes the settle test stands up.</summary>
    public string[] BakedVines()
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        return DirAccess.GetFilesAt(stats.SceneFolder)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^prp_vine_[a-z]+_[0-9]+c?[.]tscn$"))
            .Select(f => $"{stats.SceneFolder}/{f}").ToArray();
    }

    /// <summary>
    /// The settle test, with Jolt: a baked vine stood on flat ground with no
    /// wind, player or shots. <c>vinecheck.gd</c> lets the physics run, then
    /// asks <see cref="Settled"/>. Not a gate on growing (Jolt is not the same
    /// to the bit on every machine): a vine that fails it says its kind's
    /// ranges, or the game's physics, need looking at.
    /// </summary>
    public Node3D Stand(string scenePath)
    {
        FlexField.Wind = new WindStats { Speed = 0.0f, GustSpeed = 0.0f, Swirl = 0.0f };
        var stage = new Node3D { Name = "Settle" };
        var ground = new StaticBody3D { Name = "Ground", Position = new Vector3(0, -1, 0) };
        ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(600, 2, 600) } });
        stage.AddChild(ground);
        stage.AddChild(GD.Load<PackedScene>(scenePath).Instantiate());
        AddChild(stage);
        _bodiesBefore = Bodies(GetTree().Root).Count;
        return stage;
    }

    private int _bodiesBefore;

    /// <summary>After the physics has run: what moved that should not have.
    /// Frees the stage.</summary>
    public string Settled(Node3D stage, float seconds)
    {
        Node vine = stage.GetChild(1);
        var flex = vine.GetNodeOrNull<PlantFlex>("Flex");
        Rod rod = flex?.Rod;
        float tip = rod != null ? rod.Offset(rod.Segments).Length() : float.NaN;
        List<RigidBody3D> bodies = Bodies(GetTree().Root);
        int loose = bodies.Count - _bodiesBefore;
        int sunk = bodies.Count(b => b.GlobalPosition.Y < -1.0f);
        bool ok = rod != null && tip < 0.05f && !rod.Restless && loose <= 0 && sunk == 0;
        string line = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  {(ok ? "ok  " : "FAIL")} {vine.Name}: stands {seconds:0} s untouched  tip {tip:0.000} m, {(rod?.Restless == true ? "still moving" : "at rest")}, {Math.Max(0, loose)} pieces loose, {sunk} under the ground");
        stage.QueueFree();
        return line;
    }

    private static List<RigidBody3D> Bodies(Node root)
    {
        var list = new List<RigidBody3D>();
        foreach (Node n in root.FindChildren("*", nameof(RigidBody3D), true, false))
        {
            if (n is RigidBody3D body)
                list.Add(body);
        }
        return list;
    }

    private static int FindChunk(byte[] bytes, string id)
    {
        for (int i = 20; i + 12 < bytes.Length; i++)
        {
            if (bytes[i] == id[0] && bytes[i + 1] == id[1] && bytes[i + 2] == id[2] && bytes[i + 3] == id[3])
                return i + 12;
        }
        return -1;
    }

    private void BakedScenes(VineBakeStats stats)
    {
        int found = 0;
        foreach (string file in DirAccess.GetFilesAt(stats.SceneFolder))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(file, @"^prp_vine_[a-z]+_\d+c?\.tscn$"))
                continue;
            found++;
            string name = file[..^5];
            var scene = GD.Load<PackedScene>($"{stats.SceneFolder}/{file}");
            Node root = scene?.Instantiate();
            if (root == null)
            {
                Check($"{name} loads", false);
                continue;
            }
            var body = root.GetNodeOrNull<VoxelBody>("Voxels");
            var flex = root.GetNodeOrNull<PlantFlex>("Flex");
            var model = root.GetNodeOrNull<Node3D>("Model");
            var leaves = root.GetNodeOrNull<VineLeaves>("Leaves");
            Check($"{name}: Voxels, Model, Flex, Leaves wired", body != null && flex != null && model != null
                && body.Static && body.WholeUntilChanged && body.Stats != null && body.Model == model
                && flex.Body == body && flex.Stats != null
                && leaves != null && leaves.Set != null && leaves.Body == body && leaves.Flex == flex && leaves.Model == model && leaves.Stats != null);
            var mesh = model?.GetNodeOrNull<Node3D>("Stalk");
            int markers = model?.GetChildren().Count(c => c is Marker3D) ?? 0;
            if (mesh == null || !mesh.HasMeta(VoxelMeta.Cells))
            {
                Check($"{name}: imported", false, "no Stalk with cells: import it (--editor --quit)");
                root.Free();
                continue;
            }
            // The bottom layer's centre, in the wrapper's space.
            byte[] cells = mesh.GetMeta(VoxelMeta.Cells).AsByteArray();
            Vector3I dims = mesh.GetMeta(VoxelMeta.Dims).AsVector3I();
            Vector3 origin = mesh.GetMeta(VoxelMeta.Origin).AsVector3();
            float vs = (float)model.GetMeta(VoxelMeta.VoxelSize).AsDouble();
            Vector3 sum = Vector3.Zero;
            int n = 0;
            for (int y = 0; y < dims.Y; y++)
            {
                for (int x = 0; x < dims.X; x++)
                {
                    if (cells[x + dims.X * y] == 0)
                        continue;
                    sum += mesh.Transform * (VoxSpace.ToGodot(new Vector3(x + 0.5f, y + 0.5f, 0) - origin) * vs);
                    n++;
                }
            }
            Vector3 foot = n > 0 ? sum / n : new Vector3(999, 999, 999);
            Check($"{name}: foot on the origin, {markers} pods", vs > 0 && Mathf.Abs(foot.X) <= vs * 0.5f + 1e-3f
                && Mathf.Abs(foot.Z) <= vs * 0.5f + 1e-3f && Mathf.Abs(foot.Y) < 1e-3f && markers > 0,
                $"foot {foot}, voxel {vs} m");
            LeavesOn(name, model, leaves?.Set);
            SameInMemory(name, model, leaves?.Set, stats);
            root.Free();
        }
        _report.AppendLine($"  {found} baked vine scenes");
    }

    /// <summary>One vine's voxels, one per line as x y z c, for a diff.</summary>
    public string Dump(string name)
    {
        string path = ProjectSettings.GlobalizePath(GoldenPath);
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (JsonElement g in doc.RootElement.GetProperty("vines").EnumerateArray())
        {
            if (g.GetProperty("name").GetString() != name)
                continue;
            VineGenerator.Result vine = VineGenerator.Generate(ParamsOf(g.GetProperty("params")));
            var text = new StringBuilder();
            foreach (VineGenerator.Voxel v in vine.Voxels)
                text.Append(v.X).Append(' ').Append(v.Y).Append(' ').Append(v.Z).Append(' ').Append(v.C).Append('\n');
            return text.ToString();
        }
        return "";
    }
}
