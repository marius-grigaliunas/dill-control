#if TOOLS
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Scenes as the user handles them: <c>open</c> one in the editor (its own
/// tab, or the tab it already has), <c>save</c> the open one, as Ctrl+S does,
/// which also writes what plugins keep beside it (a terrain's heights).
/// </summary>
public static class SceneCommands
{
    public static void Register()
    {
        Bridge.Register("open",
            "Open a scene in the editor, in its own tab (or switch to its tab if it is open). The commands then work on it.",
            """{"type":"object","required":["scene"],"properties":{"scene":{"type":"string","description":"res:// path"}}}""",
            Open);
        Bridge.Register("new_level",
            "Make a new level scene with the tree every level has (CLAUDE.md): Level (LevelRoot) with Geometry, Water, Foliage, Props, " +
            "Lighting, Audio, Spawns (and Spawns/PlayerSpawn) and Navigation, the player scene wired; save it and open it. " +
            "'scene' is its res:// path, e.g. res://scenes/levels/lvl_x.tscn. Refuses to overwrite.",
            """{"type":"object","required":["scene"],"properties":{"scene":{"type":"string"}}}""",
            NewLevel);
        Bridge.Register("project_setting",
            "Read a project setting, or with 'value' set it and save project.godot, as Project Settings does (the editor keeps its own " +
            "copy and writes it back, so never edit project.godot by hand while it is open). 'name' as in project.godot with its section, " +
            "e.g. rendering/rendering_device/driver.windows. Some settings apply when the editor or the game next starts.",
            """{"type":"object","required":["name"],"properties":{"name":{"type":"string"},"value":{}}}""",
            ProjectSetting);
        Bridge.Register("plugin",
            "Switch one of our own editor plugins (a folder in addons/) on or off, as Project Settings > Plugins does.",
            """{"type":"object","required":["plugin"],"properties":{"plugin":{"type":"string","description":"its folder, e.g. terrain_brush"},"enabled":{"type":"boolean"}}}""",
            Plugin);
        Bridge.Register("save",
            "Save the open scene, as Ctrl+S does, with what plugins keep beside it (a terrain's heights and chunks).",
            """{"type":"object","properties":{}}""",
            _ =>
            {
                Node root = Bridge.SceneRoot;
                Error error = EditorInterface.Singleton.SaveScene();
                if (error != Error.Ok)
                    throw new Bridge.Refused($"not saved: {error}");
                return new JsonObject { ["saved"] = root.SceneFilePath };
            });
    }

    private static JsonNode ProjectSetting(JsonObject args)
    {
        string name = Bridge.Text(args, "name") ?? throw new Bridge.Refused("'name' is missing");
        if (!ProjectSettings.HasSetting(name))
            throw new Bridge.Refused($"no project setting '{name}'");
        Variant was = ProjectSettings.GetSetting(name);
        var result = new JsonObject { ["name"] = name, ["value"] = Values.ToJson(was) };
        if (args["value"] is JsonNode value)
        {
            Variant set = Values.FromJson(value, was.VariantType, "", name);
            ProjectSettings.SetSetting(name, set);
            Error error = ProjectSettings.Save();
            if (error != Error.Ok)
                throw new Bridge.Refused($"not saved: {error}");
            result["was"] = Values.ToJson(was);
            result["value"] = Values.ToJson(set);
            result["saved"] = true;
        }
        return result;
    }

    // Only ours, under addons/ (CLAUDE.md rule 7).
    private static JsonNode Plugin(JsonObject args)
    {
        string name = Bridge.Text(args, "plugin") ?? throw new Bridge.Refused("'plugin' is missing");
        string cfg = $"res://addons/{name}/plugin.cfg";
        if (!FileAccess.FileExists(cfg))
            throw new Bridge.Refused($"no {cfg}");
        bool on = Bridge.Flag(args, "enabled", true);
        EditorInterface.Singleton.SetPluginEnabled(cfg, on);
        return new JsonObject { ["plugin"] = cfg, ["enabled"] = EditorInterface.Singleton.IsPluginEnabled(cfg) };
    }

    private static readonly string[] Branches = { "Geometry", "Water", "Foliage", "Props", "Lighting", "Audio", "Spawns", "Navigation" };

    private static async Task<JsonNode> NewLevel(JsonObject args)
    {
        string path = Bridge.Text(args, "scene") ?? throw new Bridge.Refused("'scene' is missing");
        if (!path.StartsWith("res://scenes/levels/") || !path.GetFile().StartsWith("lvl_") || !path.EndsWith(".tscn"))
            throw new Bridge.Refused("a level is res://scenes/levels/lvl_<name>.tscn");
        if (ResourceLoader.Exists(path))
            throw new Bridge.Refused($"'{path}' exists; open it instead");
        var level = new Node3D { Name = "Level" };
        level.SetScript(GD.Load<Script>("res://scripts/levels/LevelRoot.cs"));
        foreach (string name in Branches)
        {
            var branch = new Node3D { Name = name };
            level.AddChild(branch);
            branch.Owner = level;
        }
        var spawn = new Marker3D { Name = "PlayerSpawn" };
        level.GetNode("Spawns").AddChild(spawn);
        spawn.Owner = level;
        level.Set("PlayerScene", GD.Load<PackedScene>("res://scenes/player/player.tscn"));
        level.Set("PlayerSpawn", spawn);
        var packed = new PackedScene();
        Error packing = packed.Pack(level);
        level.Free();
        if (packing != Error.Ok)
            throw new Bridge.Refused($"could not pack the level: {packing}");
        Error saving = ResourceSaver.Save(packed, path);
        if (saving != Error.Ok)
            throw new Bridge.Refused($"could not save '{path}': {saving}");
        EditorInterface.Singleton.GetResourceFilesystem().UpdateFile(path);
        return await Open(new JsonObject { ["scene"] = path });
    }

    private static async Task<JsonNode> Open(JsonObject args)
    {
        string path = Bridge.Text(args, "scene") ?? throw new Bridge.Refused("'scene' is missing");
        if (!ResourceLoader.Exists(path))
            throw new Bridge.Refused($"no scene '{path}'");
        EditorInterface.Singleton.OpenSceneFromPath(path);
        for (int i = 0; i < 120 && EditorInterface.Singleton.GetEditedSceneRoot()?.SceneFilePath != path; i++)
            await Bridge.NextFrame();
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root?.SceneFilePath != path)
            throw new Bridge.Refused($"'{path}' did not open");
        return new JsonObject
        {
            ["open"] = path,
            ["tabs"] = new JsonArray(EditorInterface.Singleton.GetOpenScenes().Select(s => (JsonNode)s).ToArray()),
            ["unsaved"] = new JsonArray(EditorInterface.Singleton.GetUnsavedScenes().Select(s => (JsonNode)s).ToArray()),
        };
    }
}
#endif
