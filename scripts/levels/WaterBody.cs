using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Levels;

/// <summary>
/// A body of shallow water (docs/decisions/0045): flat at this node's height,
/// over the cells marked in <see cref="Cells"/>, a grid relative to this node.
/// Made by flooding a hollow with the terrain brush or the bridge's
/// <c>flood</c>; its surface is a saved mesh child, drawn as it was made.
/// Moving the node moves the water. Never rotated or scaled.
///
/// A tool script so the brush can ask where water is while it scatters. It
/// adds no nodes and builds nothing: everything that asks about water asks
/// <see cref="DepthAt"/>, which reads the bodies in the tree.
/// </summary>
[Tool]
[GlobalClass]
public partial class WaterBody : Node3D
{
    [Export]
    public WaterStats Stats { get; set; }

    /// <summary>The corner of cell (0, 0), in metres from this node across X and Z.</summary>
    [Export]
    public Vector2 Corner { get; set; }

    [Export(PropertyHint.Range, "0.5,16,0.5,suffix:m")]
    public float CellSize { get; set; } = 4.0f;

    /// <summary>Cells across X.</summary>
    [Export]
    public int Width { get; set; }

    /// <summary>Cells across Z.</summary>
    [Export]
    public int Length { get; set; }

    /// <summary>1 where there is water: index (z * Width) + x.</summary>
    [Export]
    public byte[] Cells { get; set; } = Array.Empty<byte>();

    private static readonly List<WaterBody> All = new();

    public override void _EnterTree() => All.Add(this);

    public override void _ExitTree() => All.Remove(this);

    /// <summary>Metres of water over <paramref name="point"/> (its surface's height less the point's), 0 if dry.</summary>
    public static float DepthAt(Vector3 point) => DepthAt(point, out _);

    public static float DepthAt(Vector3 point, out WaterBody body)
    {
        body = null;
        float deepest = 0.0f;
        foreach (WaterBody water in All)
        {
            float depth = water.Over(point);
            if (depth > deepest)
            {
                deepest = depth;
                body = water;
            }
        }
        return deepest;
    }

    /// <summary>Depth over a point if it is over this body's water, else 0.</summary>
    public float Over(Vector3 point)
    {
        Vector3 origin = GlobalPosition;
        int x = Mathf.FloorToInt((point.X - origin.X - Corner.X) / CellSize);
        int z = Mathf.FloorToInt((point.Z - origin.Z - Corner.Y) / CellSize);
        if (x < 0 || z < 0 || x >= Width || z >= Length || Cells.Length < Width * Length || Cells[(z * Width) + x] == 0)
            return 0.0f;
        return Mathf.Max(0.0f, origin.Y - point.Y);
    }

    /// <summary>How fast one wades at <paramref name="depth"/>: 1 dry, down to <paramref name="share"/> at full depth.</summary>
    public float Pace(float depth, float share)
    {
        WaterStats stats = Stats ?? new WaterStats();
        return Mathf.Lerp(1.0f, share, Mathf.Clamp(depth / stats.FullSlowDepthM, 0.0f, 1.0f));
    }

    /// <summary>
    /// The surface over the marked cells, at this node's height, into
    /// <paramref name="mesh"/> (kept, so its file stays the scene's).
    /// </summary>
    public void FillSurface(ArrayMesh mesh, Material material)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        var corners = new Dictionary<(int, int), int>();
        int Vertex(int x, int z)
        {
            if (!corners.TryGetValue((x, z), out int i))
            {
                i = vertices.Count;
                corners[(x, z)] = i;
                vertices.Add(new Vector3(Corner.X + (x * CellSize), 0.0f, Corner.Y + (z * CellSize)));
                normals.Add(Vector3.Up);
            }
            return i;
        }
        for (int z = 0; z < Length; z++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Cells[(z * Width) + x] == 0)
                    continue;
                int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x, z + 1), d = Vertex(x + 1, z + 1);
                indices.AddRange(new[] { a, b, c, b, d, c });
            }
        }
        mesh.ClearSurfaces();
        if (indices.Count == 0)
            return;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null)
            mesh.SurfaceSetMaterial(0, material);
    }
}
