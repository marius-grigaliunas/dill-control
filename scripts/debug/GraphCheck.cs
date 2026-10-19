using System.Collections.Generic;
using System.Text;
using Godot;
using DillControl.Voxel;

namespace DillControl.Debug;

/// <summary>
/// Checks a static voxel model's chunk graph against a flood of its cells,
/// headless: <c>graphcheck.gd</c> runs it. After each hit the graph decides
/// what falls; what it drops must be what the cells say is loose, and what it
/// keeps must still reach the ground (docs/decisions/0027).
/// </summary>
[GlobalClass]
public partial class GraphCheck : Node
{
    public string Run(string[] scenes, int hits)
    {
        var report = new StringBuilder();
        bool pass = true;
        foreach (string path in scenes)
        {
            var packed = GD.Load<PackedScene>(path);
            Node root = packed?.Instantiate();
            if (root == null)
            {
                report.AppendLine($"{path}: does not load");
                pass = false;
                continue;
            }
            foreach (VoxelBody body in Bodies(root))
            {
                // Not in a tree, the Model export may not be resolved yet:
                // every wrapper puts it beside the body.
                Node3D model = body.Model ?? body.GetParent()?.GetNodeOrNull<Node3D>("Model");
                if (!body.Static || model == null)
                {
                    continue;
                }
                foreach (MeshInstance3D host in Hosts(model))
                {
                    pass &= Check(path.GetFile(), host,
                        body.Stats ?? GD.Load<VoxelDestructionStats>("res://resources/voxel/voxel_destruction_default.tres"), hits, report);
                }
            }
            root.Free();
        }
        return (pass ? "PASS\n" : "FAIL\n") + report;
    }

