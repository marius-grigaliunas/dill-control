#if TOOLS
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Making: <c>place</c> instances a real scene into the open scene, its
/// origin stood on whatever is drawn under it, as one undo.
/// </summary>
public static class PlaceCommands
{
    private const string PropsFolder = "res://scenes/props";

    public static void Register()
    {
        Bridge.Register("place",
            "Instance a real scene (a prop, tree, vine or cluster) into the open scene, as the same node the user would drag in. " +
            "Its origin stands on the highest mesh under it unless 'stand' is false; trees and vines go under Foliage, everything else under Props, unless 'parent' says. " +
            "One Ctrl+Z takes it out. The new node is selected.",
            """
            {"type":"object","required":["scene","at"],"properties":{
              "scene":{"type":"string","description":"res:// path to a .tscn, or a name in scenes/props such as prp_tree_tall_a"},
              "at":{"description":"[x, z] or [x, y, z] in world space, or \"here\" for the point under the middle of the user's view","oneOf":[{"type":"array","items":{"type":"number"}},{"type":"string","enum":["here"]}]},
              "yaw":{"type":"number","description":"degrees about up; default 0"},
              "stand":{"type":"boolean","description":"stand on what is under it; default true"},
              "parent":{"type":"string","description":"path from the scene's root"},
              "name":{"type":"string","description":"PascalCase; default from the file, prp_tree_tall_a -> TreeTallA"}
            }}
            """,
            Place);
        Bridge.Register("delete",
            "Take nodes out of the open scene, as one undo. Never the scene's root.",
            """
            {"type":"object","required":["nodes"],"properties":{
              "nodes":{"type":"array","items":{"type":"string"},"description":"paths from the scene's root"}
            }}
            """,
            Delete);
    }

    private static JsonNode Delete(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        if (args["nodes"] is not JsonArray paths || paths.Count == 0)
            throw new Bridge.Refused("'nodes' wants a list of paths");
        var nodes = paths.Select(p => Bridge.NodeAt(p!.GetValue<string>())).Distinct().ToList();
        if (nodes.Contains(root))
            throw new Bridge.Refused("the scene's root cannot be deleted");
        // A node under another being deleted goes with it.
        nodes.RemoveAll(n => nodes.Any(other => other != n && other.IsAncestorOf(n)));

        EditorUndoRedoManager undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction($"Bridge: delete {string.Join(", ", nodes.Select(n => n.Name))}", UndoRedo.MergeMode.Disable, root);
        var deleted = new JsonArray();
        foreach (Node node in nodes)
        {
            Node parent = node.GetParent();
            deleted.Add(Bridge.PathOf(node));
            undo.AddDoMethod(parent, Node.MethodName.RemoveChild, node);
            undo.AddUndoMethod(parent, Node.MethodName.AddChild, node, true);
            undo.AddUndoMethod(parent, Node.MethodName.MoveChild, node, node.GetIndex());
            // Out of the tree a node loses its owner; the undo gives it back to all the scene owned.
            foreach (Node owned in new[] { node }.Concat(node.FindChildren("*", "", true, true)).Where(n => n.Owner == root))
                undo.AddUndoProperty(owned, Node.PropertyName.Owner, root);
            undo.AddUndoReference(node);
        }
        undo.CommitAction();
        EditorInterface.Singleton.GetSelection().Clear();
        return new JsonObject { ["deleted"] = deleted };
    }

