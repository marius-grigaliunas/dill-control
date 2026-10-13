using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The per-frame allowance every voxel piece in the world shares: how many
/// sections may be re-meshing on worker threads, how much main-thread time a
/// frame may spend handing finished ones over, and how many structural checks
/// run this frame. Past it, work waits. Static, like ImpactEffect's live
/// budget, so a piece needs no path to anything to ask.
/// </summary>
public static class VoxelBudget
{
    private static ulong _frame = ulong.MaxValue;
    private static int _checks;
    private static int _blasts;
    private static double _applied;
    private static bool _appliedAny;
    private static int _workers;
    private static int _sectionsApplied;
    private static int _boxesSet;

    /// <summary>Voxel parts in the tree: buildings' parts, loose pieces' and heaps'.</summary>
    public static int LiveParts;

    /// <summary>Sections being re-meshed on worker threads now.</summary>
    public static int BusyWorkers => System.Threading.Volatile.Read(ref _workers);

    /// <summary>Sections handed to the renderer and the physics this frame.</summary>
    public static int SectionsApplied => _frame == Engine.GetProcessFrames() ? _sectionsApplied : 0;

    /// <summary>Collision boxes made this frame, every part together.</summary>
    public static int BoxesSet => _frame == Engine.GetProcessFrames() ? _boxesSet : 0;

    private static void Roll()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _frame)
        {
            _frame = frame;
            _checks = 0;
            _blasts = 0;
            _applied = 0.0;
            _appliedAny = false;
            _sectionsApplied = 0;
            _boxesSet = 0;
        }
    }

    /// <summary>A section handed over, for the counts in a recording.</summary>
    public static void CountApplied()
    {
        Roll();
        _sectionsApplied++;
    }

    /// <summary>Boxes made, for the counts in a recording.</summary>
    public static void CountBoxes(int boxes)
    {
        Roll();
        _boxesSet += boxes;
    }

    /// <summary>A worker for one section's re-mesh, if one is free. Give it
    /// back with <see cref="ReleaseWorker"/>, from any thread.</summary>
    public static bool TakeWorker(VoxelDestructionStats stats)
    {
        if (System.Threading.Volatile.Read(ref _workers) >= stats.MeshWorkers)
        {
            return false;
        }
        System.Threading.Interlocked.Increment(ref _workers);
        return true;
    }

    public static void ReleaseWorker() => System.Threading.Interlocked.Decrement(ref _workers);

    /// <summary>Whether a finished section may be handed over this frame.
    /// Report what it took with <see cref="Spent"/>.</summary>
    public static bool TakeApply(VoxelDestructionStats stats)
    {
        Roll();
        if (_appliedAny && _applied >= stats.ApplyMillisecondsPerFrame)
        {
            return false;
        }
        _appliedAny = true;
        return true;
    }

    public static void Spent(double milliseconds) => _applied += milliseconds;

    public static bool TakeLandingBlast(VoxelDestructionStats stats)
    {
        Roll();
        if (_blasts >= stats.LandingBlastsPerFrame)
        {
            return false;
        }
        _blasts++;
        return true;
    }

    public static bool TakeIntegrityCheck(VoxelDestructionStats stats)
    {
        Roll();
        if (_checks >= stats.IntegrityChecksPerFrame)
        {
            return false;
        }
        _checks++;
        return true;
    }
}

/// <summary>
/// The pieces of one split, made over the next ticks. What they came from
/// keeps its old look until the last of them is made (or half a second has
/// gone, whichever is first): it must never be gone before its pieces are
/// there, or a building blinks out for a frame and its rubble pops in after.
/// </summary>
public sealed class SpawnBatch
{
    internal int Remaining;
    internal ulong Deadline;
    private Action _done;

    public SpawnBatch(Action done)
    {
        _done = done;
        Deadline = Time.GetTicksMsec() + 500;
    }

    public bool IsOpen => _done != null;

    internal void One()
    {
        if (--Remaining <= 0)
        {
            Finish();
        }
    }

    internal void Finish()
    {
        Action done = _done;
        _done = null;
        done?.Invoke();
    }
}