    private static bool Check(string name, MeshInstance3D host, VoxelDestructionStats stats, int hits, StringBuilder report)
    {
        var grid = new VoxelGrid(
            host.GetMeta(VoxelMeta.Dims).AsVector3I(),
            (byte[])host.GetMeta(VoxelMeta.Cells).AsByteArray().Clone(),
            host.GetMeta(VoxelMeta.Origin).AsVector3(),
            0.1f);
        VoxelChunkGraph graph = VoxelChunkGraph.TryRead(host, grid.Cells.Length, host.Name);
        if (graph == null)
        {
            report.AppendLine($"{name}/{host.Name}: no graph");
            return true;
        }
        grid.CellCleared = index => graph.NoteRemoved(grid, index);
        Contacts(name, host, grid, graph, report);
        bool ok = true;
        uint state = 2463534242u;
        var cells = new List<int>();
        var hit = new List<int>();
        int wrongDrop = 0, floating = 0, dropped = 0;
        int firstWrong = -1, firstFloat = -1;
        for (int h = 0; h <= hits; h++)
        {
            if (h > 0)
            {
                // A small hit on a solid cell with air beside it, as a shot lands.
                int target = -1;
                for (int tries = 0; tries < 2000 && target < 0; tries++)
                {
                    state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                    int i = (int)(state % (uint)grid.Cells.Length);
                    if (grid.Cells[i] != 0 && Exposed(grid, i))
                    {
                        target = i;
                    }
                }
                if (target < 0)
                {
                    break;
                }
                Vector3I c = grid.CellOf(target);
                hit.Clear();
                for (int z = -2; z <= 2; z++)
                    for (int y = -2; y <= 2; y++)
                        for (int x = -2; x <= 2; x++)
                        {
                            if (x * x + y * y + z * z <= 5 && grid.InBounds(c.X + x, c.Y + y, c.Z + z))
                            {
                                hit.Add(grid.Index(c.X + x, c.Y + y, c.Z + z));
                            }
                        }
                grid.Remove(hit, null);
            }

            bool[] anchored = Anchored(grid);
            graph.DepthVoxels = 8;
            GraphDecision decision = graph.Decide(grid, stats, VoxelAnchor.Ground, null, null);
            cells.Clear();
            foreach (List<int> piece in decision.PieceChunks)
            {
                foreach (int chunk in piece)
                {
                    graph.CellsOf(grid, chunk, cells);
                }
            }
            int wrongNow = 0;
            foreach (int i in cells)
            {
                if (anchored[i])
                {
                    wrongNow++;
                }
            }
            if (wrongNow > 0 && firstWrong < 0)
            {
                firstWrong = h;
                Contacts(name + $" at hit {h}", host, grid, graph, report);
            }
            wrongDrop += wrongNow;
            dropped += cells.Count;
            grid.Remove(cells, null);
            grid.Remove(decision.DustCells, null);

            bool[] after = Anchored(grid);
            int floatNow = 0;
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                if (grid.Cells[i] != 0 && !after[i])
                {
                    floatNow++;
                }
            }
            if (floatNow > 0 && firstFloat < 0)
            {
                firstFloat = h;
            }
            floating = floatNow;
        }
        ok = wrongDrop == 0 && floating == 0;
        report.AppendLine($"{(ok ? "ok  " : "BAD ")}{name}/{host.Name}: chunks={graph.Count} voxels={grid.InitialCount} dropped={dropped} " +
            $"wrongly-dropped={wrongDrop} (first at hit {firstWrong}) floating-at-end={floating} (first at hit {firstFloat})");
        return ok;
    }

    /// <summary>Contacts counted from the cells against the graph's links.</summary>
    private static void Contacts(string name, MeshInstance3D host, VoxelGrid grid, VoxelChunkGraph graph, StringBuilder report)
    {
        var faces = new Dictionary<long, int>();
        int unchunked = 0, mismatchSolid = 0;
        var solid = new int[graph.Count];
        for (int i = 0; i < grid.Cells.Length; i++)
        {
            if (grid.Cells[i] == 0)
            {
                if (graph.ChunkOf(i) >= 0) mismatchSolid++;
                continue;
            }
            int a = graph.ChunkOf(i);
            if (a < 0) { unchunked++; continue; }
            solid[a]++;
            Vector3I c = grid.CellOf(i);
            foreach (Vector3I d in new[] { new Vector3I(1, 0, 0), new Vector3I(0, 1, 0), new Vector3I(0, 0, 1) })
            {
                Vector3I n = c + d;
                if (!grid.InBounds(n.X, n.Y, n.Z) || grid.At(n.X, n.Y, n.Z) == 0) continue;
                int b = graph.ChunkOf(grid.Index(n.X, n.Y, n.Z));
                if (b < 0 || b == a) continue;
                long key = a < b ? a + (long)b * graph.Count : b + (long)a * graph.Count;
                faces.TryGetValue(key, out int f);
                faces[key] = f + 1;
            }
        }
        var linksField = typeof(VoxelChunkGraph).GetField("_links", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var links = (System.Collections.IList)linksField.GetValue(graph);
        int missing = 0, extra = 0, wrongCount = 0, solidOff = 0;
        var linkFaces = new Dictionary<long, int>();
        for (int a = 0; a < graph.Count; a++)
        {
            foreach (object l in (System.Collections.IEnumerable)links[a])
            {
                int b = (int)l.GetType().GetField("Other").GetValue(l);
                int live = (int)l.GetType().GetField("Live").GetValue(l);
                long key = a < b ? a + (long)b * graph.Count : b + (long)a * graph.Count;
                linkFaces[key] = live;
            }
            if (solid[a] != graph.SolidOf(a)) solidOff++;
        }
        foreach ((long key, int f) in faces)
        {
            if (!linkFaces.TryGetValue(key, out int live)) missing++;
            else if (live != f) wrongCount++;
        }
        foreach (long key in linkFaces.Keys)
        {
            if (!faces.ContainsKey(key)) extra++;
        }
        report.AppendLine($"     {name}/{host.Name}: contacts={faces.Count} links={linkFaces.Count} missing={missing} extra={extra} " +
            $"face-count-off={wrongCount} solid-off={solidOff} unchunked-cells={unchunked} chunked-air={mismatchSolid}");
    }

    private static bool Exposed(VoxelGrid grid, int i)
    {
        Vector3I c = grid.CellOf(i);
        return grid.At(c.X + 1, c.Y, c.Z) == 0 || grid.At(c.X - 1, c.Y, c.Z) == 0
            || grid.At(c.X, c.Y + 1, c.Z) == 0 || grid.At(c.X, c.Y - 1, c.Z) == 0
            || grid.At(c.X, c.Y, c.Z + 1) == 0;
    }

    /// <summary>Cells face-connected to the bottom layer, as the old flood anchored them.</summary>
    private static bool[] Anchored(VoxelGrid grid)
    {
        var seen = new bool[grid.Cells.Length];
        var queue = new Queue<int>();
        int layer = grid.Size.X * grid.Size.Y;
        for (int i = 0; i < layer; i++)
        {
            if (grid.Cells[i] != 0)
            {
                seen[i] = true;
                queue.Enqueue(i);
            }
        }
        int sx = grid.Size.X, sxy = layer;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            Vector3I c = grid.CellOf(i);
            void Visit(int j)
            {
                if (grid.Cells[j] != 0 && !seen[j])
                {
                    seen[j] = true;
                    queue.Enqueue(j);
                }
            }
            if (c.X > 0) Visit(i - 1);
            if (c.X < grid.Size.X - 1) Visit(i + 1);
            if (c.Y > 0) Visit(i - sx);
            if (c.Y < grid.Size.Y - 1) Visit(i + sx);
            if (c.Z > 0) Visit(i - sxy);
            if (c.Z < grid.Size.Z - 1) Visit(i + sxy);
        }
        return seen;
    }

    private static IEnumerable<VoxelBody> Bodies(Node root)
    {
        if (root is VoxelBody body)
        {
            yield return body;
        }
        foreach (Node child in root.GetChildren())
        {
            foreach (VoxelBody b in Bodies(child))
            {
                yield return b;
            }
        }
    }

    private static IEnumerable<MeshInstance3D> Hosts(Node root)
    {
        if (root == null)
        {
            yield break;
        }
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.HasMeta(VoxelMeta.Cells))
            {
                yield return mesh;
            }
            foreach (MeshInstance3D m in Hosts(child))
            {
                yield return m;
            }
        }
    }
}
