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
            JsonElement v = json.GetProperty(field.Key);
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

            var markers = g.GetProperty("markers").EnumerateArray().Select(m =>
                $"{m[0].GetString()} {m[1].GetInt32()} {m[2].GetInt32()} {m[3].GetInt32()}").ToList();
            var ours = vine.Markers.Select(m => $"{m.Name} {m.X} {m.Y} {m.Z}").ToList();
            bool markersSame = markers.SequenceEqual(ours);

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
        Check("default vine has no warnings", vine.Warnings.Count == 0, string.Join(" ", vine.Warnings));
        Check("every voxel reaches the ground face to face", Grounded(vine));
        Check($"{podCount} pod markers", vine.Markers.Count == podCount && vine.Counts.Markers == podCount);
        var names = new HashSet<string>(vine.Markers.Select(m => m.Name));
        Check("marker names are unique @ names", names.Count == podCount && names.All(n => n.StartsWith('@')));
        Check("first and last pod", names.Contains("@Pod01") && names.Contains("@Pod" + podCount));
        var occupied = new HashSet<(int, int, int)>(vine.Voxels.Select(v => (v.X, v.Y, v.Z)));
        Check("markers are not stalk voxels", vine.Markers.All(m => !occupied.Contains((m.X, m.Y, m.Z))));
        Check("no two pods share a cell", vine.Markers.Select(m => (m.X, m.Y, m.Z)).Distinct().Count() == podCount);
        Check("every pod has a pickle's drop", vine.Counts.PodsBlocked == 0 && vine.Counts.PodsGrounded == 0);
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
        overParams.Tendrils = false;
        VineGenerator.Result over = VineGenerator.Generate(overParams);
        Check("a model past 256 is refused", !over.Ok && over.BoundsZ > 256);
        VineParams bareParams = defaults.Copy();
        bareParams.ArmsPerRing = 0;
        bareParams.PedicelM = 0;
        VineGenerator.Result bare = VineGenerator.Generate(bareParams);
        Check("no arms puts the pods on the stalk", bare.Ok && bare.Markers.Count == podCount);

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
        VineGenerator.Result leafy = VineGenerator.Generate(leafyKind.Roll(leafyKind.PoolSeed(stats.ArchetypeSeed, 0), vs));
        VineGenerator.Result fruity = VineGenerator.Generate(before);
        Check("a leafy vine is mostly leaf, a fruiting one mostly pods",
            leafy.Counts.Leaf > fruity.Counts.Leaf * 2 && fruity.Counts.Markers > leafy.Counts.Markers * 4,
            $"leaf {leafy.Counts.Leaf} vs {fruity.Counts.Leaf}, pods {leafy.Counts.Markers} vs {fruity.Counts.Markers}");
        Check("a fruiting vine's collision bodies", fruity.Counts.Bodies <= stats.BodiesWarnAbove,
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

    /// <summary>A vine made in memory (<see cref="VineFactory"/>) against its
    /// baked, imported self: the same nodes in the same places, and every
    /// voxel and chunk field the same, so the game can grow what the editor bakes.</summary>
    private void SameInMemory(string name, Node3D baked, VineBakeStats stats)
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
            Check($"{name}: grows in memory", false, data.Vine.Error);
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
        Check($"{name}: made in memory, the same voxels and chunks", differ.Count == 0,
            differ.Count > 0 ? string.Join(", ", differ.Take(6))
                : string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"generate {data.GenerateMs:F0} ms, mesh and chunks {data.ModelMs:F0} ms, {(managed1 - managed0) / 1048576.0:F1} MB"));
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
            Check($"{name}: Voxels, Model, Flex wired", body != null && flex != null && model != null
                && body.Static && body.WholeUntilChanged && body.Stats != null && body.Model == model
                && flex.Body == body && flex.Stats != null);
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
            SameInMemory(name, model, stats);
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
