using System;
using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>Where a part can bear weight, as a box in its node's space.</summary>
public enum ContactKind
{
    /// <summary>Its lowest layer as it stands at rest: a foot's sole, a
    /// body's belly.</summary>
    Sole,

    /// <summary>Its far end from its pivot: a fist, a knee (a thigh's end),
    /// an ankle (a shin's).</summary>
    Tip,
}

/// <summary>
/// What the importer bakes for one part of an articulated model, a mech
/// (docs/decisions/0046): so a hit is lookups and subtraction, never an
/// analysis of the voxels. Positions are in the part's node space, metres.
///
/// The copy of its original cells that repair regrows from is not here: it
/// is <see cref="VoxelMeta.Cells"/>, which the game never writes
/// (<see cref="VoxelBody"/> clones it).
/// </summary>
public sealed class DamageBake
{
    /// <summary>The part it hangs from: the one with a voxel nearest its
    /// pivot. Empty for the root, the part with no pivot (the core).</summary>
    public string Parent = "";

    /// <summary>Its solid voxels as built. Mass is these times a voxel's
    /// volume times <see cref="VoxelDestructionStats.Density"/>.</summary>
    public int Voxels;

    /// <summary>The mean of its voxel centres.</summary>
    public Vector3 Centre;

    /// <summary>Its cells that touch its parent's, face, edge or corner:
    /// what holds it on. Empty for the root.</summary>
    public int[] Hold = Array.Empty<int>();

    public ContactKind[] ContactKinds = Array.Empty<ContactKind>();
    public Vector3[] ContactCentre = Array.Empty<Vector3>();
    public Vector3[] ContactSize = Array.Empty<Vector3>();
}

/// <summary>
/// Bakes, writes and reads the damage map. Only a model with a pivot voxel in
/// some part is articulated and gets one; vines and buildings have none.
/// Thread-safe to bake: nothing of Godot's but maths.
/// </summary>
public static class VoxDamageMap
{
    public const int FormatVersion = 1;

    /// <summary>A tip is the cells this many voxels short of the farthest.</summary>
    private const float TipDepthVoxels = 1.5f;

    /// <summary>Bakes every shape of the model in place (<c>Part.Damage</c>).
    /// <paramref name="pivoted"/> holds the shapes whose origin is a pivot voxel.</summary>
    public static void Bake(VoxModelBuilder.Part root, HashSet<VoxModelBuilder.Part> pivoted, float voxelSize, string path)
    {
        if (pivoted.Count == 0)
        {
            return;
        }
        var shapes = new List<Shape>();
        Collect(root, Transform3D.Identity, voxelSize, pivoted, shapes);

        // Every solid cell in the model's space, in voxel units, keyed to its
        // shape. Cell centres all land on the same half-voxel lattice: a
        // pivoted origin is a cell centre, a centred one a cell corner, and
        // MagicaVoxel translates by whole voxels.
        var owner = new Dictionary<Vector3I, int>();
        for (int s = 0; s < shapes.Count; s++)
        {
            Shape shape = shapes[s];
            VoxModelBuilder.Part part = shape.Part;
            Vector3I d = part.Dims;
            for (int z = 0; z < d.Z; z++)
                for (int y = 0; y < d.Y; y++)
                    for (int x = 0; x < d.X; x++)
                    {
                        int i = x + d.X * (y + d.Y * z);
                        if (part.Cells[i] == 0)
                        {
                            continue;
                        }
                        Vector3 local = VoxSpace.ToGodot(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - part.Origin);
                        Vector3 at = shape.Basis * local + shape.OriginVoxels;
                        var key = new Vector3I(Mathf.RoundToInt(at.X - 0.5f), Mathf.RoundToInt(at.Y - 0.5f), Mathf.RoundToInt(at.Z - 0.5f));
                        shape.Index.Add(i);
                        shape.Local.Add(local);
                        shape.Keys.Add(key);
                        owner[key] = s;
                    }
        }

        for (int s = 0; s < shapes.Count; s++)
        {
            shapes[s].Parent = shapes[s].Pivoted ? NearestOther(shapes, s) : -1;
        }
        for (int s = 0; s < shapes.Count; s++)
        {
            if (Loops(shapes, s))
            {
                GD.PushWarning($"VoxDamageMap: {path}: '{shapes[s].Part.Name}' hangs from itself through its parents; check its pivot voxel.");
            }
        }

        foreach (Shape shape in shapes)
        {
            shape.Part.Damage = BakeOne(shapes, shape, owner, voxelSize);
        }
    }