/// <summary>
/// Where broken-off pieces go: every loose chunk in the level lives here, and
/// so does the dust. It holds the cap on loose pieces (the oldest, preferring
/// settled ones, turn to dust past it), so destruction has a ceiling however
/// much is being destroyed.
///
/// <see cref="Levels.LevelRoot"/> adds one to every level, as it adds the perf
/// instruments. Pieces reach it through <see cref="Current"/>: they are made
/// at runtime, deep inside whatever broke, and cannot hold a path to it.
/// </summary>
public partial class VoxelDebris : Node3D
{
    private static readonly int SectionSpawn = FrameProfiler.Section("chunk spawn");

    public static VoxelDebris Current { get; private set; }

    [Export]
    public VoxelDestructionStats Stats { get; set; }

    private DustField _dust;
    private readonly List<VoxelChunk> _chunks = new();

    /// <summary>
    /// Chunks ready to be used, out of the tree.
    ///
    /// DECISION -- kept and used again rather than made and freed. Making a
    /// <see cref="VoxelChunk"/> costs 0.7 ms on the target machine (a plain
    /// RigidBody3D costs nothing: it is setting up the script on one), and a
    /// collapse makes a hundred. These are made while the level loads, one a
    /// frame after that when the spare runs low, and every spent chunk comes
    /// back here.
    /// </summary>
    private readonly Stack<VoxelChunk> _spare = new();
    private const int SpareChunks = 32;

    public int ChunkCount => _chunks.Count;

    private int _madeThisFrame;
    private ulong _madeFrame;

    /// <summary>What a <c>U</c> recording counts of destruction, every frame.
    /// Registered when the first level readies, before anyone can press U.</summary>
    private static void RegisterCounters()
    {
        PerfLog.Counter("chunks", () => Current?._chunks.Count ?? 0);
        PerfLog.Counter("chunks_moving", () => Current?.CountMoving() ?? 0);
        PerfLog.Counter("chunks_settled", () => Current?.CountSettled() ?? 0);
        PerfLog.Counter("chunks_still", () => Current?.CountStill() ?? 0);
        PerfLog.Counter("chunks_unbakeable", () => Current?.CountRefused() ?? 0);
        PerfLog.Counter("chunk_queue", () => Current?._pending.Count ?? 0);
        PerfLog.Counter("chunks_made", () => Current != null && Current._madeFrame == Engine.GetPhysicsFrames() ? Current._madeThisFrame : 0);
        PerfLog.Counter("heaps", () => Current?._piles.Count ?? 0);
        PerfLog.Counter("voxel_parts", () => VoxelBudget.LiveParts);
        PerfLog.Counter("mesh_workers", () => VoxelBudget.BusyWorkers);
        PerfLog.Counter("sections_applied", () => VoxelBudget.SectionsApplied);
        PerfLog.Counter("boxes_set", () => VoxelBudget.BoxesSet);
    }

    private int CountMoving()
    {
        int moving = 0;
        foreach (VoxelChunk chunk in _chunks)
        {
            if (!chunk.IsSettled && !chunk.Sleeping)
            {
                moving++;
            }
        }
        return moving;
    }

    /// <summary>Lying still long enough to join a heap.</summary>
    private int CountStill()
    {
        int still = 0;
        foreach (VoxelChunk chunk in _chunks)
        {
            still += chunk.StillFor >= Stats.BakeAfterSeconds ? 1 : 0;
        }
        return still;
    }

    private int CountRefused()
    {
        int refused = 0;
        foreach (VoxelChunk chunk in _chunks)
        {
            refused += chunk.BakeRefused ? 1 : 0;
        }
        return refused;
    }

    private int CountSettled()
    {
        int settled = 0;
        foreach (VoxelChunk chunk in _chunks)
        {
            settled += chunk.IsSettled ? 1 : 0;
        }
        return settled;
    }

    public override void _EnterTree()
    {
        Current = this;
    }

    public override void _ExitTree()
    {
        while (_spare.Count > 0)
        {
            _spare.Pop().Free();
        }
        if (Current == this)
        {
            Current = null;
        }
    }

