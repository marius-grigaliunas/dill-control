using System.Collections.Generic;
using Godot;
using DillControl.Voxel;

namespace DillControl.Debug;

/// <summary>
/// Ways to look at destruction through its dust, chosen by words after
/// <c>--</c> on the command line, with any bench mode:
/// <c>dust=solid|lens|shell|lensshell|thin|near</c> swaps the experimental
/// <c>fx_toon_puff_seethrough</c> onto every dust cloud (its uniforms keep
/// their defaults unless given as <c>dust.lens_min=0.2</c> and the like);
/// <c>wire</c> draws everything as wireframe; <c>boxes</c> draws every voxel
/// collision box as lines over everything (loose pieces orange, buildings and
/// heaps green): Godot's own collision view draws only CollisionShape3D nodes,
/// and voxel boxes are shape owners. <c>nodust</c> is <see cref="SinkProbe"/>'s. Not part of the game:
/// <see cref="PerfDriver"/> adds it only when asked.
/// </summary>
public partial class DustLab : Node
{
    private const string ShaderPath = "res://shaders/fx_toon_puff_seethrough.gdshader";

    private static readonly Dictionary<string, int> Modes = new()
    {
        ["solid"] = 0, ["lens"] = 1, ["shell"] = 2, ["lensshell"] = 3, ["thin"] = 4, ["near"] = 5,
    };

    private int _mode = -1;
    private bool _wire;
    private readonly Dictionary<string, float> _params = new();
    private bool _swapped;
    private ImmediateMesh _boxes;
    private readonly List<CollisionObject3D> _bodies = new();
    private int _scan;

    public static bool Wanted(string[] args)
    {
        foreach (string a in args)
        {
            if (a.StartsWith("dust=") || a.StartsWith("dust.") || a == "wire" || a == "boxes")
            {
                return true;
            }
        }
        return false;
    }

    public override void _EnterTree()
    {
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("dust=") && Modes.TryGetValue(a.Substring(5), out int mode))
            {
                _mode = mode;
            }
            else if (a.StartsWith("dust.") && a.Contains('='))
            {
                string[] kv = a.Substring(5).Split('=');
                _params[kv[0]] = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (a == "wire")
            {
                _wire = true;
            }
            else if (a == "boxes")
            {
                _boxes = new ImmediateMesh();
                var material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    VertexColorUseAsAlbedo = true,
                    NoDepthTest = true,
                    RenderPriority = 127,
                };
                AddChild(new MeshInstance3D
                {
                    Name = "Boxes", Mesh = _boxes, MaterialOverride = material, TopLevel = true,
                    ExtraCullMargin = 16384.0f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
        if (_wire)
        {
            // Before more meshes are made: only those made after this have wireframes.
            RenderingServer.SetDebugGenerateWireframes(true);
        }
        GD.Print($"[dustlab] mode={_mode} wire={_wire} params={_params.Count}");
    }

    public override void _Process(double delta)
    {
        DrawBoxes();
        if (_wire && GetViewport().DebugDraw != Viewport.DebugDrawEnum.Wireframe)
        {
            GetViewport().DebugDraw = Viewport.DebugDrawEnum.Wireframe;
        }
        if (_swapped || _mode < 0 || VoxelDebris.Current?.GetNodeOrNull("Dust") is not Node dust
            || dust.GetChildCount() == 0)
        {
            return;
        }
        // The same material each cloud slot already has, with the experiment's
        // shader: DustField keeps setting its tint and never knows.
        var shader = GD.Load<Shader>(ShaderPath);
        foreach (Node child in dust.GetChildren())
        {
            if (child is GpuParticles3D { MaterialOverride: ShaderMaterial material } particles)
            {
                material.Shader = shader;
                // After the ink quad (ToonPass, the last transparent by
                // sorting): the ink never sees the dust, which inks itself.
                material.RenderPriority = 10;
                material.SetShaderParameter("see_mode", _mode);
                particles.DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth;
                foreach ((string name, float value) in _params)
                {
                    material.SetShaderParameter(name, value);
                }
            }
        }
        _swapped = true;
    }

    private void DrawBoxes()
    {
        if (_boxes == null)
        {
            return;
        }
        // The tree is walked twice a second for bodies; their boxes every frame.
        if (_scan-- <= 0)
        {
            _scan = 30;
            _bodies.Clear();
            Collect(GetTree().Root);
        }
        _boxes.ClearSurfaces();
        bool open = false;
        foreach (CollisionObject3D body in _bodies)
        {
            if (!IsInstanceValid(body) || !body.IsInsideTree())
            {
                continue;
            }
            Color colour = body is VoxelChunk ? new Color(1.0f, 0.55f, 0.1f) : new Color(0.2f, 1.0f, 0.35f);
            // From the physics server, not the node: a building's sections add
            // their boxes by RID, which the shape-owner API never sees. This
            // is what Jolt collides with.
            Rid rid = body.GetRid();
            if (!PhysicsServer3D.BodyGetSpace(rid).IsValid)
            {
                continue;
            }
            Transform3D at = PhysicsServer3D.BodyGetState(rid, PhysicsServer3D.BodyState.Transform).AsTransform3D();
            int count = PhysicsServer3D.BodyGetShapeCount(rid);
            for (int i = 0; i < count; i++)
            {
                Rid shape = PhysicsServer3D.BodyGetShape(rid, i);
                if (PhysicsServer3D.ShapeGetType(shape) != PhysicsServer3D.ShapeType.Box)
                {
                    continue;
                }
                if (!open)
                {
                    _boxes.SurfaceBegin(Mesh.PrimitiveType.Lines);
                    open = true;
                }
                Vector3 half = PhysicsServer3D.ShapeGetData(shape).AsVector3();
                Box(at * PhysicsServer3D.BodyGetShapeTransform(rid, i), half, colour);
            }
        }
        if (open)
        {
            _boxes.SurfaceEnd();
        }
    }

    private void Box(Transform3D t, Vector3 h, Color colour)
    {
        for (int e = 0; e < 12; e++)
        {
            // The twelve edges: along each axis, at the four corners across it.
            int axis = e / 4, corner = e % 4;
            Vector3 a = new(), b;
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            a[axis] = -h[axis];
            a[u] = (corner & 1) == 0 ? -h[u] : h[u];
            a[v] = (corner & 2) == 0 ? -h[v] : h[v];
            b = a;
            b[axis] = h[axis];
            _boxes.SurfaceSetColor(colour);
            _boxes.SurfaceAddVertex(t * a);
            _boxes.SurfaceSetColor(colour);
            _boxes.SurfaceAddVertex(t * b);
        }
    }

    private void Collect(Node node)
    {
        if (node is VoxelChunk or VoxelCollider || (node is StaticBody3D && node.GetParent() is RubblePile))
        {
            _bodies.Add((CollisionObject3D)node);
        }
        foreach (Node child in node.GetChildren())
        {
            Collect(child);
        }
    }
}
