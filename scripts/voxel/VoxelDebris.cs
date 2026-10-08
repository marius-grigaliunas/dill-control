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

    /// <summary>
    /// Makes a loose chunk of a grid, at a world transform, moving. Too small
    /// a piece is dust instead. Returns the chunk, or null.
    /// </summary>
    public VoxelChunk SpawnChunk(VoxelGrid grid, Color[] palette, float[] emission, Material material,
        Transform3D at, Vector3 velocity)
    {
        if (grid.SolidCount < Stats.MinChunkVoxels || Stats.MaxChunks <= 0)
        {
            Dust(at.Origin, Vector3.Up, velocity, AverageColour(grid, palette),
                Mathf.Pow(grid.SolidCount, 1.0f / 3.0f) * grid.VoxelSize);
            return null;
        }

        var chunk = new VoxelChunk { Name = "Chunk" };
        chunk.Setup(grid, palette, emission, material, Stats);
        AddChild(chunk);
        chunk.GlobalTransform = at;
        chunk.LinearVelocity = velocity;
        chunk.AngularVelocity = new Vector3(
            (float)GD.RandRange(-1.5, 1.5), (float)GD.RandRange(-1.5, 1.5), (float)GD.RandRange(-1.5, 1.5));
        _chunks.Add(chunk);
        chunk.TreeExiting += () => _chunks.Remove(chunk);

        while (_chunks.Count > Stats.MaxChunks)
        {
            Oldest().Dissolve();
        }
        return chunk;
    }

    private VoxelChunk Oldest()
    {
        foreach (VoxelChunk chunk in _chunks)
        {
            if (chunk.IsSettled || chunk.Sleeping)
            {
                return chunk;
            }
        }
        return _chunks[0];
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
        GD.Print($"[bench]   debris chunks={_chunks.Count} sleeping={sleeping} settled={settled}");
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
