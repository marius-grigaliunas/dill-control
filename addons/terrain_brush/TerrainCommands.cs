#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using DillControl.Core;
using DillControl.Levels;

namespace DillControl.Editor;

/// <summary>
/// The terrain on the editor bridge (docs/decisions/0044): make a
/// <see cref="SculptedTerrain"/>, sculpt it with the brush's own dabs, read
/// its heights. Each change one undo.
/// </summary>
public static class TerrainCommands
{
    private const string MaterialPath = "res://art/materials/mat_arena_terrain.tres";

    public static void Register()
    {
        Bridge.Register("terrain_create",
            "Make a SculptedTerrain in the open scene: real nodes (a body, a HeightMapShape3D of the heights, chunk meshes) and their files, " +
            "saved beside the scene in <scene>_terrain/. Flat at 'height', or with the heights of 'from', any ground with an ArenaHeights " +
            "Heights property, resampled. One undo takes the nodes out; the files stay.",
            """
            {"type":"object","properties":{
              "name":{"type":"string","description":"default Terrain"},
              "parent":{"type":"string","description":"default Geometry"},
              "at":{"type":"array","items":{"type":"number"},"description":"the middle [x, y, z]; default from's, else the origin"},
              "size":{"type":"number","description":"metres along a side; default from's, else 1000"},
              "spacing":{"type":"number","description":"metres between samples; default 4"},
              "height":{"type":"number","description":"for a flat ground; default 0"},
              "from":{"type":"string","description":"path of a node whose Heights to copy"},
              "folder":{"type":"string","description":"res:// folder for the files"}
            }}
            """,
            Create);
        Bridge.Register("sculpt",
            "Sculpt a SculptedTerrain as the brush does: dabs of 'radius' metres along 'points' (or at 'at'), as one undo. " +
            "Modes: raise / lower (strength = metres a dab at the middle), smooth, flatten (strength 0-1, how far a dab goes; flatten toward 'height', " +
            "metres over the terrain's node, default the ground at the first point). 'hardness' 0-0.95: the share of the radius at full strength. " +
            "A mesa: flatten, strength 1, hardness 0.8, height 25. A pass of several dabs: give the points along it.",
            """
            {"type":"object","required":["mode","radius"],"properties":{
              "terrain":{"type":"string","description":"path; default the scene's only SculptedTerrain"},
              "mode":{"type":"string","enum":["raise","lower","smooth","flatten"]},
              "at":{"type":"array","items":{"type":"number"},"description":"[x, z]"},
              "points":{"type":"array","items":{"type":"array","items":{"type":"number"}},"description":"[[x, z], ...]: dabs along them, a fifth of the radius apart"},
              "radius":{"type":"number"},"strength":{"type":"number","description":"default 1"},
              "hardness":{"type":"number","description":"default 0.3"},"height":{"type":"number"}
            }}
            """,
            Sculpt);
        Bridge.Register("scatter",
            "Paint real instances of scenes onto a SculptedTerrain, as the brush's Scatter does: along 'points' (or at 'at'), " +
            "up to 'density' per 1,000 m² within 'radius', 'spacing' metres from anything standing, no steeper than 'max_slope', never in water; " +
            "each stood on the ground with a random turn, under Foliage or Props. 'erase' takes the scenes' instances away instead. One undo.",
            """
            {"type":"object","required":["scenes","radius"],"properties":{
              "terrain":{"type":"string"},
              "scenes":{"type":"array","items":{"type":"string"},"description":"res:// paths or names in scenes/props, e.g. prp_tree_tall_a"},
              "at":{"type":"array","items":{"type":"number"}},"points":{"type":"array","items":{"type":"array","items":{"type":"number"}}},
              "radius":{"type":"number"},"density":{"type":"number","description":"default 2"},"spacing":{"type":"number","description":"default 12"},
              "max_slope":{"type":"number","description":"degrees, default 30"},"erase":{"type":"boolean"}
            }}
            """,
            ScatterCommand);
        Bridge.Register("flood",
            "Flood a hollow of a SculptedTerrain with shallow water (decision 0045), as the brush's Water does: from 'at' [x, z], every connected cell " +
            "with ground below the level gets water, flat at the level. The level is 'level' (world height) or 'depth' metres over the ground at 'at' (default 0.8). " +
            "Refuses when the water would run off the terrain's edge. Makes a WaterBody under Water; one undo.",
            """
            {"type":"object","required":["at"],"properties":{
              "terrain":{"type":"string"},"at":{"type":"array","items":{"type":"number"}},
              "level":{"type":"number"},"depth":{"type":"number"},"name":{"type":"string"}
            }}
            """,
            Flood);
        Bridge.Register("terrain",
            "A SculptedTerrain's size, spacing, lowest and highest ground, and the ground's height at 'points' [[x, z], ...].",
            """
            {"type":"object","properties":{
              "terrain":{"type":"string"},"points":{"type":"array","items":{"type":"array","items":{"type":"number"}}}
            }}
            """,
            Info);
    }

