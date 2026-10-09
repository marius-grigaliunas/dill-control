using System;
using System.Buffers;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// Turns one <see cref="VoxFile.Model"/> into a mesh: greedy-meshed faces,
/// one quad per run of same-coloured exposed faces, hidden faces never made.
///
/// DECISION -- colour per vertex, not a palette texture. Every .vox carries its
/// own 256 colours, and bought packs carry theirs. Baking colour into the
/// vertices lets every voxel model in the game share one material
/// (<c>mat_voxel_toon</c>) whatever palette it was painted with, and needs no
/// remap. Emission strength rides in <c>UV.x</c>, read by
/// <c>shaders/voxel_toon.gdshader</c>. See docs/decisions/0009.
///
/// Greedy quads leave T-junctions where runs of different lengths meet, which
/// can show as a stray pixel along a seam. The vertex count saved is worth it.
/// </summary>
public static class VoxMesher
{
    /// <summary>
    /// Builds surface arrays for <see cref="Mesh.ArrayType"/>, or null when the
    /// model has no visible face.
    /// </summary>
    /// <param name="origin">The point, in the model's voxel coordinates, that
    /// becomes the mesh's origin.</param>
    /// <param name="skip">A palette index treated as empty (the pivot marker),
    /// or -1.</param>
    public static Godot.Collections.Array Build(
        VoxFile.Model model, Color[] palette, float[] emission, Vector3 origin, float voxelSize, int skip) =>
        Build(model.Cells, model.Size, Vector3I.Zero, model.Size, palette, emission, origin, voxelSize, skip);

    /// <summary>
    /// Meshes one box of a grid: the cells from <paramref name="regionMin"/>
    /// (inclusive) to <paramref name="regionMax"/> (exclusive). A face on the
    /// region's edge still looks at the neighbour outside it, so sections meshed
    /// one by one meet with no seam and no doubled face. How a destructible
    /// body re-meshes only the section a hit landed in.
    /// </summary>
    public static Godot.Collections.Array Build(
        byte[] cells, Vector3I size, Vector3I regionMin, Vector3I regionMax,
        Color[] palette, float[] emission, Vector3 origin, float voxelSize, int skip) =>
        Hand(BuildArrays(cells, size, regionMin, regionMax, palette, emission, origin, voxelSize, skip));

    private static Godot.Collections.Array Hand(Arrays arrays)
    {
        if (arrays == null)
        {
            return null;
        }
        Godot.Collections.Array godot = arrays.ToGodot();
        arrays.Release();
        return godot;
    }

    /// <summary>A mesh's arrays as plain C# arrays: what a worker thread can
    /// build without touching Godot, for the main thread to hand over.</summary>
    ///
    /// The arrays are rented from the shared pools and longer than they need
    /// be: <see cref="VertexCount"/> and <see cref="IndexCount"/> say how much
    /// is used. Hand them back with <see cref="Release"/> once Godot has its
    /// copy. A collapse re-meshes sections by the dozen, and new arrays (and
    /// the lists grown to fill them) for each kept the collector running
    /// twice a crush.
    public sealed class Arrays
    {
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color[] Colours;
        public Vector2[] Uvs;
        public int[] Indices;
        public int VertexCount;
        public int IndexCount;

        /// <summary>Godot's surface arrays, copied out of the rented ones.</summary>
        public Godot.Collections.Array ToGodot()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Variant.CreateFrom(new Span<Vector3>(Vertices, 0, VertexCount));
            arrays[(int)Mesh.ArrayType.Normal] = Variant.CreateFrom(new Span<Vector3>(Normals, 0, VertexCount));
            arrays[(int)Mesh.ArrayType.Color] = Variant.CreateFrom(new Span<Color>(Colours, 0, VertexCount));
            arrays[(int)Mesh.ArrayType.TexUV] = Variant.CreateFrom(new Span<Vector2>(Uvs, 0, VertexCount));
            arrays[(int)Mesh.ArrayType.Index] = Variant.CreateFrom(new Span<int>(Indices, 0, IndexCount));
            return arrays;
        }

