using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A MagicaVoxel file made into a model: the node tree, each part's mesh, and
/// the voxels and chunk graph that travel with it for <see cref="VoxelBody"/>.
/// The .vox importer and the game build models the same way through it, so a
/// vine grown in memory is the model its .vox would import as, field for field.
///
/// Two steps. <see cref="Build"/> is pure, nothing of Godot's scene, and may
/// run on a worker: the meshing and the chunk bake are the cost.
/// <see cref="Instantiate"/> makes the nodes on the main thread, with whatever
/// mesh node the caller wants (the importer's <c>ImporterMeshInstance3D</c>,
/// the game's <c>MeshInstance3D</c>).
///
/// - a group becomes a <c>Node3D</c>, named after it;
/// - an object becomes a mesh node, named after it (unnamed ones are
///   <c>Model0</c>, <c>Model1</c> ...);
/// - an object whose name starts with <c>@</c> becomes a <c>Marker3D</c> at the
///   centre of its voxels and draws nothing;
/// - a voxel painted in the pivot index is not drawn, and its centre becomes
///   the object's origin. Without one, the origin is the object's centre;
/// - hidden objects and hidden layers are left out.
/// </summary>
public static class VoxModelBuilder
{
    public sealed class Options
    {
        public float VoxelSize = 0.1f;
        /// <summary>The palette index that marks a pivot; below 1, none.</summary>
        public int PivotIndex = 255;
        public int ChunkSize = 8;
        public int ChunkJitter = 2;
        /// <summary>Where the model came from: seeds the chunk boundaries'
        /// jitter (<see cref="VoxChunker.SeedOf"/>) and names it in warnings.</summary>
        public string Path = "";
    }

    public enum PartKind
    {
        Group,
        Marker,
        Shape,
    }

    /// <summary>One node of the model, with what a shape carries.</summary>
    public sealed class Part
    {
        public PartKind Kind;
        public string Name;
        public Transform3D Transform = Transform3D.Identity;
        public readonly List<Part> Children = new();

        /// <summary>A shape's mesh, pooled: <see cref="Instantiate"/> hands it to
        /// Godot and releases it. Null when nothing of it is visible.</summary>
        public VoxMesher.Arrays Mesh;
        /// <summary>The shape's cells with the pivot voxel removed.</summary>
        public byte[] Cells;
        public Vector3I Dims;
        public Vector3 Origin;
        public ChunkBake Chunks;
    }

    public sealed class Model
    {
        public Part Root;
        public float VoxelSize;
        public Color[] Palette;
        public float[] Emission;
    }

    /// <summary>Makes the node a shape is drawn by, from its surface arrays.</summary>
    public delegate Node3D MeshNodeMaker(string name, Godot.Collections.Array arrays, Transform3D transform);

    /// <summary>Everything that costs: meshing and chunking. Pure; any thread.</summary>
    public static Model Build(VoxFile file, Options options)
    {
        var ctx = new Context { File = file, Options = options };
        var root = new Part { Kind = PartKind.Group, Name = "" };
        if (file.HasSceneGraph)
        {
            WalkTransform(ctx, 0, root, isRoot: true);
        }
        else
        {
            for (int i = 0; i < file.Models.Count; i++)
            {
                AddShape(ctx, i, null, $"Model{i}", root);
            }
        }
        return new Model { Root = root, VoxelSize = options.VoxelSize, Palette = file.Palette, Emission = file.Emission };
    }

    /// <summary>The nodes, on the main thread: a <c>Node3D</c> named
    /// <paramref name="rootName"/> with the voxel size, palette and emission
    /// on it, and a node per part.</summary>
    public static Node3D Instantiate(Model model, string rootName, MeshNodeMaker makeMesh)
    {
        var root = new Node3D { Name = rootName };
        foreach (Part child in model.Root.Children)
        {
            AddNode(child, root, makeMesh);
        }
        root.SetMeta(VoxelMeta.VoxelSize, model.VoxelSize);
        root.SetMeta(VoxelMeta.Palette, model.Palette);
        root.SetMeta(VoxelMeta.Emission, model.Emission);
        return root;
    }

    /// <summary>Where each marker whose name starts with <paramref name="prefix"/>
    /// would stand, in the space of <see cref="Instantiate"/>'s root, in the
    /// order its nodes would be made. No nodes; any thread.</summary>
    public static List<Vector3> Markers(Model model, string prefix)
    {
        var found = new List<Vector3>();
        CollectMarkers(model.Root, Transform3D.Identity, prefix, found);
        return found;
    }

