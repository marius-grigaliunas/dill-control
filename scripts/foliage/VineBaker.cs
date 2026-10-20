using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using DillControl.Voxel;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// Bakes a vine to files a scene can drop in: <c>art/voxels/prp_vine_kind_seed.vox</c>
/// with its <c>.vox.import</c> (at the bake's voxel size) and a <c>.json</c> of
/// what it was grown from, and <c>scenes/props/prp_vine_kind_seed.tscn</c>, the
/// wrapper every vine has: a static <see cref="VoxelBody"/> drawn whole until
/// hit, the model, and a <see cref="PlantFlex"/> after it. The stalk's foot
/// stands on the scene's origin, which is the spine's foot. Nothing here runs
/// in the game: the vine dock and <c>vinebake.gd</c> call it (docs/decisions/0033).
/// </summary>
public static class VineBaker
{
    public const string Tool = "dill-control-vine";
    public const int Version = 3;

    public sealed class Baked
    {
        public string Name;
        public string VoxPath;
        public string ImportPath;
        public string JsonPath;
        public string ScenePath;
        public bool SceneWritten;
        public VineGenerator.Result Vine;
    }

    /// <summary>A vine's name regrows it: its kind and seed, and a <c>c</c>
    /// when a setting was changed by hand (its .json holds every setting).</summary>
    public static string NameOf(VineKind kind, int seed, bool changed = false) =>
        $"prp_vine_{kind?.Slug ?? "vine"}_{seed}{(changed ? "c" : "")}";

    /// <summary>Grows a vine from <paramref name="kind"/> and <paramref name="seed"/>,
    /// or from <paramref name="settings"/>, and writes it. A vine that fails
    /// its hard checks is not written: <see cref="Baked.Vine"/> says why.</summary>
    public static Baked Bake(VineKind kind, int seed, VineBakeStats stats, VineParams settings = null, bool changed = false)
    {
        VineParams p = settings ?? kind.Roll(seed, Math.Round((double)stats.VoxelSize, 6));
        VineGenerator.Result vine = VineGenerator.Generate(p, stats.Limits());
        string name = NameOf(kind, (int)vine.Params.Seed, changed);
        var baked = new Baked
        {
            Name = name,
            Vine = vine,
            VoxPath = $"{stats.VoxelFolder}/{name}.vox",
            ImportPath = $"{stats.VoxelFolder}/{name}.vox.import",
            JsonPath = $"{stats.VoxelFolder}/{name}.json",
            ScenePath = $"{stats.SceneFolder}/{name}.tscn",
        };
        if (!vine.Ok)
            return baked;

        WriteBytes(baked.VoxPath, Encode(vine, stats.Palette));
        WriteImport(baked.ImportPath, baked.VoxPath, stats.VoxelSize);
        WriteText(baked.JsonPath, Describe(kind, vine, changed));
        // Re-baking changes the model and keeps the wrapper: whatever was set
        // on it in the editor, and its uid, survive.
        if (!FileAccess.FileExists(baked.ScenePath))
        {
            WriteText(baked.ScenePath, Wrapper(name, baked.VoxPath, stats));
            baked.SceneWritten = true;
        }
        return baked;
    }

    /// <summary>The model's translation that stands the stalk's foot on the
    /// origin: a voxel corner p lands at t + p - floor(size / 2).</summary>
    public static (int X, int Y, int Z) FootTranslation(VineGenerator.Result vine) => (
        (vine.BoundsX >> 1) - (int)Math.Floor(vine.FootX + 0.5),
        (vine.BoundsY >> 1) - (int)Math.Floor(vine.FootY + 0.5),
        vine.BoundsZ >> 1);

    public static byte[] Encode(VineGenerator.Result vine, VinePalette palette)
    {
        palette ??= new VinePalette();
        var cells = new VoxWriter.Cell[vine.Voxels.Count];
        for (int i = 0; i < cells.Length; i++)
        {
            VineGenerator.Voxel v = vine.Voxels[i];
            cells[i] = new VoxWriter.Cell { X = v.X, Y = v.Y, Z = v.Z, C = (byte)v.C };
        }
        var markers = new VoxWriter.Marker[vine.Markers.Count];
        for (int i = 0; i < markers.Length; i++)
        {
            VineGenerator.Marker m = vine.Markers[i];
            markers[i] = new VoxWriter.Marker { Name = m.Name, X = m.X, Y = m.Y, Z = m.Z };
        }

        // Index i is at (i - 1) * 4; everything unused, 255 included, stays empty.
        var rgba = new byte[256 * 4];
        for (int index = VineGenerator.Shell; index <= VineGenerator.Tendril; index++)
        {
            Color c = palette.Of(index);
            int o = (index - 1) * 4;
            rgba[o] = (byte)c.R8;
            rgba[o + 1] = (byte)c.G8;
            rgba[o + 2] = (byte)c.B8;
            rgba[o + 3] = 255;
        }
        // Shell and core differ: a chunk graph never crosses a material type
        // (VoxChunker.KindOf), and the core is what a carve reveals.
        var materials = new List<(int, string)>();
        for (int index = VineGenerator.Shell; index <= VineGenerator.Tendril; index++)
            materials.Add((index, index == VineGenerator.Core ? "_glass" : "_diffuse"));

        return VoxWriter.Write("Stalk", vine.BoundsX, vine.BoundsY, vine.BoundsZ, cells,
            FootTranslation(vine), markers, rgba, materials, VineGenerator.Shell);
    }

