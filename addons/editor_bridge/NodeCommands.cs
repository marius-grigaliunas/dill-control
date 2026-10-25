#if TOOLS
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Reading and changing the open scene's nodes: <c>tree</c>,
/// <c>selection</c>, <c>get</c>, <c>set</c>, <c>select</c>, <c>frame</c>,
/// and <c>batch</c>, which makes many commands one undo.
/// </summary>
public static class NodeCommands
{
    public static void Register()
    {
        Bridge.Register("tree",
            "The open scene's nodes as the Scene dock shows them (an instanced scene's insides are left out): name, class or script, " +
            "the scene it instances, its world position. From 'node' (default the root), 'depth' levels down (default 2).",
            """
            {"type":"object","properties":{
              "node":{"type":"string"},"depth":{"type":"integer"},"max":{"type":"integer","description":"nodes at most, default 400"}
            }}
            """,
            Tree);
        Bridge.Register("add",
            "Add a built-in node (any Godot class that is a Node, e.g. Label3D, OmniLight3D, Marker3D) to the open scene, as one undo, " +
            "with 'properties' set as in 'set' (e.g. {\"text\":\"...\",\"font_size\":64}). 'position' is world space; 'rotation_degrees' too. " +
            "Default parent Props. 'script' (a res:// path) attaches our own script to it first, so its exports can be set: " +
            "a node's export is 'node:<path>', e.g. {\"Level\":\"node:.\"}.",
            """
            {"type":"object","required":["type"],"properties":{
              "type":{"type":"string"},"name":{"type":"string"},"parent":{"type":"string"},"script":{"type":"string"},
              "position":{"type":"array","items":{"type":"number"}},"rotation_degrees":{"type":"array","items":{"type":"number"}},
              "properties":{"type":"object"}
            }}
            """,
            Add);
        Bridge.Register("selection",
            "What the user has selected in the editor: each node's path, class, scene, world position and size.",
            """{"type":"object","properties":{}}""",
            _ => new JsonArray(EditorInterface.Singleton.GetSelection().GetSelectedNodes().Select(n => (JsonNode)Describe(n, withBounds: true)).ToArray()));
        Bridge.Register("get",
            "A node's properties as the Inspector shows them, or only those named. Vectors are arrays, rotations degrees, " +
            "a resource its res:// path, a node 'node:<path>'.",
            """
            {"type":"object","required":["node"],"properties":{
              "node":{"type":"string"},"properties":{"type":"array","items":{"type":"string"}}
            }}
            """,
            Get);
        Bridge.Register("set",
            "Set properties on one or more nodes, as one undo, e.g. {\"position\":[1,0,2],\"rotation_degrees\":[0,45,0],\"scale\":[2,2,2]} " +
            "or an export by its name. A resource is a res:// path, a node 'node:<path>', a colour [r,g,b,a] or \"#rrggbb\". " +
            "'stand' then stands each node's origin on the mesh under it.",
            """
            {"type":"object","properties":{
              "node":{"type":"string"},"nodes":{"type":"array","items":{"type":"string"}},
              "properties":{"type":"object"},"stand":{"type":"boolean"}
            }}
            """,
            Set);
        Bridge.Register("select",
            "Select nodes in the editor, as if the user clicked them (none clears it); 'frame' also turns the user's 3D view to them.",
            """
            {"type":"object","properties":{
              "nodes":{"type":"array","items":{"type":"string"}},"frame":{"type":"boolean"}
            }}
            """,
            Select);
        Bridge.Register("frame",
            "Turn the user's 3D view to nodes (default the selection), as the view's Focus Selection does. Moves the user's view: use it to show them something.",
            """{"type":"object","properties":{"nodes":{"type":"array","items":{"type":"string"}}}}""",
            args => Frame(args["nodes"] as JsonArray));
        Bridge.Register("history",
            "The open scene's undo history as the editor keeps it: each action's name, and which is current (what Ctrl+Z would undo next).",
            """{"type":"object","properties":{"last":{"type":"integer","description":"default 15"}}}""",
            History);
        Bridge.Register("undo",
            "The editor's own Undo, from its menu, as Ctrl+Z: takes back the newest action, the user's or Claude's.",
            """{"type":"object","properties":{}}""",
            _ => PressMenu("Undo"));
        Bridge.Register("redo",
            "The editor's own Redo, from its menu, as Ctrl+Y.",
            """{"type":"object","properties":{}}""",
            _ => PressMenu("Redo"));
        Bridge.Register("batch",
            "Run commands in order as one undo: [{\"command\":\"place\",\"args\":{...}}, ...]. A later command sees what an earlier one made. " +
            "It stops at the first that fails; what ran before stays, and one Ctrl+Z takes it all out.",
            """
            {"type":"object","required":["commands"],"properties":{
              "commands":{"type":"array","items":{"type":"object","required":["command"],"properties":{
                "command":{"type":"string"},"args":{"type":"object"}}}}
            }}
            """,
            Batch);
    }