        /// <summary>Back to the pools. Not used again after.</summary>
        public void Release()
        {
            if (Vertices == null)
            {
                return;
            }
            ArrayPool<Vector3>.Shared.Return(Vertices);
            ArrayPool<Vector3>.Shared.Return(Normals);
            ArrayPool<Color>.Shared.Return(Colours);
            ArrayPool<Vector2>.Shared.Return(Uvs);
            ArrayPool<int>.Shared.Return(Indices);
            Vertices = null;
            Normals = null;
            Colours = null;
            Uvs = null;
            Indices = null;
        }
    }

    // Each thread's working lists, kept and cleared rather than made per build.
    [ThreadStatic] private static List<Vector3> _vertices;
    [ThreadStatic] private static List<Vector3> _normals;
    [ThreadStatic] private static List<Color> _colours;
    [ThreadStatic] private static List<Vector2> _uvs;
    [ThreadStatic] private static List<int> _indices;

    private static T[] Rent<T>(List<T> from)
    {
        T[] array = ArrayPool<T>.Shared.Rent(Math.Max(1, from.Count));
        from.CopyTo(array);
        return array;
    }

    /// <summary>The same as <see cref="Build(byte[], Vector3I, Vector3I, Vector3I, Color[], float[], Vector3, float, int)"/>,
    /// touching nothing of Godot's: safe on any thread.</summary>
    public static Arrays BuildArrays(
        byte[] cells, Vector3I size, Vector3I regionMin, Vector3I regionMax,
        Color[] palette, float[] emission, Vector3 origin, float voxelSize, int skip)
    {
        List<Vector3> vertices = _vertices ??= new List<Vector3>();
        List<Vector3> normals = _normals ??= new List<Vector3>();
        List<Color> colours = _colours ??= new List<Color>();
        List<Vector2> uvs = _uvs ??= new List<Vector2>();
        List<int> indices = _indices ??= new List<int>();
        vertices.Clear();
        normals.Clear();
        colours.Clear();
        uvs.Clear();
        indices.Clear();

        int[] dims = { size.X, size.Y, size.Z };
        int[] lo = { regionMin.X, regionMin.Y, regionMin.Z };
        int[] hi = { regionMax.X, regionMax.Y, regionMax.Z };

        byte Solid(int[] p)
        {
            if (p[0] < 0 || p[1] < 0 || p[2] < 0 || p[0] >= dims[0] || p[1] >= dims[1] || p[2] >= dims[2])
            {
                return 0;
            }
            byte c = cells[p[0] + dims[0] * (p[1] + dims[1] * p[2])];
            return c == skip ? (byte)0 : c;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            int su = hi[u] - lo[u], sv = hi[v] - lo[v];
            byte[] mask = new byte[su * sv];
            int[] p = new int[3];
            int[] n = new int[3];

            for (int dir = -1; dir <= 1; dir += 2)
            {
                Vector3 normalVox = Vector3.Zero;
                normalVox[axis] = dir;
                Vector3 normal = VoxSpace.ToGodot(normalVox);

                for (int slice = lo[axis]; slice < hi[axis]; slice++)
                {
                    for (int pv = 0; pv < sv; pv++)
                        for (int pu = 0; pu < su; pu++)
                        {
                            p[axis] = slice; p[u] = lo[u] + pu; p[v] = lo[v] + pv;
                            byte c = Solid(p);
                            if (c != 0)
                            {
                                n[axis] = slice + dir; n[u] = lo[u] + pu; n[v] = lo[v] + pv;
                                if (Solid(n) != 0)
                                    c = 0;
                            }
                            mask[pu + pv * su] = c;
                        }

                    float plane = slice + (dir > 0 ? 1 : 0);
                    for (int pv = 0; pv < sv; pv++)
                        for (int pu = 0; pu < su; pu++)
                        {
                            byte c = mask[pu + pv * su];
                            if (c == 0)
                                continue;

                            int w = 1;
                            while (pu + w < su && mask[pu + w + pv * su] == c)
                                w++;
                            int h = 1;
                            while (pv + h < sv && RowMatches(mask, su, pu, pv + h, w, c))
                                h++;
                            for (int dv = 0; dv < h; dv++)
                                for (int du = 0; du < w; du++)
                                    mask[pu + du + (pv + dv) * su] = 0;

                            int first = vertices.Count;
                            int cu = lo[u] + pu, cv = lo[v] + pv;
                            AddCorner(vertices, axis, u, v, plane, cu, cv, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, cu + w, cv, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, cu + w, cv + h, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, cu, cv + h, origin, voxelSize);
                            for (int k = 0; k < 4; k++)
                            {
                                normals.Add(normal);
                                colours.Add(palette[c]);
                                uvs.Add(new Vector2(emission[c], 0.0f));
                            }

                            // Godot culls counter-clockwise triangles, so a
                            // front face winds clockwise seen from its normal.
                            Vector3 cross = (vertices[first + 1] - vertices[first]).Cross(vertices[first + 2] - vertices[first]);
                            bool clockwise = cross.Dot(normal) < 0.0f;
                            indices.Add(first);
                            indices.Add(clockwise ? first + 1 : first + 2);
                            indices.Add(clockwise ? first + 2 : first + 1);
                            indices.Add(first);
                            indices.Add(clockwise ? first + 2 : first + 3);
                            indices.Add(clockwise ? first + 3 : first + 2);
                        }
                }
            }
        }

        if (indices.Count == 0)
            return null;

        return new Arrays
        {
            Vertices = Rent(vertices),
            Normals = Rent(normals),
            Colours = Rent(colours),
            Uvs = Rent(uvs),
            Indices = Rent(indices),
            VertexCount = vertices.Count,
            IndexCount = indices.Count,
        };
    }

    private static bool RowMatches(byte[] mask, int su, int pu, int pv, int w, byte c)
    {
        for (int du = 0; du < w; du++)
            if (mask[pu + du + pv * su] != c)
                return false;
        return true;
    }

    private static void AddCorner(
        List<Vector3> vertices, int axis, int u, int v, float plane, int cu, int cv, Vector3 origin, float voxelSize)
    {
        Vector3 vox = Vector3.Zero;
        vox[axis] = plane;
        vox[u] = cu;
        vox[v] = cv;
        vertices.Add(VoxSpace.ToGodot(vox - origin) * voxelSize);
    }
}
