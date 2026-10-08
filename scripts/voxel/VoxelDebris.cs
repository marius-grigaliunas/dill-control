using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The per-frame allowance every voxel piece in the world shares: how many
/// sections may be re-meshed and how many structural checks run this frame.
/// Past it, work waits a frame. Static, like ImpactEffect's live budget, so a
/// piece needs no path to anything to ask.
/// </summary>
public static class VoxelBudget
{
    private static ulong _frame = ulong.MaxValue;
    private static int _sections;
    private static int _checks;

    private static void Roll()
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame != _frame)
        {
            _frame = frame;
            _sections = 0;
            _checks = 0;
        }
    }

    public static bool TakeSection(VoxelDestructionStats stats)
    {
        Roll();
        if (_sections >= stats.SectionsPerFrame)
        {
            return false;
        }
        _sections++;
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
    public static VoxelDebris Current { get; private set; }

    [Export]
    public VoxelDestructionStats Stats { get; set; }

    private DustField _dust;
    private readonly List<VoxelChunk> _chunks = new();

    public int ChunkCount => _chunks.Count;

    public override void _EnterTree()
    {
        Current = this;
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    public override void _Ready()
    {
        Stats ??= GD.Load<VoxelDestructionStats>(VoxelDestructionStats.DefaultPath);
        _dust = new DustField { Name = "Dust" };
        AddChild(_dust);
    }

    public void Dust(Vector3 position, Vector3 up, Vector3 velocity, Color colour, float edge) =>
        _dust?.Request(position, up, velocity, colour, edge);

    private readonly struct Pending
    {
        public readonly VoxelGrid Grid;
        public readonly Color[] Palette;
        public readonly float[] Emission;
        public readonly Material Material;
        public readonly Transform3D At;
        public readonly Vector3 Velocity;
        public readonly Vector3 Spin;
        public readonly ulong Asked;

        public Pending(VoxelGrid grid, Color[] palette, float[] emission, Material material, Transform3D at,
            Vector3 velocity, Vector3 spin)
        {
            Grid = grid;
            Palette = palette;
            Emission = emission;
            Material = material;
            At = at;
            Velocity = velocity;
            Spin = spin;
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
    public void SpawnChunk(VoxelGrid grid, Color[] palette, float[] emission, Material material,
        Transform3D at, Vector3 velocity, Vector3? spin = null)
    {
        if (grid.SolidCount < Stats.MinChunkVoxels || Stats.MaxChunks <= 0)
        {
            Dust(at.Origin, Vector3.Up, velocity, AverageColour(grid, palette),
                Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize);
            return;
        }

        if (spin == null)
        {
            float across = Mathf.Max(1.0f, Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize);
            float s = Stats.LooseSpin / across;
            spin = new Vector3((float)GD.RandRange(-s, s), (float)GD.RandRange(-s, s), (float)GD.RandRange(-s, s));
        }

        var pending = new Pending(grid, palette, emission, material, at, velocity, spin.Value);
        if (_pending.Count == 0 && TakeSpawn())
        {
            Make(pending, 0.0f);
            return;
        }
        _pending.Enqueue(pending);
    }

    public override void _PhysicsProcess(double delta)
    {
        ulong now = Time.GetTicksUsec();
        while (_pending.Count > 0 && TakeSpawn())
        {
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

    /// <summary>Makes a chunk that was asked for <paramref name="late"/>
    /// seconds ago, where it would be now had it been made then.</summary>
    private void Make(in Pending p, float late)
    {
        Transform3D at = p.At;
        Vector3 velocity = p.Velocity;
        if (late > 0.0f)
        {
            Vector3 gravity = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()
                              * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
            at.Origin += velocity * late + gravity * (0.5f * late * late);
            velocity += gravity * late;
            if (p.Spin.LengthSquared() > 1e-8f)
            {
                at.Basis = new Basis(p.Spin.Normalized(), p.Spin.Length() * late) * at.Basis;
            }
        }

        var chunk = new VoxelChunk { Name = "Chunk" };
        chunk.Setup(p.Grid, p.Palette, p.Emission, p.Material, Stats);
        AddChild(chunk);
        chunk.GlobalTransform = at;
        chunk.LinearVelocity = velocity;
        chunk.AngularVelocity = p.Spin;
        _chunks.Add(chunk);
        chunk.TreeExiting += () => _chunks.Remove(chunk);

        while (_chunks.Count > Stats.MaxChunks)
        {
            // Out of the count now: it is only freed at the end of the frame.
            VoxelChunk expendable = Expendable();
            _chunks.Remove(expendable);
            expendable.Dissolve();
        }
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
        int settled = 0, sleeping = 0;
        foreach (VoxelChunk chunk in _chunks)
        {
            settled += chunk.IsSettled ? 1 : 0;
            sleeping += chunk.Sleeping ? 1 : 0;
        }
        GD.Print($"[bench]   debris chunks={_chunks.Count} sleeping={sleeping} settled={settled} queued={_pending.Count}");
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
