#if TOOLS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Testing through the bridge: <c>run</c> plays a scene as the editor's Play
/// does (the game window opens; the user allows that unasked), <c>stop</c>
/// stops it, <c>output</c> reads what the game printed (its log file,
/// user://logs/godot.log) or what the editor printed (caught by
/// <see cref="Catcher"/>, a logger on the editor).
/// </summary>
public static partial class RunCommands
{
    public static void Register()
    {
        Bridge.Register("run",
            "Play a scene as the editor's Play does: the open scene, or 'scene' (res:// path). The game window opens. " +
            "Read what it prints with output {\"source\":\"game\"}; stop it with stop.",
            """{"type":"object","properties":{"scene":{"type":"string"}}}""",
            Run);
        Bridge.Register("stop",
            "Stop the game the editor is playing.",
            """{"type":"object","properties":{}}""",
            _ =>
            {
                bool was = EditorInterface.Singleton.IsPlayingScene();
                EditorInterface.Singleton.StopPlayingScene();
                return new JsonObject { ["was_playing"] = was };
            });
        Bridge.Register("output",
            "What was printed: 'game' (default) is the playing (or last played) game's log; 'editor' is the editor's own messages since the bridge started; " +
            "'panel' is the editor's Output panel as it shows, which keeps what was printed while the code reloaded (the bridge restarts then). " +
            "The last 'lines' (default 80); 'errors' true keeps only errors and warnings; 'grep' keeps lines containing it.",
            """
            {"type":"object","properties":{
              "source":{"type":"string","enum":["game","editor","panel"]},"lines":{"type":"integer"},
              "errors":{"type":"boolean"},"grep":{"type":"string"}
            }}
            """,
            Output);
    }

    private static async Task<JsonNode> Run(JsonObject args)
    {
        string scene = Bridge.Text(args, "scene");
        if (scene != null && !ResourceLoader.Exists(scene))
            throw new Bridge.Refused($"no scene '{scene}'");
        if (EditorInterface.Singleton.IsPlayingScene())
            EditorInterface.Singleton.StopPlayingScene();
        if (scene != null)
            EditorInterface.Singleton.PlayCustomScene(scene);
        else
            EditorInterface.Singleton.PlayCurrentScene();
        for (int i = 0; i < 120 && !EditorInterface.Singleton.IsPlayingScene(); i++)
            await Bridge.NextFrame();
        return new JsonObject
        {
            ["playing"] = EditorInterface.Singleton.IsPlayingScene(),
            ["scene"] = scene ?? Bridge.SceneRoot.SceneFilePath,
            ["note"] = "the game writes user://logs/godot.log as it runs: output {\"source\":\"game\"}",
        };
    }

    private static JsonNode Output(JsonObject args)
    {
        int count = Math.Clamp((int)Bridge.Number(args, "lines", 80), 1, 2000);
        bool errors = Bridge.Flag(args, "errors", false);
        string grep = Bridge.Text(args, "grep");
        string source = Bridge.Text(args, "source", "game");
        IEnumerable<string> lines;
        string from;
        if (source == "editor")
        {
            lines = Catcher.Lines();
            from = "the editor, since the bridge started";
        }
        else if (source == "panel")
        {
            RichTextLabel log = EditorInterface.Singleton.GetBaseControl().FindChildren("*", "EditorLog", true, false)
                .SelectMany(n => n.FindChildren("*", nameof(RichTextLabel), true, false)).OfType<RichTextLabel>().FirstOrDefault()
                ?? throw new Bridge.Refused("the editor's Output panel was not found");
            lines = log.GetParsedText().Split('\n').Select(l => l.TrimEnd('\r'));
            from = "the editor's Output panel";
        }
        else
        {
            from = ProjectSettings.GlobalizePath("user://logs/godot.log");
            if (!File.Exists(from))
                throw new Bridge.Refused($"no game log at {from}");
            // The game holds it open: read it shared.
            using var stream = new FileStream(from, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            lines = reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r'));
        }
        if (errors)
            lines = lines.Where(l => l.Contains("ERROR") || l.Contains("WARNING") || l.Contains("Exception") || l.Contains("SCRIPT ERROR"));
        if (!string.IsNullOrEmpty(grep))
            lines = lines.Where(l => l.Contains(grep, StringComparison.OrdinalIgnoreCase));
        string[] kept = lines.Where(l => l.Length > 0).ToArray();
        return new JsonObject
        {
            ["source"] = from,
            ["playing"] = EditorInterface.Singleton.IsPlayingScene(),
            ["matched"] = kept.Length,
            ["lines"] = new JsonArray(kept.Skip(Math.Max(0, kept.Length - count)).Select(l => (JsonNode)l).ToArray()),
        };
    }

    /// <summary>
    /// The editor's own messages, kept while the bridge runs. Called from any
    /// thread, so everything it holds is behind a lock.
    /// </summary>
    public sealed partial class Catcher : Logger
    {
        private static readonly List<string> Kept = new();
        private const int Most = 4000;

        public static IEnumerable<string> Lines()
        {
            lock (Kept)
                return Kept.ToArray();
        }

        private static void Keep(string line)
        {
            lock (Kept)
            {
                Kept.Add(line);
                if (Kept.Count > Most)
                    Kept.RemoveRange(0, Kept.Count - Most);
            }
        }

        public override void _LogMessage(string message, bool error) =>
            Keep((error ? "ERROR: " : "") + message.TrimEnd('\n'));

        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces) =>
            Keep($"{(errorType == 1 ? "WARNING" : "ERROR")}: {(string.IsNullOrEmpty(rationale) ? code : rationale)} ({file}:{line})");
    }
}
#endif
