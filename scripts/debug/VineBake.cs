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
        VineKind kind = null;
        foreach (VineKind k in VineCheck.LoadKinds().Values)
        {
            if (k.Slug == kindName.ToLowerInvariant())
                kind = k;
        }
        if (kind == null)
            return $"FAIL no kind {kindName} in {VineKind.Folder}";
        int value = seed.StartsWith("pool:")
            ? kind.PoolSeed(stats.ArchetypeSeed, int.Parse(seed.Substring(5), CultureInfo.InvariantCulture))
            : int.Parse(seed, CultureInfo.InvariantCulture);

        VineBaker.Baked baked = VineBaker.Bake(kind, value, stats);
        VineGenerator.Result vine = baked.Vine;
        string head = vine.Ok ? $"OK {baked.Name}" : $"FAIL {baked.Name}: {vine.Error}";
        string lines = $"{head}\n  {vine.Counts.Total} voxels, {vine.Counts.Markers} pods, {vine.Counts.Bodies} bodies, "
            + string.Create(CultureInfo.InvariantCulture, $"{vine.BoundsX}x{vine.BoundsY}x{vine.BoundsZ} ({vine.BoundsZ * vine.VoxelSize:F1} m tall)");
        foreach (string w in vine.Warnings)
            lines += "\n  warning: " + w;
        if (vine.Ok)
            lines += $"\n  wrote {baked.VoxPath}, {baked.JsonPath}" + (baked.SceneWritten ? $", {baked.ScenePath}" : $" (kept {baked.ScenePath})");
        return lines;
    }
}