    public override void _Ready()
    {
        Stats ??= GD.Load<VoxelDestructionStats>(VoxelDestructionStats.DefaultPath);
        RegisterCounters();
        _dust = new DustField { Name = "Dust" };
        AddChild(_dust);
        VoxelRehearsal.Run(this, Stats);
        while (_spare.Count < SpareChunks)
        {
            _spare.Push(NewChunk());
        }
    }

    private float _bakeTimer;

    public override void _Process(double delta)
    {
        if (_spare.Count < SpareChunks)
        {
            _spare.Push(NewChunk());
        }

        // Small pieces lying still join their heap, a few at a time.
        _bakeTimer -= (float)delta;
        if (_bakeTimer > 0.0f)
        {
            return;
        }
        _bakeTimer = 0.25f;
        int baked = 0;
        for (int k = _chunks.Count - 1; k >= 0 && baked < 8; k--)
        {
            VoxelChunk chunk = _chunks[k];
            if (chunk.StillFor < Stats.BakeAfterSeconds || chunk.IsHinged || chunk.VoxelCount > Stats.BakeMaxVoxels
                || chunk.Grid == null || chunk.IsHeld || chunk.BakeRefused)
            {
                continue;
            }
            _chunks.RemoveAt(k);
            if (Bake(chunk))
            {
                chunk.RetireAfter(0.3f);
                baked++;
            }
            else
            {
                // Not again until it has moved (VoxelChunk clears it).
                chunk.BakeRefused = true;
                _chunks.Add(chunk);
            }
        }
    }

    private VoxelChunk NewChunk()
    {
        var chunk = new VoxelChunk { Name = "Chunk" };
        chunk.TreeExiting += () => _chunks.Remove(chunk);
        return chunk;
    }

    /// <summary>A spent chunk, back for use again.</summary>
    internal void Recycle(VoxelChunk chunk)
    {
        RemoveChild(chunk);
        chunk.Clear();
        if (_spare.Count < Stats.MaxChunks)
        {
            _spare.Push(chunk);
        }
        else
        {
            chunk.QueueFree();
        }
    }

    /// <param name="amount">The source's <see cref="VoxelDestructionStats.DustScale"/>.</param>
    public void Dust(Vector3 position, Vector3 up, Vector3 velocity, Color colour, float edge, float amount = 1.0f) =>
        _dust?.Request(position, up, velocity, colour, edge, amount);

    private readonly struct Pending
    {
        public readonly VoxelGrid Grid;
        public readonly Color[] Palette;
        public readonly float[] Emission;
        public readonly Material Material;
        public readonly Transform3D At;
        public readonly Vector3 Velocity;
        public readonly Vector3 Spin;
        public readonly VoxelHinge? Hinge;
        public readonly (MeshInstance3D[] meshes, Vector3 offset)? Meshes;
        public readonly ChunkBuild Build;
        public readonly SpawnBatch Batch;
        public readonly float DustScale;
        public readonly ulong Asked;
        public readonly VoxelChunkGraph Graph;

        public Pending(VoxelGrid grid, Color[] palette, float[] emission, Material material, Transform3D at,
            Vector3 velocity, Vector3 spin, VoxelHinge? hinge, (MeshInstance3D[], Vector3)? meshes, ChunkBuild build,
            SpawnBatch batch, float dustScale, VoxelChunkGraph graph = null)
        {
            Graph = graph;
            DustScale = dustScale;
            Build = build;
            Batch = batch;
            Meshes = meshes;
            Grid = grid;
            Palette = palette;
            Emission = emission;
            Material = material;
            At = at;
            Velocity = velocity;
            Spin = spin;
            Hinge = hinge;
            Asked = Time.GetTicksUsec();
        }
    }

    private readonly Queue<Pending> _pending = new();
    private ulong _spawnFrame = ulong.MaxValue;
    private int _spawnedThisFrame;

