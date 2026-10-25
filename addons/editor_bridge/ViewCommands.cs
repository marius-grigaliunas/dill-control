#if TOOLS
using System;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// Seeing: <c>view</c> (the user's 3D view, or one Claude sets, through a
/// camera of its own so the user's view never moves) and <c>here</c> (the
/// point under the middle of the user's view).
/// </summary>
public static class ViewCommands
{
    public static void Register()
    {
        Bridge.Register("view",
            "A picture of the editor's 3D view of the open scene. With no arguments, the user's own view, as they see it, and where its camera is. " +
            "With 'from' and 'at' (world positions) or 'node' (a path from the scene's root), Claude's own camera renders the same world, and the user's view does not move.",
            """
            {"type":"object","properties":{
              "from":{"type":"array","items":{"type":"number"},"description":"camera position [x, y, z]"},
              "at":{"type":"array","items":{"type":"number"},"description":"the point looked at [x, y, z]"},
              "node":{"type":"string","description":"look at this node, from 'from' or from a little above and to one side"},
              "fov":{"type":"number","description":"degrees, vertical; default 70"},
              "width":{"type":"integer","description":"pixels, at most 1920; default 1280"},
              "fog":{"type":"boolean","description":"Claude's own camera only: false draws without the level's fog, for an overview; default true"}
            }}
            """,
            View);
        Bridge.Register("here",
            "The point under the middle of the user's 3D view: the nearest mesh it hits, its normal and its node. What 'put it here' means.",
            """{"type":"object","properties":{}}""",
            _ => Here());
    }

    private static SubViewport EditorViewport() =>
        EditorInterface.Singleton.GetEditorViewport3D(0) ?? throw new Bridge.Refused("the editor has no 3D viewport");

    private static async Task<JsonNode> View(JsonObject args)
    {
        Node root = Bridge.SceneRoot;
        int width = Math.Clamp((int)Bridge.Number(args, "width", 1280), 64, 1920);
        Vector3? from = Bridge.Vec3(args, "from");
        Vector3? at = Bridge.Vec3(args, "at");
        string nodePath = Bridge.Text(args, "node");

        if (from == null && at == null && nodePath == null)
            return await UserView(root, width);

        if (nodePath != null)
        {
            Node3D node = Bridge.NodeAt(nodePath) as Node3D ?? throw new Bridge.Refused($"'{nodePath}' is not a 3D node");
            Aabb box = Bounds(node);
            at ??= box.GetCenter();
            float reach = Mathf.Max(box.Size.Length(), 4.0f);
            from ??= at.Value + (new Vector3(0.7f, 0.45f, 0.7f).Normalized() * reach * 1.1f);
        }
        if (from == null || at == null)
            throw new Bridge.Refused("give both 'from' and 'at', or a 'node'");
        return await OwnView(root, from.Value, at.Value, (float)Bridge.Number(args, "fov", 70.0), width, Bridge.Flag(args, "fog", true));
    }

    private static async Task<JsonNode> UserView(Node root, int width)
    {
        SubViewport viewport = EditorViewport();
        bool shown = (viewport.GetParent() as Control)?.IsVisibleInTree() ?? viewport.GetParent() is Node3D;
        await Bridge.Drawn();
        Camera3D camera = viewport.GetCamera3D();
        JsonObject result = Picture(viewport.GetTexture().GetImage(), width);
        result["scene"] = root.SceneFilePath;
        result["camera"] = Camera(camera);
        if (!shown)
            result["note"] = "the 3D view is not on screen (another main screen is open): this is the last picture it drew";
        return result;
    }

    private static async Task<JsonNode> OwnView(Node root, Vector3 from, Vector3 at, float fov, int width, bool fog = true)
    {
        if (from.IsEqualApprox(at))
            throw new Bridge.Refused("'from' and 'at' are the same point");
        var viewport = new SubViewport
        {
            World3D = EditorViewport().FindWorld3D(),
            Size = new Vector2I(width, width * 9 / 16),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa2X,
        };
        Vector3 forward = (at - from).Normalized();
        Vector3 up = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
        var camera = new Camera3D
        {
            Fov = Mathf.Clamp(fov, 5.0f, 150.0f),
            Far = 20000.0f,
            Transform = new Transform3D(Basis.LookingAt(forward, up), from),
            Current = true,
        };
        // Without fog: a copy of the level's environment on this camera only.
        if (!fog && viewport.World3D?.Environment is Godot.Environment environment)
        {
            var clear = (Godot.Environment)environment.Duplicate();
            clear.FogEnabled = false;
            clear.VolumetricFogEnabled = false;
            camera.Environment = clear;
        }
        viewport.AddChild(camera);
        EditorInterface.Singleton.GetBaseControl().AddChild(viewport);
        try
        {
            await Bridge.Drawn(3);
            JsonObject result = Picture(viewport.GetTexture().GetImage(), width);
            result["scene"] = root.SceneFilePath;
            result["camera"] = Camera(camera);
            return result;
        }
        finally
        {
            viewport.QueueFree();
        }
    }

    private static JsonNode Here()
    {
        Node root = Bridge.SceneRoot;
        Camera3D camera = EditorViewport().GetCamera3D() ?? throw new Bridge.Refused("the 3D view has no camera");
        Vector3 from = camera.GlobalPosition;
        Vector3 forward = -camera.GlobalBasis.Z;
        MeshRay.Hit? hit = MeshRay.Cast(root, from, forward);
        var result = new JsonObject { ["camera"] = Camera(camera) };
        if (hit == null)
        {
            result["hit"] = false;
            return result;
        }
        result["hit"] = true;
        result["point"] = Bridge.Json(hit.Value.Point);
        result["normal"] = Bridge.Json(hit.Value.Normal);
        result["node"] = Bridge.PathOf(hit.Value.Mesh);
        result["distance"] = Math.Round(hit.Value.Distance, 2);
        return result;
    }

    // The width asked for at most, keeping the shape; PNG, for the MCP server to show.
    private static JsonObject Picture(Image image, int width)
    {
        if (image == null || image.IsEmpty())
            throw new Bridge.Refused("the view has drawn nothing yet");
        if (image.GetWidth() > width)
            image.Resize(width, Mathf.Max(1, image.GetHeight() * width / image.GetWidth()), Image.Interpolation.Lanczos);
        return new JsonObject
        {
            ["image_png"] = Convert.ToBase64String(image.SavePngToBuffer()),
            ["size"] = new JsonArray(image.GetWidth(), image.GetHeight()),
        };
    }

    private static JsonObject Camera(Camera3D camera) => camera == null ? null : new JsonObject
    {
        ["position"] = Bridge.Json(camera.GlobalPosition),
        ["forward"] = Bridge.Json(-camera.GlobalBasis.Z),
        ["fov"] = Math.Round(camera.Fov, 1),
    };

    // Everything drawn under a node, in world space.
    public static Aabb Bounds(Node3D node)
    {
        Aabb? box = null;
        foreach (Node child in node.FindChildren("*", nameof(VisualInstance3D), true, false))
        {
            if (child is not VisualInstance3D visual || !visual.IsVisibleInTree())
                continue;
            Aabb b = visual.GlobalTransform * visual.GetAabb();
            box = box == null ? b : box.Value.Merge(b);
        }
        if (node is VisualInstance3D self)
        {
            Aabb b = self.GlobalTransform * self.GetAabb();
            box = box == null ? b : box.Value.Merge(b);
        }
        return box ?? new Aabb(node.GlobalPosition - Vector3.One, Vector3.One * 2.0f);
    }
}
#endif
