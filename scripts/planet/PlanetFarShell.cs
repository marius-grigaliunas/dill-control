using Godot;
using DillControl.Core;

namespace DillControl.Planet;

/// <summary>
/// The whole planet in six coarse meshes, one per cube face, always drawn:
/// what you see from orbit, and what fills in past the near chunks' draw
/// distance on the way down.
///
/// The lab had nothing like it. Its near chunks were the only planet there
/// was, culled to what the viewer's horizon could see, so from space the
/// planet was a jagged disc of chunks round the point below you -- the pitch's
/// "jagged edge" open question. Six meshes of a few thousand vertices draw the
/// silhouette whole for six draw calls.
///
/// Each shell vertex takes the lowest ground its neighbourhood covers, sunk a
/// little further, so the coarse triangles stay under the fine ones wherever
/// both are drawn and the near chunks always win the depth test.
/// </summary>
public partial class PlanetFarShell : Node3D
{
    private readonly MeshInstance3D[] _faces = new MeshInstance3D[CubeSphere.FaceCount];

    public void Build(PlanetHeightfield heights, int vertexEdge, float sink, Material material)
    {
        int edge = Mathf.Max(3, vertexEdge);
        int fine = heights.FaceVertexEdge;
        float stride = (fine - 1) / (float)(edge - 1);
        int window = Mathf.CeilToInt(stride);
        float sampleArc = stride * heights.VertexSpacing;

        int[] indices = BuildIndices(edge);

        for (int face = 0; face < CubeSphere.FaceCount; face++)
        {
            var vertices = new Vector3[edge * edge];
            var normals = new Vector3[edge * edge];
            int f = face;

            Jobs.For(edge, j =>
            {
                for (int i = 0; i < edge; i++)
                {
                    float a = (i / (float)(edge - 1) * 2.0f) - 1.0f;
                    float b = (j / (float)(edge - 1) * 2.0f) - 1.0f;
                    Vector3 direction = CubeSphere.Direction(f, a, b);

                    int cu = Mathf.RoundToInt(i * stride);
                    int cv = Mathf.RoundToInt(j * stride);
                    float lowest = float.MaxValue;
                    for (int v = Mathf.Max(0, cv - window); v <= Mathf.Min(fine - 1, cv + window); v++)
                    {
                        for (int u = Mathf.Max(0, cu - window); u <= Mathf.Min(fine - 1, cu + window); u++)
                        {
                            lowest = Mathf.Min(lowest, heights.HeightAt(heights.VertexIndex(f, u, v)));
                        }
                    }

                    int k = (j * edge) + i;
                    vertices[k] = direction * (heights.Radius + lowest - sink);
                    normals[k] = heights.NormalAt(direction, sampleArc);
                }
            });

            Godot.Collections.Array surface = new();
            surface.Resize((int)Mesh.ArrayType.Max);
            surface[(int)Mesh.ArrayType.Vertex] = vertices;
            surface[(int)Mesh.ArrayType.Normal] = normals;
            surface[(int)Mesh.ArrayType.Index] = indices;

            ArrayMesh mesh = new();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface);

            _faces[face]?.QueueFree();
            _faces[face] = new MeshInstance3D
            {
                Name = $"Face{face}",
                Mesh = mesh,
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_faces[face]);
        }
    }

    /// <summary>Wound as the chunks are: AxisA cross AxisB is outward on every face.</summary>
    private static int[] BuildIndices(int edge)
    {
        int[] indices = new int[(edge - 1) * (edge - 1) * 6];
        int write = 0;
        for (int j = 0; j < edge - 1; j++)
        {
            for (int i = 0; i < edge - 1; i++)
            {
                int v00 = (j * edge) + i;
                int v10 = v00 + 1;
                int v01 = v00 + edge;
                int v11 = v01 + 1;

                indices[write++] = v00;
                indices[write++] = v01;
                indices[write++] = v11;

                indices[write++] = v00;
                indices[write++] = v11;
                indices[write++] = v10;
            }
        }
        return indices;
    }
}