    private static void WriteBytes(string path, byte[] bytes)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
            ?? throw new InvalidOperationException($"Cannot write {path}: {FileAccess.GetOpenError()}");
        file.StoreBuffer(bytes);
    }

    private static void WriteText(string path, string text)
    {
        using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write)
            ?? throw new InvalidOperationException($"Cannot write {path}: {FileAccess.GetOpenError()}");
        file.StoreString(text);
    }

    /// <summary>A new vine's import settings, which Godot completes with a uid
    /// when it imports it. A re-bake keeps the file and its uid, setting only
    /// the voxel size.</summary>
    private static void WriteImport(string path, string voxPath, float voxelSize)
    {
        string size = voxelSize.ToString("0.###", CultureInfo.InvariantCulture);
        if (FileAccess.FileExists(path))
        {
            string text = FileAccess.GetFileAsString(path);
            WriteText(path, Regex.Replace(text, @"(?m)^voxel/size=.*$", "voxel/size=" + size));
            return;
        }
        WriteText(path, $"""
            [remap]

            importer="scene"
            importer_version=1
            type="PackedScene"

            [deps]

            source_file="{voxPath}"

            [params]

            nodes/root_type=""
            nodes/root_name=""
            nodes/root_script=null
            mesh_library/use_node_names_as_mesh_names=false
            array_mesh/deduplicate_surfaces=true
            nodes/apply_root_scale=true
            nodes/root_scale=1.0
            nodes/import_as_skeleton_bones=false
            nodes/use_name_suffixes=true
            nodes/use_node_type_suffixes=true
            meshes/ensure_tangents=true
            meshes/generate_lods=true
            meshes/create_shadow_meshes=true
            meshes/light_baking=1
            meshes/lightmap_texel_size=0.2
            meshes/force_disable_compression=false
            skins/use_named_skins=true
            animation/import=true
            animation/fps=30
            animation/trimming=false
            animation/remove_immutable_tracks=true
            animation/import_rest_as_RESET=false
            import_script/path=""
            materials/extract=0
            materials/extract_format=0
            materials/extract_path=""
            _subresources={"{}"}
            voxel/size={size}
            voxel/pivot_index=255
            voxel/material="res://art/materials/mat_voxel_toon.tres"
            voxel/chunk_size=8
            voxel/chunk_jitter=2

            """);
    }

    /// <summary>The wrapper of prp_vine_41051, the reference vine.</summary>
    private static string Wrapper(string name, string voxPath, VineBakeStats stats)
    {
        string destruction = stats.Destruction?.ResourcePath ?? "res://resources/voxel/voxel_destruction_vine.tres";
        string flex = stats.Flex?.ResourcePath ?? FlexStats.VinePath;
        return $"""
            [gd_scene format=3]

            [ext_resource type="Script" path="res://scripts/voxel/VoxelBody.cs" id="1_voxels"]
            [ext_resource type="Resource" path="{destruction}" id="2_stats"]
            [ext_resource type="PackedScene" path="{voxPath}" id="3_model"]
            [ext_resource type="Script" path="res://scripts/foliage/PlantFlex.cs" id="4_flex"]
            [ext_resource type="Resource" path="{flex}" id="5_flexstats"]

            [node name="{name}" type="Node3D"]

            [node name="Voxels" type="Node" parent="." node_paths=PackedStringArray("Model")]
            script = ExtResource("1_voxels")
            Model = NodePath("../Model")
            Stats = ExtResource("2_stats")
            Static = true
            WholeUntilChanged = true

            [node name="Model" parent="." instance=ExtResource("3_model")]

            [node name="Flex" type="Node" parent="." node_paths=PackedStringArray("Body")]
            script = ExtResource("4_flex")
            Body = NodePath("../Voxels")
            Stats = ExtResource("5_flexstats")

            """;
    }

    /// <summary>What the vine was grown from and what it came to, beside it,
    /// for the nest step and for whoever wonders where a vine came from.</summary>
    public static string Describe(VineKind kind, VineGenerator.Result vine, bool changed = false)
    {
        var settings = new Godot.Collections.Dictionary();
        foreach (VineParams.Field field in VineParams.Fields)
        {
            double v = field.Get(vine.Params);
            settings[field.Key] = field.Bool ? v != 0 : v;
        }
        VineGenerator.Counts c = vine.Counts;
        var warnings = new Godot.Collections.Array();
        foreach (string w in vine.Warnings)
            warnings.Add(w);
        var data = new Godot.Collections.Dictionary
        {
            ["tool"] = Tool,
            ["version"] = Version,
            ["kind"] = kind?.KindName ?? "",
            ["changedByHand"] = changed,
            ["seed"] = (int)vine.Params.Seed,
            ["params"] = settings,
            ["counts"] = new Godot.Collections.Dictionary
            {
                ["total"] = c.Total, ["shell"] = c.Shell, ["knuckle"] = c.Knuckle, ["core"] = c.Core,
                ["leaf"] = c.Leaf, ["tendril"] = c.Tendril, ["bridged"] = c.Bridged, ["stranded"] = c.Stranded,
                ["pods"] = c.Markers, ["podsBlocked"] = c.PodsBlocked, ["podsGrounded"] = c.PodsGrounded,
                ["podSpacingM"] = c.PodSpacingM, ["bodies"] = c.Bodies,
            },
            ["bounds"] = new Godot.Collections.Array { vine.BoundsX, vine.BoundsY, vine.BoundsZ },
            ["heightM"] = vine.BoundsZ * vine.VoxelSize,
            ["warnings"] = warnings,
        };
        return Json.Stringify(data, "  ", sortKeys: false) + "\n";
    }
}