    private static void CollectMarkers(Part part, Transform3D parent, string prefix, List<Vector3> found)
    {
        foreach (Part child in part.Children)
        {
            Transform3D at = parent * child.Transform;
            if (child.Kind == PartKind.Marker && child.Name.StartsWith(prefix))
                found.Add(at.Origin);
            else if (child.Kind == PartKind.Group)
                CollectMarkers(child, at, prefix, found);
        }
    }

    /// <summary>The game's mesh node: a <c>MeshInstance3D</c> drawing an
    /// <c>ArrayMesh</c> in <paramref name="material"/>.</summary>
    public static MeshNodeMaker MeshInstances(Material material) => (name, arrays, transform) =>
    {
        var mesh = new ArrayMesh { ResourceName = name };
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        mesh.SurfaceSetName(0, "voxel");
        return new MeshInstance3D { Name = name, Mesh = mesh, Transform = transform };
    };

    private static void AddNode(Part part, Node3D parent, MeshNodeMaker makeMesh)
    {
        switch (part.Kind)
        {
            case PartKind.Group:
                var group = new Node3D { Name = part.Name, Transform = part.Transform };
                parent.AddChild(group, forceReadableName: true);
                foreach (Part child in part.Children)
                {
                    AddNode(child, group, makeMesh);
                }
                break;
            case PartKind.Marker:
                parent.AddChild(new Marker3D { Name = part.Name, Transform = part.Transform }, forceReadableName: true);
                break;
            case PartKind.Shape:
                Godot.Collections.Array arrays = part.Mesh.ToGodot();
                part.Mesh.Release();
                part.Mesh = null;
                Node3D node = makeMesh(part.Name, arrays, part.Transform);
                ApplyMeta(node, part);
                parent.AddChild(node, forceReadableName: true);
                break;
        }
    }

    /// <summary>A shape's voxels and chunk graph, on the node that draws it.</summary>
    public static void ApplyMeta(GodotObject node, Part part)
    {
        node.SetMeta(VoxelMeta.Cells, part.Cells);
        node.SetMeta(VoxelMeta.Dims, part.Dims);
        node.SetMeta(VoxelMeta.Origin, part.Origin);
        ChunkBake bake = part.Chunks;
        if (bake == null)
        {
            return;
        }
        node.SetMeta(VoxelMeta.ChunkVersion, VoxChunker.FormatVersion);
        node.SetMeta(VoxelMeta.ChunkCells, bake.CellChunk);
        node.SetMeta(VoxelMeta.ChunkCount, bake.Count);
        node.SetMeta(VoxelMeta.ChunkSolid, bake.Solid);
        node.SetMeta(VoxelMeta.ChunkMin, bake.Min);
        node.SetMeta(VoxelMeta.ChunkMax, bake.Max);
        node.SetMeta(VoxelMeta.ChunkLayers, bake.Layers);
        node.SetMeta(VoxelMeta.ChunkLayerCount, bake.LayerCount);
        node.SetMeta(VoxelMeta.ChunkLayerSumX, bake.LayerSumX);
        node.SetMeta(VoxelMeta.ChunkLayerSumY, bake.LayerSumY);
        node.SetMeta(VoxelMeta.ChunkEdgeA, bake.EdgeA);
        node.SetMeta(VoxelMeta.ChunkEdgeB, bake.EdgeB);
        node.SetMeta(VoxelMeta.ChunkEdgeFaces, bake.EdgeFaces);
        node.SetMeta(VoxelMeta.ChunkBoxOffset, bake.BoxOffset);
        node.SetMeta(VoxelMeta.ChunkBoxCentre, bake.BoxCentre);
        node.SetMeta(VoxelMeta.ChunkBoxSize, bake.BoxSize);
        node.SetMeta(VoxelMeta.ChunkGround, bake.GroundCells);
    }

    private sealed class Context
    {
        public VoxFile File;
        public Options Options;
        public int Unnamed;
    }

    private static void WalkTransform(Context ctx, int id, Part parent, bool isRoot = false)
    {
        if (!ctx.File.Transforms.TryGetValue(id, out VoxFile.TransformNode trn))
            return;
        if (trn.Hidden || ctx.File.HiddenLayers.Contains(trn.Layer))
            return;

        if (ctx.File.Groups.TryGetValue(trn.Child, out VoxFile.GroupNode group))
        {
            Part target = parent;
            if (!isRoot)
            {
                target = new Part
                {
                    Kind = PartKind.Group,
                    Name = trn.Name.Length > 0 ? trn.Name : $"Group{id}",
                    Transform = new Transform3D(VoxSpace.ToGodot(trn.Rotation), VoxSpace.ToGodot(trn.Translation) * ctx.Options.VoxelSize),
                };
                parent.Children.Add(target);
            }
            foreach (int child in group.Children)
                WalkTransform(ctx, child, target);
        }
        else if (ctx.File.Shapes.TryGetValue(trn.Child, out VoxFile.ShapeNode shape))
        {
            string name = trn.Name.Length > 0 ? trn.Name : $"Model{ctx.Unnamed++}";
            AddShape(ctx, shape.Model, trn, name, parent);
        }
    }