    private static SculptedTerrain Find(JsonObject args)
    {
        string path = Bridge.Text(args, "terrain");
        if (path != null)
            return Bridge.NodeAt(path) as SculptedTerrain ?? throw new Bridge.Refused($"'{path}' is not a SculptedTerrain");
        List<SculptedTerrain> all = Bridge.SceneRoot.FindChildren("*", nameof(StaticBody3D), true, true).OfType<SculptedTerrain>().ToList();
        return all.Count switch
        {
            1 => all[0],
            0 => throw new Bridge.Refused("the open scene has no SculptedTerrain: terrain_create makes one"),
            _ => throw new Bridge.Refused("the scene has several terrains: name one with 'terrain'"),
        };
    }

    private static JsonNode Create(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        Node parent = Bridge.NodeAt(Bridge.Text(args, "parent", "Geometry"));
        string name = Bridge.Text(args, "name", "Terrain");
        float spacing = (float)Bridge.Number(args, "spacing", 4.0);
        if (spacing < 0.5f || spacing > 16.0f)
            throw new Bridge.Refused("'spacing' must be 0.5-16 m");

        // The older ground to copy, by its Heights property, whatever its class.
        ArenaHeights source = null;
        Vector3 at = Vector3.Zero;
        float size = 1000.0f;
        string from = Bridge.Text(args, "from");
        if (from != null)
        {
            Node old = Bridge.NodeAt(from);
            source = old.GetType().GetProperty("Heights")?.GetValue(old) as ArenaHeights
                ?? throw new Bridge.Refused($"'{from}' has no Heights to copy (is its ground built in the editor?)");
            at = old is Node3D o3 ? o3.GlobalPosition : Vector3.Zero;
            size = source.Size;
        }
        at = Bridge.Vec3(args, "at") ?? at;
        size = (float)Bridge.Number(args, "size", size);
        int count = Mathf.Max(2, Mathf.RoundToInt(size / spacing) + 1);
        size = (count - 1) * spacing;
        float flat = (float)Bridge.Number(args, "height", 0.0);

        var heights = new float[count * count];
        for (int z = 0; z < count; z++)
        {
            for (int x = 0; x < count; x++)
            {
                float wx = at.X - (size * 0.5f) + (x * spacing);
                float wz = at.Z - (size * 0.5f) + (z * spacing);
                heights[(z * count) + x] = source != null ? source.HeightAt(wx, wz) - at.Y : flat;
            }
        }

        string scene = root.SceneFilePath;
        string folder = Bridge.Text(args, "folder")
            ?? $"{scene.GetBaseDir()}/{scene.GetFile().GetBaseName()}_terrain";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(folder));

        var map = new HeightMapShape3D { MapWidth = count, MapDepth = count, MapData = heights };
        SaveAs(map, $"{folder}/heights.res");
        var stats = ResourceLoader.Load<TerrainStats>(TerrainStats.DefaultPath);
        var terrain = new SculptedTerrain
        {
            Name = name,
            Stats = stats,
            Spacing = spacing,
            Material = ResourceLoader.Load<Material>(MaterialPath),
            CollisionLayer = GameLayers.Mask(GameLayers.World),
            CollisionMask = 0,
        };
        var shape = new CollisionShape3D { Name = "Heights", Shape = map, Scale = new Vector3(spacing, 1.0f, spacing) };
        terrain.AddChild(shape);
        terrain.Shape = shape;

        parent.AddChild(terrain, true);
        terrain.GlobalPosition = at;
        int cells = stats?.ChunkCells ?? 25;
        int chunks = 0;
        for (int cz = 0; cz * cells < count - 1; cz++)
        {
            for (int cx = 0; cx * cells < count - 1; cx++)
            {
                var mesh = new ArrayMesh();
                TerrainMesher.Fill(mesh, terrain.Heights, cx * cells, cz * cells,
                    Math.Min((cx + 1) * cells, count - 1), Math.Min((cz + 1) * cells, count - 1), terrain.Material);
                SaveAs(mesh, $"{folder}/chunk_{cx}_{cz}.res");
                terrain.AddChild(new MeshInstance3D { Name = $"{SculptedTerrain.ChunkPrefix}{cx}_{cz}", Mesh = mesh });
                chunks++;
            }
        }
        terrain.Owner = root;
        foreach (Node child in terrain.GetChildren())
            child.Owner = root;

