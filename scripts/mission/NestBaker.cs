using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DillControl.Foliage;
using DillControl.Planet;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// Turns a <see cref="NestPreset"/> into an authored nest (docs/decisions/0056):
/// the preset's plan, each vine baked to a .vox and its wrapper
/// (<see cref="VineBaker"/>), and one scene of those wrappers laid out on flat
/// ground round the heart at the origin, as the planet's placer would lay them.
/// The scene is then a set piece like any other: moved, swapped and deleted in
/// the editor. A level's <see cref="PlacedNest"/> points its Vines at the
/// scene's root and hands the pods to the colony.
/// One-way: the scene no longer regrows from the seed, so it is never written
/// over unless asked.
/// </summary>
public static class NestBaker
{
    public const string SceneFolder = "res://scenes/props/nests";

    public sealed class Result
    {
        public string ScenePath;
        public List<string> VineScenes = new();
        public List<string> VoxFiles = new();
        public int Vines;
        public int Pods;
        public float RadiusM;
    }

    /// <summary>prp_nest_default_145525444_1000 from nest_default_145525444_1000.tres.</summary>
    public static string SceneName(NestPreset preset)
    {
        string stem = string.IsNullOrEmpty(preset.ResourcePath)
            ? string.Create(CultureInfo.InvariantCulture,
                $"{(preset.Kit?.ResourcePath.GetFile().GetBaseName().Replace("nest_kit_", "") ?? "nest")}_{preset.Seed}_{preset.Population}")
            : preset.ResourcePath.GetFile().GetBaseName();
        return "prp_" + (stem.StartsWith("nest_", StringComparison.Ordinal) ? stem : "nest_" + stem);
    }

    public static string ScenePath(NestPreset preset) => $"{SceneFolder}/{SceneName(preset)}.tscn";

    /// <summary>The plan, on any thread, off the engine's resources: read
    /// <paramref name="context"/> on the main thread first.</summary>
    public static NestLayout Plan(NestPreset preset, VineGrower.Context context) =>
        NestLayout.Plan(preset.Kit, context, (ulong)preset.Seed, preset.Population, preset.RadiusM, preset.SpacingM);

    /// <summary>Bakes every vine of <paramref name="layout"/> and writes the nest
    /// scene. Main thread. Throws if the scene exists and
    /// <paramref name="overwrite"/> is not set.</summary>
    public static Result Bake(NestPreset preset, NestLayout layout, VineBakeStats stats, bool overwrite)
    {
        string name = SceneName(preset);
        string scenePath = ScenePath(preset);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SceneFolder));
        if (!overwrite && FileAccess.FileExists(scenePath))
        {
            throw new InvalidOperationException($"{scenePath} exists and may hold hand edits; not written over");
        }

        var result = new Result { ScenePath = scenePath, RadiusM = layout.RadiusM };
        var text = new StringBuilder();
        var nodes = new StringBuilder();
        var ids = new Dictionary<string, int>();
        foreach (NestLayout.Vine vine in layout.Vines)
        {
            // A nest's vine is grown at a size its kind and seed do not regrow.
            string vineName = VineBaker.NameOf(vine.Kind, vine.Seed)
                + (Math.Abs(vine.Scale - 1.0f) > 1e-4f ? string.Create(CultureInfo.InvariantCulture, $"s{Math.Round(vine.Scale * 1000.0f)}") : "");
            VineBaker.Baked baked = VineBaker.Write(vine.Kind, vine.Grown, vineName, stats);
            if (!ids.TryGetValue(baked.ScenePath, out int id))
            {
                id = ids.Count + 1;
                ids[baked.ScenePath] = id;
                result.VineScenes.Add(baked.ScenePath);
                result.VoxFiles.Add(baked.VoxPath);
                text.Append(CultureInfo.InvariantCulture, $"[ext_resource type=\"PackedScene\" path=\"{baked.ScenePath}\" id=\"{id}\"]\n");
            }
            nodes.Append(CultureInfo.InvariantCulture,
                $"\n[node name=\"Vine{vine.Index:000}_{vine.Kind.Slug}\" parent=\".\" instance=ExtResource(\"{id}\")]\ntransform = {GD.VarToStr(Place(vine, preset.Kit))}\n");
            result.Vines++;
            result.Pods += vine.Pods;
        }

        string header = string.Create(CultureInfo.InvariantCulture,
            $"[gd_scene format=3]\n\n{text}\n[node name=\"{name}\" type=\"Node3D\"]\n");
        using FileAccess file = FileAccess.Open(scenePath, FileAccess.ModeFlags.Write)
            ?? throw new InvalidOperationException($"Cannot write {scenePath}: {FileAccess.GetOpenError()}");
        file.StoreString(header + nodes);
        return result;
    }

    // Where NestPlacer puts the vine on a planet, on a plane: the heart at the
    // origin, bearings from north (-Z), the foot sunk, turned so its lean faces the heart.
    private static Transform3D Place(NestLayout.Vine vine, NestKit kit)
    {
        Vector3 up = Vector3.Up;
        Vector3 at = Vector3.Forward.Rotated(up, vine.Bearing) * vine.RadiusM;
        Vector3 toHeart = (-at).Slide(up);
        if (toHeart.LengthSquared() < 1e-8f)
        {
            toHeart = Vector3.Forward;
        }
        Basis facing = PlanetHeightfield.UprightBasis(up, toHeart.Normalized());
        Vector3 lean = vine.Lean;
        float turn = lean.LengthSquared() > 1e-6f ? Mathf.Pi - Mathf.Atan2(lean.X, lean.Z) : 0.0f;
        return new Transform3D(facing * new Basis(up, turn), at - (up * kit.SinkM));
    }
}
