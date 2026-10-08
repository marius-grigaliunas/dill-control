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
        VoxFile.Model model, Color[] palette, float[] emission, Vector3 origin, float voxelSize, int skip)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var colours = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        Vector3I size = model.Size;
        int[] dims = { size.X, size.Y, size.Z };

        byte Solid(int[] p)
        {
            byte c = model.At(p[0], p[1], p[2]);
            return c == skip ? (byte)0 : c;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            int su = dims[u], sv = dims[v];
            byte[] mask = new byte[su * sv];
            int[] p = new int[3];
            int[] n = new int[3];

            for (int dir = -1; dir <= 1; dir += 2)
            {
                Vector3 normalVox = Vector3.Zero;
                normalVox[axis] = dir;
                Vector3 normal = VoxSpace.ToGodot(normalVox);

                for (int slice = 0; slice < dims[axis]; slice++)
                {
                    for (int pv = 0; pv < sv; pv++)
                        for (int pu = 0; pu < su; pu++)
                        {
                            p[axis] = slice; p[u] = pu; p[v] = pv;
                            byte c = Solid(p);
                            if (c != 0)
                            {
                                n[axis] = slice + dir; n[u] = pu; n[v] = pv;
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
                            AddCorner(vertices, axis, u, v, plane, pu, pv, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, pu + w, pv, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, pu + w, pv + h, origin, voxelSize);
                            AddCorner(vertices, axis, u, v, plane, pu, pv + h, origin, voxelSize);
                            for (int k = 0; k < 4; k++)
                            {
                                normals.Add(normal);
                                colours.Add(palette[c]);
                                uvs.Add(new Vector2(emission[c], 0.0f));
                            }

                            // Godot culls counter-clockwise triangles, so a
                            // front face winds clockwise seen from its normal.
                            Vector3 cross = (vertices[first + 1] - vertices[first]).Cross(vertices[first + 2] - vertices[first]);
                            if (cross.Dot(normal) < 0.0f)
                                indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
                            else
                                indices.AddRange(new[] { first, first + 2, first + 1, first, first + 3, first + 2 });
                        }
                }
            }
        }

        if (indices.Count == 0)
            return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        return arrays;
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
