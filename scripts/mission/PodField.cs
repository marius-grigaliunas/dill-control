using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Foliage;
using DillControl.Voxel;
using Godot;

namespace DillControl.Mission;

/// <summary>What a pod holds (docs/decisions/0034).</summary>
public enum PodState : byte
{
    /// <summary>A pickle hangs from it.</summary>
    Full,
    /// <summary>Its pickle dropped or was killed on it.</summary>
    Empty,
    /// <summary>Growing its pickle again.</summary>
    Regrowing,
    /// <summary>Shot off, or its vine felled or cut away: for good.</summary>
    Destroyed,
}

/// <summary>
/// Every pod in a nest, in bulk (docs/handoffs/vine-tool-3.md, step 7):
/// flat arrays, never a node a pod, for up to 25,000.
///
/// - <see cref="NestBuilder"/> adds each vine as it plants it, with its pods
///   (<see cref="VineFactory.Data.Pods"/>, its <c>@PodNN</c> markers).
/// - A pod hangs from the voxel nearest its marker. Once the vine's voxels
///   change, its pods check they are still held, as its leaves do; a pod
///   whose voxel has gone (cut away, the vine felled or unrooted, the piece
///   it hung from fallen) is destroyed, and so is every pod of a vine that
///   has left the tree.
/// - Where a pod is drawn is where it was built plus the vine's bend
///   (<see cref="PlantFlex.OffsetAt"/>), the same move <c>flex_bend</c>
///   gives its voxels and leaves.
///
/// Its numbers are <see cref="Stats"/>.
/// </summary>
[GlobalClass]
public partial class PodField : Node
{
    private static readonly int SectionCheck = FrameProfiler.Section("pods");

    [Export] public PodStats Stats { get; set; }

    /// <summary>Pod <c>i</c> is gone for good. After its state is <see cref="PodState.Destroyed"/>.</summary>
    public event Action<int> PodDestroyed;

    public int Count { get; private set; }
    public int Full { get; private set; }
    public int Destroyed { get; private set; }
    public int Vines => _vines.Count;

    private sealed class Vine
    {
        public Node3D Root;
        public VoxelBody Body;
        public PlantFlex Flex;
        public int First, Count;
        public bool Anchored, Gone;
        public int Version = int.MinValue;
    }

    private readonly List<Vine> _vines = new();
    // Per pod: where it hangs as built, in world space; its vine; its state.
    private Vector3[] _built = Array.Empty<Vector3>();
    private int[] _vine = Array.Empty<int>();
    private PodState[] _state = Array.Empty<PodState>();
    // The voxel it hangs from; a null part hangs from nothing.
    private VoxelPart[] _part = Array.Empty<VoxelPart>();
    private Vector3I[] _cell = Array.Empty<Vector3I>();
    private float _recheck;

    public override void _Ready()
    {
        Stats ??= GD.Load<PodStats>(PodStats.DefaultPath);
    }

    /// <summary>Room for this many pods, before the vines are added.</summary>
    public void Reserve(int pods)
    {
        if (pods > _built.Length)
        {
            Grow(pods);
        }
    }

    /// <summary>A planted vine, in the tree and placed, and its pods in its own
    /// space. Every pod starts full. Returns its index.</summary>
    public int AddVine(Node3D root, Vector3[] pods)
    {
        var vine = new Vine
        {
            Root = root,
            Body = root.GetNodeOrNull<VoxelBody>("Voxels"),
            Flex = root.GetNodeOrNull<PlantFlex>("Flex"),
            First = Count,
            Count = pods.Length,
        };
        int index = _vines.Count;
        _vines.Add(vine);
        if (Count + pods.Length > _built.Length)
        {
            Grow(Math.Max(Count + pods.Length, _built.Length * 2));
        }
        Transform3D at = root.GlobalTransform;
        for (int k = 0; k < pods.Length; k++)
        {
            int i = Count + k;
            _built[i] = at * pods[k];
            _vine[i] = index;
            _state[i] = PodState.Full;
        }
        Count += pods.Length;
        Full += pods.Length;
        return index;
    }

    private void Grow(int size)
    {
        Array.Resize(ref _built, size);
        Array.Resize(ref _vine, size);
        Array.Resize(ref _state, size);
        Array.Resize(ref _part, size);
        Array.Resize(ref _cell, size);
    }

    public PodState StateOf(int i) => _state[i];

    public int VineOf(int i) => _vine[i];

    /// <summary>Where pod <paramref name="i"/> hangs now, its vine's bend and all.</summary>
    public Vector3 Drawn(int i)
    {
        PlantFlex flex = _vines[_vine[i]].Flex;
        return flex != null && IsInstanceValid(flex) ? _built[i] + flex.OffsetAt(_built[i]) : _built[i];
    }