    private static void AddShape(Context ctx, int modelIndex, VoxFile.TransformNode trn, string name, Part parent)
    {
        if (modelIndex < 0 || modelIndex >= ctx.File.Models.Count)
            return;

        VoxFile.Model model = ctx.File.Models[modelIndex];
        bool marker = name.StartsWith('@');
        Vector3 origin = marker ? VoxelCentre(model, -1) : PivotOrCentre(ctx, model, name);

        // The node sits where MagicaVoxel puts the chosen origin: the model is
        // centred on floor(size / 2), then rotated and translated.
        VoxRotation rotation = trn?.Rotation ?? VoxRotation.Identity;
        Vector3 translation = trn?.Translation ?? Vector3I.Zero;
        Vector3 placed = translation + rotation.Apply(origin - VoxSpace.Centre(model.Size));
        var transform = new Transform3D(VoxSpace.ToGodot(rotation), VoxSpace.ToGodot(placed) * ctx.Options.VoxelSize);

        if (marker)
        {
            parent.Children.Add(new Part { Kind = PartKind.Marker, Name = name.Substring(1), Transform = transform });
            return;
        }

        int pivot = ctx.Options.PivotIndex;
        VoxMesher.Arrays mesh = VoxMesher.BuildArrays(
            model.Cells, model.Size, Vector3I.Zero, model.Size, ctx.File.Palette, ctx.File.Emission,
            origin, ctx.Options.VoxelSize, pivot);
        if (mesh == null)
            return;

        // The pivot voxel is a marker, never material: it does not travel.
        byte[] cells = (byte[])model.Cells.Clone();
        if (pivot >= 1)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == pivot)
                {
                    cells[i] = 0;
                }
            }
        }
        parent.Children.Add(new Part
        {
            Kind = PartKind.Shape,
            Name = name,
            Transform = transform,
            Mesh = mesh,
            Cells = cells,
            Dims = model.Size,
            Origin = origin,
            Chunks = BakeChunks(ctx, cells, model.Size, origin),
        });
    }

    /// <summary>Chunks, their as-built contacts and boxes, after the pivot
    /// voxel is gone so it is not a chunk of its own.</summary>
    private static ChunkBake BakeChunks(Context ctx, byte[] cells, Vector3I size, Vector3 origin)
    {
        Options o = ctx.Options;
        if (o.ChunkSize * o.ChunkSize * o.ChunkSize > VoxChunker.GrabWarnVoxels)
        {
            GD.PushWarning($"VoxModelBuilder: {o.Path}: chunk_size {o.ChunkSize} makes a full chunk heavier than the gravity gun can tear out ({VoxChunker.GrabWarnVoxels} voxels).");
        }
        return VoxChunker.Build(
            cells, size, VoxChunker.KindOf(ctx.File.MaterialType), o.ChunkSize, o.ChunkJitter,
            VoxChunker.SeedOf(o.Path), origin, o.VoxelSize);
    }

    private static Vector3 PivotOrCentre(Context ctx, VoxFile.Model model, string name)
    {
        int pivot = ctx.Options.PivotIndex;
        if (pivot < 1)
            return VoxSpace.Centre(model.Size);

        var pivots = new List<Vector3>();
        for (int z = 0; z < model.Size.Z; z++)
            for (int y = 0; y < model.Size.Y; y++)
                for (int x = 0; x < model.Size.X; x++)
                    if (model.Cells[model.Index(x, y, z)] == pivot)
                        pivots.Add(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));

        if (pivots.Count == 0)
            return VoxSpace.Centre(model.Size);
        if (pivots.Count > 1)
            GD.PushWarning($"VoxModelBuilder: {ctx.Options.Path}: '{name}' has {pivots.Count} pivot voxels (index {pivot}); using the first.");
        return pivots[0];
    }

    /// <summary>The mean of a model's voxel centres, ignoring one index.</summary>
    private static Vector3 VoxelCentre(VoxFile.Model model, int ignore)
    {
        Vector3 sum = Vector3.Zero;
        int count = 0;
        for (int z = 0; z < model.Size.Z; z++)
            for (int y = 0; y < model.Size.Y; y++)
                for (int x = 0; x < model.Size.X; x++)
                {
                    byte c = model.Cells[model.Index(x, y, z)];
                    if (c == 0 || c == ignore)
                        continue;
                    sum += new Vector3(x + 0.5f, y + 0.5f, z + 0.5f);
                    count++;
                }
        return count > 0 ? sum / count : VoxSpace.Centre(model.Size);
    }
}
