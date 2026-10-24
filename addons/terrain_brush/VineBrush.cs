#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using DillControl.Foliage;

namespace DillControl.Editor;

/// <summary>
/// The Vines brush: every vine it paints is a new seed of a kind, grown
/// valid (docs/decisions/0036) and baked as the Vine tab bakes, so no two are
/// alike. A stroke takes its spots as it goes; when it ends they are baked,
/// the editor imports them, and they are placed as one undo. Shift erases
/// that kind's vines.
/// </summary>
public sealed class VineBrush
{
    private readonly Scatter _scatter;
    private readonly VineKind _kind;
    private readonly List<(Vector3 At, float Yaw, int Seed)> _pending = new();
    private string Pending => "pending:" + _kind.Slug;

    public VineBrush(Scatter scatter, VineKind kind)
    {
        _scatter = scatter;
        _kind = kind;
    }

    public int Waiting => _pending.Count;

    /// <summary>A baked vine of this kind, or one still to bake.</summary>
    public bool Mine(string scene) => scene == Pending || IsKind(scene, _kind);

    public static bool IsKind(string scene, VineKind kind) => scene.GetFile().StartsWith($"prp_vine_{kind.Slug}_");

    public void Paint(float x, float z, Scatter.Settings s)
    {
        foreach (Vector3 at in _scatter.Spots(x, z, s, Mine, Pending))
            _pending.Add((at, _scatter.Random.Range(0.0f, Mathf.Tau), _scatter.Random.RangeInt(0, 9_999_999)));
    }

    public void Erase(float x, float z, Scatter.Settings s) => _scatter.Erase(x, z, s, Mine);

    /// <summary>Bakes every spot's vine, waits for the editor to import them, and places them. Returns how many were placed.</summary>
    public async Task<int> Finish(System.Action<string> status)
    {
        if (_pending.Count == 0)
            return 0;
        var stats = ResourceLoader.Load(VineBakeStats.DefaultPath, cacheMode: ResourceLoader.CacheMode.Replace) as VineBakeStats;
        var baked = new List<(VineBaker.Baked Baked, Vector3 At, float Yaw)>();
        for (int i = 0; i < _pending.Count; i++)
        {
            (Vector3 at, float yaw, int seed) = _pending[i];
            status?.Invoke($"Baking vine {i + 1} of {_pending.Count}...");
            await Bridge.NextFrame();
            VineBaker.Baked b = VineBaker.Bake(_kind, seed, stats);
            if (b.Grown.Ok)
                baked.Add((b, at, yaw));
        }
        _pending.Clear();

        // The editor imports each .vox; a vine's scene is usable once its .vox has been.
        status?.Invoke($"Importing {baked.Count} vines...");
        var waiting = new HashSet<string>(baked.Select(b => b.Baked.VoxPath));
        EditorFileSystem files = EditorInterface.Singleton.GetResourceFilesystem();
        void Reimported(string[] paths)
        {
            foreach (string p in paths)
                waiting.Remove(p);
        }
        files.ResourcesReimported += Reimported;
        files.Scan();
        try
        {
            for (int i = 0; i < 3600 && (waiting.Count > 0 || files.IsScanning()); i++)
                await Bridge.NextFrame();
        }
        finally
        {
            files.ResourcesReimported -= Reimported;
        }

        int placed = 0;
        foreach ((VineBaker.Baked b, Vector3 at, float yaw) in baked)
        {
            if (!waiting.Contains(b.VoxPath) && _scatter.Make(b.ScenePath, at, yaw) != null)
                placed++;
        }
        return placed;
    }
}
#endif