    /// <summary>Where pod <paramref name="i"/> hangs as its vine was built.</summary>
    public Vector3 Built(int i) => _built[i];

    /// <summary>Pod <paramref name="i"/> is gone for good: shot off, or its vine with it.</summary>
    public void Destroy(int i)
    {
        if (_state[i] == PodState.Destroyed)
        {
            return;
        }
        if (_state[i] == PodState.Full)
        {
            Full--;
        }
        _state[i] = PodState.Destroyed;
        Destroyed++;
        PodDestroyed?.Invoke(i);
    }

    public override void _PhysicsProcess(double delta)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionCheck);
        // A vine's parts have their voxels a frame after it enters.
        foreach (Vine vine in _vines)
        {
            if (!vine.Anchored && !vine.Gone && vine.Body != null && IsInstanceValid(vine.Body) && vine.Body.Parts.Count > 0)
            {
                Anchor(vine);
            }
        }
        _recheck -= (float)delta;
        if (_recheck > 0.0f)
        {
            return;
        }
        _recheck = Stats.RecheckSeconds;
        foreach (Vine vine in _vines)
        {
            if (vine.Gone)
            {
                continue;
            }
            if (!IsInstanceValid(vine.Root) || !vine.Root.IsInsideTree() || vine.Body == null || !IsInstanceValid(vine.Body))
            {
                vine.Gone = true;
                for (int i = vine.First; i < vine.First + vine.Count; i++)
                {
                    Destroy(i);
                }
                continue;
            }
            if (!vine.Anchored)
            {
                continue;
            }
            int version = 0;
            foreach (VoxelPart part in vine.Body.Parts)
            {
                version = unchecked((version * 31) + (part.Grid?.Version ?? 0));
            }
            if (version != vine.Version)
            {
                vine.Version = version;
                for (int i = vine.First; i < vine.First + vine.Count; i++)
                {
                    if (_state[i] != PodState.Destroyed && !Held(i))
                    {
                        Destroy(i);
                    }
                }
            }
        }
    }

    // Each pod's voxel: the solid one nearest its marker, in whichever part
    // holds the marker.
    private void Anchor(Vine vine)
    {
        vine.Anchored = true;
        int reach = Stats.AnchorSearchVoxels;
        for (int i = vine.First; i < vine.First + vine.Count; i++)
        {
            _part[i] = null;
            foreach (VoxelPart part in vine.Body.Parts)
            {
                VoxelGrid grid = part.Grid;
                if (grid == null)
                {
                    continue;
                }
                Vector3 v = grid.ToVox(part.ToLocal(_built[i]));
                if (v.X < -reach || v.Y < -reach || v.Z < -reach
                    || v.X > grid.Size.X + reach || v.Y > grid.Size.Y + reach || v.Z > grid.Size.Z + reach)
                {
                    continue;
                }
                var at = new Vector3I(Mathf.FloorToInt(v.X), Mathf.FloorToInt(v.Y), Mathf.FloorToInt(v.Z));
                float best = float.MaxValue;
                for (int z = -reach; z <= reach; z++)
                {
                    for (int y = -reach; y <= reach; y++)
                    {
                        for (int x = -reach; x <= reach; x++)
                        {
                            float d = (x * x) + (y * y) + (z * z);
                            if (d < best && grid.At(at.X + x, at.Y + y, at.Z + z) != 0)
                            {
                                best = d;
                                _part[i] = part;
                                _cell[i] = new Vector3I(at.X + x, at.Y + y, at.Z + z);
                            }
                        }
                    }
                }
                if (_part[i] != null)
                {
                    break;
                }
            }
        }
    }

    // Is its voxel still there?
    private bool Held(int i)
    {
        VoxelPart part = _part[i];
        if (part == null)
        {
            return true;
        }
        if (!IsInstanceValid(part) || part.Grid == null)
        {
            return false;
        }
        Vector3I c = _cell[i];
        return part.Grid.At(c.X, c.Y, c.Z) != 0;
    }

    /// <summary>For <c>bench.gd</c> and the checks: how many pods, and in what state.</summary>
    public string Summary()
    {
        int empty = 0, regrowing = 0, unheld = 0;
        for (int i = 0; i < Count; i++)
        {
            empty += _state[i] == PodState.Empty ? 1 : 0;
            regrowing += _state[i] == PodState.Regrowing ? 1 : 0;
            unheld += _part[i] == null ? 1 : 0;
        }
        return FormattableString.Invariant(
            $"[pods] {Count} on {_vines.Count} vines: {Full} full, {empty} empty, {regrowing} regrowing, {Destroyed} destroyed; {unheld} hang from no voxel");
    }

    public void PrintBenchStatus() => GD.Print(Summary());
}
