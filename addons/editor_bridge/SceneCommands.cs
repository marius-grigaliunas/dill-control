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
