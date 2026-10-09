using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DillControl.Audio;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>What a piece hangs from: what keeps its voxels in place.</summary>
public enum VoxelAnchor
{
    /// <summary>The ground: cells in its bottom layer. A building.</summary>
    Ground,

    /// <summary>Its hinge: cells near its pivot. A leg, a turret, an arm.</summary>
    Hinge,

    /// <summary>Itself: the biggest connected piece stays, the rest falls.
    /// A mech's body, a loose chunk.</summary>
    Largest,
}

/// <summary>Where a falling piece hangs on what is left of its support: a
/// point and a horizontal axis on the standing building's collision.</summary>
public readonly record struct VoxelHinge(CollisionObject3D Anchor, Vector3 Pivot, Vector3 Axis);

/// <summary>
/// One destructible piece: a voxel grid drawn as sections, optionally given
/// box collision, carved by hits, and checked after each burst of hits for
/// what is no longer held. Whatever has come loose is lifted out and handed
/// to <see cref="VoxelDebris"/> as a falling chunk, or dust if it is small.
///
/// It sits under the node it replaces (an imported part's mesh, a chunk's
/// body) with an identity transform, so it moves with whatever moves that
/// node: a leg swinging, a chunk tumbling.
///
/// A building also carries its own weight: a layer that has lost too much of
/// what held it up gives way, its storey buckles, and what is above comes down
/// whole: straight down if the support went evenly, hanging on what is left
/// and leaning over if it went on one side. It breaks where it lands, crushing
/// through storey by storey (docs/decisions/0013, 0014).
///
/// Every voxel model in the game is made of these, which is what makes them all
/// break the same way (docs/decisions/0011).
/// </summary>
public partial class VoxelPart : Node3D
{
    private static readonly int SectionMesh = FrameProfiler.Section("voxel mesh");
    private static readonly int SectionCollision = FrameProfiler.Section("voxel collision");
    private static readonly int SectionIntegrity = FrameProfiler.Section("voxel integrity");
    private static readonly int SectionCrush = FrameProfiler.Section("voxel crush");
    private static readonly int SectionApply = FrameProfiler.Section("voxel apply");
    private static readonly int SectionRelease = FrameProfiler.Section("voxel release");

    // Registered here, not in VoxelChunk, so they exist before the first chunk
    // does: a recording's columns are fixed when it starts.
    internal static readonly int SectionChunkReady = FrameProfiler.Section("chunk ready");
    internal static readonly int SectionDamage = FrameProfiler.Section("voxel damage");
    internal static readonly int SectionRubble = FrameProfiler.Section("rubble deposit");
    internal static readonly int SectionRubbleMap = FrameProfiler.Section("rubble map");
    internal static readonly int SectionChunkAdd = FrameProfiler.Section("chunk add");
    internal static readonly int SectionChunkLand = FrameProfiler.Section("chunk land");
    internal static readonly int SectionChunkBlast = FrameProfiler.Section("chunk blast");

    public VoxelGrid Grid { get; private set; }
    public VoxelAnchor Anchor { get; private set; }
    public Color[] Palette { get; private set; }
    public float[] Emission { get; private set; }
    public Material Material { get; private set; }

    /// <summary>Gets box collision, section by section. Null for a part whose
    /// body is something else's (a mech moves on its hull).</summary>
    public CollisionObject3D CollisionOwner { get; set; }

    /// <summary>
    /// Makes a body for one section's collision, for a part whose collision is
    /// split by section (a building); <see cref="CollisionOwner"/> stays null.
    ///
    /// DECISION -- a body per section, not one for the building. With every
    /// box of a tower on one body, re-boxing one section after a hit cost
    /// 30-60 ms: Godot renumbers every other shape on each removal, and Jolt
    /// rebuilds the whole body's shape. A section's body holds a few hundred
    /// boxes, and changing it touches nothing else.
    /// </summary>
    public Func<CollisionObject3D> SectionBody { get; set; }

    /// <summary>How coarse its collision boxes are, in voxels: 1 follows every
    /// voxel; 2 boxes 2x2x2 blocks, for a loose chunk with no need to be exact.</summary>
    public int CollisionStep { get; set; } = 1;

    /// <summary>The piece's velocity, for what it throws off. Defaults to still.</summary>
    public Func<Vector3> Velocity { get; set; } = () => Vector3.Zero;

    /// <summary>Up, for dust and for what is thrown. Defaults to world up.</summary>
    public Func<Vector3> Up { get; set; } = () => Vector3.Up;

    /// <summary>Raised once, when nothing of it is left or nothing holds it.</summary>
    public event Action<VoxelPart> Emptied;

    /// <summary>Raised after every carve, with the voxels it took.</summary>
    public event Action<VoxelPart, int> Carved;

    public float Health01 => Grid == null || Grid.InitialCount == 0 ? 0.0f : (float)Grid.SolidCount / Grid.InitialCount;

    public bool IsEmpty => Grid == null || Grid.SolidCount <= 0 || _emptied;

    private VoxelDestructionStats _stats;
    private Vector3I _sectionCount;
    private MeshInstance3D[] _meshes;
    private List<uint>[] _shapeOwners;
    private CollisionObject3D[] _sectionBodies;

    // Box shapes by size, in millimetres: shared by every body that needs one
    // that size, so re-boxing a section makes no new resources.
    private static readonly Dictionary<Vector3I, BoxShape3D> BoxShapes = new();
    private bool[] _meshDirty;
    private int[] _version;
    private bool[] _inFlight;
    private readonly List<SectionJob> _jobs = new();
    private MeshInstance3D[] _adopt;
    private Vector3 _adoptOffset;

    /// <summary>Builds every section where it is made, all at once: a building,
    /// at load. False for a loose piece made mid-game, whose meshes are built
    /// on worker threads like any re-mesh. Its collision is built at once
    /// either way: a body that starts without any falls into what it stood on.</summary>
    public bool BuildAtOnce { get; set; } = true;