    /// <summary>
    /// Makes a loose chunk of a grid, at a world transform, moving. Too small
    /// a piece is dust instead. A collapse asks for dozens at once; past
    /// <see cref="VoxelDestructionStats.ChunksPerFrame"/> they are made over
    /// the next frames, each where it would have got to by then.
    /// </summary>
    /// <param name="spin">Its angular velocity; null for a spin of its own,
    /// slower the bigger it is.</param>
    /// <param name="hinge">What it hangs on until it leans far enough to tear
    /// free; null for none.</param>
    /// <param name="meshes">Meshes to take over from the part it broke off,
    /// by section, and where that part's origin is in its space
    /// (<see cref="VoxelPart.Adopt"/>).</param>
    /// <param name="build">Its collision and meshes, built already; null to
    /// build them as it is made.</param>
    /// <param name="dustScale">The dust of what it came off
    /// (<see cref="VoxelDestructionStats.DustScale"/>), which it keeps.</param>
    public void SpawnChunk(VoxelGrid grid, Color[] palette, float[] emission, Material material,
        Transform3D at, Vector3 velocity, Vector3? spin = null, VoxelHinge? hinge = null,
        (MeshInstance3D[], Vector3)? meshes = null, ChunkBuild build = null, SpawnBatch batch = null,
        float dustScale = 1.0f, VoxelChunkGraph graph = null)
    {
        if (batch != null)
        {
            batch.Remaining++;
            if (!_batches.Contains(batch))
            {
                _batches.Add(batch);
            }
        }
        if (grid.SolidCount < Stats.MinChunkVoxels || Stats.MaxChunks <= 0)
        {
            Dust(at.Origin, WorldUp.At(at.Origin), velocity, AverageColour(grid, palette),
                Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize, dustScale);
            batch?.One();
            return;
        }

        if (spin == null)
        {
            float across = Mathf.Max(1.0f, Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize);
            float s = Stats.LooseSpin / across;
            spin = new Vector3((float)GD.RandRange(-s, s), (float)GD.RandRange(-s, s), (float)GD.RandRange(-s, s));
        }

        // Made on the next physics tick, never in the frame that asked: that
        // frame has just split the thing it came from (VoxelPart.Apply).
        _pending.Enqueue(new Pending(grid, palette, emission, material, at, velocity, spin.Value, hinge, meshes, build, batch,
            dustScale, graph));
    }

    private readonly List<SpawnBatch> _batches = new();

    public override void _PhysicsProcess(double delta)
    {
        // A batch whose pieces are slow in coming lets go of its old look anyway.
        ulong ms = Time.GetTicksMsec();
        for (int k = _batches.Count - 1; k >= 0; k--)
        {
            if (!_batches[k].IsOpen)
            {
                _batches.RemoveAt(k);
            }
            else if (ms > _batches[k].Deadline)
            {
                _batches[k].Finish();
                _batches.RemoveAt(k);
            }
        }

        ulong now = Time.GetTicksUsec();
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        double allowance = Stats.SpawnMillisecondsPerFrame * System.Diagnostics.Stopwatch.Frequency / 1000.0;
        bool first = true;
        while (_pending.Count > 0 && TakeSpawn()
               && (first || System.Diagnostics.Stopwatch.GetTimestamp() - start < allowance))
        {
            first = false;
            Pending p = _pending.Dequeue();
            Make(p, (now - p.Asked) / 1_000_000.0f);
        }
    }

    private bool TakeSpawn()
    {
        ulong frame = Engine.GetPhysicsFrames();
        if (frame != _spawnFrame)
        {
            _spawnFrame = frame;
            _spawnedThisFrame = 0;
        }
        if (_spawnedThisFrame >= Stats.ChunksPerFrame)
        {
            return false;
        }
        _spawnedThisFrame++;
        return true;
    }

    /// <summary>Makes a chunk at once, not on the next tick: a piece torn
    /// off a heap by the gravity gun is held this frame.</summary>
    public VoxelChunk MakeNow(VoxelGrid grid, Color[] palette, float[] emission, Material material, Transform3D at,
        VoxelChunkGraph graph = null)
    {
        return Make(new Pending(grid, palette, emission, material, at, Vector3.Zero, Vector3.Zero, null, null, null, null, 1.0f, graph), 0.0f);
    }

