using Godot;

namespace DillControl.Levels;

/// <summary>
/// One chunk of a <see cref="SculptedTerrain"/> as a mesh: a grid of its
/// height samples, positions relative to the terrain's node, normals from
/// the heights. Written into a mesh that already exists, so a chunk keeps
/// its saved file and the scene keeps pointing at it.
/// </summary>
public static class TerrainMesher
{
    /// <summary>Samples x0..x1, z0..z1 (inclusive) into <paramref name="mesh"/>, replacing what it held.</summary>
    public static void Fill(ArrayMesh mesh, ArenaHeights heights, int x0, int z0, int x1, int z1, Material material)
    {
        int w = x1 - x0 + 1;
        int d = z1 - z0 + 1;
        var vertices = new Vector3[w * d];
        var normals = new Vector3[w * d];
        var origin = new Vector3(heights.Min.X + (heights.Size * 0.5f), heights.Base, heights.Min.Y + (heights.Size * 0.5f));
        for (int z = 0; z < d; z++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector3 point = heights.SamplePoint(x0 + x, z0 + z);
                vertices[(z * w) + x] = point - origin;
                normals[(z * w) + x] = heights.NormalAt(point.X, point.Z, heights.Spacing);
            }
        }

        var indices = new int[(w - 1) * (d - 1) * 6];
        int n = 0;
        for (int z = 0; z < d - 1; z++)
        {
            for (int x = 0; x < w - 1; x++)
            {
                int a = (z * w) + x;
                int b = a + 1;
                int c = a + w;
                int e = c + 1;
                indices[n++] = a;
                indices[n++] = b;
                indices[n++] = c;
                indices[n++] = b;
                indices[n++] = e;
                indices[n++] = c;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        mesh.ClearSurfaces();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (material != null)
            mesh.SurfaceSetMaterial(0, material);
    }
}
