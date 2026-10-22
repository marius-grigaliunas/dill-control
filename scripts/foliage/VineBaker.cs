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
    public const int Version = 4;
    /// <summary>What a vine is drawn in and broken by, and how its import
    /// chunks it: the .vox.import a bake writes and a vine made in memory
    /// (<see cref="VineFactory"/>) both use these.</summary>
    public const string Material = "res://art/materials/mat_voxel_toon.tres";
    public const string DestructionPath = "res://resources/voxel/voxel_destruction_vine.tres";
    public const int ChunkSize = 8;
    public const int ChunkJitter = 2;

    public sealed class Baked
    {
        public string Name;
        public string VoxPath;
        public string ImportPath;
        public string JsonPath;
        public string ScenePath;
        public string LeavesPath;
        public bool SceneWritten;
        public VineGrower.Grown Grown;
        public VineGenerator.Result Vine => Grown.Vine;
    }

    /// <summary>A vine's name regrows it: its kind and seed, and a <c>c</c>
    /// when a setting was changed by hand (its .json holds every setting).</summary>
    public static string NameOf(VineKind kind, int seed, bool changed = false) =>
        $"prp_vine_{kind?.Slug ?? "vine"}_{seed}{(changed ? "c" : "")}";

    /// <summary>Grows the vine <paramref name="seed"/> means (<see cref="VineGrower"/>),
    /// or one from <paramref name="settings"/>, and writes it. A vine that is
    /// not valid is not written: <see cref="Baked.Grown"/> says why.</summary>
    public static Baked Bake(VineKind kind, int seed, VineBakeStats stats, VineParams settings = null, bool changed = false)
    {
        VineGrower.Grown grown = settings != null ? VineGrower.FromSettings(settings, stats) : VineGrower.Grow(kind, seed, stats);
        VineGenerator.Result vine = grown.Vine;
        string name = NameOf(kind, seed, changed);
        var baked = new Baked
        {
            Name = name,
            Grown = grown,
            VoxPath = $"{stats.VoxelFolder}/{name}.vox",
            ImportPath = $"{stats.VoxelFolder}/{name}.vox.import",
            JsonPath = $"{stats.VoxelFolder}/{name}.json",
            ScenePath = $"{stats.SceneFolder}/{name}.tscn",
            LeavesPath = $"{stats.VoxelFolder}/{name}_leaves.tres",
        };
        if (!grown.Ok)
            return baked;

        VinePalette palette = PaletteFor(kind, stats, vine);
        WriteBytes(baked.VoxPath, Encode(vine, palette));
        WriteImport(baked.ImportPath, baked.VoxPath, stats.VoxelSize);
        WriteText(baked.JsonPath, Describe(kind, grown, changed));
        Error saved = ResourceSaver.Save(VineLeafSet.Of(vine, kind, palette), baked.LeavesPath);
        if (saved != Error.Ok)
            throw new InvalidOperationException($"Cannot write {baked.LeavesPath}: {saved}");
        // Re-baking changes the model and its leaves and keeps the wrapper:
        // whatever was set on it in the editor, and its uid, survive. A
        // wrapper from before leaves were meshes is written again.
        if (!FileAccess.FileExists(baked.ScenePath) || !FileAccess.GetFileAsString(baked.ScenePath).Contains("name=\"Leaves\""))
        {
            WriteText(baked.ScenePath, Wrapper(name, baked.VoxPath, baked.LeavesPath, stats));
            baked.SceneWritten = true;
        }
        return baked;
    }

    /// <summary>The colours a grown vine is written in: its kind's palette (or
    /// the bake's) in the shade its seed rolls.</summary>
    public static VinePalette PaletteFor(VineKind kind, VineBakeStats stats, VineGenerator.Result vine) =>
        (kind?.Palette ?? stats.Palette ?? new VinePalette()).Shaded((int)vine.Params.Seed);

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
        for (int index = VineGenerator.Shell; index <= VineGenerator.Petiole; index++)
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
        for (int index = VineGenerator.Shell; index <= VineGenerator.Petiole; index++)
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
            voxel/material="{Material}"
            voxel/chunk_size={ChunkSize}
            voxel/chunk_jitter={ChunkJitter}

            """);
    }

    /// <summary>The wrapper every vine has: the reference vine's (prp_vine_41051)
    /// and its leaves.</summary>
    private static string Wrapper(string name, string voxPath, string leavesPath, VineBakeStats stats)
    {
        string destruction = stats.Destruction?.ResourcePath ?? DestructionPath;
        string flex = stats.Flex?.ResourcePath ?? FlexStats.VinePath;
        return $"""
            [gd_scene format=3]

            [ext_resource type="Script" path="res://scripts/voxel/VoxelBody.cs" id="1_voxels"]
            [ext_resource type="Resource" path="{destruction}" id="2_stats"]
            [ext_resource type="PackedScene" path="{voxPath}" id="3_model"]
            [ext_resource type="Script" path="res://scripts/foliage/PlantFlex.cs" id="4_flex"]
            [ext_resource type="Resource" path="{flex}" id="5_flexstats"]
            [ext_resource type="Script" path="res://scripts/foliage/VineLeaves.cs" id="6_leaves"]
            [ext_resource type="Resource" path="{leavesPath}" id="7_set"]
            [ext_resource type="Resource" path="{LeafStats.DefaultPath}" id="8_leafstats"]

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

            [node name="Leaves" type="Node3D" parent="." node_paths=PackedStringArray("Body", "Flex", "Model")]
            script = ExtResource("6_leaves")
            Set = ExtResource("7_set")
            Body = NodePath("../Voxels")
            Flex = NodePath("../Flex")
            Model = NodePath("../Model")
            Stats = ExtResource("8_leafstats")

            """;
    }

    /// <summary>What the vine was grown from and what it came to, beside it,
    /// for the nest step and for whoever wonders where a vine came from.</summary>
    public static string Describe(VineKind kind, VineGrower.Grown grown, bool changed = false)
    {
        VineGenerator.Result vine = grown.Vine;
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
            ["seed"] = grown.Seed,
            // A seed that failed the checks was rolled again: which attempt
            // grew this one, and the seed that attempt rolled from.
            ["attempt"] = grown.Attempt,
            ["rolledSeed"] = (int)vine.Params.Seed,
            ["params"] = settings,
            ["counts"] = new Godot.Collections.Dictionary
            {
                ["total"] = c.Total, ["shell"] = c.Shell, ["knuckle"] = c.Knuckle, ["core"] = c.Core,
                ["leaves"] = c.Leaves, ["leavesLeftOut"] = c.LeavesLeftOut, ["bridged"] = c.Bridged, ["stranded"] = c.Stranded,
                ["pods"] = c.Markers, ["podsBlocked"] = c.PodsBlocked, ["podsGrounded"] = c.PodsGrounded, ["podsCrowded"] = c.PodsCrowded,
                ["podSpacingM"] = c.PodSpacingM, ["bodies"] = c.Bodies,
            },
            ["bounds"] = new Godot.Collections.Array { vine.BoundsX, vine.BoundsY, vine.BoundsZ },
            ["heightM"] = vine.BoundsZ * vine.VoxelSize,
            ["warnings"] = warnings,
        };
        return Json.Stringify(data, "  ", sortKeys: false) + "\n";
    }
}
