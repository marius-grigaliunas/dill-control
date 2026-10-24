using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Enemies;
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
/// - A pickle hangs from every full pod, drawn as the horde draws its own:
///   instanced, in world space, no node a pickle and no shadows. Pods are
///   drawn by square cells of the ground (<see cref="PodStats.CellM"/>), each
///   a MultiMesh of capsules near and one of five-sided columns further off,
///   handed over by visibility range. A cell's buffer is written again only
///   when one of its pods changes; the bend and the swing are the shader's
///   (<c>pickle_hang</c>), each pickle bending by its vine's flex row, which
///   it carries in its custom data.
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
        public Cell Cell;
    }

    // A square of the ground and the pickles hanging over it.
    private sealed class Cell
    {
        public readonly List<int> Vines = new();
        public MultiMesh Near, Far;
        public float[] Buffer = Array.Empty<float>();
        public Aabb Bounds;
        public bool HasBounds, Dirty;
    }

    // Per instance: a transform's 12 floats, then its custom data (the flex row).
    private const int FloatsPerPickle = 16;

    public const string ShaderPath = "res://shaders/pickle_hang.gdshader";
    public const string FarShaderPath = "res://shaders/pickle_hang_far.gdshader";

    private static readonly int SectionDraw = FrameProfiler.Section("pods draw");

    private readonly Dictionary<Vector2I, Cell> _cells = new();
    private readonly List<Cell> _dirty = new();
    private bool _gridSet;
    private Vector3 _gridOrigin, _gridX, _gridZ;
    private ShaderMaterial _material, _farMaterial;
    private Mesh _nearMesh, _farMesh;
    private bool _hidden;

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
        // For a bench's A/B: the pods, with nothing hanging from them drawn.
        _hidden = Array.IndexOf(OS.GetCmdlineUserArgs(), "pickles=0") >= 0;
        AgentKindStats pickle = Stats.Pickle;
        float radius = pickle?.Radius ?? 0.35f, height = pickle?.Height ?? 1.8f;
        _material = Material(ShaderPath, pickle, height);
        _farMaterial = Material(FarShaderPath, pickle, height);
        // The horde's two shapes (HordeManager.BuildMultiMeshes).
        _nearMesh = new CapsuleMesh { Radius = radius, Height = height, RadialSegments = 6, Rings = 1 };
        _farMesh = new CylinderMesh { TopRadius = radius * 0.75f, BottomRadius = radius, Height = height, RadialSegments = 5, Rings = 0 };
    }

    private ShaderMaterial Material(string path, AgentKindStats pickle, float height)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>(path) };
        material.SetShaderParameter("colour", pickle?.Colour ?? new Color(0.36f, 0.62f, 0.18f));
        material.SetShaderParameter("half_height", height * 0.5f);
        material.SetShaderParameter("sway_m", Stats.SwayM);
        material.SetShaderParameter("sway_hz", Stats.SwayHz);
        FlexField.Track(material);
        return material;
    }

    public override void _ExitTree()
    {
        foreach (ShaderMaterial material in new[] { _material, _farMaterial })
        {
            if (material != null)
            {
                FlexField.Untrack(material);
            }
        }
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

        Cell cell = CellAt(root.GlobalPosition);
        vine.Cell = cell;
        cell.Vines.Add(index);
        for (int i = vine.First; i < vine.First + vine.Count; i++)
        {
            cell.Bounds = cell.HasBounds ? cell.Bounds.Expand(_built[i]) : new Aabb(_built[i], Vector3.Zero);
            cell.HasBounds = true;
        }
        MarkDirty(cell);
        return index;
    }

    // The cell a vine's foot stands in: squares of the ground about the
    // first vine's, across its up.
    private Cell CellAt(Vector3 foot)
    {
        if (!_gridSet)
        {
            _gridSet = true;
            _gridOrigin = foot;
            Vector3 up = WorldUp.At(foot);
            _gridX = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
            _gridZ = _gridX.Cross(up).Normalized();
        }
        Vector3 d = foot - _gridOrigin;
        var key = new Vector2I(Mathf.FloorToInt(d.Dot(_gridX) / Stats.CellM), Mathf.FloorToInt(d.Dot(_gridZ) / Stats.CellM));
        if (!_cells.TryGetValue(key, out Cell cell))
        {
            cell = new Cell();
            _cells[key] = cell;
        }
        return cell;
    }

    private void MarkDirty(Cell cell)
    {
        if (!cell.Dirty)
        {
            cell.Dirty = true;
            _dirty.Add(cell);
        }
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
        MarkDirty(_vines[_vine[i]].Cell);
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

    public override void _Process(double delta)
    {
        if (_dirty.Count == 0 || _hidden)
        {
            return;
        }
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionDraw);
        for (int c = _dirty.Count - 1; c >= 0; c--)
        {
            Cell cell = _dirty[c];
            if (Draw(cell))
            {
                cell.Dirty = false;
                _dirty.RemoveAt(c);
            }
        }
    }

    // A cell's pickles into its buffers: one for every full pod. Not before
    // each of its vines has its flex row, which its pickles bend by.
    private bool Draw(Cell cell)
    {
        int count = 0;
        foreach (int v in cell.Vines)
        {
            Vine vine = _vines[v];
            if (!vine.Gone && vine.Flex != null && IsInstanceValid(vine.Flex) && vine.Flex.Row < 0)
            {
                return false;
            }
            for (int i = vine.First; i < vine.First + vine.Count; i++)
            {
                count += _state[i] == PodState.Full ? 1 : 0;
            }
        }
        if (cell.Near == null)
        {
            Make(cell);
        }
        if (cell.Buffer.Length < count * FloatsPerPickle)
        {
            cell.Buffer = new float[count * FloatsPerPickle];
        }
        float half = (Stats.Pickle?.Height ?? 1.8f) * 0.5f;
        float[] b = cell.Buffer;
        int o = 0;
        foreach (int v in cell.Vines)
        {
            Vine vine = _vines[v];
            float row = vine.Flex != null && IsInstanceValid(vine.Flex) ? vine.Flex.Row : 0;
            for (int i = vine.First; i < vine.First + vine.Count; i++)
            {
                if (_state[i] != PodState.Full)
                {
                    continue;
                }
                Vector3 up = WorldUp.At(_built[i]);
                Vector3 side = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
                // Turned about its up as its place says: no two in a row alike.
                float turn = Mathf.PosMod(_built[i].Dot(new Vector3(12.9898f, 78.233f, 37.719f)), Mathf.Tau);
                Vector3 right = (side * Mathf.Cos(turn)) + (side.Cross(up) * Mathf.Sin(turn));
                Vector3 back = right.Cross(up);
                Vector3 at = _built[i] - (up * half);
                b[o + 0] = right.X;
                b[o + 1] = up.X;
                b[o + 2] = back.X;
                b[o + 3] = at.X;
                b[o + 4] = right.Y;
                b[o + 5] = up.Y;
                b[o + 6] = back.Y;
                b[o + 7] = at.Y;
                b[o + 8] = right.Z;
                b[o + 9] = up.Z;
                b[o + 10] = back.Z;
                b[o + 11] = at.Z;
                b[o + 12] = row;
                b[o + 13] = 0.0f;
                b[o + 14] = 0.0f;
                b[o + 15] = 0.0f;
                o += FloatsPerPickle;
            }
        }
        foreach (MultiMesh multi in new[] { cell.Near, cell.Far })
        {
            if (multi.InstanceCount != count)
            {
                multi.InstanceCount = count;
            }
            if (count > 0)
            {
                RenderingServer.MultimeshSetBuffer(multi.GetRid(), new ReadOnlySpan<float>(b, 0, count * FloatsPerPickle));
            }
        }
        return true;
    }

    // Its two draws: capsules near its middle, columns further off.
    private void Make(Cell cell)
    {
        // Room for the vines to bend and the pickles to hang.
        Aabb bounds = cell.Bounds.Grow(25.0f);
        cell.Near = NewMulti(_nearMesh, bounds);
        cell.Far = NewMulti(_farMesh, bounds);
        AddChild(NewInstance("PicklesNear", cell.Near, _material, bounds, 0.0f, Stats.NearMetres + VoxelBody.HandoverOverlapM));
        AddChild(NewInstance("PicklesFar", cell.Far, _farMaterial, bounds, Stats.NearMetres, Stats.DrawMetres));
    }

    private static MultiMesh NewMulti(Mesh mesh, Aabb bounds) => new()
    {
        TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
        UseCustomData = true,
        Mesh = mesh,
        CustomAabb = bounds,
    };

    private static MultiMeshInstance3D NewInstance(string name, MultiMesh multi, Material material, Aabb bounds, float begin, float end) => new()
    {
        Name = name,
        Multimesh = multi,
        MaterialOverride = material,
        CustomAabb = bounds,
        TopLevel = true,
        // As the horde's: shadows on thousands of capsules are the dearest
        // thing an integrated GPU can be asked for.
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        VisibilityRangeBegin = begin,
        VisibilityRangeEnd = end,
    };

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