    private static JsonNode Place(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        string path = ScenePath(Bridge.Text(args, "scene") ?? throw new Bridge.Refused("'scene' is missing"));
        PackedScene packed = ResourceLoader.Load<PackedScene>(path) ?? throw new Bridge.Refused($"'{path}' did not load as a scene");
        if (path == root.SceneFilePath)
            throw new Bridge.Refused("a scene cannot be placed inside itself");

        Vector3 at = Where(root, args["at"]);
        bool stand = Bridge.Flag(args, "stand", true);
        Node stoodOn = null;
        if (stand)
        {
            MeshRay.Hit? ground = MeshRay.Down(root, at.X, at.Z);
            if (ground != null)
            {
                at = ground.Value.Point;
                stoodOn = ground.Value.Mesh;
            }
        }

        string parentPath = Bridge.Text(args, "parent");
        Node parent = parentPath != null ? Bridge.NodeAt(parentPath) : DefaultParent(root, path);
        if (parent is not Node3D && parent != root)
            throw new Bridge.Refused($"'{Bridge.PathOf(parent)}' is not a 3D node");

        Node instance = packed.Instantiate(PackedScene.GenEditState.Instance);
        if (instance is not Node3D node)
        {
            instance.Free();
            throw new Bridge.Refused($"'{path}' is not a 3D scene");
        }
        node.Name = Bridge.Text(args, "name") ?? NameOf(path);
        var world = new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad((float)Bridge.Number(args, "yaw", 0.0))), at);
        node.Transform = parent is Node3D p3 ? p3.GlobalTransform.AffineInverse() * world : world;

        EditorUndoRedoManager undo = EditorInterface.Singleton.GetEditorUndoRedo();
        undo.CreateAction($"Bridge: place {node.Name}", UndoRedo.MergeMode.Disable, root);
        undo.AddDoMethod(parent, Node.MethodName.AddChild, node, true);
        undo.AddDoProperty(node, Node.PropertyName.Owner, root);
        undo.AddDoReference(node);
        undo.AddUndoMethod(parent, Node.MethodName.RemoveChild, node);
        undo.CommitAction();

        EditorSelection selection = EditorInterface.Singleton.GetSelection();
        selection.Clear();
        selection.AddNode(node);

        return new JsonObject
        {
            ["node"] = Bridge.PathOf(node),
            ["scene"] = path,
            ["position"] = Bridge.Json(node.GlobalPosition),
            ["stood_on"] = stoodOn != null ? Bridge.PathOf(stoodOn) : null,
            ["note"] = stand && stoodOn == null ? "nothing drawn under it: left at the height given" : null,
        };
    }

    private static Vector3 Where(Node root, JsonNode at)
    {
        if (at is JsonValue v && v.TryGetValue(out string word))
        {
            if (word != "here")
                throw new Bridge.Refused("'at' is [x, z], [x, y, z] or \"here\"");
            Camera3D camera = EditorInterface.Singleton.GetEditorViewport3D(0)?.GetCamera3D()
                ?? throw new Bridge.Refused("the 3D view has no camera");
            MeshRay.Hit? hit = MeshRay.Cast(root, camera.GlobalPosition, -camera.GlobalBasis.Z);
            return hit?.Point ?? throw new Bridge.Refused("the middle of the user's view is on nothing");
        }
        if (at is not JsonArray a || (a.Count != 2 && a.Count != 3))
            throw new Bridge.Refused("'at' is [x, z], [x, y, z] or \"here\"");
        float[] n = a.Select(e => (float)e!.GetValue<double>()).ToArray();
        return n.Length == 2 ? new Vector3(n[0], 0.0f, n[1]) : new Vector3(n[0], n[1], n[2]);
    }

    private static string ScenePath(string scene)
    {
        if (scene.StartsWith("res://"))
            return scene.EndsWith(".tscn") || scene.EndsWith(".scn") ? scene : scene + ".tscn";
        string path = $"{PropsFolder}/{scene.TrimEnd('/')}{(scene.EndsWith(".tscn") ? "" : ".tscn")}";
        return ResourceLoader.Exists(path) ? path : throw new Bridge.Refused($"no scene '{scene}' in {PropsFolder}");
    }

    // The level tree's branches (CLAUDE.md): plants under Foliage, the rest under Props.
    private static Node DefaultParent(Node root, string path)
    {
        string file = path.GetFile();
        string branch = file.StartsWith("prp_tree") || file.StartsWith("prp_vine") ? "Foliage" : "Props";
        return root.GetNodeOrNull(branch) ?? root;
    }

    // prp_tree_tall_a.tscn -> TreeTallA
    private static string NameOf(string path)
    {
        string stem = path.GetFile().GetBaseName();
        foreach (string prefix in new[] { "prp_", "nme_", "shp_", "mch_", "vxl_" })
        {
            if (stem.StartsWith(prefix))
            {
                stem = stem[prefix.Length..];
                break;
            }
        }
        var name = new StringBuilder();
        foreach (string part in stem.Split('_', System.StringSplitOptions.RemoveEmptyEntries))
            name.Append(char.ToUpperInvariant(part[0])).Append(part[1..]);
        return name.Length > 0 ? name.ToString() : "Placed";
    }
}
#endif