    /// <summary>Whether its sections cast shadows. Off for small loose pieces:
    /// their shadows cannot be seen at this scale, and each shadow is another
    /// draw of the piece.</summary>
    public bool CastShadows { get; set; } = true;
    private bool _anyDirty;
    private float _integrityTimer = -1.0f;
    private bool _emptied;
    private readonly List<VoxelGrid.Removed> _removed = new();

    // As built, per layer (Ground only): how many cells, and half the narrower
    // side of their footprint, in voxels. What the load check measures against.
    private int[] _builtCount;
    private float[] _builtHalf;

    public void Setup(VoxelGrid grid, VoxelAnchor anchor, Color[] palette, float[] emission, Material material,
        VoxelDestructionStats stats)
    {
        Grid = grid;
        Anchor = anchor;
        Palette = palette;
        Emission = emission;
        Material = material;
        _stats = stats;
        if (anchor == VoxelAnchor.Ground)
        {
            MeasureBuilt();
        }
    }

    private void MeasureBuilt()
    {
        int layers = Grid.Size.Z;
        _builtCount = new int[layers];
        _builtHalf = new float[layers];
        var min = new Vector2I[layers];
        var max = new Vector2I[layers];
        for (int z = 0; z < layers; z++)
        {
            min[z] = new Vector2I(int.MaxValue, int.MaxValue);
            max[z] = new Vector2I(int.MinValue, int.MinValue);
        }
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            if (Grid.Cells[i] == 0)
            {
                continue;
            }
            Vector3I c = Grid.CellOf(i);
            _builtCount[c.Z]++;
            min[c.Z] = new Vector2I(Math.Min(min[c.Z].X, c.X), Math.Min(min[c.Z].Y, c.Y));
            max[c.Z] = new Vector2I(Math.Max(max[c.Z].X, c.X), Math.Max(max[c.Z].Y, c.Y));
        }
        for (int z = 0; z < layers; z++)
        {
            if (_builtCount[z] > 0)
            {
                Vector2I size = max[z] - min[z] + Vector2I.One;
                _builtHalf[z] = Mathf.Max(1.0f, Math.Min(size.X, size.Y) * 0.5f);
            }
        }
    }

    public override void _Ready()
    {
        if (Grid == null)
        {
            return;
        }

        int edge = Math.Max(4, _stats.SectionEdge);
        _sectionCount = new Vector3I(
            (Grid.Size.X + edge - 1) / edge, (Grid.Size.Y + edge - 1) / edge, (Grid.Size.Z + edge - 1) / edge);
        int sections = _sectionCount.X * _sectionCount.Y * _sectionCount.Z;
        _meshes = new MeshInstance3D[sections];
        _shapeOwners = new List<uint>[sections];
        _meshDirty = new bool[sections];
        _version = new int[sections];
        _inFlight = new bool[sections];
        for (int s = 0; s < sections; s++)
        {
            _shapeOwners[s] = new List<uint>();
            if (Grid.SolidCount == 0)
            {
                // Nothing yet (a rubble heap starts empty): nothing to build.
                continue;
            }
            bool adopted = _adopt != null && s < _adopt.Length && IsInstanceValid(_adopt[s]);
            if (adopted)
            {
                // Taken over from what it broke off: the mesh is already right.
                MeshInstance3D instance = _adopt[s];
                instance.GetParent()?.RemoveChild(instance);
                AddChild(instance);
                instance.Transform = new Transform3D(Basis.Identity, -_adoptOffset);
                _meshes[s] = instance;
            }

            if (_build != null)
            {
                // Built on the worker that worked out the split: collision now,
                // meshes as the frame's allowance has room.
                SetBoxes(s, _build.Boxes[s]);
                if (!adopted && _build.Meshes[s] != null)
                {
                    // Drawn the moment it is made: what it came from is
                    // waiting on it (SpawnBatch). Within the spawn allowance.
                    SetMesh(s, _build.Meshes[s]);
                }
            }
            else if (adopted)
            {
                RebuildCollision(s);
            }
            else if (BuildAtOnce)
            {
                RebuildSection(s);
            }
            else
            {
                RebuildCollision(s);
                _meshDirty[s] = true;
                _anyDirty = true;
            }
        }
        _adopt = null;
        _build = null;
    }

    private ChunkBuild _build;

    /// <summary>Before it is added: its collision and meshes, already built.</summary>
    public void Prebuilt(ChunkBuild build) => _build = build;

    /// <summary>Before it is added: meshes to take over rather than build, one
    /// per section (null where there is none), made by a part whose sections
    /// line up with these, and where that part's origin is in this one's space.</summary>
    public void Adopt(MeshInstance3D[] meshes, Vector3 offset)
    {
        _adopt = meshes;
        _adoptOffset = offset;
    }

    /// <summary>
    /// One section re-meshed and re-boxed on a worker thread, from a copy of
    /// its cells and a one-cell border, so the grid can change meanwhile. Its
    /// result is used only if the section has not changed again since.
    /// </summary>
    private sealed class SectionJob
    {
        public int Section;
        public int Version;
        public bool WithMesh;
        public VoxMesher.Arrays Mesh;
        public List<(Vector3 centre, Vector3 size)> Boxes;
        public volatile bool Done;
    }

    private void Submit(int s, bool mesh)
    {
        (Vector3I min, Vector3I max) = SectionBounds(s);
        Vector3I lo = (min - Vector3I.One).Max(Vector3I.Zero);
        Vector3I hi = (max + Vector3I.One).Min(Grid.Size);
        Vector3I size = hi - lo;
        // Pooled: a collapse re-meshes sections by the dozen, and a fresh copy
        // each time kept the collector running twice a crush.
        byte[] cells = System.Buffers.ArrayPool<byte>.Shared.Rent(size.X * size.Y * size.Z);
        for (int z = 0; z < size.Z; z++)
            for (int y = 0; y < size.Y; y++)
            {
                Array.Copy(Grid.Cells, Grid.Index(lo.X, lo.Y + y, lo.Z + z), cells, size.X * (y + size.Y * z), size.X);
            }

        var job = new SectionJob { Section = s, Version = _version[s], WithMesh = mesh };
        _jobs.Add(job);
        _inFlight[s] = true;
        Vector3 origin = Grid.Origin - new Vector3(lo.X, lo.Y, lo.Z);
        Vector3I from = min - lo, to = max - lo;
        float voxel = Grid.VoxelSize;
        int step = CollisionStep;
        bool boxes = CollisionOwner != null || SectionBody != null;
        Color[] palette = Palette;
        float[] emission = Emission;
        VoxelWorkers.Run(() =>
        {
            try
            {
                if (mesh)
                {
                    job.Mesh = VoxMesher.BuildArrays(cells, size, from, to, palette, emission, origin, voxel, -1);
                }
                if (boxes)
                {
                    job.Boxes = new List<(Vector3, Vector3)>();
                    new VoxelGrid(size, cells, origin, voxel).Boxes(from, to, step, job.Boxes);
                }
            }
            catch (Exception exception)
            {
                GD.PushError($"VoxelPart: re-meshing a section failed: {exception}");
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(cells);
                job.Done = true;
                VoxelBudget.ReleaseWorker();
            }
        });
    }


    // ---------------- Damage ----------------

    /// <summary>
    /// Takes a sphere out, world space. Dust where it went, the touched
    /// sections re-meshed next frame, and a structural check after the
    /// burst. Returns the voxels removed.
    /// </summary>
    public int Carve(Vector3 worldCentre, float radius)
    {
        if (IsEmpty)
        {
            return 0;
        }

        _removed.Clear();
        int count = Grid.Carve(ToLocal(worldCentre), radius, _removed, out Vector3I min, out Vector3I max);
        if (count == 0)
        {
            return 0;
        }

        MarkDirty(min, max);
        Dust(_removed);
        _integrityTimer = _stats.IntegrityDelay;
        Carved?.Invoke(this, count);
        if (Grid.SolidCount <= 0)
        {
            RaiseEmptied();
        }
        return count;
    }

    /// <summary>
    /// Takes a horizontal slit out, <paramref name="height"/> metres up and
    /// <paramref name="thickness"/> thick, through the given share of its width
    /// from its -X side. A clean, repeatable cut for benches.
    /// </summary>
    public void Cut(float height, float thickness, float share)
    {
        if (IsEmpty)
        {
            return;
        }
        int z0 = Mathf.RoundToInt(height / Grid.VoxelSize);
        int z1 = Math.Min(Grid.Size.Z, z0 + Math.Max(1, Mathf.RoundToInt(thickness / Grid.VoxelSize)));
        int x1 = Mathf.RoundToInt(Grid.Size.X * share);
        var cells = new List<int>();
        for (int z = z0; z < z1; z++)
            for (int y = 0; y < Grid.Size.Y; y++)
                for (int x = 0; x < x1; x++)
                {
                    int i = Grid.Index(x, y, z);
                    if (Grid.Cells[i] != 0)
                    {
                        cells.Add(i);
                    }
                }
        _removed.Clear();
        Grid.Remove(cells, _removed);
        CollapseDust(_removed);
        MarkAllDirty();
        RebuildCollisionNow(new Vector3I(0, 0, z0), new Vector3I(x1, Grid.Size.Y - 1, z1));
        _integrityTimer = _stats.IntegrityDelay;
    }

    /// <summary>The first solid cell a world-space ray meets, as a world point.</summary>
    public bool Raycast(Vector3 from, Vector3 direction, float maxDistance, out Vector3 point)
    {
        point = default;
        if (IsEmpty)
        {
            return false;
        }
        Vector3 localFrom = ToLocal(from);
        Vector3 localDirection = GlobalBasis.Inverse() * direction;
        if (!Grid.Raycast(localFrom, localDirection, maxDistance, out Vector3I cell))
        {
            return false;
        }
        point = ToGlobal(Grid.CellCentre(cell.X, cell.Y, cell.Z));
        return true;
    }

    /// <summary>Nearest point of the solid box to a world point.</summary>
    public Vector3 NearestPoint(Vector3 from)
    {
        if (!Grid.SolidBounds(out Aabb box))
        {
            return GlobalPosition;
        }
        Vector3 local = ToLocal(from);
        Vector3 clamped = local.Clamp(box.Position, box.End);
        return ToGlobal(clamped);
    }

    /// <summary>
    /// Splits along a plane through its middle (node space normal): the voxels
    /// on the plane go to dust, what is left either side falls as chunks. How
    /// a loose piece breaks when it lands hard.
    /// </summary>
    public void Shatter(Vector3 localNormal, Vector3 push)
    {
        if (IsEmpty)
        {
            return;
        }
        Vector3 n = VoxSpace.FromGodot(localNormal.Normalized());
        _removed.Clear();
        for (int i = 0; i < Grid.Cells.Length; i++)
        {
            if (Grid.Cells[i] == 0)
            {
                continue;
            }
            Vector3I c = Grid.CellOf(i);
            float d = (new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - Grid.Origin).Dot(n);
            if (Mathf.Abs(d) < 0.6f)
            {
                _removed.Add(new VoxelGrid.Removed(Grid.CellCentre(c.X, c.Y, c.Z), Grid.Cells[i]));
                Grid.Carve(Grid.CellCentre(c.X, c.Y, c.Z), Grid.VoxelSize * 0.1f, null, out _, out _);
            }
        }
        Dust(_removed);
        BreakAway(push);
    }

    /// <summary>
    /// Breaks into blocks about half its own size, each thrown a little apart,
    /// in a burst of dust: how a big loose piece comes apart when it lands
    /// hard. With <paramref name="snap"/>, it breaks across its length only,
    /// into segments of about <see cref="VoxelDestructionStats.SnapLength"/>:
    /// how a fallen tower lands on its side. False when it is too small to
    /// split, and nothing was done.
    ///
    /// Worked out on a worker thread: it comes apart a frame or two after it
    /// is asked, lying where it landed meanwhile.
    /// </summary>
    public bool Crumble(Vector3 push, bool snap = false)
    {
        if (IsEmpty || _plan != null || VoxelSplit.TooSmallToSplit(Grid, _stats, snap))
        {
            return false;
        }
        Schedule(snap ? VoxelBreakup.Snap : VoxelBreakup.Crumble, push);
        return true;
    }

    /// <summary>Turns every voxel left into loose chunks: the part is shot off
    /// or its owner destroyed. At once, on this thread.</summary>
    public void BreakAway(Vector3 push)
    {
        if (IsEmpty)
        {
            return;
        }
        Apply(Splitter(Grid).Breakup(VoxelBreakup.Fracture), push);
    }

    /// <summary>
    /// Everything has gone: its collision goes now, in one step, rather than
    /// being re-boxed piece by piece as each one leaves (which cost a crumbling
    /// block 30 ms), and before the pieces' own collision comes up inside it.
    /// </summary>
    private void ClearCollision()
    {
        if (CollisionOwner != null)
        {
            CollisionOwner.CollisionLayer = 0;
            CollisionOwner.CollisionMask = 0;
            // It stays drawn until its pieces are there; with no collision it
            // must not fall through the ground meanwhile.
            if (CollisionOwner is RigidBody3D body)
            {
                body.Freeze = true;
            }
        }
        if (_sectionBodies == null)
        {
            return;
        }
        foreach (CollisionObject3D body in _sectionBodies)
        {
            if (body != null)
            {
                PhysicsServer3D.BodyClearShapes(body.GetRid());
            }
        }
    }

    // ---------------- Per frame ----------------

    public override void _Process(double delta)
    {
        if (Grid == null)
        {
            return;
        }

        if (_plan != null && _plan.IsCompleted)
        {
            FinishPlan();
        }

        if (_integrityTimer >= 0.0f)
        {
            _integrityTimer -= (float)delta;
            if (_integrityTimer < 0.0f && (_plan != null || !VoxelBudget.TakeIntegrityCheck(_stats)))
            {
                // One plan at a time; or over the frame's allowance.
                _integrityTimer = 0.0f;
            }
            else if (_integrityTimer < 0.0f && !IsEmpty)
            {
                Schedule(null, Vector3.Zero);
            }
        }

        // Hand over what the workers have finished, within the frame's
        // allowance. A result for a section that has changed since is dropped:
        // the section is dirty again and goes back to them.
        for (int k = 0; k < _jobs.Count; k++)
        {
            SectionJob job = _jobs[k];
            if (!job.Done)
            {
                continue;
            }
            if (job.Version == _version[job.Section] && !VoxelBudget.TakeApply(_stats))
            {
                break;
            }
            _jobs.RemoveAt(k--);
            _inFlight[job.Section] = false;
            if (job.Version != _version[job.Section])
            {
                job.Mesh?.Release();
                continue;
            }
            long start = Stopwatch.GetTimestamp();
            using (FrameProfiler.Begin(SectionApply))
            {
                if (job.WithMesh)
                {
                    SetMesh(job.Section, job.Mesh);
                }
                if (job.Boxes != null)
                {
                    SetBoxes(job.Section, job.Boxes);
                }
            }
            VoxelBudget.Spent((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        }

        if (!_anyDirty)
        {
            return;
        }
        bool remaining = false;
        for (int s = 0; s < _meshDirty.Length; s++)
        {
            if (!_meshDirty[s])
            {
                continue;
            }
            if (_inFlight[s] || !VoxelBudget.TakeWorker(_stats))
            {
                remaining = true;
                continue;
            }
            _meshDirty[s] = false;
            Submit(s, mesh: true);
        }
        _anyDirty = remaining;
    }

    // ---------------- Structure ----------------

    private Task<SplitPlan> _plan;
    private byte[] _snapshot;
    private VoxelBreakup? _planBreakup;
    private Vector3 _planPush;

    /// <summary>A planner for this part's grid, or a copy of it.</summary>
    private VoxelSplit Splitter(VoxelGrid grid)
    {
        VoxelDestructionStats debris = VoxelDebris.Current?.Stats ?? _stats;
        return new VoxelSplit
        {
            Grid = grid,
            Anchor = Anchor,
            Stats = _stats,
            BuiltCount = _builtCount,
            BuiltHalf = _builtHalf,
            Edge = Math.Max(4, _stats.SectionEdge),
            SectionCount = _sectionCount,
            Palette = Palette,
            Emission = Emission,
            Seed = (int)(GD.Randi() & 0xffff),
            ChunkEdge = Math.Max(4, debris.SectionEdge),
            MaxChunkBoxes = debris.MaxChunkBoxes,
            // Without anywhere to put pieces, everything is dust.
            MinChunkVoxels = VoxelDebris.Current == null ? int.MaxValue : _stats.MinChunkVoxels,
        };
    }

    /// <summary>
    /// Works out on a worker thread, from a copy of the cells, what has come
    /// loose (with no <paramref name="breakup"/>) or how all of it breaks up.
    /// A structural check of a tower floods some 200 000 cells, splits off
    /// what falls and builds its collision and meshes: 70-100 ms here when
    /// it was done in the frame.
    /// </summary>
    private void Schedule(VoxelBreakup? breakup, Vector3 push)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionIntegrity);
        // One copy per part, kept: only one plan is ever in flight.
        if (_snapshot == null || _snapshot.Length != Grid.Cells.Length)
        {
            _snapshot = new byte[Grid.Cells.Length];
        }
        Array.Copy(Grid.Cells, _snapshot, Grid.Cells.Length);
        byte[] cells = _snapshot;
        int version = Grid.Version;
        Vector3I size = Grid.Size;
        Vector3 origin = Grid.Origin;
        float voxel = Grid.VoxelSize;
        VoxelSplit split = Splitter(null);
        int step = CollisionStep;
        bool boxes = CollisionOwner != null || SectionBody != null;
        _planBreakup = breakup;
        _planPush = push;
        var done = new TaskCompletionSource<SplitPlan>(TaskCreationOptions.RunContinuationsAsynchronously);
        _plan = done.Task;
        VoxelWorkers.Run(() =>
        {
            try
            {
                split.Grid = new VoxelGrid(size, cells, origin, voxel);
                SplitPlan plan = breakup is VoxelBreakup b ? split.Breakup(b) : split.Integrity();
                plan.Version = version;
                split.Settle(plan, step, boxes);
                done.SetResult(plan);
            }
            catch (Exception exception)
            {
                done.SetException(exception);
            }
        });
    }

    /// <summary>Carries out a finished plan if the cells are as it found them;
    /// else works it out again.</summary>
    private void FinishPlan()
    {
        Task<SplitPlan> task = _plan;
        _plan = null;
        if (!task.IsCompletedSuccessfully)
        {
            GD.PushError($"VoxelPart: working out a split failed: {task.Exception}");
            return;
        }
        if (IsEmpty)
        {
            return;
        }
        if (task.Result.Version != Grid.Version)
        {
            if (_planBreakup is VoxelBreakup breakup)
            {
                Schedule(breakup, _planPush);
            }
            else
            {
                _integrityTimer = 0.0f;
            }
            return;
        }
        Apply(task.Result, _planPush);
    }

    /// <summary>
    /// Carries out a plan: what was crushed and what was too small go to dust,
    /// each piece leaves as a loose chunk made from what the plan built for it
    /// (taking over this part's meshes where it takes whole sections), and the
    /// sections it left are re-boxed and re-meshed from what the plan built.
    /// </summary>
    private void Apply(SplitPlan plan, Vector3 push)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionRelease);
        if (plan.BurstSize > 0.0f && VoxelDebris.Current != null)
        {
            VoxelDebris.Current.Dust(ToGlobal(plan.BurstCentre), Up(), Velocity() * 0.3f,
                VoxelDebris.AverageColour(Grid, Palette), plan.BurstSize);
        }
        if (plan.Dust.Count > 0)
        {
            _removed.Clear();
            Grid.Remove(plan.Dust, _removed);
            Dust(_removed);
        }

        // A collapse tips about the remains of the support, towards where the
        // load hangs: not at all if it went evenly all round. Lost on one side
        // only, what is left on the other still holds: the building hangs on
        // it, leans over and tears free (VoxelChunk).
        Vector3 pivot = Vector3.Zero, spin = Vector3.Zero;
        VoxelHinge? hinge = null;
        if (plan.Collapsed)
        {
            _removed.Clear();
            Grid.Remove(plan.Crushed, _removed);
            CollapseDust(_removed);
            LeaveRubble(_removed);
            pivot = ToGlobal(VoxSpace.ToGodot(new Vector3(plan.Support.X, plan.Support.Y, plan.Top) - Grid.Origin) * Grid.VoxelSize);
            Vector3 up = Up();
            Vector3 lean = GlobalBasis * VoxSpace.ToGodot(new Vector3(plan.Lean.X, plan.Lean.Y, 0.0f));
            lean -= up * lean.Dot(up);
            Vector3 axis = lean.LengthSquared() < 1e-4f ? Vector3.Zero : up.Cross(lean.Normalized()).Normalized();
            spin = axis * (_stats.ToppleSpin * plan.OffCentre);
            CollisionObject3D anchor = SectionBodyAt(new Vector3I(
                Mathf.Clamp((int)plan.Support.X, 0, Grid.Size.X - 1), Mathf.Clamp((int)plan.Support.Y, 0, Grid.Size.Y - 1),
                Math.Max(plan.Fail - 1, 0)));
            if (plan.Held && plan.OffCentre >= _stats.HingeOffCentre && axis != Vector3.Zero && anchor != null)
            {
                hinge = new VoxelHinge(anchor, pivot, axis);
            }
        }

        // Its old look stays until its pieces are there (SpawnBatch): the
        // sections it keeps get their new meshes then, and a thing breaking up
        // altogether stays drawn, still, until then.
        var held = new List<(int section, int version, VoxMesher.Arrays mesh)>();
        bool breakingUp = plan.BreakingUp, emptied = plan.Emptied;
        var batch = new SpawnBatch(() =>
        {
            if (!IsInstanceValid(this))
            {
                return;
            }
            foreach ((int s, int version, VoxMesher.Arrays mesh) in held)
            {
                if (_version[s] == version)
                {
                    SetMesh(s, mesh);
                    _inFlight[s] = false;
                }
            }
            if (breakingUp)
            {
                MarkAllDirty();
            }
            if (emptied || Grid.SolidCount <= 0)
            {
                RaiseEmptied();
            }
        });

        foreach (SplitPiece piece in plan.Pieces)
        {
            MeshInstance3D[] given = Give(piece);
            Grid.Remove(piece.Cells, null);
            if (VoxelDebris.Current == null)
            {
                continue;
            }
            Vector3 velocity = Velocity() + push;
            if (piece.Away != Vector3.Zero)
            {
                velocity += GlobalBasis * piece.Away * (_stats.BreakawaySpeed * 0.5f);
            }
            Transform3D at = GlobalTransform * new Transform3D(Basis.Identity, piece.Offset);
            Vector3? turning = piece.Spin switch
            {
                VoxelSpin.Still => Vector3.Zero,
                VoxelSpin.Topple => spin,
                _ => null,
            };
            if (turning is Vector3 w)
            {
                velocity += w.Cross(at.Origin - pivot);
            }
            VoxelDebris.Current.SpawnChunk(piece.Grid, Palette, Emission, Material, at, velocity, turning,
                piece.Hinge ? hinge : null, given == null ? null : (given, piece.Offset), piece.Build, batch);
            if (breakingUp)
            {
                // Cover: each piece comes out of a cloud its own size, so the
                // moment the old shape gives way to its pieces is in dust.
                VoxelDebris.Current.Dust(at.Origin, Up(), velocity * 0.2f, VoxelDebris.AverageColour(piece.Grid, Palette),
                    Mathf.Pow(piece.Grid.SolidCount, 1.0f / 3.0f) * Grid.VoxelSize * _stats.CoverDust);
            }
        }

        // What is left: its new collision now, so nothing that fell is still
        // standing on it; its new meshes once its pieces are there.
        foreach ((int s, VoxMesher.Arrays mesh, List<(Vector3, Vector3)> boxes) in plan.Sections)
        {
            _version[s]++;
            _meshDirty[s] = false;
            if (boxes != null)
            {
                SetBoxes(s, boxes);
            }
            held.Add((s, _version[s], mesh));
            _inFlight[s] = true;
        }

        if (plan.BreakingUp)
        {
            ClearCollision();
        }
        if (plan.Collapsed)
        {
            AudioDirector.PlayAt(Sfx.VoxelCollapse, pivot);
        }
        if (batch.Remaining <= 0)
        {
            batch.Finish();
        }
    }

    /// <summary>
    /// Hands a released piece the meshes of the sections it takes whole, so
    /// what breaks off is not re-meshed at all: a tower's upper part keeps the
    /// meshes it stood with. Only meshes that are up to date; the piece builds
    /// any other itself. By the piece's section, or null if there are none.
    /// </summary>
    private MeshInstance3D[] Give(SplitPiece piece)
    {
        if (piece.Give == null)
        {
            return null;
        }
        MeshInstance3D[] given = null;
        foreach ((int part, int index) in piece.Give)
        {
            if (_meshes[part] == null || _meshDirty[part] || _inFlight[part])
            {
                continue;
            }
            given ??= new MeshInstance3D[piece.Build.Meshes.Length];
            given[index] = _meshes[part];
            _meshes[part] = null;
        }
        return given;
    }

    /// <summary>
    /// Crushes a slab out of it: every cell from a world point to
    /// <paramref name="depth"/> metres along <paramref name="direction"/>,
    /// within <paramref name="radius"/> of the line through the point. What is
    /// crushed goes up as dust. Collision follows at once, since what is
    /// crushing through must not rest on what it crushed; meshes follow as
    /// usual. Returns the voxels removed.
    /// </summary>
    public int CrushSlab(Vector3 worldPoint, Vector3 direction, float depth, float radius) =>
        CrushSlab(worldPoint, direction, depth, radius, out _, null);

    /// <summary>Fills cells (a rubble pile growing): re-meshed and re-boxed on
    /// the workers like any change.</summary>
    public void Fill(List<(int index, byte colour)> cells, Vector3I min, Vector3I max)
    {
        if (cells.Count == 0 || _meshDirty == null)
        {
            return;
        }
        Grid.Fill(cells);
        MarkDirty(min, max);
    }

    /// <param name="reach">How far from the line through the point the
    /// crushed cells reached, in metres: the footprint of what was crushed.</param>
    /// <param name="colours">Gets a sample of the crushed cells' colours, for
    /// the rubble they leave; null for none.</param>
    public int CrushSlab(Vector3 worldPoint, Vector3 direction, float depth, float radius, out float reach,
        List<byte> colours)
    {
        reach = 0.0f;
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionCrush);
        if (IsEmpty)
        {
            return 0;
        }
        Vector3 from = Grid.ToVox(ToLocal(worldPoint));
        Vector3 along = VoxSpace.FromGodot(GlobalBasis.Inverse() * direction).Normalized();
        float deep = depth / Grid.VoxelSize;
        float wide2 = radius / Grid.VoxelSize * (radius / Grid.VoxelSize);

        // Only the box round the slab is looked at: a disc of the radius,
        // swept along the direction from just behind the point to its depth.
        float wide = radius / Grid.VoxelSize;
        Vector3 a = from - along, b = from + along * deep;
        Vector3 spread = new Vector3(
            wide * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - along.X * along.X)),
            wide * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - along.Y * along.Y)),
            wide * Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - along.Z * along.Z))) + Vector3.One;
        Vector3 low = a.Min(b) - spread, high = a.Max(b) + spread;
        Vector3I lo = new Vector3I(Mathf.FloorToInt(low.X), Mathf.FloorToInt(low.Y), Mathf.FloorToInt(low.Z))
            .Clamp(Vector3I.Zero, Grid.Size - Vector3I.One);
        Vector3I hi = new Vector3I(Mathf.CeilToInt(high.X), Mathf.CeilToInt(high.Y), Mathf.CeilToInt(high.Z))
            .Clamp(Vector3I.Zero, Grid.Size - Vector3I.One);
        var cells = new List<int>();
        Vector3I min = hi, max = lo;
        float sideways2 = 0.0f;
        // Row by row, jumping the empty runs with a vector search: a tilted
        // landing's box takes in most of a hollow tower's grid.
        int width = hi.X - lo.X + 1;
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
            {
                int row = Grid.Index(lo.X, y, z);
                ReadOnlySpan<byte> cellsInRow = Grid.Cells.AsSpan(row, width);
                int offset = 0;
                while (offset < width)
                {
                    int next = cellsInRow.Slice(offset).IndexOfAnyExcept((byte)0);
                    if (next < 0)
                    {
                        break;
                    }
                    int x = lo.X + offset + next;
                    offset += next + 1;
                    int i = row + (x - lo.X);
                    Vector3 q = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - from;
                    float d = q.Dot(along);
                    if (d < -1.0f || d > deep || q.LengthSquared() - d * d > wide2)
                    {
                        continue;
                    }
                    cells.Add(i);
                    var c = new Vector3I(x, y, z);
                    min = min.Min(c);
                    max = max.Max(c);
                    sideways2 = Mathf.Max(sideways2, q.LengthSquared() - d * d);
                }
            }
        reach = Mathf.Sqrt(sideways2) * Grid.VoxelSize;
        if (cells.Count == 0)
        {
            return 0;
        }

        _removed.Clear();
        Grid.Remove(cells, _removed);
        CollapseDust(_removed);
        if (colours != null)
        {
            for (int k = 0; k < _removed.Count; k += Math.Max(1, _removed.Count / 48))
            {
                colours.Add(_removed[k].Colour);
            }
        }
        MarkDirty(min, max);
        RebuildCollisionNow(min, max);
        // A crush checks the structure once the crushing has stopped: a slab off
        // the bottom of a falling tower all but never parts it, and a check
        // per crush copied and flooded the whole grid each time.
        _integrityTimer = Mathf.Max(_integrityTimer, _stats.CrushIntegrityDelay);
        Carved?.Invoke(this, cells.Count);
        if (Grid.SolidCount <= 0)
        {
            RaiseEmptied();
        }
        return cells.Count;
    }

    private void RaiseEmptied()
    {
        if (_emptied)
        {
            return;
        }
        _emptied = true;
        Emptied?.Invoke(this);
    }

    // ---------------- Sections ----------------

    private void MarkDirty(Vector3I min, Vector3I max)
    {
        int edge = Math.Max(4, _stats.SectionEdge);
        // A carve changes the faces of its neighbours too: one cell of margin.
        Vector3I lo = (min - Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        Vector3I hi = (max + Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++)
                {
                    int s = x + _sectionCount.X * (y + _sectionCount.Y * z);
                    _meshDirty[s] = true;
                    _version[s]++;
                }
        _anyDirty = true;
    }

    private void MarkAllDirty()
    {
        for (int s = 0; s < _meshDirty.Length; s++)
        {
            _meshDirty[s] = true;
            _version[s]++;
        }
        _anyDirty = true;
    }

    /// <summary>Re-boxes the collision of the sections a change touched, now
    /// rather than within the frame budget. Their meshes still wait their turn.</summary>
    private void RebuildCollisionNow(Vector3I min, Vector3I max)
    {
        if ((CollisionOwner == null && SectionBody == null) || _shapeOwners == null)
        {
            return;
        }
        int edge = Math.Max(4, _stats.SectionEdge);
        Vector3I lo = (min - Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        Vector3I hi = (max + Vector3I.One).Clamp(Vector3I.Zero, Grid.Size - Vector3I.One) / edge;
        for (int z = lo.Z; z <= hi.Z; z++)
            for (int y = lo.Y; y <= hi.Y; y++)
                for (int x = lo.X; x <= hi.X; x++)
                {
                    RebuildCollision(x + _sectionCount.X * (y + _sectionCount.Y * z));
                }
    }

    private (Vector3I min, Vector3I max) SectionBounds(int s)
    {
        int edge = Math.Max(4, _stats.SectionEdge);
        int sx = s % _sectionCount.X, sy = (s / _sectionCount.X) % _sectionCount.Y, sz = s / (_sectionCount.X * _sectionCount.Y);
        var min = new Vector3I(sx * edge, sy * edge, sz * edge);
        return (min, (min + new Vector3I(edge, edge, edge)).Min(Grid.Size));
    }

    /// <summary>Re-meshes and re-boxes a section here and now: at load.</summary>
    private void RebuildSection(int s)
    {
        FrameProfiler.Scope meshing = FrameProfiler.Begin(SectionMesh);
        (Vector3I min, Vector3I max) = SectionBounds(s);
        SetMesh(s, VoxMesher.BuildArrays(Grid.Cells, Grid.Size, min, max, Palette, Emission,
            Grid.Origin, Grid.VoxelSize, -1));
        meshing.Dispose();
        RebuildCollision(s);
    }

    private void SetMesh(int s, VoxMesher.Arrays arrays)
    {
        MeshInstance3D instance = _meshes[s];
        if (arrays?.Vertices == null)
        {
            if (instance != null)
            {
                instance.Mesh = null;
            }
            return;
        }
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays.ToGodot());
        arrays.Release();
        mesh.SurfaceSetMaterial(0, Material);
        if (instance == null)
        {
            instance = new MeshInstance3D
            {
                Name = $"Section{s}",
                CastShadow = CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(instance);
            _meshes[s] = instance;
        }
        // An adopted mesh sat where its old part had its origin.
        instance.Transform = Transform3D.Identity;
        instance.Mesh = mesh;
    }

    private void RebuildCollision(int s)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionCollision);
        if (CollisionOwner == null && SectionBody == null)
        {
            return;
        }
        (Vector3I min, Vector3I max) = SectionBounds(s);
        _boxes.Clear();
        Grid.Boxes(min, max, CollisionStep, _boxes);
        SetBoxes(s, _boxes);
    }

    private void SetBoxes(int s, List<(Vector3 centre, Vector3 size)> boxes)
    {
        if (CollisionOwner == null && SectionBody == null)
        {
            return;
        }
        if (CollisionOwner == null)
        {
            // A section's own body: its shapes straight on the server, no
            // shape owners to keep in step.
            CollisionObject3D body = SectionBodyOf(s, boxes.Count > 0);
            if (body == null)
            {
                return;
            }
            Rid rid = body.GetRid();
            PhysicsServer3D.BodyClearShapes(rid);
            foreach ((Vector3 centre, Vector3 size) in boxes)
            {
                PhysicsServer3D.BodyAddShape(rid, Box(size).GetRid(), new Transform3D(Basis.Identity, centre));
            }
            return;
        }

        // One shape owner per box: an owner's shapes share its transform.
        foreach (uint owner in _shapeOwners[s])
        {
            CollisionOwner.RemoveShapeOwner(owner);
        }
        _shapeOwners[s].Clear();
        Transform3D toOwner = CollisionOwner.GlobalTransform.AffineInverse() * GlobalTransform;
        foreach ((Vector3 centre, Vector3 size) in boxes)
        {
            uint owner = CollisionOwner.CreateShapeOwner(this);
            CollisionOwner.ShapeOwnerAddShape(owner, Box(size));
            CollisionOwner.ShapeOwnerSetTransform(owner, toOwner * new Transform3D(Basis.Identity, centre));
            _shapeOwners[s].Add(owner);
        }
    }

    private readonly List<(Vector3 centre, Vector3 size)> _boxes = new();

    private static BoxShape3D Box(Vector3 size)
    {
        var key = (Vector3I)(size * 1000.0f).Round();
        if (!BoxShapes.TryGetValue(key, out BoxShape3D shape))
        {
            shape = new BoxShape3D { Size = size };
            BoxShapes[key] = shape;
        }
        return shape;
    }

    /// <summary>A section's own collision body, made the first time it has
    /// anything to collide with (or <paramref name="make"/> is false: null).</summary>
    private CollisionObject3D SectionBodyOf(int s, bool make)
    {
        if (SectionBody == null)
        {
            return null;
        }
        _sectionBodies ??= new CollisionObject3D[_meshDirty.Length];
        if (_sectionBodies[s] == null && make)
        {
            CollisionObject3D body = SectionBody();
            body.Name = $"Collision{s}";
            AddChild(body);
            _sectionBodies[s] = body;
        }
        return _sectionBodies[s];
    }

    /// <summary>The collision body of the section a cell is in.</summary>
    private CollisionObject3D SectionBodyAt(Vector3I cell)
    {
        if (CollisionOwner != null)
        {
            return CollisionOwner;
        }
        int edge = Math.Max(4, _stats.SectionEdge);
        Vector3I c = cell / edge;
        return SectionBodyOf(c.X + _sectionCount.X * (c.Y + _sectionCount.Y * c.Z), true);
    }

    // ---------------- Dust ----------------

    private void Dust(List<VoxelGrid.Removed> removed)
    {
        if (removed.Count == 0 || VoxelDebris.Current == null)
        {
            return;
        }
        Vector3 centre = Vector3.Zero;
        Color colour = new(0, 0, 0, 0);
        foreach (VoxelGrid.Removed r in removed)
        {
            centre += r.Position;
            colour += Palette[r.Colour];
        }
        centre /= removed.Count;
        colour /= removed.Count;
        float edge = Mathf.Pow(removed.Count, 1.0f / 3.0f) * Grid.VoxelSize;
        VoxelDebris.Current.Dust(ToGlobal(centre), Up(), Velocity(), colour.LinearToSrgb(), edge);
    }

    /// <summary>What a failed storey was goes on the heap round its base,
    /// the share of it that is not dust.</summary>
    private void LeaveRubble(List<VoxelGrid.Removed> removed)
    {
        if (removed.Count == 0 || VoxelDebris.Current == null)
        {
            return;
        }
        Vector3 sum = Vector3.Zero;
        var colours = new List<byte>();
        for (int k = 0; k < removed.Count; k++)
        {
            sum += removed[k].Position;
            if (k % Math.Max(1, removed.Count / 48) == 0)
            {
                colours.Add(removed[k].Colour);
            }
        }
        Vector3 centre = sum / removed.Count;
        float inner = 0.0f;
        foreach (VoxelGrid.Removed r in removed)
        {
            inner = Mathf.Max(inner, new Vector2(r.Position.X - centre.X, r.Position.Z - centre.Z).Length());
        }
        VoxelDebris.Current.LeaveRubble(ToGlobal(centre), inner + Grid.VoxelSize, removed.Count, colours,
            Palette, Emission, Material, Grid.VoxelSize);
    }

    /// <summary>A failed layer goes up as big clouds all round its footprint,
    /// not one puff at its middle: four-metre bins, each raising a cloud sized
    /// for the storey coming down into it.</summary>
    private void CollapseDust(List<VoxelGrid.Removed> removed)
    {
        if (removed.Count == 0 || VoxelDebris.Current == null)
        {
            return;
        }
        var bins = new Dictionary<Vector2I, (Vector3 sum, Color colour, int n)>();
        foreach (VoxelGrid.Removed r in removed)
        {
            var key = new Vector2I(Mathf.FloorToInt(r.Position.X / 4.0f), Mathf.FloorToInt(r.Position.Z / 4.0f));
            bins.TryGetValue(key, out (Vector3 sum, Color colour, int n) bin);
            bins[key] = (bin.sum + r.Position, bin.colour + Palette[r.Colour], bin.n + 1);
        }
        foreach ((Vector3 sum, Color colour, int n) in bins.Values)
        {
            VoxelDebris.Current.Dust(ToGlobal(sum / n), Up(), Velocity(), (colour / n).LinearToSrgb(), 4.0f);
        }
    }
}