    private sealed class Shape
    {
        public VoxModelBuilder.Part Part;
        public Basis Basis;
        /// <summary>The node's origin in the model's space, in voxels.</summary>
        public Vector3 OriginVoxels;
        public bool Pivoted;
        public int Parent = -1;
        public readonly List<int> Index = new();
        public readonly List<Vector3> Local = new();
        public readonly List<Vector3I> Keys = new();
    }

    private static void Collect(VoxModelBuilder.Part part, Transform3D parent, float voxelSize,
        HashSet<VoxModelBuilder.Part> pivoted, List<Shape> into)
    {
        foreach (VoxModelBuilder.Part child in part.Children)
        {
            Transform3D at = parent * child.Transform;
            if (child.Kind == VoxModelBuilder.PartKind.Shape && child.Cells != null)
            {
                into.Add(new Shape { Part = child, Basis = at.Basis, OriginVoxels = at.Origin / voxelSize, Pivoted = pivoted.Contains(child) });
            }
            else if (child.Kind == VoxModelBuilder.PartKind.Group)
            {
                Collect(child, at, voxelSize, pivoted, into);
            }
        }
    }

    /// <summary>The shape with a voxel nearest shape <paramref name="s"/>'s pivot.</summary>
    private static int NearestOther(List<Shape> shapes, int s)
    {
        Vector3 pivot = shapes[s].OriginVoxels;
        int best = -1;
        float bestD = float.MaxValue;
        for (int o = 0; o < shapes.Count; o++)
        {
            if (o == s)
            {
                continue;
            }
            foreach (Vector3I k in shapes[o].Keys)
            {
                float d = (new Vector3(k.X + 0.5f, k.Y + 0.5f, k.Z + 0.5f) - pivot).LengthSquared();
                if (d < bestD)
                {
                    bestD = d;
                    best = o;
                }
            }
        }
        return best;
    }

    private static bool Loops(List<Shape> shapes, int s)
    {
        int at = shapes[s].Parent;
        for (int steps = 0; at >= 0 && steps <= shapes.Count; steps++)
        {
            if (at == s)
            {
                return true;
            }
            at = shapes[at].Parent;
        }
        return false;
    }

    private static DamageBake BakeOne(List<Shape> shapes, Shape shape, Dictionary<Vector3I, int> owner, float voxel)
    {
        var bake = new DamageBake { Voxels = shape.Index.Count };
        if (shape.Index.Count == 0)
        {
            return bake;
        }
        Vector3 sum = Vector3.Zero;
        int lowest = int.MaxValue;
        foreach (Vector3 local in shape.Local)
        {
            sum += local;
        }
        foreach (Vector3I key in shape.Keys)
        {
            lowest = Math.Min(lowest, key.Y);
        }
        bake.Centre = sum / shape.Index.Count * voxel;

        var kinds = new List<ContactKind>();
        var centres = new List<Vector3>();
        var sizes = new List<Vector3>();
        AddContact(shape, voxel, i => shape.Keys[i].Y == lowest, ContactKind.Sole, kinds, centres, sizes);

        if (shape.Parent >= 0)
        {
            Shape parent = shapes[shape.Parent];
            bake.Parent = parent.Part.Name;
            int parentIndex = shape.Parent;
            var hold = new List<int>();
            for (int c = 0; c < shape.Keys.Count; c++)
            {
                if (Touches(shape.Keys[c], parentIndex, owner))
                {
                    hold.Add(shape.Index[c]);
                }
            }
            bake.Hold = hold.ToArray();

            // The pivot is the node's origin: local distance from zero.
            float far = 0.0f;
            foreach (Vector3 local in shape.Local)
            {
                far = Math.Max(far, local.Length());
            }
            float near = far - TipDepthVoxels;
            AddContact(shape, voxel, i => shape.Local[i].Length() >= near, ContactKind.Tip, kinds, centres, sizes);
        }
        bake.ContactKinds = kinds.ToArray();
        bake.ContactCentre = centres.ToArray();
        bake.ContactSize = sizes.ToArray();
        return bake;
    }

