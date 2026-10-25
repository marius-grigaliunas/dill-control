#if TOOLS
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's commands on the editor bridge (docs/decisions/0047): what
/// the tab does, through the same panel, so Claude sees and changes what the
/// user does. A resource saved here is the shared one; the reply names who
/// else it tunes.
/// </summary>
public static class AssetLabCommands
{
    public static Func<AssetLabPanel> Panel;

    public static void Register()
    {
        Bridge.Register("lab_assets",
            "Every asset the asset lab can show: wrapper scenes in scenes/props, vehicles, enemies, weapons, and bare .vox and .glb models. " +
            "'filter' keeps those whose path contains it.",
            """{"type":"object","properties":{"filter":{"type":"string"}}}""",
            Assets);
        Bridge.Register("lab_open",
            "Show an asset in the asset lab (by path, file name or name) and read it: size, meshes, voxel parts and chunks, a mech's damage map, " +
            "and the resources it is tuned by with who else shares each. 'show': true also brings the Lab tab to the front for the user.",
            """{"type":"object","required":["asset"],"properties":{"asset":{"type":"string"},"show":{"type":"boolean"}}}""",
            Open);
        Bridge.Register("lab_view",
            "A picture of the asset shown in the asset lab. The camera orbits it: 'yaw' and 'pitch' in degrees (0, 0 looks from its front, +Z), " +
            "'distance' in metres; 'frame': true first frames the whole asset. 'damage_map' and 'ink' switch the overlay and outlines, " +
            "'look' is 'lab' or a biome (brine_flats, rust_dunes, vinegar_frost), 'sun' the sun's elevation in degrees. 'width' default 960.",
            """
            {"type":"object","properties":{
              "yaw":{"type":"number"},"pitch":{"type":"number"},"distance":{"type":"number"},"frame":{"type":"boolean"},
              "damage_map":{"type":"boolean"},"ink":{"type":"boolean"},"look":{"type":"string"},"sun":{"type":"number"},
              "width":{"type":"integer"}
            }}
            """,
            View);
        Bridge.Register("lab_resource",
            "Read a resource the shown asset is tuned by (its file name or path), and with 'values' change it, e.g. {\"HardenedCarveScale\":0.3}. " +
            "Changes apply at once to the loaded resource; 'save': true writes the .tres, which is shared: the reply names every other scene " +
            "or resource that uses it.",
            """
            {"type":"object","required":["resource"],"properties":{
              "resource":{"type":"string"},"values":{"type":"object"},"save":{"type":"boolean"}
            }}
            """,
            ResourceCommand);
    }

    private static AssetLabPanel Lab() => Panel?.Invoke() ?? throw new Bridge.Refused("the asset lab is not open: is the asset_lab plugin on?");

    private static JsonNode Assets(JsonObject args)
    {
        string filter = Bridge.Text(args, "filter", "").ToLowerInvariant();
        var list = new JsonArray();
        foreach (AssetCatalog.Asset asset in AssetCatalog.All().Where(a => filter == "" || a.Path.ToLowerInvariant().Contains(filter)))
        {
            list.Add(new JsonObject { ["path"] = asset.Path, ["group"] = asset.Group });
        }
        return new JsonObject { ["count"] = list.Count, ["assets"] = list };
    }

    private static JsonNode Open(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        AssetCatalog.Asset asset = AssetCatalog.Find(Bridge.Text(args, "asset"))
            ?? throw new Bridge.Refused($"no asset '{Bridge.Text(args, "asset")}'; lab_assets lists them");
        lab.Open(asset.Path);
        if (lab.Current != asset.Path)
        {
            throw new Bridge.Refused($"{asset.Path} did not load as a scene");
        }
        if (Bridge.Flag(args, "show", false))
        {
            EditorInterface.Singleton.SetMainScreenEditor("Lab");
        }
        return Described(lab);
    }

    private static JsonObject Described(AssetLabPanel lab)
    {
        JsonObject info = lab.Info.Json();
        var shared = new JsonObject();
        foreach (string resource in lab.Info.Resources)
        {
            shared[resource] = new JsonArray(lab.SharedWith(resource).Select(u => (JsonNode)u).ToArray());
        }
        info["shared_with"] = shared;
        return info;
    }

