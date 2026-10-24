using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Enemies;
using DillControl.Foliage;
using DillControl.Horde;
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
/// - A hanging pickle can be shot: a pool of <see cref="PodHitProxy"/> is
///   handed each few ticks to the full pods nearest the line the player
///   aims along, and a blast on the bus (<see cref="EventBus.AreaBlast"/>)
///   reaches every pickle in it. Killed, it drops dead (a body that falls
///   and puffs, under a cap) and its pod is left empty. A pod itself goes
///   with the voxel it hangs from, shot away like any other.
/// - The colony never strikes first (docs/decisions/0029). Harm to any of
///   it wakes all of it: a hanging pickle hit, a vine or a leaf hit, a blast
///   in the nest. Then over <see cref="PodStats.WakeSeconds"/> every full
///   pod lets its pickle drop, nearest the harm first, into the
///   <see cref="Horde"/> (<see cref="HordeManager.SpawnOrigin.Pod"/>).
/// - An empty pod grows its pickle again (docs/decisions/0034), the longest
///   empty first, so many at once, slower on a damaged vine; it is drawn
///   small while it grows. In a colony that is awake a regrown pickle hangs
///   a moment and then drops to fight. A destroyed pod never regrows: only
///   destroying the pods ends the colony.
///
/// Its numbers are <see cref="Stats"/>.
/// </summary>
[GlobalClass]
public partial class PodField : Node
{
    /// <summary>Asleep until harmed; waking while its pickles drop; awake once they all have.</summary>
    public enum ColonyState
    {
        Asleep,
        Waking,
        Awake,
    }

    private static readonly int SectionCheck = FrameProfiler.Section("pods");

    [Export] public PodStats Stats { get; set; }
    /// <summary>Where a woken colony's pickles drop into. None: they stay hanging.</summary>
    [Export] public HordeDirector Horde { get; set; }

    public ColonyState Colony { get; private set; } = ColonyState.Asleep;
    /// <summary>The colony was harmed, here: it starts to wake.</summary>
    public event Action<Vector3> Aggravated;
    /// <summary>Pickles dropped into the horde.</summary>
    public int Dropped { get; private set; }
    /// <summary>Pickles grown again on emptied pods.</summary>
    public int Regrown { get; private set; }
    /// <summary>Pods growing a pickle now.</summary>
    public int Regrowing => _regrowing.Count;

    /// <summary>Pod <c>i</c> is gone for good. After its state is <see cref="PodState.Destroyed"/>.</summary>
    public event Action<int> PodDestroyed;
    /// <summary>Pod <c>i</c>'s pickle was killed hanging on it; the pod is empty.</summary>
    public event Action<int> PickleKilled;

    public int Count { get; private set; }
    public int Full { get; private set; }
    public int Destroyed { get; private set; }
    /// <summary>Pickles killed on their pods.</summary>
    public int Killed { get; private set; }
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
        public Action<DamageInfo> OnDamaged;
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
    private StandardMaterial3D _fallingMaterial;
    private PodHitProxy[] _proxies = Array.Empty<PodHitProxy>();
    private int[] _bestPod;
    private float[] _bestScore;
    private int _ticks;
    private int _falling;
    private readonly HashSet<int> _chosen = new();
    // The order a waking colony drops in, how far along it is, and for how long.
    private int[] _wakeOrder = Array.Empty<int>();
    private int _woken;
    private float _waking;
    private bool _hidden;

