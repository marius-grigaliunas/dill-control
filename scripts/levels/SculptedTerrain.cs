using System;
using System.Collections.Generic;
using Godot;
using DillControl.Horde;

namespace DillControl.Levels;

/// <summary>
/// Ground that is sculpted, not generated (docs/decisions/0044). It is
/// ordinary nodes with their data saved beside the scene: this body, a
/// <see cref="CollisionShape3D"/> whose <see cref="HeightMapShape3D"/> holds
/// the heights (the one copy of the ground, one sample every
/// <see cref="Spacing"/> metres, the shape scaled to match), and chunk
/// <see cref="MeshInstance3D"/>s whose meshes were made from those heights
/// when they were last sculpted. Nothing is built when the scene loads: the
/// editor shows what was saved, as the game does.
///
/// A tool script only so the editor's brush (addons/terrain_brush) and the
/// bridge can write new heights and redraw the chunks under them. The horde
/// stands on the same heights (<see cref="ArenaGround"/>).
///
/// Its position is the middle of the square and the height of zero. It is
/// never rotated or scaled. Flat-world, up is +Y, as the arena is (0026).
/// </summary>
[Tool]
[GlobalClass]
public partial class SculptedTerrain : StaticBody3D, IHordeGroundProvider
{
    [Export]
    public TerrainStats Stats { get; set; }

    /// <summary>The heights: a <see cref="HeightMapShape3D"/>, the node scaled by <see cref="Spacing"/> across.</summary>
    [Export]
    public CollisionShape3D Shape { get; set; }

    /// <summary>Metres between height samples.</summary>
    [Export(PropertyHint.Range, "0.5,16,0.5,suffix:m")]
    public float Spacing { get; set; } = 4.0f;

    /// <summary>Drawn on every chunk: <c>mat_arena_terrain.tres</c>, the planet's terrain shader told the ground is flat.</summary>
    [Export]
    public Material Material { get; set; }

    public const string ChunkPrefix = "Chunk_";

    private ArenaHeights _heights;
    private ArenaGround _ground;

    public HeightMapShape3D Map => Shape?.Shape as HeightMapShape3D;

    /// <summary>Samples along one side.</summary>
    public int Count => Map?.MapWidth ?? 0;

    /// <summary>Metres along one side.</summary>
    public float Size => (Count - 1) * Spacing;

    /// <summary>The ground as data, in world space; made again after every change.</summary>
    public ArenaHeights Heights
    {
        get
        {
            if (_heights == null && Map != null)
            {
                _heights = new ArenaHeights(GlobalPosition, Size, Spacing);
                float[] data = Map.MapData;
                Array.Copy(data, _heights.Heights, Math.Min(data.Length, _heights.Heights.Length));
                _heights.Measure();
            }
            return _heights;
        }
    }

    // ---------------- Sculpting ----------------

    /// <summary>A copy of the heights, row by row along X: index (z * Count) + x, metres over this node.</summary>
    public float[] CopyHeights() => Map?.MapData ?? Array.Empty<float>();

    /// <summary>
    /// New heights, as a whole: the shape takes them and every chunk where
    /// one differs is drawn again. The brush's strokes and their undo both
    /// come through here.
    /// </summary>
    public void SetHeights(float[] heights)
    {
        HeightMapShape3D map = Map;
        if (map == null || heights.Length != Count * Count)
        {
            GD.PushError($"SculptedTerrain '{Name}': {heights.Length} heights for a {Count}x{Count} map");
            return;
        }
        float[] old = map.MapData;
        int n = Count;
        int x0 = n, z0 = n, x1 = -1, z1 = -1;
        for (int i = 0; i < heights.Length; i++)
        {
            if (old[i] == heights[i])
                continue;
            int z = i / n, x = i - (z * n);
            x0 = Math.Min(x0, x);
            x1 = Math.Max(x1, x);
            z0 = Math.Min(z0, z);
            z1 = Math.Max(z1, z);
        }
        map.MapData = heights;
        _heights = null;
        if (x1 >= 0)
            Redraw(x0, z0, x1, z1);
    }

    /// <summary>Draws again the chunks over samples x0..x1, z0..z1, and their neighbours' shared edges (normals reach a sample over).</summary>
    public void Redraw(int x0, int z0, int x1, int z1)
    {
        int cells = Stats?.ChunkCells ?? 25;
        foreach (MeshInstance3D chunk in Chunks())
        {
            (int cx, int cz) = ChunkOf(chunk);
            int cx0 = cx * cells, cz0 = cz * cells;
            if (cx0 > x1 + 1 || cz0 > z1 + 1 || cx0 + cells < x0 - 1 || cz0 + cells < z0 - 1)
                continue;
            if (chunk.Mesh is ArrayMesh mesh)
                TerrainMesher.Fill(mesh, Heights, cx0, cz0, Math.Min(cx0 + cells, Count - 1), Math.Min(cz0 + cells, Count - 1), Material);
        }
    }

    public IEnumerable<MeshInstance3D> Chunks()
    {
        foreach (Node child in GetChildren())
        {
            if (child is MeshInstance3D mesh && child.Name.ToString().StartsWith(ChunkPrefix))
                yield return mesh;
        }
    }

    public static (int X, int Z) ChunkOf(Node chunk)
    {
        string[] parts = chunk.Name.ToString()[ChunkPrefix.Length..].Split('_');
        return (int.Parse(parts[0]), int.Parse(parts[1]));
    }

    /// <summary>The ground along a ray, marched a third of a sample at a time; null if it misses.</summary>
    public Vector3? Cast(Vector3 from, Vector3 direction, float length = 20000.0f)
    {
        ArenaHeights h = Heights;
        if (h == null)
            return null;
        direction = direction.Normalized();
        float step = Spacing / 3.0f;
        Vector3 last = from;
        bool above = !h.Contains(from.X, from.Z) || from.Y >= h.HeightAt(from.X, from.Z);
        for (float t = step; t <= length; t += step)
        {
            Vector3 p = from + (direction * t);
            if (!h.Contains(p.X, p.Z))
            {
                last = p;
                continue;
            }
            bool nowAbove = p.Y >= h.HeightAt(p.X, p.Z);
            if (above && !nowAbove)
            {
                // Between the last point above and this one below, by halves.
                Vector3 a = last, b = p;
                for (int i = 0; i < 12; i++)
                {
                    Vector3 m = (a + b) * 0.5f;
                    if (m.Y >= h.HeightAt(m.X, m.Z))
                        a = m;
                    else
                        b = m;
                }
                return (a + b) * 0.5f;
            }
            above = nowAbove;
            last = p;
        }
        return null;
    }

    // ---------------- The horde's ground ----------------

    public bool HordeGroundReady => Heights != null;

    public IHordeGround CreateHordeGround()
    {
        TerrainStats stats = Stats ?? new TerrainStats();
        _ground = new ArenaGround(Heights, stats.FlowCellSize, stats.MaxSlopeDegrees);
        return _ground;
    }

    public int PrepareFlow(FlowField flow, PhysicsDirectSpaceState3D space, Vector3 garrison)
    {
        _ground.StampSlope(flow);
        // The ground itself is never an obstacle; what stands on it is.
        return _ground.StampObstacles(space, flow, FlatGround.ObstacleMask, new Godot.Collections.Array<Rid> { GetRid() });
    }
}