    // ---------------- Reading ----------------

    private static JsonObject Describe(Node node, bool withBounds = false)
    {
        var d = new JsonObject
        {
            ["path"] = Bridge.PathOf(node),
            ["class"] = ClassOf(node),
        };
        if (!string.IsNullOrEmpty(node.SceneFilePath) && node != Bridge.SceneRoot)
            d["scene"] = node.SceneFilePath;
        if (node is Node3D n3)
        {
            d["position"] = Bridge.Json(n3.GlobalPosition);
            Vector3 r = n3.GlobalRotationDegrees;
            if (!r.IsZeroApprox())
                d["rotation_deg"] = Bridge.Json(r);
            if (!n3.Scale.IsEqualApprox(Vector3.One))
                d["scale"] = Bridge.Json(n3.Scale);
            if (withBounds)
                d["size"] = Bridge.Json(ViewCommands.Bounds(n3).Size);
        }
        return d;
    }

    private static string ClassOf(Node node)
    {
        string script = (node.GetScript().AsGodotObject() as Script)?.ResourcePath;
        return string.IsNullOrEmpty(script) ? node.GetClass() : $"{node.GetClass()} ({script.GetFile().GetBaseName()})";
    }

    // The scene's own nodes under a node, as the Scene dock shows them.
    private static IEnumerable<Node> Children(Node node, Node root) =>
        node.GetChildren().Where(c => c.Owner == root);

    private static JsonNode Tree(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        Node from = Bridge.NodeAt(Bridge.Text(args, "node", "."));
        int depth = Math.Clamp((int)Bridge.Number(args, "depth", 2), 0, 12);
        int left = Math.Clamp((int)Bridge.Number(args, "max", 400), 1, 4000);

        JsonObject Walk(Node node, int level)
        {
            left--;
            JsonObject d = Describe(node);
            var kids = Children(node, root).ToList();
            if (kids.Count == 0)
                return d;
            d["children"] = kids.Count;
            if (level >= depth)
                return d;
            var list = new JsonArray();
            foreach (Node kid in kids)
            {
                if (left <= 0)
                {
                    list.Add($"... {kids.Count - list.Count} more (max reached)");
                    break;
                }
                list.Add(Walk(kid, level + 1));
            }
            d["nodes"] = list;
            return d;
        }

        JsonObject tree = Walk(from, 0);
        tree["scene"] = root.SceneFilePath;
        return tree;
    }

    private static JsonNode Get(JsonObject args)
    {
        Node node = Bridge.NodeAt(Bridge.Text(args, "node") ?? throw new Bridge.Refused("'node' is missing"));
        var result = new JsonObject { ["node"] = Bridge.PathOf(node), ["class"] = ClassOf(node) };
        var values = new JsonObject();
        if (args["properties"] is JsonArray names)
        {
            foreach (string name in names.Select(n => n!.GetValue<string>()))
            {
                if (Info(node, name) == null)
                    throw new Bridge.Refused($"'{Bridge.PathOf(node)}' has no property '{name}'");
                values[name] = Values.ToJson(node.Get(name));
            }
        }
        else
        {
            const int hidden = (int)(PropertyUsageFlags.Category | PropertyUsageFlags.Group | PropertyUsageFlags.Subgroup);
            foreach (Godot.Collections.Dictionary p in node.GetPropertyList())
            {
                int usage = p["usage"].AsInt32();
                if ((usage & (int)PropertyUsageFlags.Editor) == 0 || (usage & hidden) != 0)
                    continue;
                string name = p["name"].AsString();
                values[name] = Values.ToJson(node.Get(name));
            }
            // The Inspector shows rotation through 'rotation', in radians: degrees are what set takes.
            if (node is Node3D n3)
            {
                values["rotation_degrees"] = Bridge.Json(n3.RotationDegrees);
                values["global_position"] = Bridge.Json(n3.GlobalPosition);
            }
        }
        result["properties"] = values;
        return result;
    }