        EditorUndoRedoManager undo = Bridge.BeginAction($"make terrain {terrain.Name}");
        undo.AddDoMethod(parent, Node.MethodName.AddChild, terrain, true);
        undo.AddDoProperty(terrain, Node.PropertyName.Owner, root);
        foreach (Node child in terrain.GetChildren())
            undo.AddDoProperty(child, Node.PropertyName.Owner, root);
        undo.AddDoReference(terrain);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, terrain);
        Bridge.EndAction(undo);

        ArenaHeights made = terrain.Heights;
        return new JsonObject
        {
            ["node"] = Bridge.PathOf(terrain),
            ["samples"] = $"{count}x{count}",
            ["size_m"] = size,
            ["spacing_m"] = spacing,
            ["chunks"] = chunks,
            ["lowest"] = Math.Round(made.Lowest, 2),
            ["highest"] = Math.Round(made.Highest, 2),
            ["folder"] = folder,
            ["copied_from"] = from,
        };
    }

    private static void SaveAs(Resource resource, string path)
    {
        Error error = ResourceSaver.Save(resource, path);
        if (error != Error.Ok)
            throw new Bridge.Refused($"{path} not written: {error}");
        resource.TakeOverPath(path);
    }

    private static JsonNode Sculpt(JsonObject args)
    {
        SculptedTerrain terrain = Find(args);
        string modeName = Bridge.Text(args, "mode") ?? throw new Bridge.Refused("'mode' is missing");
        if (!TerrainSculpt.TryMode(modeName, out TerrainSculpt.Mode mode))
            throw new Bridge.Refused("'mode' is raise, lower, smooth or flatten");
        var points = new List<Vector2>();
        if (args["points"] is JsonArray list)
            points.AddRange(list.Select(XZ));
        if (args["at"] != null)
            points.Add(XZ(args["at"]));
        if (points.Count == 0)
            throw new Bridge.Refused("give 'at' or 'points'");

        ArenaHeights h = terrain.Heights;
        var brush = new TerrainSculpt.Brush
        {
            Mode = mode,
            RadiusM = (float)Bridge.Number(args, "radius", 20),
            Strength = (float)Bridge.Number(args, "strength", 1.0),
            Hardness = (float)Bridge.Number(args, "hardness", 0.3),
            Target = (float)Bridge.Number(args, "height", h.HeightAt(points[0].X, points[0].Y) - terrain.GlobalPosition.Y),
        };

        float[] before = terrain.CopyHeights();
        float[] after = (float[])before.Clone();
        int dabs = 0;
        float step = Mathf.Max(brush.RadiusM * 0.2f, terrain.Spacing * 0.5f);
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = i + 1 < points.Count ? points[i + 1] : a;
            int n = Math.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / step));
            for (int k = 0; k < (i + 1 < points.Count ? n : 1); k++)
            {
                Vector2 p = a.Lerp(b, (float)k / n);
                TerrainSculpt.Dab(terrain, after, p.X, p.Y, brush);
                dabs++;
            }
        }
        terrain.SetHeights(after);
        EditorUndoRedoManager undo = Bridge.BeginAction($"sculpt: {mode}");
        TerrainSculpt.Record(undo, terrain, before, after);
        Bridge.EndAction(undo);

        float most = 0.0f;
        for (int i = 0; i < after.Length; i++)
            most = Math.Max(most, Math.Abs(after[i] - before[i]));
        h = terrain.Heights;
        return new JsonObject
        {
            ["terrain"] = Bridge.PathOf(terrain),
            ["dabs"] = dabs,
            ["most_changed_m"] = Math.Round(most, 2),
            ["heights_at_points"] = new JsonArray(points.Select(p => (JsonNode)Math.Round(h.HeightAt(p.X, p.Y), 2)).ToArray()),
            ["note"] = "saved to its files with the scene",
        };
    }

    private static List<Vector2> Points(JsonObject args)
    {
        var points = new List<Vector2>();
        if (args["points"] is JsonArray list)
            points.AddRange(list.Select(XZ));
        if (args["at"] != null)
            points.Add(XZ(args["at"]));
        if (points.Count == 0)
            throw new Bridge.Refused("give 'at' or 'points'");
        return points;
    }

    private static JsonNode ScatterCommand(JsonObject args)
    {
        SculptedTerrain terrain = Find(args);
        List<string> scenes = (args["scenes"] as JsonArray ?? throw new Bridge.Refused("'scenes' is missing"))
            .Select(s => PlaceCommands.ScenePath(s!.GetValue<string>())).ToList();
        var settings = new Scatter.Settings
        {
            RadiusM = (float)Bridge.Number(args, "radius", 20),
            Density = (float)Bridge.Number(args, "density", 2),
            SpacingM = (float)Bridge.Number(args, "spacing", 12),
            MaxSlopeDeg = (float)Bridge.Number(args, "max_slope", 30),
        };
        bool erase = Bridge.Flag(args, "erase", false);
        List<Vector2> points = Points(args);
        var scatter = new Scatter(terrain, scenes);
        float step = Mathf.Max(settings.RadiusM * 0.5f, 1.0f);
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 a = points[i];
            Vector2 b = i + 1 < points.Count ? points[i + 1] : a;
            int n = i + 1 < points.Count ? Math.Max(1, Mathf.CeilToInt(a.DistanceTo(b) / step)) : 1;
            for (int k = 0; k < n; k++)
            {
                Vector2 p = a.Lerp(b, (float)k / Math.Max(n, 1));
                if (erase)
                    scatter.Erase(p.X, p.Y, settings);
                else
                    scatter.Paint(p.X, p.Y, settings);
            }
        }
        if (scatter.Changed)
        {
            EditorUndoRedoManager undo = Bridge.BeginAction(erase ? "scatter: erase" : "scatter");
            scatter.Record(undo);
            Bridge.EndAction(undo);
        }
        return new JsonObject
        {
            ["added"] = scatter.Added.Count,
            ["removed"] = scatter.Removed.Count,
            ["now_placed"] = Scatter.Count(Bridge.SceneRoot, scenes),
            ["nodes"] = new JsonArray(scatter.Added.Take(50).Select(n => (JsonNode)Bridge.PathOf(n)).ToArray()),
        };
    }

    private static JsonNode Flood(JsonObject args)
    {
        SculptedTerrain terrain = Find(args);
        Vector2 at = XZ(args["at"] ?? throw new Bridge.Refused("'at' is missing"));
        float ground = terrain.Heights.HeightAt(at.X, at.Y);
        float level = (float)Bridge.Number(args, "level", ground + Bridge.Number(args, "depth", 0.8));
        EditorUndoRedoManager undo = Bridge.BeginAction("flood");
        WaterBody body;
        try
        {
            body = WaterFlood.Flood(terrain, at.X, at.Y, level, undo);
        }
        catch (WaterFlood.Refused e)
        {
            Bridge.EndAction(undo);
            throw new Bridge.Refused(e.Message);
        }
        if (Bridge.Text(args, "name") is { } name)
            body.Name = name;
        Bridge.EndAction(undo);
        int cells = body.Cells.Count(c => c != 0);
        return new JsonObject
        {
            ["node"] = Bridge.PathOf(body),
            ["level"] = Math.Round(level, 2),
            ["ground_at_point"] = Math.Round(ground, 2),
            ["cells"] = cells,
            ["area_m2"] = Math.Round(cells * body.CellSize * body.CellSize),
            ["extent_m"] = new JsonArray(body.Width * body.CellSize, body.Length * body.CellSize),
        };
    }

    private static Vector2 XZ(JsonNode node)
    {
        if (node is not JsonArray a || a.Count is not (2 or 3))
            throw new Bridge.Refused("a point is [x, z]");
        float x = (float)a[0]!.GetValue<double>();
        float z = (float)a[a.Count - 1]!.GetValue<double>();
        return new Vector2(x, z);
    }

    private static JsonNode Info(JsonObject args)
    {
        SculptedTerrain terrain = Find(args);
        ArenaHeights h = terrain.Heights;
        var result = new JsonObject
        {
            ["terrain"] = Bridge.PathOf(terrain),
            ["middle"] = Bridge.Json(terrain.GlobalPosition),
            ["size_m"] = terrain.Size,
            ["spacing_m"] = terrain.Spacing,
            ["samples"] = $"{terrain.Count}x{terrain.Count}",
            ["lowest"] = Math.Round(h.Lowest, 2),
            ["highest"] = Math.Round(h.Highest, 2),
            ["files"] = terrain.Map?.ResourcePath?.GetBaseDir(),
        };
        if (args["points"] is JsonArray list)
            result["heights"] = new JsonArray(list.Select(p => { Vector2 v = XZ(p); return (JsonNode)Math.Round(h.HeightAt(v.X, v.Y), 2); }).ToArray());
        return result;
    }
}
#endif