    private static bool Touches(Vector3I key, int parent, Dictionary<Vector3I, int> owner)
    {
        for (int z = -1; z <= 1; z++)
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    if ((x != 0 || y != 0 || z != 0) && owner.TryGetValue(key + new Vector3I(x, y, z), out int o) && o == parent)
                    {
                        return true;
                    }
                }
        return false;
    }

    /// <summary>The box round the chosen cells, each a whole voxel, in node space.</summary>
    private static void AddContact(Shape shape, float voxel, Func<int, bool> pick, ContactKind kind,
        List<ContactKind> kinds, List<Vector3> centres, List<Vector3> sizes)
    {
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = -min;
        bool any = false;
        for (int c = 0; c < shape.Local.Count; c++)
        {
            if (!pick(c))
            {
                continue;
            }
            any = true;
            min = min.Min(shape.Local[c] - Vector3.One * 0.5f);
            max = max.Max(shape.Local[c] + Vector3.One * 0.5f);
        }
        if (!any)
        {
            return;
        }
        kinds.Add(kind);
        centres.Add((min + max) * 0.5f * voxel);
        sizes.Add((max - min) * voxel);
    }

    /// <summary>The bake as node meta, beside the chunk graph.</summary>
    public static void Write(GodotObject node, DamageBake bake)
    {
        node.SetMeta(VoxelMeta.PartVersion, FormatVersion);
        node.SetMeta(VoxelMeta.PartParent, bake.Parent);
        node.SetMeta(VoxelMeta.PartVoxels, bake.Voxels);
        node.SetMeta(VoxelMeta.PartCentre, bake.Centre);
        node.SetMeta(VoxelMeta.PartHold, bake.Hold);
        var kinds = new int[bake.ContactKinds.Length];
        for (int k = 0; k < kinds.Length; k++)
        {
            kinds[k] = (int)bake.ContactKinds[k];
        }
        node.SetMeta(VoxelMeta.PartContactKind, kinds);
        node.SetMeta(VoxelMeta.PartContactCentre, bake.ContactCentre);
        node.SetMeta(VoxelMeta.PartContactSize, bake.ContactSize);
    }

    /// <summary>A part's bake, or null when it has none or an older one
    /// (reimport the .vox).</summary>
    public static DamageBake TryRead(GodotObject node)
    {
        if (node == null || !node.HasMeta(VoxelMeta.PartVersion) || node.GetMeta(VoxelMeta.PartVersion).AsInt32() != FormatVersion)
        {
            return null;
        }
        int[] kinds = node.GetMeta(VoxelMeta.PartContactKind).AsInt32Array();
        var bake = new DamageBake
        {
            Parent = node.GetMeta(VoxelMeta.PartParent).AsString(),
            Voxels = node.GetMeta(VoxelMeta.PartVoxels).AsInt32(),
            Centre = node.GetMeta(VoxelMeta.PartCentre).AsVector3(),
            Hold = node.GetMeta(VoxelMeta.PartHold).AsInt32Array(),
            ContactKinds = new ContactKind[kinds.Length],
            ContactCentre = node.GetMeta(VoxelMeta.PartContactCentre).AsVector3Array(),
            ContactSize = node.GetMeta(VoxelMeta.PartContactSize).AsVector3Array(),
        };
        for (int k = 0; k < kinds.Length; k++)
        {
            bake.ContactKinds[k] = (ContactKind)kinds[k];
        }
        return bake;
    }
}