    private static Godot.Collections.Dictionary Info(GodotObject o, string name) =>
        o.GetPropertyList().FirstOrDefault(p => p["name"].AsString() == name);

    // ---------------- Changing ----------------

    private static JsonNode Set(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        var paths = new List<string>();
        if (Bridge.Text(args, "node") is { } one)
            paths.Add(one);
        if (args["nodes"] is JsonArray many)
            paths.AddRange(many.Select(n => n!.GetValue<string>()));
        if (paths.Count == 0)
            throw new Bridge.Refused("give 'node' or 'nodes'");
        List<Node> nodes = paths.Select(Bridge.NodeAt).Distinct().ToList();
        JsonObject properties = args["properties"] as JsonObject ?? new JsonObject();
        bool stand = Bridge.Flag(args, "stand", false);
        if (properties.Count == 0 && !stand)
            throw new Bridge.Refused("nothing to set: give 'properties', or 'stand'");

        // Every value read and checked before anything changes.
        var changes = new List<(Node Node, string Name, Variant Old, Variant New)>();
        foreach (Node node in nodes)
        {
            foreach ((string name, JsonNode value) in properties)
            {
                Godot.Collections.Dictionary info = Info(node, name)
                    ?? throw new Bridge.Refused($"'{Bridge.PathOf(node)}' has no property '{name}'");
                var type = (Variant.Type)info["type"].AsInt32();
                string className = info["class_name"].AsString();
                if (type == Variant.Type.Nil)
                    type = node.Get(name).VariantType;
                changes.Add((node, name, node.Get(name), Values.FromJson(value, type, className, name)));
            }
        }
        if (stand && nodes.Any(n => n is not Node3D))
            throw new Bridge.Refused("only 3D nodes can stand on the ground");

        EditorUndoRedoManager undo = Bridge.BeginAction($"set {string.Join(", ", properties.Select(p => p.Key))} on {string.Join(", ", nodes.Select(n => n.Name))}");
        foreach ((Node node, string name, Variant old, Variant value) in changes)
        {
            node.Set(name, value);
            undo.AddDoProperty(node, name, value);
            undo.AddUndoProperty(node, name, old);
        }
        var stood = new JsonObject();
        if (stand)
        {
            foreach (Node3D node in nodes.Cast<Node3D>())
            {
                Vector3 old = node.Position;
                Vector3 at = node.GlobalPosition;
                MeshRay.Hit? ground = MeshRay.Down(root, at.X, at.Z, exclude: node);
                stood[Bridge.PathOf(node)] = ground != null ? Bridge.PathOf(ground.Value.Mesh) : null;
                if (ground == null)
                    continue;
                node.GlobalPosition = ground.Value.Point;
                undo.AddDoProperty(node, Node3D.PropertyName.Position, node.Position);
                undo.AddUndoProperty(node, Node3D.PropertyName.Position, old);
            }
        }
        Bridge.EndAction(undo);

        var result = new JsonObject();
        foreach (Node node in nodes)
        {
            JsonObject d = Describe(node);
            foreach ((string name, _) in properties)
                d[name] = Values.ToJson(node.Get(name));
            result[Bridge.PathOf(node)] = d;
        }
        if (stand)
            result["stood_on"] = stood;
        return result;
    }

