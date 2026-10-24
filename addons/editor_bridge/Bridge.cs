#if TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The commands the editor bridge answers (docs/decisions/0044). A tool
/// registers its own, so the bridge knows no tool. Names are
/// <c>snake_case</c>, since an MCP tool's name may not hold a dot.
/// Starts empty with every load of the project's code, and is filled again
/// by the plugins that load with it.
/// </summary>
public static class Bridge
{
    public sealed record Command(string Name, string Description, JsonObject Schema, Func<JsonObject, Task<JsonNode>> Run);

    private static readonly SortedDictionary<string, Command> All = new();

    public static IEnumerable<Command> Commands => All.Values;

    public static bool TryGet(string name, out Command command) => All.TryGetValue(name, out command);

    /// <param name="schema">The arguments as a JSON Schema object, e.g. <c>{"type":"object","properties":{...}}</c>.</param>
    public static void Register(string name, string description, string schema, Func<JsonObject, Task<JsonNode>> run) =>
        All[name] = new Command(name, description, JsonNode.Parse(schema)!.AsObject(), run);

    public static void Register(string name, string description, string schema, Func<JsonObject, JsonNode> run) =>
        Register(name, description, schema, args => Task.FromResult(run(args)));

    /// <summary>A mistake in a command's arguments or state: its message goes back as the error.</summary>
    public sealed class Refused(string message) : Exception(message);

    // ---------------- Waiting for the editor ----------------

    private static SceneTree Tree => (SceneTree)Engine.GetMainLoop();

    public static async Task NextFrame() => await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);

    /// <summary>
    /// Until <paramref name="frames"/> frames have really been drawn. The
    /// editor draws only when something changes, so it is asked for each.
    /// </summary>
    public static async Task Drawn(int frames = 2)
    {
        int start = Engine.GetFramesDrawn();
        for (int i = 0; i < 600 && Engine.GetFramesDrawn() - start < frames; i++)
        {
            EditorInterface.Singleton.GetBaseControl().QueueRedraw();
            await NextFrame();
        }
    }

    // ---------------- The edited scene ----------------

    public static Node SceneRoot =>
        EditorInterface.Singleton.GetEditedSceneRoot() ?? throw new Refused("no scene is open in the editor");

    /// <summary>A node by its path from the scene's root ("" or "." is the root).</summary>
    public static Node NodeAt(string path)
    {
        Node root = SceneRoot;
        if (string.IsNullOrEmpty(path) || path == ".")
            return root;
        return root.GetNodeOrNull(path.TrimStart('/')) ?? throw new Refused($"no node '{path}' in {root.SceneFilePath}");
    }

    public static string PathOf(Node node) =>
        node == SceneRoot ? "." : SceneRoot.GetPathTo(node).ToString();

    // ---------------- Arguments and results ----------------

    public static string Text(JsonObject args, string key, string fallback = null) =>
        args.TryGetPropertyValue(key, out JsonNode v) && v != null ? v.GetValue<string>() : fallback;

    public static double Number(JsonObject args, string key, double fallback) =>
        args.TryGetPropertyValue(key, out JsonNode v) && v != null ? v.GetValue<double>() : fallback;

    public static bool Flag(JsonObject args, string key, bool fallback) =>
        args.TryGetPropertyValue(key, out JsonNode v) && v != null ? v.GetValue<bool>() : fallback;

    /// <summary>[x, y, z], or null when the key is missing.</summary>
    public static Vector3? Vec3(JsonObject args, string key)
    {
        if (!args.TryGetPropertyValue(key, out JsonNode v) || v is not JsonArray a)
            return null;
        if (a.Count != 3)
            throw new Refused($"'{key}' wants [x, y, z]");
        return new Vector3((float)a[0]!.GetValue<double>(), (float)a[1]!.GetValue<double>(), (float)a[2]!.GetValue<double>());
    }

    public static JsonArray Json(Vector3 v) => new(Round(v.X), Round(v.Y), Round(v.Z));

    private static double Round(float f) => Math.Round(f, 3);

    public static string Invariant(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
}
#endif
