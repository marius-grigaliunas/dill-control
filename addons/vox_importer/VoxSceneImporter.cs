#if TOOLS
using System.Collections.Generic;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Imports a MagicaVoxel <c>.vox</c> as a scene, the way Godot imports a
/// <c>.glb</c>: save in MagicaVoxel, and Godot reimports on focus. A wrapper
/// <c>.tscn</c> instances the result and owns everything added to it.
///
/// The MagicaVoxel scene graph comes across as nodes, so a model built of
/// parts arrives as parts that code can move:
///
/// - a group becomes a <c>Node3D</c>, named after it;
/// - an object becomes a mesh node, named after it (unnamed ones are
///   <c>Model0</c>, <c>Model1</c> ...);
/// - an object whose name starts with <c>@</c> becomes a <c>Marker3D</c> at the
///   centre of its voxels and draws nothing: <c>@Seat</c>, <c>@Muzzle</c>;
/// - a voxel painted in the pivot index (255 unless set otherwise) is not
///   drawn, and its centre becomes the object's origin: the hinge a leg turns
///   about. Without one, the origin is the object's centre;
/// - hidden objects and hidden layers are left out.
///
/// Every mesh gets <c>mat_voxel_toon</c>: colour per vertex, so every model
/// shares it whatever its palette. See docs/decisions/0009.
/// </summary>
[Tool]
public partial class VoxSceneImporter : EditorSceneFormatImporter
{
    private const string VoxelSizeOption = "voxel/size";
    private const string PivotIndexOption = "voxel/pivot_index";
    private const string MaterialOption = "voxel/material";

    public override string[] _GetExtensions() => new[] { "vox" };

    public override void _GetImportOptions(string path)
    {
        // MagicaVoxel's own OBJ export uses 0.1 m, and so does the pack art.
        AddImportOptionAdvanced(Variant.Type.Float, VoxelSizeOption, 0.1f, PropertyHint.Range, "0.01,2,0.005,suffix:m");
        AddImportOptionAdvanced(Variant.Type.Int, PivotIndexOption, 255, PropertyHint.Range, "-1,255,1");
        AddImportOptionAdvanced(Variant.Type.String, MaterialOption, "res://art/materials/mat_voxel_toon.tres", PropertyHint.File, "*.tres");
    }

    public override Variant _GetOptionVisibility(string path, bool forAnimation, string option) => true;

    public override GodotObject _ImportScene(string path, uint flags, Godot.Collections.Dictionary options)
    {
        byte[] data = FileAccess.GetFileAsBytes(path);
        if (data.Length == 0)
        {
            GD.PushError($"VoxSceneImporter: could not read {path}.");
            return null;
        }

        VoxFile file;
        try
        {
            file = VoxFile.Load(data);
        }
        catch (System.Exception e)
        {
            GD.PushError($"VoxSceneImporter: {path}: {e.Message}");
            return null;
        }

        var context = new Context
        {
            File = file,
            VoxelSize = Option(options, VoxelSizeOption, 0.1f),
            PivotIndex = Option(options, PivotIndexOption, 255),
            Material = GD.Load<Material>(Option(options, MaterialOption, "res://art/materials/mat_voxel_toon.tres")),
            Path = path,
        };

        var root = new Node3D { Name = path.GetFile().GetBaseName() };
        if (file.HasSceneGraph)
        {
            WalkTransform(context, 0, root, isRoot: true);
        }
        else
        {
            for (int i = 0; i < file.Models.Count; i++)
                AddShape(context, i, null, $"Model{i}", root);
        }

        // The voxels themselves travel with the scene, so a destructible body
        // can carve, re-mesh and break the model at runtime (VoxelBody).
        root.SetMeta(VoxelMeta.VoxelSize, context.VoxelSize);
        root.SetMeta(VoxelMeta.Palette, file.Palette);
        root.SetMeta(VoxelMeta.Emission, file.Emission);

        SetOwner(root, root);
        return root;
    }

    private sealed class Context
    {
        public VoxFile File;
        public float VoxelSize;
        public int PivotIndex;
        public Material Material;
        public string Path;
        public int Unnamed;
    }

    private static void WalkTransform(Context ctx, int id, Node3D parent, bool isRoot = false)
    {
        if (!ctx.File.Transforms.TryGetValue(id, out VoxFile.TransformNode trn))
            return;
        if (trn.Hidden || ctx.File.HiddenLayers.Contains(trn.Layer))
            return;

        if (ctx.File.Groups.TryGetValue(trn.Child, out VoxFile.GroupNode group))
        {
            Node3D target = parent;
            if (!isRoot)
            {
                target = new Node3D
                {
                    Name = trn.Name.Length > 0 ? trn.Name : $"Group{id}",
                    Transform = new Transform3D(VoxSpace.ToGodot(trn.Rotation), VoxSpace.ToGodot(trn.Translation) * ctx.VoxelSize),
                };
                parent.AddChild(target, forceReadableName: true);
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

    private static void AddShape(Context ctx, int modelIndex, VoxFile.TransformNode trn, string name, Node3D parent)
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
        var transform = new Transform3D(VoxSpace.ToGodot(rotation), VoxSpace.ToGodot(placed) * ctx.VoxelSize);

        if (marker)
        {
            parent.AddChild(new Marker3D { Name = name.Substring(1), Transform = transform }, forceReadableName: true);
            return;
        }

        Godot.Collections.Array arrays = VoxMesher.Build(
            model, ctx.File.Palette, ctx.File.Emission, origin, ctx.VoxelSize, ctx.PivotIndex);
        if (arrays == null)
            return;

        var mesh = new ImporterMesh { ResourceName = name };
        mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays, material: ctx.Material, name: "voxel");
        var node = new ImporterMeshInstance3D { Name = name, Mesh = mesh, Transform = transform };

        // The pivot voxel is a marker, never material: it does not travel.
        byte[] cells = (byte[])model.Cells.Clone();
        if (ctx.PivotIndex >= 1)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] == ctx.PivotIndex)
                {
                    cells[i] = 0;
                }
            }
        }
        node.SetMeta(VoxelMeta.Cells, cells);
        node.SetMeta(VoxelMeta.Dims, model.Size);
        node.SetMeta(VoxelMeta.Origin, origin);
        parent.AddChild(node, forceReadableName: true);
    }

    private static Vector3 PivotOrCentre(Context ctx, VoxFile.Model model, string name)
    {
        if (ctx.PivotIndex < 1)
            return VoxSpace.Centre(model.Size);

        var pivots = new List<Vector3>();
        for (int z = 0; z < model.Size.Z; z++)
            for (int y = 0; y < model.Size.Y; y++)
                for (int x = 0; x < model.Size.X; x++)
                    if (model.Cells[model.Index(x, y, z)] == ctx.PivotIndex)
                        pivots.Add(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));

        if (pivots.Count == 0)
            return VoxSpace.Centre(model.Size);
        if (pivots.Count > 1)
            GD.PushWarning($"VoxSceneImporter: {ctx.Path}: '{name}' has {pivots.Count} pivot voxels (index {ctx.PivotIndex}); using the first.");
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

    private static T Option<[MustBeVariant] T>(Godot.Collections.Dictionary options, string key, T fallback) =>
        options.TryGetValue(key, out Variant value) ? value.As<T>() : fallback;

    private static void SetOwner(Node node, Node owner)
    {
        foreach (Node child in node.GetChildren())
        {
            child.Owner = owner;
            SetOwner(child, owner);
        }
    }
}
#endif