    private static JsonNode Add(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        string type = Bridge.Text(args, "type") ?? throw new Bridge.Refused("'type' is missing");
        if (!ClassDB.ClassExists(type) || !ClassDB.CanInstantiate(type) || !ClassDB.IsParentClass(type, nameof(Node)))
            throw new Bridge.Refused($"'{type}' is not a node class that can be made");
        Node parent = Bridge.NodeAt(Bridge.Text(args, "parent", "Props"));
        var node = (Node)ClassDB.Instantiate(type).AsGodotObject();
        // A script first, so its exports are properties that can be set.
        if (Bridge.Text(args, "script") is string scriptPath)
        {
            var script = ResourceLoader.Exists(scriptPath) ? GD.Load<Script>(scriptPath) : null;
            if (script == null || !ClassDB.IsParentClass(type, script.GetInstanceBaseType()))
            {
                node.Free();
                throw new Bridge.Refused($"'{scriptPath}' is not a script for a {type}");
            }
            node.SetScript(script);
        }

        // Every value read and checked before the node joins the scene.
        var values = new List<(string Name, Variant Value)>();
        if (args["properties"] is JsonObject properties)
        {
            foreach ((string name, JsonNode value) in properties)
            {
                Godot.Collections.Dictionary info = Info(node, name);
                if (info == null)
                {
                    node.Free();
                    throw new Bridge.Refused($"a {type} has no property '{name}'");
                }
                var t = (Variant.Type)info["type"].AsInt32();
                values.Add((name, Values.FromJson(value, t == Variant.Type.Nil ? node.Get(name).VariantType : t, info["class_name"].AsString(), name)));
            }
        }
        foreach ((string name, Variant value) in values)
            node.Set(name, value);
        node.Name = Bridge.Text(args, "name") ?? type;
        parent.AddChild(node, true);
        node.Owner = root;
        if (node is Node3D n3)
        {
            if (Bridge.Vec3(args, "position") is { } at)
                n3.GlobalPosition = at;
            if (Bridge.Vec3(args, "rotation_degrees") is { } r)
                n3.GlobalRotationDegrees = r;
        }

        EditorUndoRedoManager undo = Bridge.BeginAction($"add {node.Name}");
        undo.AddDoMethod(parent, Node.MethodName.AddChild, node, true);
        undo.AddDoProperty(node, Node.PropertyName.Owner, root);
        undo.AddDoReference(node);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, node);
        Bridge.EndAction(undo);
        return Describe(node);
    }

    private static List<Node> Nodes(JsonArray paths) =>
        paths == null ? null : paths.Select(p => Bridge.NodeAt(p!.GetValue<string>())).Distinct().ToList();

    private static async Task<JsonNode> Select(JsonObject args)
    {
        List<Node> nodes = Nodes(args["nodes"] as JsonArray) ?? new List<Node>();
        Choose(nodes);
        var result = new JsonObject { ["selected"] = new JsonArray(nodes.Select(n => (JsonNode)Bridge.PathOf(n)).ToArray()) };
        if (Bridge.Flag(args, "frame", false) && nodes.Count > 0)
            result["frame"] = await Frame(null);
        return result;
    }

    private static void Choose(List<Node> nodes)
    {
        EditorSelection selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        foreach (Node node in nodes)
            selection.AddNode(node);
        if (nodes.Count > 0)
            EditorInterface.Singleton.EditNode(nodes[^1]);
    }

    // The 3D view's own Focus Selection, from its menu: no keystrokes, so
    // nothing can land in a text field the user is typing in.
    private static async Task<JsonNode> Frame(JsonArray paths)
    {
        List<Node> nodes = Nodes(paths);
        if (nodes != null)
            Choose(nodes);
        if (EditorInterface.Singleton.GetSelection().GetSelectedNodes().Count == 0)
            throw new Bridge.Refused("nothing is selected to frame");

        EditorInterface.Singleton.SetMainScreenEditor("3D");
        SubViewport view = EditorInterface.Singleton.GetEditorViewport3D(0);
        Node viewport = view;
        while (viewport != null && viewport.GetClass() != "Node3DEditorViewport")
            viewport = viewport.GetParent();
        if (viewport == null)
            throw new Bridge.Refused("the 3D view's controls were not found");

        PopupMenu menu = null;
        int id = -1;
        var seen = new List<string>();
        foreach (MenuButton button in viewport.FindChildren("*", nameof(MenuButton), true, false).OfType<MenuButton>())
        {
            PopupMenu popup = button.GetPopup();
            for (int i = 0; i < popup.ItemCount && id < 0; i++)
            {
                string text = popup.GetItemText(i);
                seen.Add(text);
                if (text.Contains("Focus Selection", StringComparison.OrdinalIgnoreCase))
                {
                    menu = popup;
                    id = popup.GetItemId(i);
                }
            }
        }
        if (menu == null)
            throw new Bridge.Refused("no Focus Selection in the 3D view's menus; found: " + string.Join(", ", seen.Where(s => s.Length > 0).Take(40)));

        await Bridge.Drawn();
        Vector3 before = view.GetCamera3D().GlobalPosition;
        menu.EmitSignal(PopupMenu.SignalName.IdPressed, id);
        // The view glides there over a few frames.
        for (int i = 0; i < 40; i++)
            await Bridge.Drawn(1);
        Vector3 after = view.GetCamera3D().GlobalPosition;
        return new JsonObject
        {
            ["framed"] = new JsonArray(EditorInterface.Singleton.GetSelection().GetSelectedNodes().Select(n => (JsonNode)Bridge.PathOf(n)).ToArray()),
            ["camera_moved_m"] = Math.Round(before.DistanceTo(after), 2),
            ["camera"] = Bridge.Json(after),
        };
    }

    private static JsonNode History(JsonObject args)
    {
        EditorUndoRedoManager manager = EditorInterface.Singleton.GetEditorUndoRedo();
        int id = manager.GetObjectHistoryId(Bridge.SceneRoot);
        UndoRedo undo = manager.GetHistoryUndoRedo(id);
        int last = Math.Clamp((int)Bridge.Number(args, "last", 15), 1, 200);
        int count = undo.GetHistoryCount();
        int current = undo.GetCurrentAction();
        var actions = new JsonArray();
        for (int i = Math.Max(0, count - last); i < count; i++)
            actions.Add(new JsonObject { ["index"] = i, ["name"] = undo.GetActionName(i), ["current"] = i == current });
        return new JsonObject
        {
            ["history_id"] = id,
            ["count"] = count,
            ["current"] = current,
            ["current_name"] = undo.GetCurrentActionName(),
            ["committing"] = manager.IsCommittingAction(),
            ["actions"] = actions,
        };
    }

    // An item of the editor's own menus, pressed: the same path as its shortcut.
    private static JsonNode PressMenu(string label)
    {
        int before = EditorInterface.Singleton.GetEditorUndoRedo()
            .GetHistoryUndoRedo(EditorInterface.Singleton.GetEditorUndoRedo().GetObjectHistoryId(Bridge.SceneRoot)).GetCurrentAction();
        foreach (PopupMenu popup in EditorInterface.Singleton.GetBaseControl().FindChildren("*", nameof(PopupMenu), true, false).OfType<PopupMenu>())
        {
            for (int i = 0; i < popup.ItemCount; i++)
            {
                if (popup.GetItemText(i) != label)
                    continue;
                popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(i));
                JsonObject history = (JsonObject)History(new JsonObject { ["last"] = 3 });
                history["pressed"] = $"{label} (menu item {popup.GetItemId(i)} of '{popup.Name}')";
                history["was_current"] = before;
                return history;
            }
        }
        throw new Bridge.Refused($"no '{label}' in the editor's menus");
    }

    private static async Task<JsonNode> Batch(JsonObject args)
    {
        if (args["commands"] is not JsonArray commands || commands.Count == 0)
            throw new Bridge.Refused("'commands' wants a list of {command, args}");
        var steps = new List<(Bridge.Command Command, JsonObject Args)>();
        foreach (JsonNode c in commands)
        {
            string name = (c as JsonObject)?["command"]?.GetValue<string>() ?? throw new Bridge.Refused("each step needs a 'command'");
            if (!Bridge.TryGet(name, out Bridge.Command command))
                throw new Bridge.Refused($"no command '{name}'");
            if (name == "batch")
                throw new Bridge.Refused("a batch cannot hold a batch");
            steps.Add((command, (c["args"] as JsonObject)?.DeepClone().AsObject() ?? new JsonObject()));
        }
        return await Bridge.Batch(async () =>
        {
            var results = new JsonArray();
            foreach ((Bridge.Command command, JsonObject a) in steps)
            {
                try
                {
                    JsonNode r = await command.Run(a);
                    if (r is JsonObject o && o.Remove("image_png"))
                        o["image"] = "left out: run view on its own to see it";
                    results.Add(new JsonObject { ["command"] = command.Name, ["result"] = r });
                }
                catch (Exception e)
                {
                    if (e is not Bridge.Refused)
                        GD.PushError($"[bridge] batch {command.Name}: {e}");
                    return new JsonObject
                    {
                        ["done"] = results.Count,
                        ["stopped_at"] = results.Count,
                        ["error"] = $"{command.Name}: {e.Message}",
                        ["note"] = "what ran before stays; one Ctrl+Z takes it out",
                        ["results"] = results,
                    };
                }
            }
            return new JsonObject { ["done"] = results.Count, ["results"] = results };
        });
    }
}
#endif
