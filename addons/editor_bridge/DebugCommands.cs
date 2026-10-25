#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The editor's Debugger panel, read (docs/decisions/0044): whether the
/// playing game is held at a break, why, its stack and its errors, and a way
/// to let it go on. A game held there stops answering and its window stops
/// responding; nothing of it reaches the game's log.
/// </summary>
public static class DebugCommands
{
    public static void Register()
    {
        Bridge.Register("debugger",
            "The editor's Debugger panel for the playing game: whether it is held at a break ('held'), the panel's messages, " +
            "the stack frames and the errors listed. 'continue': true presses Continue.",
            """{"type":"object","properties":{"continue":{"type":"boolean"}}}""",
            Debugger);
    }

    private static JsonNode Debugger(JsonObject args)
    {
        Control debugger = EditorInterface.Singleton.GetBaseControl()
            .FindChildren("*", "ScriptEditorDebugger", true, false).OfType<Control>()
            .OrderByDescending(d => d.IsVisibleInTree()).FirstOrDefault()
            ?? throw new Bridge.Refused("no Debugger panel found");
        List<Button> buttons = debugger.FindChildren("*", nameof(Button), true, false).OfType<Button>().ToList();
        Button go = buttons.FirstOrDefault(b => b.TooltipText.StartsWith("Continue") || b.Text == "Continue");
        bool held = go != null && !go.Disabled;
        bool pressed = false;
        if (held && Bridge.Flag(args, "continue", false))
        {
            go.EmitSignal(BaseButton.SignalName.Pressed);
            pressed = true;
        }
        var labels = new JsonArray();
        foreach (Label label in debugger.FindChildren("*", nameof(Label), true, false).OfType<Label>())
        {
            if (label.IsVisibleInTree() && label.Text.Trim().Length > 0)
                labels.Add(label.Text.Trim());
        }
        var trees = new JsonObject();
        foreach (Tree tree in debugger.FindChildren("*", nameof(Tree), true, false).OfType<Tree>())
        {
            var rows = new JsonArray();
            Collect(tree.GetRoot(), tree.Columns, rows, 0);
            if (rows.Count > 0)
                trees[tree.Name.ToString()] = rows;
        }
        return new JsonObject
        {
            ["held"] = held,
            ["continued"] = pressed,
            ["playing"] = EditorInterface.Singleton.IsPlayingScene(),
            ["messages"] = labels,
            ["lists"] = trees,
        };
    }

    private static void Collect(TreeItem item, int columns, JsonArray into, int depth)
    {
        for (TreeItem at = item; at != null && into.Count < 60; at = at.GetNext())
        {
            string text = string.Join("  ", Enumerable.Range(0, columns).Select(c => at.GetText(c)).Where(t => t.Length > 0));
            if (text.Length > 0)
                into.Add(new string(' ', depth * 2) + text);
            Collect(at.GetFirstChild(), columns, into, depth + 1);
        }
    }
}
#endif
