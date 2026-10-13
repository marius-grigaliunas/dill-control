using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// Runs everything a collapse runs, once, while the level loads, so none of
/// it is happening for the first time in play.
///
/// <see cref="JitWarmup"/> compiles the game's own methods but not the library
/// code they use for the first time (span searches, lists and dictionaries of
/// the game's structs), and nothing warms Godot's side of a class the first
/// time one is made. The first tower to fall paid both: 4-5 ms for its first
/// contact query and first crush. Here the voxel work runs on a small made-up
/// building on a worker thread, and the Godot objects a collapse makes are
/// made and freed once on the main thread (chunks are pooled: VoxelDebris).
/// </summary>
public static class VoxelRehearsal
{
    /// <summary>On the main thread, as the level loads.</summary>
    public static void Run(Node parent, VoxelDestructionStats stats)
    {
        new HingeJoint3D().Free();

        // A contact query, on a body far below the world that never moves.
        var probe = new RigidBody3D
        {
            Name = "RehearsalProbe",
            Freeze = true,
            ContactMonitor = true,
            MaxContactsReported = 1,
            CollisionLayer = 0,
            CollisionMask = 0,
            Position = new Vector3(0.0f, -10000.0f, 0.0f),
        };
        parent.AddChild(probe);
        PhysicsServer3D.BodyGetDirectState(probe.GetRid())?.GetContactCount();
        probe.QueueFree();

        VoxelWorkers.Run(() => Voxels(stats));
    }

    /// <summary>A small hollow tower, cut, checked, split, crushed and broken
    /// up, through the same code a real one goes through.</summary>
    private static void Voxels(VoxelDestructionStats stats)
    {
        try
        {
            var size = new Vector3I(24, 24, 72);
            var cells = new byte[size.X * size.Y * size.Z];
            for (int z = 0; z < size.Z; z++)
                for (int y = 0; y < size.Y; y++)
                    for (int x = 0; x < size.X; x++)
                    {
                        bool wall = x == 0 || y == 0 || x == size.X - 1 || y == size.Y - 1;
                        if (wall || z % 8 == 0)
                        {
                            cells[x + size.X * (y + size.Y * z)] = (byte)(1 + (x + y + z) % 3);
                        }
                    }
            var grid = new VoxelGrid(size, cells, new Vector3(size.X / 2, size.Y / 2, 0), 0.5f);
            var palette = new Color[256];
            var emission = new float[256];

            var built = new int[size.Z];
            var half = new float[size.Z];
            for (int z = 0; z < size.Z; z++)
            {
                for (int i = 0; i < size.X * size.Y; i++)
                {
                    built[z] += cells[i + size.X * size.Y * z] != 0 ? 1 : 0;
                }
                half[z] = size.X * 0.5f;
            }
            VoxelSplit Split(VoxelGrid on, VoxelAnchor anchor) => new()
            {
                Grid = on,
                Anchor = anchor,
                Stats = stats,
                BuiltCount = built,
                BuiltHalf = half,
                Edge = Math.Max(4, stats.SectionEdge),
                SectionCount = new Vector3I(1, 1, (size.Z + stats.SectionEdge - 1) / stats.SectionEdge),
                Palette = palette,
                Emission = emission,
                Seed = 1,
                ChunkEdge = Math.Max(4, stats.SectionEdge),
                MaxChunkBoxes = stats.MaxChunkBoxes,
                MinChunkVoxels = stats.MinChunkVoxels,
            };

            // Cut one side low down, as a beam would: a load failure, a hinge,
            // a released piece built whole.
            var removed = new List<VoxelGrid.Removed>();
            for (int x = 0; x < size.X / 2; x++)
            {
                grid.Carve(grid.CellCentre(x, 0, 4), 1.0f, removed, out _, out _);
            }
            grid.SolidBounds(out _);
            SplitPlan plan = Split(grid, VoxelAnchor.Ground).Integrity();
            Split(grid, VoxelAnchor.Ground).Settle(plan, 1, true);

            // A piece of it: crushed, crumbled, snapped and fractured.
            var copy = new VoxelGrid(size, (byte[])cells.Clone(), grid.Origin, grid.VoxelSize);
            var all = new List<int>();
            for (int i = 0; i < copy.Cells.Length; i++)
            {
                if (copy.Cells[i] != 0)
                {
                    all.Add(i);
                }
            }
            copy.Remove(all.GetRange(0, all.Count / 8), removed);
            foreach (VoxelBreakup breakup in new[] { VoxelBreakup.Crumble, VoxelBreakup.Snap, VoxelBreakup.Fracture })
            {
                Split(new VoxelGrid(size, (byte[])cells.Clone(), grid.Origin, grid.VoxelSize), VoxelAnchor.Largest).Breakup(breakup);
            }
            VoxelSplit.TooSmallToSplit(copy, stats, true);
            var boxes = new List<(Vector3, Vector3)>();
            foreach (int step in new[] { 1, 2, 4, 8 })
            {
                boxes.Clear();
                copy.Boxes(Vector3I.Zero, size, step, boxes);
            }
            copy.Raycast(new Vector3(0, 0, 10), Vector3.Back, 50.0f, out _);
            copy.Extract(all.GetRange(all.Count / 2, all.Count / 4), out _, 8);

            // The chunk graph: a cut chunk split, a decision, a piece's
            // sub-graph, a breakup and a whole-chunk crush. First time at
            // load, same as the flood above.
            ChunkBake bake = VoxChunker.Build((byte[])grid.Cells.Clone(), size, null, 8, 2, 1, grid.Origin, grid.VoxelSize);
            if (bake != null)
            {
                VoxelChunkGraph graph = VoxelChunkGraph.FromBake(bake);
                graph.DepthVoxels = Math.Max(1, (int)Math.Round(stats.FailureDepth / grid.VoxelSize));
                grid.CellCleared = index => graph.NoteRemoved(grid, index);
                grid.Carve(grid.CellCentre(size.X - 1, 0, 8), 1.2f, removed, out _, out _);
                GraphDecision decision = graph.Decide(grid, stats, VoxelAnchor.Ground, built, half);
                var chunkOf = new int[graph.CellLength];
                graph.CopyCells(chunkOf);
                decision.Snapshot = graph.Snapshot(chunkOf);
                VoxelSplit assembled = Split(new VoxelGrid(size, (byte[])grid.Cells.Clone(), grid.Origin, grid.VoxelSize), VoxelAnchor.Ground);
                assembled.Assemble(decision, chunkOf);
                graph.Breakup(grid, stats, VoxelBreakup.Crumble);
                var slab = new List<int>();
                graph.ChunksInSlab(new Vector3(size.X * 0.5f, 0.0f, 6.0f), Vector3.Up, 10.0f, 36.0f, slab);
            }
        }
        catch (Exception exception)
        {
            GD.PushWarning($"VoxelRehearsal: {exception.Message}");
        }
    }
}