    /// <summary>Makes a chunk that was asked for <paramref name="late"/>
    /// seconds ago, where it would be now had it been made then.</summary>
    private VoxelChunk Make(in Pending p, float late)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionSpawn);
        Transform3D at = p.At;
        Vector3 velocity = p.Velocity;
        // A hinged piece has not fallen while it waited: it hangs. Nor is a
        // long wait projected in full: free fall over a crowded queue's wait
        // would put a piece into whatever it would really have landed on.
        late = Mathf.Min(late, 0.1f);
        if (late > 0.0f && p.Hinge == null)
        {
            Vector3 gravity = WorldUp.GravityAt(at.Origin);
            at.Origin += velocity * late + gravity * (0.5f * late * late);
            velocity += gravity * late;
            if (p.Spin.LengthSquared() > 1e-8f)
            {
                at.Basis = new Basis(p.Spin.Normalized(), p.Spin.Length() * late) * at.Basis;
            }
        }

        // At the cap with nothing lying still to give up, the smallest piece
        // goes: if that is this one, it never becomes a body. Made and then
        // dissolved in the same tick, a collapse's queue of three hundred
        // cost a millisecond or two a piece for nothing.
        if (p.Batch != null && _chunks.Count >= Stats.MaxChunks && !HasRestingChunk()
            && p.Grid.SolidCount <= SmallestMoving())
        {
            DustInstead(p.Grid, p.Palette, p.Emission, p.Material, at.Origin, velocity, p.DustScale);
            p.Batch.One();
            return null;
        }

        // Placed before it is added: added first, it would enter the world at
        // the origin, inside whatever is there, and be moved after.
        VoxelChunk chunk = _spare.Count > 0 ? _spare.Pop() : NewChunk();
        chunk.Transform = GlobalTransform.AffineInverse() * at;
        chunk.Setup(p.Grid, p.Palette, p.Emission, p.Material, Stats, p.Meshes, p.Build, p.DustScale);
        chunk.GiveGraph(p.Graph);
        using (FrameProfiler.Begin(VoxelPart.SectionChunkAdd))
        {
            AddChild(chunk);
        }
        chunk.LinearVelocity = velocity;
        chunk.AngularVelocity = p.Spin;
        if (p.Hinge is VoxelHinge hinge)
        {
            // Hung on its next tick: the joint costs a frame of its own.
            chunk.HangSoon(hinge);
        }
        _chunks.Add(chunk);
        p.Batch?.One();
        ulong physicsFrame = Engine.GetPhysicsFrames();
        _madeThisFrame = _madeFrame == physicsFrame ? _madeThisFrame + 1 : 1;
        _madeFrame = physicsFrame;

        while (_chunks.Count > Stats.MaxChunks)
        {
            // Out of the count now: it is only freed at the end of the frame.
            VoxelChunk expendable = Expendable();
            if (expendable == null)
            {
                break;
            }
            _chunks.Remove(expendable);
            // Lying still, it joins the heap where it lies and stays on the
            // screen; only one still moving goes to dust.
            if ((expendable.IsSettled || expendable.Sleeping) && !expendable.BakeRefused && Bake(expendable))
            {
                // Until the heap has drawn it: the heap's meshes come off the
                // workers a few frames later.
                expendable.RetireAfter(0.3f);
                if (PerfLog.Recording)
                {
                    PerfLog.Event($"cap-baked {expendable.VoxelCount}v");
                }
            }
            else
            {
                if (PerfLog.Recording)
                {
                    PerfLog.Event($"cap-dissolved {expendable.VoxelCount}v");
                }
                expendable.Dissolve();
            }
        }
        return chunk;
    }

    private bool HasRestingChunk()
    {
        foreach (VoxelChunk chunk in _chunks)
        {
            if (!chunk.IsHeld && (chunk.IsSettled || chunk.Sleeping))
            {
                return true;
            }
        }
        return false;
    }

    private int SmallestMoving()
    {
        int smallest = int.MaxValue;
        foreach (VoxelChunk chunk in _chunks)
        {
            if (!chunk.IsHeld)
            {
                smallest = Math.Min(smallest, chunk.VoxelCount);
            }
        }
        return smallest;
    }

    /// <summary>A piece there is no room for as a body: a cloud its size,
    /// and its rubble share on the heap under it, so a collapse past the cap
    /// still leaves its pile.</summary>
    private void DustInstead(VoxelGrid grid, Color[] palette, float[] emission, Material material, Vector3 at,
        Vector3 velocity, float dustScale)
    {
        Dust(at, WorldUp.At(at), velocity, AverageColour(grid, palette),
            Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize, dustScale);
        _sample.Clear();
        int stride = Math.Max(1, grid.SolidCount / 48);
        int seen = 0;
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            if (grid.Cells[i] != 0 && seen++ % stride == 0)
            {
                _sample.Add(grid.Cells[i]);
            }
        }
        float across = Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize * 0.5f;
        LeaveRubble(at, across, grid.SolidCount, _sample, palette, emission, material, grid.VoxelSize);
        if (PerfLog.Recording)
        {
            PerfLog.Event($"cap-dusted {grid.SolidCount}v");
        }
    }

    private readonly List<byte> _sample = new();

    /// <summary>Hangs a chunk on a hinge: a joint about the hinge's axis to
    /// what is left of the building. The joint is not the chunk's child, so it
    /// keeps where it was made; the chunk tears it off (VoxelChunk).</summary>
    internal void Hang(VoxelChunk chunk, VoxelHinge hinge)
    {
        if (!IsInstanceValid(hinge.Anchor) || !hinge.Anchor.IsInsideTree())
        {
            return;
        }
        Vector3 z = hinge.Axis.Normalized();
        Vector3 x = z.Cross(Mathf.Abs(z.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var joint = new HingeJoint3D
        {
            Name = "Hinge",
            // The building still stands under it: the two must still collide.
            ExcludeNodesFromCollision = false,
        };
        AddChild(joint);
        joint.GlobalTransform = new Transform3D(new Basis(x, z.Cross(x), z), hinge.Pivot);
        joint.NodeA = joint.GetPathTo(chunk);
        joint.NodeB = joint.GetPathTo(hinge.Anchor);
        chunk.HangOn(joint, hinge.Pivot);
    }

    private readonly List<RubblePile> _piles = new();

    /// <summary>
    /// Heaps the rubble share of <paramref name="count"/> crushed voxels round
    /// a footprint <paramref name="inner"/> metres in radius, on the ground
    /// under <paramref name="centre"/>: on the pile already there, or a new one.
    /// </summary>
    public void LeaveRubble(Vector3 centre, float inner, int count, IReadOnlyList<byte> colours,
        Color[] palette, float[] emission, Material material, float voxel)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionRubble);
        int rubble = Mathf.RoundToInt(count * Stats.RubbleShare);
        if (rubble <= 0 || colours.Count == 0)
        {
            return;
        }
        RubblePile pile = null;
        foreach (RubblePile existing in _piles)
        {
            Vector3 d = (existing.GlobalPosition - centre).Slide(WorldUp.At(centre));
            if (existing.Palette == palette && d.Length() + inner < existing.Reach)
            {
                pile = existing;
                break;
            }
        }
        if (pile == null)
        {
            if (!Ground(centre, out Vector3 ground))
            {
                return;
            }
            pile = new RubblePile { Name = "Rubble", Transform = GlobalTransform.AffineInverse() * WorldUp.StandingAt(ground) };
            pile.Setup(palette, emission, material, Stats, voxel, inner + Stats.RubbleReach, Stats.RubbleHeight);
            AddChild(pile);
            _piles.Add(pile);
        }
        pile.Deposit(centre, inner, rubble, colours);
    }

    /// <summary>A settled piece, turned into part of the static heap where
    /// it lies: the same voxels, no body. False if there is no ground.</summary>
    private bool Bake(VoxelChunk chunk)
    {
        VoxelGrid grid = chunk.Grid;
        if (grid == null || grid.SolidCount == 0 || !RestsOnGround(chunk))
        {
            return false;
        }
        float size = Mathf.Max(grid.Size.X, Mathf.Max(grid.Size.Y, grid.Size.Z)) * grid.VoxelSize;
        Vector3 at = chunk.GlobalPosition;
        RubblePile pile = null;
        foreach (RubblePile existing in _piles)
        {
            Vector3 d = (existing.GlobalPosition - at).Slide(WorldUp.At(at));
            if (existing.Palette == chunk.Palette && d.Length() + size < existing.Reach)
            {
                pile = existing;
                break;
            }
        }
        if (pile == null)
        {
            if (!Ground(at, out Vector3 ground))
            {
                return false;
            }
            // Room for the pieces round it to join, rather than a heap of
            // its own each: a collapse made forty, forty more parts to draw
            // and to process. In voxels, so a mech's fine debris does not get
            // a grid of millions of cells.
            float room = Mathf.Min(Stats.RubbleReach, 48.0f * grid.VoxelSize);
            float high = Mathf.Min(Stats.RubbleHeight, 48.0f * grid.VoxelSize);
            pile = new RubblePile { Name = "Rubble", Transform = GlobalTransform.AffineInverse() * WorldUp.StandingAt(ground) };
            pile.Setup(chunk.Palette, chunk.Emission, chunk.Material, Stats, grid.VoxelSize,
                Mathf.Max(size + 2.0f, room), Mathf.Max(size + 2.0f, high));
            AddChild(pile);
            _piles.Add(pile);
            if (PerfLog.Recording)
            {
                PerfLog.Event($"heap-made #{_piles.Count}");
            }
        }
        return pile.Bake(grid, chunk.GlobalTransform);
    }

    /// <summary>
    /// Whether a piece lies on the ground or on a heap, where it can join the
    /// heap: not on a building's ledge, a mech, or another loose piece. A heap's
    /// collision is a column from its base to its top voxel, so a piece baked
    /// in where it lay on a ledge became an invisible pillar from the ground
    /// up to it. One ray down from its middle to just under its lowest point.
    /// </summary>
    private bool RestsOnGround(VoxelChunk chunk)
    {
        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null || !chunk.Grid.SolidBounds(out Aabb local))
        {
            return false;
        }
        Vector3 at = chunk.GlobalTransform * local.GetCenter();
        Vector3 up = WorldUp.At(at);
        float below = 0.0f;
        for (int k = 0; k < 8; k++)
        {
            below = Mathf.Max(below, (at - chunk.GlobalTransform * local.GetEndpoint(k)).Dot(up));
        }
        _ignore.Clear();
        _ignore.Add(chunk.GetRid());
        using var query = PhysicsRayQueryParameters3D.Create(at, at - up * (below + 1.5f),
            GameLayers.Mask(GameLayers.World, GameLayers.Prop), _ignore);
        Godot.Collections.Dictionary hit = space.IntersectRay(query);
        if (hit.Count == 0)
        {
            return false;
        }
        GodotObject collider = hit["collider"].AsGodotObject();
        // The ground and heaps are static bodies; so is a building's section.
        return collider is StaticBody3D and not VoxelCollider;
    }

    private readonly Godot.Collections.Array<Rid> _ignore = new();

    /// <summary>The ground under a point: the first World hit straight down
    /// that is not a building, a loose piece or a heap.</summary>
    private bool Ground(Vector3 from, out Vector3 ground)
    {
        ground = default;
        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return false;
        }
        var exclude = new Godot.Collections.Array<Rid>();
        for (int tries = 0; tries < 8; tries++)
        {
            Vector3 up = WorldUp.At(from);
            using var query = PhysicsRayQueryParameters3D.Create(from + up, from - up * 400.0f,
                GameLayers.Mask(GameLayers.World), exclude);
            Godot.Collections.Dictionary hit = space.IntersectRay(query);
            if (hit.Count == 0)
            {
                return false;
            }
            GodotObject collider = hit["collider"].AsGodotObject();
            if (collider is VoxelCollider or VoxelChunk || (collider is Node node && node.GetParent() is RubblePile))
            {
                exclude.Add(hit["rid"].AsRid());
                continue;
            }
            ground = hit["position"].AsVector3();
            return true;
        }
        return false;
    }

    /// <summary>
    /// A small piece of what was crushed, thrown out from the edge of the
    /// crush: something for the eye to follow out of the dust.
    /// </summary>
    public void Spall(Vector3 at, Vector3 velocity, IReadOnlyList<byte> colours, Color[] palette, float[] emission,
        Material material, float voxel, float dustScale = 1.0f)
    {
        if (colours.Count == 0 || !HasRoom(1))
        {
            return;
        }
        var random = new Random();
        var size = new Vector3I(random.Next(2, 5), random.Next(2, 5), random.Next(2, 4));
        var cells = new byte[size.X * size.Y * size.Z];
        for (int i = 0; i < cells.Length; i++)
        {
            if (random.NextDouble() < 0.8)
            {
                cells[i] = colours[random.Next(colours.Count)];
            }
        }
        var grid = new VoxelGrid(size, cells, new Vector3(size.X, size.Y, size.Z) * 0.5f, voxel);
        SpawnChunk(grid, palette, emission, material, new Transform3D(Basis.Identity, at), velocity,
            dustScale: dustScale);
    }

    /// <summary>Room for this many more pieces under the cap, counting those
    /// still waiting to be made.</summary>
    public bool HasRoom(int pieces) => _chunks.Count + _pending.Count + pieces <= Stats.MaxChunks;

    /// <summary>
    /// The piece to give up for the cap: the smallest settled rubble, else the
    /// smallest asleep, else the smallest of all. Never simply the oldest: a
    /// collapse makes its pieces bottom first, and the oldest was the base of
    /// a falling tower, which vanished in the air.
    /// </summary>
    private VoxelChunk Expendable()
    {
        VoxelChunk settled = null, sleeping = null, any = null;
        foreach (VoxelChunk chunk in _chunks)
        {
            if (chunk.IsHeld)
            {
                continue;
            }
            int n = chunk.VoxelCount;
            if (chunk.IsSettled && (settled == null || n < settled.VoxelCount))
            {
                settled = chunk;
            }
            else if (chunk.Sleeping && (sleeping == null || n < sleeping.VoxelCount))
            {
                sleeping = chunk;
            }
            if (any == null || n < any.VoxelCount)
            {
                any = chunk;
            }
        }
        return settled ?? sleeping ?? any;
    }

    /// <summary>One line of state, for <c>bench.gd</c>.</summary>
    public void PrintBenchStatus()
    {
        int settled = 0, sleeping = 0, held = 0;
        VoxelChunk biggest = null;
        foreach (VoxelChunk chunk in _chunks)
        {
            held += chunk.IsHeld ? 1 : 0;
            settled += chunk.IsSettled ? 1 : 0;
            sleeping += chunk.Sleeping ? 1 : 0;
            if (biggest == null || chunk.VoxelCount > biggest.VoxelCount)
            {
                biggest = chunk;
            }
        }
        string big = biggest == null ? "" :
            $" biggest={biggest.VoxelCount} at y={biggest.GlobalPosition.Y:F1} tilt={Mathf.RadToDeg(biggest.GlobalBasis.Y.AngleTo(Vector3.Up)):F1}° speed={biggest.LinearVelocity.Length():F1} hinged={biggest.IsHinged}";
        GD.Print($"[bench]   debris chunks={_chunks.Count} sleeping={sleeping} settled={settled} queued={_pending.Count} held={held} heaps={_piles.Count}{big}");
    }

    public static Color AverageColour(VoxelGrid grid, Color[] palette)
    {
        Color sum = new(0, 0, 0, 0);
        int n = 0;
        for (int i = 0; i < grid.Cells.Length; i += 7)
        {
            if (grid.Cells[i] != 0)
            {
                sum += palette[grid.Cells[i]];
                n++;
            }
        }
        return n > 0 ? (sum / n).LinearToSrgb() : new Color(0.5f, 0.5f, 0.5f);
    }
}