    private static async Task<JsonNode> View(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        LabPreview preview = lab.Preview;
        if (preview.Asset == null)
        {
            throw new Bridge.Refused("no asset shown; lab_open one first");
        }
        if (Bridge.Flag(args, "frame", false))
        {
            preview.Frame();
        }
        if (args.ContainsKey("yaw"))
        {
            preview.Yaw = Mathf.DegToRad((float)Bridge.Number(args, "yaw", 0));
        }
        if (args.ContainsKey("pitch"))
        {
            preview.Pitch = Mathf.DegToRad((float)Bridge.Number(args, "pitch", 0));
        }
        if (args.ContainsKey("distance"))
        {
            preview.Distance = (float)Bridge.Number(args, "distance", preview.Distance);
        }
        preview.PlaceCamera();
        if (args.ContainsKey("damage_map"))
        {
            preview.DamageMap = Bridge.Flag(args, "damage_map", true);
        }
        if (args.ContainsKey("ink"))
        {
            preview.Ink = Bridge.Flag(args, "ink", true);
        }
        if (args.ContainsKey("look"))
        {
            string look = Bridge.Text(args, "look");
            if (look == "lab")
            {
                preview.SetLook(null);
            }
            else
            {
                string path = $"res://resources/planet/biomes/biome_{look}.tres";
                preview.SetLook(ResourceLoader.Exists(path) ? GD.Load<Planet.PlanetBiome>(path) : throw new Bridge.Refused($"no biome {path}"));
            }
        }
        if (args.ContainsKey("sun"))
        {
            preview.SunElevation = (float)Bridge.Number(args, "sun", 45);
        }
        int width = (int)Bridge.Number(args, "width", 960);
        Image image = await preview.Capture(new Vector2I(width, width * 9 / 16));
        if (image == null || image.IsEmpty())
        {
            throw new Bridge.Refused("the lab drew nothing");
        }
        return new JsonObject
        {
            ["image_png"] = Convert.ToBase64String(image.SavePngToBuffer()),
            ["asset"] = lab.Current,
            ["camera"] = new JsonObject
            {
                ["yaw"] = Math.Round(Mathf.RadToDeg(preview.Yaw), 1),
                ["pitch"] = Math.Round(Mathf.RadToDeg(preview.Pitch), 1),
                ["distance"] = Math.Round(preview.Distance, 2),
                ["target"] = Bridge.Json(preview.Target),
            },
        };
    }

    private static JsonNode ResourceCommand(JsonObject args)
    {
        AssetLabPanel lab = Lab();
        string name = Bridge.Text(args, "resource");
        Resource resource = lab.Resources.FirstOrDefault(r => r.ResourcePath == name || r.ResourcePath.GetFile() == name || r.ResourcePath.GetFile().GetBaseName() == name)
            ?? throw new Bridge.Refused($"'{name}' does not tune {lab.Current}; lab_open lists its resources");
        var changed = new JsonArray();
        if (args["values"] is JsonObject values)
        {
            foreach ((string key, JsonNode value) in values)
            {
                Godot.Collections.Dictionary property = resource.GetPropertyList().FirstOrDefault(p => p["name"].AsString() == key)
                    ?? throw new Bridge.Refused($"{resource.ResourcePath.GetFile()} has no property {key}");
                resource.Set(key, Values.FromJson(value, (Variant.Type)property["type"].AsInt64(), property["class_name"].AsString(), key));
                changed.Add(key);
            }
            resource.EmitChanged();
        }
        var result = new JsonObject { ["resource"] = resource.ResourcePath, ["changed"] = changed };
        if (Bridge.Flag(args, "save", false))
        {
            Error error = ResourceSaver.Save(resource, resource.ResourcePath);
            if (error != Error.Ok)
            {
                throw new Bridge.Refused($"could not save {resource.ResourcePath}: {error}");
            }
            result["saved"] = true;
            result["shared_with"] = new JsonArray(lab.SharedWith(resource.ResourcePath).Select(u => (JsonNode)u).ToArray());
        }
        var properties = new JsonObject();
        foreach (Godot.Collections.Dictionary property in resource.GetPropertyList())
        {
            var usage = (PropertyUsageFlags)property["usage"].AsInt64();
            if ((usage & PropertyUsageFlags.ScriptVariable) != 0 && (usage & PropertyUsageFlags.Storage) != 0)
            {
                string key = property["name"].AsString();
                properties[key] = Values.ToJson(resource.Get(key));
            }
        }
        result["properties"] = properties;
        return result;
    }
}
#endif