    private readonly List<Vine> _vines = new();
    // Per pod: where it hangs as built, in world space; its vine; its state.
    private Vector3[] _built = Array.Empty<Vector3>();
    private int[] _vine = Array.Empty<int>();
    private PodState[] _state = Array.Empty<PodState>();
    private float[] _health = Array.Empty<float>();
    // Seconds left: of regrowing, or of hanging before a regrown pickle drops.
    private float[] _timer = Array.Empty<float>();
    // Emptied pods, the longest empty first; those regrowing; regrown ones
    // waiting to drop into an awake colony's fight.
    private readonly Queue<int> _emptied = new();
    private readonly List<int> _regrowing = new();
    private readonly List<int> _hanging = new();
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
        _fallingMaterial = new StandardMaterial3D
        {
            AlbedoColor = pickle?.Colour ?? new Color(0.36f, 0.62f, 0.18f),
            Roughness = 0.9f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon,
        };
        _proxies = new PodHitProxy[Stats.HitProxies];
        for (int k = 0; k < _proxies.Length; k++)
        {
            var proxy = new PodHitProxy { Name = $"HitProxy{k}", Field = this };
            AddChild(proxy);
            proxy.Build(radius, height);
            _proxies[k] = proxy;
        }
        _bestPod = new int[_proxies.Length];
        _bestScore = new float[_proxies.Length];
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast += OnAreaBlast;
        }
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
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
        }
        foreach (Vine vine in _vines)
        {
            if (vine.OnDamaged != null && vine.Body != null && IsInstanceValid(vine.Body))
            {
                vine.Body.Damaged -= vine.OnDamaged;
            }
        }
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
            _health[i] = Stats.PickleHealth;
        }
        Count += pods.Length;
        Full += pods.Length;

        if (vine.Body != null)
        {
            vine.OnDamaged = info => Aggravate(info?.Position ?? root.GlobalPosition);
            vine.Body.Damaged += vine.OnDamaged;
        }

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
        Array.Resize(ref _health, size);
        Array.Resize(ref _timer, size);
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
        Proxies();
        Wake((float)delta);
        Regrow((float)delta);
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
                count += _state[i] == PodState.Full || _state[i] == PodState.Regrowing ? 1 : 0;
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
                if (_state[i] != PodState.Full && _state[i] != PodState.Regrowing)
                {
                    continue;
                }
                float size = _state[i] == PodState.Regrowing ? Stats.RegrowingScale : 1.0f;
                Vector3 up = WorldUp.At(_built[i]);
                Vector3 side = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
                // Turned about its up as its place says: no two in a row alike.
                float turn = Mathf.PosMod(_built[i].Dot(new Vector3(12.9898f, 78.233f, 37.719f)), Mathf.Tau);
                Vector3 right = (side * Mathf.Cos(turn)) + (side.Cross(up) * Mathf.Sin(turn));
                Vector3 back = right.Cross(up) * size;
                Vector3 at = _built[i] - (up * (half * size));
                right *= size;
                up *= size;
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

    /// <summary>A shot on pod <paramref name="i"/>'s pickle (through its
    /// proxy, or a blast): at no health left it drops dead.</summary>
    public void HitPickle(int i, DamageInfo info)
    {
        if (i < 0 || i >= Count || _state[i] != PodState.Full || info == null)
        {
            return;
        }
        Aggravate(info.Position);
        _health[i] -= info.Amount;
        if (_health[i] <= 0.0f)
        {
            info.Killed = true;
            Kill(i, info.Direction);
        }
    }

    // Killed hanging: it drops dead, and its pod is empty.
    private void Kill(int i, Vector3 push)
    {
        _state[i] = PodState.Empty;
        _emptied.Enqueue(i);
        Full--;
        Killed++;
        MarkDirty(_vines[_vine[i]].Cell);
        foreach (PodHitProxy proxy in _proxies)
        {
            if (proxy.Pod == i)
            {
                proxy.Release();
            }
        }
        Drop(PickleAt(i), push);
        PickleKilled?.Invoke(i);
    }

    // Where pod i's pickle hangs now: upright along its up, its middle half
    // its height under the pod.
    private Transform3D PickleAt(int i)
    {
        Vector3 pod = Drawn(i);
        Vector3 up = WorldUp.At(pod);
        Vector3 side = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        return new Transform3D(new Basis(side, up, side.Cross(up)), pod - (up * (Stats.Pickle?.Height ?? 1.8f) * 0.5f));
    }

    // A dead pickle falls as a body under the cap, and puffs where it lands;
    // past the cap it puffs where it hung.
    private void Drop(Transform3D at, Vector3 push)
    {
        float height = Stats.Pickle?.Height ?? 1.8f;
        Color colour = _fallingMaterial.AlbedoColor;
        if (_falling >= Stats.MaxFalling)
        {
            VoxelDebris.Current?.Dust(at.Origin, at.Basis.Y, Vector3.Zero, colour, height * Stats.DustScale);
            return;
        }
        var body = new RigidBody3D
        {
            Name = "FallingPickle",
            Mass = Stats.FallingMassKg,
            // Never on anything the player collides with: see GameLayers.Litter.
            CollisionLayer = GameLayers.Mask(GameLayers.Litter),
            CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Litter),
        };
        body.AddChild(new MeshInstance3D { Mesh = _nearMesh, MaterialOverride = _fallingMaterial });
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = Stats.Pickle?.Radius ?? 0.35f, Height = height } });
        Node parent = GetTree().CurrentScene ?? GetTree().Root;
        parent.AddChild(body);
        body.GlobalTransform = at;
        body.LinearVelocity = push.LengthSquared() > 1e-6f ? push.Normalized() * 2.0f : Vector3.Zero;
        body.AngularVelocity = at.Basis.X * 2.0f;
        _falling++;
        GetTree().CreateTimer(Stats.FallingSeconds).Timeout += () =>
        {
            _falling--;
            if (IsInstanceValid(body))
            {
                VoxelDebris.Current?.Dust(body.GlobalPosition, WorldUp.At(body.GlobalPosition), body.LinearVelocity, colour, height * Stats.DustScale);
                body.QueueFree();
            }
        };
    }

    // Every pickle in a blast takes its share: full inside its inner radius,
    // down to its least share at its edge. A line (the lance) is a blast
    // along it.
    private void OnAreaBlast(BlastInfo blast)
    {
        // A blast that harms nothing (LevelRoot's shader warm-up) wakes nothing.
        if (blast == null || Count == 0 || blast.Damage <= 0.0f)
        {
            return;
        }
        Vector3 end = blast.IsLine ? blast.LineEnd : blast.Centre;
        var box = new Aabb(blast.Centre, Vector3.Zero).Expand(end).Grow(blast.Radius);
        if (Colony == ColonyState.Asleep)
        {
            foreach (Cell cell in _cells.Values)
            {
                if (cell.Bounds.Grow(Stats.AlarmMarginM).Intersects(box))
                {
                    Aggravate(blast.Centre);
                    break;
                }
            }
        }
        if (Full == 0)
        {
            return;
        }
        foreach (Cell cell in _cells.Values)
        {
            if (!cell.Bounds.Grow(25.0f).Intersects(box))
            {
                continue;
            }
            foreach (int v in cell.Vines)
            {
                Vine vine = _vines[v];
                for (int i = vine.First; i < vine.First + vine.Count; i++)
                {
                    if (_state[i] != PodState.Full)
                    {
                        continue;
                    }
                    Vector3 at = Drawn(i);
                    float d = blast.IsLine ? Geometry3D.GetClosestPointToSegment(at, blast.Centre, end).DistanceTo(at) : at.DistanceTo(blast.Centre);
                    if (d > blast.Radius)
                    {
                        continue;
                    }
                    float t = blast.Radius > blast.InnerRadius ? Mathf.Clamp((d - blast.InnerRadius) / (blast.Radius - blast.InnerRadius), 0.0f, 1.0f) : 0.0f;
                    var info = new DamageInfo
                    {
                        Amount = blast.Damage * Mathf.Lerp(1.0f, blast.MinMultiplier, t),
                        IsSplash = true,
                        IsExplosive = true,
                        Position = at,
                        Direction = (at - blast.Centre).Normalized(),
                        Source = blast.Source,
                    };
                    HitPickle(i, info);
                }
            }
        }
    }

    // The proxies to the full pods nearest the aim, each few ticks; between,
    // they follow their pickles as their vines bend.
    private void Proxies()
    {
        if (_proxies.Length == 0 || Full == 0)
        {
            return;
        }
        Camera3D camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return;
        }
        if (_ticks++ % Stats.ProxyEveryTicks == 0)
        {
            Choose(camera.GlobalPosition, -camera.GlobalBasis.Z);
        }
        foreach (PodHitProxy proxy in _proxies)
        {
            if (proxy.Pod >= 0)
            {
                proxy.Assign(proxy.Pod, PickleAt(proxy.Pod));
            }
        }
    }

    // The best few by how near the aim they are: across the line over along
    // it (so a pickle aimed at far off counts as much as one near), within
    // ProxyMetres and in front.
    private void Choose(Vector3 eye, Vector3 forward)
    {
        int n = _proxies.Length, found = 0, worst = 0;
        float reach = Stats.ProxyMetres, reach2 = reach * reach;
        foreach (Cell cell in _cells.Values)
        {
            if (cell.Bounds.Grow(25.0f + reach).HasPoint(eye) == false)
            {
                continue;
            }
            foreach (int v in cell.Vines)
            {
                Vine vine = _vines[v];
                for (int i = vine.First; i < vine.First + vine.Count; i++)
                {
                    if (_state[i] != PodState.Full)
                    {
                        continue;
                    }
                    Vector3 d = _built[i] - eye;
                    float along = d.Dot(forward), far2 = d.LengthSquared();
                    if (along <= 0.0f || far2 > reach2)
                    {
                        continue;
                    }
                    float near = Mathf.Max(along, 5.0f);
                    float score = (far2 - (along * along)) / (near * near);
                    if (found < n)
                    {
                        _bestPod[found] = i;
                        _bestScore[found] = score;
                        if (score > _bestScore[worst])
                        {
                            worst = found;
                        }
                        found++;
                    }
                    else if (score < _bestScore[worst])
                    {
                        _bestPod[worst] = i;
                        _bestScore[worst] = score;
                        for (int k = 0; k < n; k++)
                        {
                            if (_bestScore[k] > _bestScore[worst])
                            {
                                worst = k;
                            }
                        }
                    }
                }
            }
        }
        // A proxy that keeps its pickle keeps it; the rest are handed out.
        HashSet<int> chosen = _chosen;
        chosen.Clear();
        for (int k = 0; k < found; k++)
        {
            chosen.Add(_bestPod[k]);
        }
        foreach (PodHitProxy proxy in _proxies)
        {
            if (proxy.Pod >= 0 && !chosen.Remove(proxy.Pod))
            {
                proxy.Release();
            }
        }
        foreach (PodHitProxy proxy in _proxies)
        {
            if (proxy.Pod < 0 && chosen.Count > 0)
            {
                int pod = 0;
                foreach (int c in chosen)
                {
                    pod = c;
                    break;
                }
                chosen.Remove(pod);
                proxy.Assign(pod, PickleAt(pod));
            }
        }
    }

    /// <summary>The colony was harmed at <paramref name="at"/>: it wakes, and
    /// its pickles start to drop, the nearest there first. Once only.</summary>
    public void Aggravate(Vector3 at)
    {
        if (Colony != ColonyState.Asleep || Count == 0)
        {
            return;
        }
        Colony = ColonyState.Waking;
        _waking = 0.0f;
        _woken = 0;
        var order = new List<int>(Full);
        for (int i = 0; i < Count; i++)
        {
            if (_state[i] == PodState.Full)
            {
                order.Add(i);
            }
        }
        var distance = new float[Count];
        foreach (int i in order)
        {
            distance[i] = _built[i].DistanceSquaredTo(at);
        }
        order.Sort((a, b) => distance[a].CompareTo(distance[b]));
        _wakeOrder = order.ToArray();
        if (PerfLog.Recording)
        {
            PerfLog.Event(FormattableString.Invariant($"colony-wakes {_wakeOrder.Length} to drop"));
        }
        Aggravated?.Invoke(at);
    }

    // A share of the colony each tick, so the last pickle drops WakeSeconds
    // after the first.
    private void Wake(float dt)
    {
        if (Colony != ColonyState.Waking)
        {
            return;
        }
        _waking += dt;
        int due = Stats.WakeSeconds <= 0.0f ? _wakeOrder.Length
            : Math.Min(_wakeOrder.Length, (int)Math.Ceiling(_wakeOrder.Length * _waking / Stats.WakeSeconds));
        HordeManager horde = Horde != null && IsInstanceValid(Horde) ? Horde.Manager : null;
        for (; _woken < due; _woken++)
        {
            int i = _wakeOrder[_woken];
            if (_state[i] == PodState.Full && horde != null)
            {
                DropIntoHorde(i, horde);
            }
        }
        if (_woken >= _wakeOrder.Length)
        {
            Colony = ColonyState.Awake;
            _wakeOrder = Array.Empty<int>();
        }
    }

    // Pod i's pickle lets go: from where it hangs, an agent of the horde,
    // landing soft (a lob: the fall from the canopy is no throw). Its pod is
    // empty. With the horde full, it holds on.
    private void DropIntoHorde(int i, HordeManager horde)
    {
        Vector3 at = PickleAt(i).Origin;
        if (!horde.SpawnFlung(at, Vector3.Zero, -1, HordeManager.SpawnOrigin.Pod, lobbed: true))
        {
            return;
        }
        _state[i] = PodState.Empty;
        _emptied.Enqueue(i);
        Full--;
        Dropped++;
        MarkDirty(_vines[_vine[i]].Cell);
        foreach (PodHitProxy proxy in _proxies)
        {
            if (proxy.Pod == i)
            {
                proxy.Release();
            }
        }
    }

    // Empty pods start to regrow while there is room; regrowing ones grow;
    // in an awake colony regrown ones hang a moment and drop.
    private void Regrow(float dt)
    {
        while (_regrowing.Count < Stats.MaxRegrowing && _emptied.Count > 0)
        {
            int i = _emptied.Dequeue();
            Vine vine = _vines[_vine[i]];
            if (_state[i] != PodState.Empty || vine.Gone)
            {
                continue;
            }
            _state[i] = PodState.Regrowing;
            _timer[i] = Stats.RegrowSeconds / GrowSpeed(vine);
            _regrowing.Add(i);
            MarkDirty(vine.Cell);
        }
        for (int k = _regrowing.Count - 1; k >= 0; k--)
        {
            int i = _regrowing[k];
            if (_state[i] == PodState.Regrowing && (_timer[i] -= dt) > 0.0f)
            {
                continue;
            }
            _regrowing[k] = _regrowing[^1];
            _regrowing.RemoveAt(_regrowing.Count - 1);
            if (_state[i] != PodState.Regrowing)
            {
                continue;
            }
            _state[i] = PodState.Full;
            _health[i] = Stats.PickleHealth;
            Full++;
            Regrown++;
            MarkDirty(_vines[_vine[i]].Cell);
            if (Colony != ColonyState.Asleep)
            {
                _timer[i] = Stats.AwakeHangSeconds;
                _hanging.Add(i);
            }
        }
        HordeManager horde = Horde != null && IsInstanceValid(Horde) ? Horde.Manager : null;
        for (int k = _hanging.Count - 1; k >= 0; k--)
        {
            int i = _hanging[k];
            if (_state[i] == PodState.Full && (_timer[i] -= dt) > 0.0f)
            {
                continue;
            }
            _hanging[k] = _hanging[^1];
            _hanging.RemoveAt(_hanging.Count - 1);
            if (_state[i] == PodState.Full && horde != null)
            {
                DropIntoHorde(i, horde);
            }
        }
    }

    // How fast a vine's pods regrow against a whole one's: slower by the
    // share of its voxels it has lost, as far as DamagedSlowdown says.
    private float GrowSpeed(Vine vine)
    {
        long solid = 0, initial = 0;
        if (vine.Body != null && IsInstanceValid(vine.Body))
        {
            foreach (VoxelPart part in vine.Body.Parts)
            {
                if (IsInstanceValid(part) && part.Grid != null)
                {
                    solid += part.Grid.SolidCount;
                    initial += part.Grid.InitialCount;
                }
            }
        }
        float whole = initial > 0 ? (float)solid / initial : 1.0f;
        return Mathf.Max(0.05f, 1.0f - (Stats.DamagedSlowdown * (1.0f - whole)));
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
            $"[pods] {Count} on {_vines.Count} vines: {Full} full, {empty} empty, {regrowing} regrowing, {Destroyed} destroyed; {Killed} pickles killed on them, {Dropped} dropped, {Regrown} regrown; the colony {Colony.ToString().ToLowerInvariant()}; {unheld} hang from no voxel");
    }

    public void PrintBenchStatus() => GD.Print(Summary());

    /// <summary>For the checks: pods that found no voxel to hang from.</summary>
    public int Unheld()
    {
        int unheld = 0;
        for (int i = 0; i < Count; i++)
        {
            unheld += _part[i] == null ? 1 : 0;
        }
        return unheld;
    }
}
