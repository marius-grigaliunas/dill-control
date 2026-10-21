using System;
using System.Diagnostics;
using System.Globalization;
using DillControl.Foliage;
using Godot;

namespace DillControl.Debug;

/// <summary>
/// Bakes one vine without the editor's dock, the same way the dock does
/// (<see cref="VineBaker"/>). <c>vinebake.gd</c> runs it; an
/// <c>--editor --quit</c> afterwards imports what it wrote.
/// </summary>
[GlobalClass]
public partial class VineBake : Node
{
    /// <summary><paramref name="seed"/> is a number, or <c>pool:N</c> for
    /// the kind's Nth pool seed.</summary>
    public string Run(string kindName, string seed)
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        if (!Find(kindName, seed, stats, out VineKind kind, out int value))
            return $"FAIL no kind {kindName} in {VineKind.Folder}";

        VineBaker.Baked baked = VineBaker.Bake(kind, value, stats);
        VineGenerator.Result vine = baked.Vine;
        VineGrower.Grown grown = baked.Grown;
        string head = grown.Ok ? $"OK {baked.Name}" : $"FAIL {baked.Name}: {string.Join(" ", grown.Verdict.Details)}";
        if (grown.Attempt > 0)
            head += $"\n  attempt {grown.Attempt} (seed {(int)vine.Params.Seed}); skipped: "
                + string.Join("; ", grown.Rejected.ConvertAll(r => string.Join(",", r.Reasons)));
        string lines = $"{head}\n  {vine.Counts.Total} voxels, {vine.Counts.Markers} pods, {vine.Counts.Bodies} bodies, "
            + string.Create(CultureInfo.InvariantCulture, $"{vine.BoundsX}x{vine.BoundsY}x{vine.BoundsZ} ({vine.BoundsZ * vine.VoxelSize:F1} m tall)");
        foreach (string w in vine.Warnings)
            lines += "\n  warning: " + w;
        if (grown.Ok)
            lines += $"\n  wrote {baked.VoxPath}, {baked.JsonPath}" + (baked.SceneWritten ? $", {baked.ScenePath}" : $" (kept {baked.ScenePath})");
        return lines;
    }

    /// <summary>For <c>bench.gd flex mem:kind:seed</c>: grows a vine in memory
    /// (<see cref="VineFactory"/>) and stands it where <paramref name="replace"/>
    /// stood, in its place, named <c>mem_kind_seed</c>. Says what it cost.</summary>
    public string Place(string kindName, string seed, Node level, string replace)
    {
        var stats = GD.Load<VineBakeStats>(VineBakeStats.DefaultPath);
        if (!Find(kindName, seed, stats, out VineKind kind, out int value))
            return $"[flex] FAIL no kind {kindName} in {VineKind.Folder}";

        long managed0 = GC.GetTotalMemory(true);
        ulong native0 = OS.GetStaticMemoryUsage();
        VineFactory.Data data = VineFactory.BuildData(kind, value, stats);
        long managed1 = GC.GetTotalMemory(true);
        var clock = Stopwatch.StartNew();
        Node3D vine = VineFactory.MakeNodes(data, stats);
        double nodesMs = clock.Elapsed.TotalMilliseconds;
        if (vine == null)
            return $"[flex] FAIL {data.Name}: {string.Join(" ", data.Grown.Verdict.Details)}";

        var old = level.FindChild(replace, true, false) as Node3D;
        Node parent = old?.GetParent() ?? level;
        // Local, under the same parent: the bench places it before the level is in the tree.
        Transform3D at = old?.Transform ?? Transform3D.Identity;
        if (old != null)
        {
            parent.RemoveChild(old);
            old.QueueFree();
        }
        vine.Name = $"mem_{kind.Slug}_{value}";
        parent.AddChild(vine);
        vine.Transform = at;
        long managed2 = GC.GetTotalMemory(true);
        ulong native1 = OS.GetStaticMemoryUsage();

        VineGenerator.Counts c = data.Vine.Counts;
        return string.Create(CultureInfo.InvariantCulture,
            $"[flex] {vine.Name} made in memory in place of {(old != null ? replace : "nothing")}: {c.Total} voxels, {c.Markers} pods\n"
            + $"[flex]   generate {data.GenerateMs:F0} ms, mesh and chunks {data.ModelMs:F0} ms, nodes and LODs {nodesMs:F0} ms\n"
            + $"[flex]   managed {(managed1 - managed0) / 1048576.0:F1} MB built, {(managed2 - managed0) / 1048576.0:F1} MB placed; native {((long)native1 - (long)native0) / 1048576.0:F1} MB");
    }

    private static bool Find(string kindName, string seed, VineBakeStats stats, out VineKind kind, out int value)
    {
        kind = null;
        value = 0;
        foreach (VineKind k in VineCheck.LoadKinds().Values)
        {
            if (k.Slug == kindName.ToLowerInvariant())
                kind = k;
        }
        if (kind == null)
            return false;
        value = seed.StartsWith("pool:")
            ? kind.PoolSeed(stats.ArchetypeSeed, int.Parse(seed.Substring(5), CultureInfo.InvariantCulture))
            : int.Parse(seed, CultureInfo.InvariantCulture);
        return true;
    }
}
