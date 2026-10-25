using System.Collections.Generic;
using System.Text;
using Godot;
using DillControl.Voxel;

namespace DillControl.Debug;

/// <summary>
/// Holds every mech to the part rules and its damage map to sense, headless:
/// <c>mechcheck.gd</c> runs it (docs/decisions/0046). Per mech: one root,
/// and it is the walker's core; every other part hangs from a part that
/// exists, through a chain that reaches the core, and touches it; the
/// walker's legs and arms are chained thigh, shin, foot from the core; every
/// part but the core is hinged; each foot has a sole on the ground; and at
/// rest its weight stands over its feet, if it has three or more.
/// </summary>
[GlobalClass]
public partial class MechCheck : Node
{
    /// <summary>A sole this far above the mech's lowest voxel is off the ground.</summary>
    private const float SoleSlackVoxels = 2.0f;

    public string Run(string[] scenes)
    {
        var report = new StringBuilder();
        bool pass = true;
        foreach (string path in scenes)
        {
            var packed = GD.Load<PackedScene>(path);
            Node root = packed?.Instantiate();
            if (root == null)
            {
                report.AppendLine($"{path.GetFile()}: does not load");
                pass = false;
                continue;
            }
            pass &= Check(path.GetFile(), packed.GetState(), root, report);
            root.Free();
        }
        return (pass ? "PASS\n" : "FAIL\n") + report;
    }

