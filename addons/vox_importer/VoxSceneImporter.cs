#if TOOLS
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Imports a MagicaVoxel <c>.vox</c> as a scene, the way Godot imports a
/// <c>.glb</c>: save in MagicaVoxel, and Godot reimports on focus. A wrapper
/// <c>.tscn</c> instances the result and owns everything added to it.
///
/// The MagicaVoxel scene graph comes across as nodes, so a model built of
/// parts arrives as parts that code can move: <c>@Seat</c> and <c>@Muzzle</c>
/// become markers, a voxel in the pivot index (255 unless set otherwise) is
/// the hinge a leg turns about. How is <see cref="VoxModelBuilder"/>, which
/// the game uses too, to build a vine in memory as its .vox would import.
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
    private const string ChunkSizeOption = "voxel/chunk_size";
    private const string ChunkJitterOption = "voxel/chunk_jitter";

    public override string[] _GetExtensions() => new[] { "vox" };

    public override void _GetImportOptions(string path)
    {
        // MagicaVoxel's own OBJ export uses 0.1 m, and so does the pack art.
        AddImportOptionAdvanced(Variant.Type.Float, VoxelSizeOption, 0.1f, PropertyHint.Range, "0.01,2,0.005,suffix:m");
        AddImportOptionAdvanced(Variant.Type.Int, PivotIndexOption, 255, PropertyHint.Range, "-1,255,1");
        AddImportOptionAdvanced(Variant.Type.String, MaterialOption, "res://art/materials/mat_voxel_toon.tres", PropertyHint.File, "*.tres");
        // About 0.8 m at the default voxel size. A chunk never crosses a
        // MagicaVoxel material type, and the boundaries jitter so a cut does
        // not read as a grid (docs/decisions/0027).
        AddImportOptionAdvanced(Variant.Type.Int, ChunkSizeOption, 8, PropertyHint.Range, "2,32,1,suffix:voxels");
        AddImportOptionAdvanced(Variant.Type.Int, ChunkJitterOption, 2, PropertyHint.Range, "0,8,1,suffix:voxels");
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

        var build = new VoxModelBuilder.Options
        {
            VoxelSize = Option(options, VoxelSizeOption, 0.1f),
            PivotIndex = Option(options, PivotIndexOption, 255),
            Path = path,
            ChunkSize = Option(options, ChunkSizeOption, 8),
            ChunkJitter = Option(options, ChunkJitterOption, 2),
        };
        var material = GD.Load<Material>(Option(options, MaterialOption, "res://art/materials/mat_voxel_toon.tres"));

        // The voxels themselves travel with the scene, so a destructible body
        // can carve, re-mesh and break the model at runtime (VoxelBody).
        VoxModelBuilder.Model model = VoxModelBuilder.Build(file, build);
        Node3D root = VoxModelBuilder.Instantiate(model, path.GetFile().GetBaseName(), (name, arrays, transform) =>
        {
            var mesh = new ImporterMesh { ResourceName = name };
            mesh.AddSurface(Mesh.PrimitiveType.Triangles, arrays, material: material, name: "voxel");
            return new ImporterMeshInstance3D { Name = name, Mesh = mesh, Transform = transform };
        });

        SetOwner(root, root);
        return root;
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
