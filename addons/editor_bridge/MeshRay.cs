#if TOOLS
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// A ray against the meshes of the edited scene, as they are drawn. The
/// editor does not step physics, and what is seen is what something should
/// stand on, so this asks the meshes, not the colliders.
/// </summary>
public static class MeshRay
{
    public readonly record struct Hit(Vector3 Point, Vector3 Normal, MeshInstance3D Mesh, float Distance);

    // A mesh's triangles, made once while the mesh lives.
    private static readonly ConditionalWeakTable<Mesh, TriangleMesh> Triangles = new();

    /// <summary>The nearest visible mesh along the ray, leaving out <paramref name="exclude"/> and everything under it.</summary>
    public static Hit? Cast(Node root, Vector3 from, Vector3 direction, Node exclude = null, float length = 20000.0f)
    {
        direction = direction.Normalized();
        Hit? best = null;
        foreach (MeshInstance3D mesh in Meshes(root))
        {
            if (exclude != null && (mesh == exclude || exclude.IsAncestorOf(mesh)))
                continue;
            Transform3D xform = mesh.GlobalTransform;
            Aabb box = xform * mesh.Mesh.GetAabb();
            if (!box.Grow(0.01f).IntersectsSegment(from, from + (direction * length)))
                continue;
            TriangleMesh triangles = Triangles.GetValue(mesh.Mesh, m => m.GenerateTriangleMesh());
            if (triangles == null)
                continue;
            Transform3D inverse = xform.AffineInverse();
            Godot.Collections.Dictionary hit = triangles.IntersectRay(inverse * from, (inverse.Basis * direction).Normalized());
            if (hit == null || hit.Count == 0)
                continue;
            Vector3 point = xform * hit["position"].AsVector3();
            float distance = from.DistanceTo(point);
            if (distance > length || (best != null && distance >= best.Value.Distance))
                continue;
            Vector3 normal = (xform.Basis.Inverse().Transposed() * hit["normal"].AsVector3()).Normalized();
            best = new Hit(point, normal, mesh, distance);
        }
        return best;
    }

    /// <summary>The highest surface under (x, z), looking straight down from high above.</summary>
    public static Hit? Down(Node root, float x, float z, Node exclude = null) =>
        Cast(root, new Vector3(x, 10000.0f, z), Vector3.Down, exclude);

    private static IEnumerable<MeshInstance3D> Meshes(Node root)
    {
        foreach (Node node in root.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null && mesh.IsVisibleInTree())
                yield return mesh;
        }
    }
}
#endif