    private static bool Check(string name, SceneState state, Node root, StringBuilder report)
    {
        // Not in a tree the exports are not resolved: read their paths.
        Dictionary<string, Variant> walker = Properties(state, 0);
        Node3D model = Resolve(root, walker, "Model");
        Node3D core = Resolve(root, walker, "Core");
        if (model == null || core == null)
        {
            report.AppendLine($"{name}: no Model or Core on the walker");
            return false;
        }
        Dictionary<string, Variant> voxels = null;
        for (int n = 0; n < state.GetNodeCount(); n++)
        {
            if (state.GetNodeName(n) == "Voxels")
            {
                voxels = Properties(state, n);
            }
        }

        var hosts = new Dictionary<string, MeshInstance3D>();
        var bakes = new Dictionary<string, DamageBake>();
        foreach (Node child in model.GetChildren())
        {
            if (child is MeshInstance3D host && host.HasMeta(VoxelMeta.Cells))
            {
                hosts[host.Name] = host;
                bakes[host.Name] = VoxDamageMap.TryRead(host);
            }
        }

        var errors = new List<string>();
        float voxel = model.GetMeta(VoxelMeta.VoxelSize, 0.1f).AsSingle();
        var stats = voxels != null && voxels.TryGetValue("Stats", out Variant s) ? s.As<VoxelDestructionStats>() : null;
        stats ??= GD.Load<VoxelDestructionStats>(VoxelDestructionStats.DefaultPath);
        float kgPerVoxel = voxel * voxel * voxel * stats.Density;

        foreach ((string part, DamageBake bake) in bakes)
        {
            if (bake == null)
            {
                errors.Add($"{part}: no damage map (reimport the .vox)");
            }
        }
        if (errors.Count > 0)
        {
            return Fail(name, errors, report);
        }

        // One root, the core; every chain reaches it; every part touches its parent.
        var roots = new List<string>();
        foreach ((string part, DamageBake bake) in bakes)
        {
            if (bake.Parent == "")
            {
                roots.Add(part);
                continue;
            }
            if (!bakes.ContainsKey(bake.Parent))
            {
                errors.Add($"{part}: hangs from '{bake.Parent}', which is not a part");
                continue;
            }
            string at = part;
            for (int steps = 0; at != "" && steps <= bakes.Count; steps++)
            {
                at = bakes.TryGetValue(at, out DamageBake up) ? up.Parent : "";
            }
            if (at != "")
            {
                errors.Add($"{part}: its parents loop");
            }
            if (bake.Hold.Length == 0)
            {
                errors.Add($"{part}: touches nothing of {bake.Parent} (a gap at its hinge)");
            }
        }
        if (roots.Count != 1 || roots[0] != core.Name)
        {
            errors.Add($"roots are [{string.Join(", ", roots)}]; the walker's core is {core.Name}");
        }

        foreach ((string part, DamageBake bake) in bakes)
        {
            byte[] cells = hosts[part].GetMeta(VoxelMeta.Cells).AsByteArray();
            int solid = 0;
            foreach (byte c in cells)
            {
                solid += c != 0 ? 1 : 0;
            }
            if (solid != bake.Voxels)
            {
                errors.Add($"{part}: baked {bake.Voxels} voxels, has {solid}");
            }
            if (bake.ContactKinds.Length == 0)
            {
                errors.Add($"{part}: no contacts");
            }
        }

        // The walker's limbs are chained the way its bake says.
        Godot.Collections.Array<Node3D> thighs = ResolveAll(root, walker, "Thighs");
        Godot.Collections.Array<Node3D> shins = ResolveAll(root, walker, "Shins");
        Godot.Collections.Array<Node3D> feet = ResolveAll(root, walker, "Feet");
        for (int i = 0; i < thighs.Count; i++)
        {
            ExpectParent(bakes, thighs[i], core, errors);
            ExpectParent(bakes, i < shins.Count ? shins[i] : null, thighs[i], errors);
            ExpectParent(bakes, i < feet.Count ? feet[i] : null, i < shins.Count ? shins[i] : null, errors);
        }
        foreach (Node3D arm in ResolveAll(root, walker, "Arms"))
        {
            ExpectParent(bakes, arm, core, errors);
        }
        ExpectParent(bakes, Resolve(root, walker, "Turret"), core, errors);

        // Every part but the core hangs from its hinge.
        var hinged = new HashSet<string>();
        if (voxels == null)
        {
            errors.Add("no VoxelBody 'Voxels' in its wrapper: it cannot be damaged");
        }
        else if (voxels.TryGetValue("Hinged", out Variant list))
        {
            foreach (NodePath p in list.AsGodotArray<NodePath>())
            {
                hinged.Add(p.GetName(p.GetNameCount() - 1));
            }
        }
        foreach ((string part, DamageBake bake) in bakes)
        {
            if (voxels != null && bake.Parent != "" && !hinged.Contains(part))
            {
                errors.Add($"{part}: not in the VoxelBody's Hinged, so it would not hang from its hinge");
            }
        }

        // Weight, its centre, and the soles under it.
        float lowest = float.MaxValue;
        foreach ((string part, DamageBake bake) in bakes)
        {
            for (int k = 0; k < bake.ContactKinds.Length; k++)
            {
                if (bake.ContactKinds[k] == ContactKind.Sole)
                {
                    lowest = Mathf.Min(lowest, SoleBottom(hosts[part], bake, k));
                }
            }
        }
        var support = new List<Vector2>();
        foreach (Node3D foot in feet)
        {
            if (foot == null || !bakes.TryGetValue(foot.Name, out DamageBake bake))
            {
                continue;
            }
            int sole = System.Array.IndexOf(bake.ContactKinds, ContactKind.Sole);
            float bottom = SoleBottom(hosts[foot.Name], bake, sole);
            if (bottom > lowest + SoleSlackVoxels * voxel)
            {
                errors.Add($"{foot.Name}: its sole is {(bottom - lowest) / voxel:0} voxels off the ground");
                continue;
            }
            Transform3D t = hosts[foot.Name].Transform;
            Vector3 c = bake.ContactCentre[sole], h = bake.ContactSize[sole] * 0.5f;
            foreach (Vector3 corner in new[] { new Vector3(-h.X, 0, -h.Z), new Vector3(h.X, 0, -h.Z), new Vector3(-h.X, 0, h.Z), new Vector3(h.X, 0, h.Z) })
            {
                Vector3 at = t * (c + corner);
                support.Add(new Vector2(at.X, at.Z));
            }
        }
        int total = 0;
        Vector3 weighted = Vector3.Zero;
        foreach ((string part, DamageBake bake) in bakes)
        {
            total += bake.Voxels;
            weighted += hosts[part].Transform * bake.Centre * bake.Voxels;
        }
        Vector3 centre = total > 0 ? weighted / total : Vector3.Zero;
        float margin = Margin(support, new Vector2(centre.X, centre.Z));
        // Two feet balance only walking: a biped is not held to this.
        if (feet.Count >= 3 && margin < 0.0f)
        {
            errors.Add($"at rest its centre of mass is {-margin:0.00} m outside its feet");
        }

        report.AppendLine($"{name}: {bakes.Count} parts, {total} voxels, {total * kgPerVoxel / 1000.0f:0.0} t; centre of mass {centre.Y - lowest:0.00} m up, {margin:0.00} m inside its feet");
        foreach ((string part, DamageBake bake) in bakes)
        {
            var contacts = new StringBuilder();
            for (int k = 0; k < bake.ContactKinds.Length; k++)
            {
                Vector3 z = bake.ContactSize[k];
                contacts.Append($" {bake.ContactKinds[k].ToString().ToLowerInvariant()} {z.X:0.0}x{z.Y:0.0}x{z.Z:0.0}");
            }
            report.AppendLine($"  {part,-8} from {(bake.Parent == "" ? "-" : bake.Parent),-8} {bake.Voxels,6} voxels {bake.Voxels * kgPerVoxel,8:0} kg  held by {bake.Hold.Length,4}{contacts}");
        }
        return errors.Count == 0 || Fail(name, errors, report);
    }

    private static float SoleBottom(MeshInstance3D host, DamageBake bake, int k) =>
        (host.Transform * (bake.ContactCentre[k] - new Vector3(0, bake.ContactSize[k].Y * 0.5f, 0))).Y;

    /// <summary>How far inside the hull of <paramref name="points"/> the point
    /// is: negative outside.</summary>
    private static float Margin(List<Vector2> points, Vector2 at)
    {
        if (points.Count < 3)
        {
            return 0.0f;
        }
        Vector2[] hull = Geometry2D.ConvexHull(points.ToArray());
        float area = 0.0f;
        for (int i = 0; i + 1 < hull.Length; i++)
        {
            area += hull[i].Cross(hull[i + 1]);
        }
        float sign = area >= 0.0f ? 1.0f : -1.0f;
        float margin = float.MaxValue;
        for (int i = 0; i + 1 < hull.Length; i++)
        {
            Vector2 edge = hull[i + 1] - hull[i];
            if (edge.LengthSquared() < 1e-8f)
            {
                continue;
            }
            margin = Mathf.Min(margin, sign * edge.Cross(at - hull[i]) / edge.Length());
        }
        return margin;
    }

    private static void ExpectParent(Dictionary<string, DamageBake> bakes, Node3D part, Node3D parent, List<string> errors)
    {
        if (part == null || parent == null)
        {
            return;
        }
        if (!bakes.TryGetValue(part.Name, out DamageBake bake))
        {
            errors.Add($"{part.Name}: the walker names it, but it is not a voxel part");
        }
        else if (bake.Parent != parent.Name)
        {
            errors.Add($"{part.Name}: hangs from {(bake.Parent == "" ? "nothing" : bake.Parent)}; the walker chains it to {parent.Name}");
        }
    }

    private static bool Fail(string name, List<string> errors, StringBuilder report)
    {
        report.AppendLine($"{name}: FAIL");
        foreach (string e in errors)
        {
            report.AppendLine($"  {e}");
        }
        return false;
    }

    private static Dictionary<string, Variant> Properties(SceneState state, int node)
    {
        var found = new Dictionary<string, Variant>();
        for (int p = 0; p < state.GetNodePropertyCount(node); p++)
        {
            found[state.GetNodePropertyName(node, p)] = state.GetNodePropertyValue(node, p);
        }
        return found;
    }

    private static Node3D Resolve(Node root, Dictionary<string, Variant> props, string key) =>
        props.TryGetValue(key, out Variant v) && v.VariantType == Variant.Type.NodePath ? root.GetNodeOrNull<Node3D>(v.AsNodePath()) : null;

    private static Godot.Collections.Array<Node3D> ResolveAll(Node root, Dictionary<string, Variant> props, string key)
    {
        var nodes = new Godot.Collections.Array<Node3D>();
        if (props.TryGetValue(key, out Variant v) && v.VariantType == Variant.Type.Array)
        {
            foreach (Variant p in v.AsGodotArray())
            {
                nodes.Add(root.GetNodeOrNull<Node3D>(p.AsNodePath()));
            }
        }
        return nodes;
    }
}
