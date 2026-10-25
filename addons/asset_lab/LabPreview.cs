#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DillControl.Planet;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's 3D view (docs/decisions/0047): a world of its own with
/// ground, sky, a sun, the game's ink and one asset at rest. Drag to orbit,
/// wheel to zoom, middle-drag to pan. The asset's scripts do not run here
/// (they are not tools), so it is shown as built; what needs physics is
/// judged in the lab level.
///
/// The damage map (0046) is drawn over a mech: each part's centre of mass
/// (white), the line to its parent, its soles (green) and tips (orange) as
/// boxes, the cells that hold it on (yellow), and the whole's centre of
/// mass (magenta) with its drop to the ground.
/// </summary>
[Tool]
public partial class LabPreview : SubViewportContainer
{
    public Node3D Asset { get; private set; }
    public Aabb Bounds { get; private set; }

    public float Yaw = 0.6f;
    public float Pitch = 0.35f;
    public float Distance = 12.0f;
    public Vector3 Target;

    private SubViewport _viewport;
    private Camera3D _camera;
    private MeshInstance3D _ink;
    private DirectionalLight3D _sun;
    private WorldEnvironment _world;
    private MeshInstance3D _overlay;
    private bool _damageMap = true;
    private float _sunElevation = 45.0f;

    public override void _Ready()
    {
        Stretch = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;

        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X, RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible };
        AddChild(_viewport);

        _world = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        } };
        _viewport.AddChild(_world);

        _sun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 200.0f };
        _viewport.AddChild(_sun);

        var ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(400.0f, 400.0f) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.38f, 0.37f, 0.34f), Roughness = 1.0f },
        };
        _viewport.AddChild(ground);

        _camera = new Camera3D { Fov = 60.0f, Far = 2000.0f, Current = true };
        _viewport.AddChild(_camera);
        _ink = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2.0f, 2.0f) },
            MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/toon_ink.gdshader") },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384.0f,
            SortingOffset = 1000.0f,
        };
        _camera.AddChild(_ink);

        _overlay = new MeshInstance3D
        {
            Mesh = new ImmediateMesh(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                NoDepthTest = true,
                RenderPriority = 10,
            },
        };
        _viewport.AddChild(_overlay);

        SetLook(null);
        PlaceCamera();
    }

    /// <summary>Shows one asset, framed. Null clears the view.</summary>
    public void Show(PackedScene scene)
    {
        if (Asset != null && IsInstanceValid(Asset))
        {
            Asset.QueueFree();
        }
        Asset = null;
        if (scene != null)
        {
            Node made = scene.Instantiate();
            Asset = made as Node3D ?? new Node3D { Name = "Asset" };
            if (Asset != made)
            {
                Asset.AddChild(made);
            }
            _viewport.AddChild(Asset);
            // A wrapper's own cameras and screen hints are for the game.
            foreach (Node node in Asset.FindChildren("*", "", true, false))
            {
                if (node is Camera3D camera)
                {
                    camera.Current = false;
                }
                else if (node is CanvasLayer layer)
                {
                    layer.Visible = false;
                }
                else if (node is Label3D label)
                {
                    label.Visible = false;
                }
            }
            _camera.Current = true;
            Bounds = DrawnBounds(Asset);
            Frame();
        }
        DrawOverlay();
    }

    /// <summary>Everything drawn under a node that is visible, in world space.</summary>
    public static Aabb DrawnBounds(Node3D node)
    {
        Aabb? box = null;
        foreach (Node child in node.FindChildren("*", nameof(VisualInstance3D), true, false))
        {
            if (child is not VisualInstance3D visual || !visual.IsVisibleInTree() || visual is Label3D)
            {
                continue;
            }
            Aabb world = visual.GlobalTransform * visual.GetAabb();
            box = box?.Merge(world) ?? world;
        }
        return box ?? new Aabb(node.GlobalPosition - Vector3.One, Vector3.One * 2.0f);
    }

    /// <summary>Back to a view of the whole asset from the front quarter.</summary>
    public void Frame()
    {
        Target = Bounds.GetCenter();
        Distance = Mathf.Max(2.0f, Bounds.Size.Length() * 1.1f);
        Yaw = 0.6f;
        Pitch = 0.3f;
        PlaceCamera();
    }

    public void PlaceCamera()
    {
        if (_camera == null)
        {
            return;
        }
        Pitch = Mathf.Clamp(Pitch, -0.2f, 1.5f);
        Distance = Mathf.Clamp(Distance, 0.5f, 1500.0f);
        var offset = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch));
        _camera.Position = Target + offset * Distance;
        _camera.LookAt(Target, Vector3.Up);
    }

    public bool Ink
    {
        get => _ink.Visible;
        set => _ink.Visible = value;
    }

    public bool DamageMap
    {
        get => _damageMap;
        set
        {
            _damageMap = value;
            DrawOverlay();
        }
    }

    /// <summary>The sun's height over the horizon, in degrees; it shines
    /// from the front left, over the camera's shoulder at the start.</summary>
    public float SunElevation
    {
        get => _sunElevation;
        set
        {
            _sunElevation = Mathf.Clamp(value, 2.0f, 90.0f);
            _sun.Rotation = new Vector3(-Mathf.DegToRad(_sunElevation), Mathf.DegToRad(35.0f), 0.0f);
        }
    }

    /// <summary>A planet's sky and sun (its colours, not its rolled angle),
    /// or the plain lab light for null.</summary>
    public void SetLook(PlanetBiome biome)
    {
        var sky = (ProceduralSkyMaterial)_world.Environment.Sky.SkyMaterial;
        if (biome == null)
        {
            sky.SkyTopColor = new Color(0.36f, 0.52f, 0.76f);
            sky.SkyHorizonColor = new Color(0.72f, 0.78f, 0.84f);
            sky.GroundHorizonColor = sky.SkyHorizonColor;
            sky.GroundBottomColor = new Color(0.3f, 0.28f, 0.25f);
            _sun.LightColor = Colors.White;
            _sun.LightEnergy = 1.0f;
        }
        else
        {
            biome.Apply(1, Vector3.Up, null, _world.Environment, _sun);
        }
        SunElevation = _sunElevation;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) != 0 || (motion.ButtonMask & MouseButtonMask.Right) != 0)
            {
                Yaw -= motion.Relative.X * 0.008f;
                Pitch += motion.Relative.Y * 0.008f;
                PlaceCamera();
                AcceptEvent();
            }
            else if ((motion.ButtonMask & MouseButtonMask.Middle) != 0)
            {
                Basis basis = _camera.GlobalBasis;
                Target += (-basis.X * motion.Relative.X + basis.Y * motion.Relative.Y) * Distance * 0.0015f;
                PlaceCamera();
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown)
            {
                Distance *= button.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f;
                PlaceCamera();
                AcceptEvent();
            }
        }
    }

    public Camera3D Camera => _camera;

    /// <summary>What the view's camera sees, drawn at a size of its own into
    /// the same world, so it works while the tab is hidden or was never shown.</summary>
    public async Task<Image> Capture(Vector2I size)
    {
        var viewport = new SubViewport { World3D = _viewport.FindWorld3D(), Size = size, Msaa3D = Viewport.Msaa.Msaa2X, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        var camera = new Camera3D { Fov = _camera.Fov, Far = _camera.Far, Transform = _camera.GlobalTransform, Current = true };
        if (_ink.Visible)
        {
            camera.AddChild(_ink.Duplicate());
        }
        viewport.AddChild(camera);
        EditorInterface.Singleton.GetBaseControl().AddChild(viewport);
        try
        {
            await Bridge.Drawn(3);
            return viewport.GetTexture().GetImage();
        }
        finally
        {
            viewport.QueueFree();
        }
    }

    /// <summary>The parts of the asset that carry a damage map, with their bakes.</summary>
    public List<(Node3D Node, DamageBake Bake)> DamageParts()
    {
        var parts = new List<(Node3D, DamageBake)>();
        if (Asset == null)
        {
            return parts;
        }
        foreach (Node node in Asset.FindChildren("*", "", true, false))
        {
            if (node is Node3D part && VoxDamageMap.TryRead(part) is DamageBake bake)
            {
                parts.Add((part, bake));
            }
        }
        return parts;
    }

    private void DrawOverlay()
    {
        var mesh = (ImmediateMesh)_overlay.Mesh;
        mesh.ClearSurfaces();
        List<(Node3D Node, DamageBake Bake)> parts = DamageParts();
        if (!_damageMap || parts.Count == 0)
        {
            return;
        }
        var byName = parts.ToDictionary(p => p.Node.Name.ToString(), p => p);
        float voxel = VoxelSizeOf(parts[0].Node);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        Vector3 weighted = Vector3.Zero;
        int total = 0;
        foreach ((Node3D node, DamageBake bake) in parts)
        {
            Transform3D t = node.GlobalTransform;
            Vector3 centre = t * bake.Centre;
            weighted += centre * bake.Voxels;
            total += bake.Voxels;
            Cross(mesh, centre, voxel * 2.0f, Colors.White);
            if (byName.TryGetValue(bake.Parent, out var parent))
            {
                Line(mesh, centre, parent.Node.GlobalTransform * parent.Bake.Centre, new Color(1, 1, 1, 0.5f));
            }
            for (int k = 0; k < bake.ContactKinds.Length; k++)
            {
                Box(mesh, t, bake.ContactCentre[k], bake.ContactSize[k],
                    bake.ContactKinds[k] == ContactKind.Sole ? new Color(0.3f, 1.0f, 0.3f) : new Color(1.0f, 0.6f, 0.2f));
            }
            Vector3I dims = node.GetMeta(VoxelMeta.Dims).AsVector3I();
            Vector3 origin = node.GetMeta(VoxelMeta.Origin).AsVector3();
            foreach (int i in bake.Hold)
            {
                int x = i % dims.X, y = i / dims.X % dims.Y, z = i / (dims.X * dims.Y);
                Vector3 local = VoxSpace.ToGodot(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - origin) * voxel;
                Cross(mesh, t * local, voxel * 0.4f, new Color(1.0f, 0.9f, 0.2f));
            }
        }
        if (total > 0)
        {
            Vector3 whole = weighted / total;
            Cross(mesh, whole, voxel * 6.0f, new Color(1.0f, 0.2f, 1.0f));
            Line(mesh, whole, new Vector3(whole.X, 0.0f, whole.Z), new Color(1.0f, 0.2f, 1.0f));
        }
        mesh.SurfaceEnd();
    }

    /// <summary>A voxel's size in its model's own space (before any scale on the wrapper).</summary>
    public static float VoxelSizeOf(Node node)
    {
        for (Node at = node; at != null; at = at.GetParent())
        {
            if (at.HasMeta(VoxelMeta.VoxelSize))
            {
                return at.GetMeta(VoxelMeta.VoxelSize).AsSingle();
            }
        }
        return 0.1f;
    }

    private static void Line(ImmediateMesh mesh, Vector3 a, Vector3 b, Color colour)
    {
        mesh.SurfaceSetColor(colour);
        mesh.SurfaceAddVertex(a);
        mesh.SurfaceSetColor(colour);
        mesh.SurfaceAddVertex(b);
    }

    private static void Cross(ImmediateMesh mesh, Vector3 at, float size, Color colour)
    {
        Line(mesh, at - Vector3.Right * size, at + Vector3.Right * size, colour);
        Line(mesh, at - Vector3.Up * size, at + Vector3.Up * size, colour);
        Line(mesh, at - Vector3.Back * size, at + Vector3.Back * size, colour);
    }

    private static void Box(ImmediateMesh mesh, Transform3D t, Vector3 centre, Vector3 size, Color colour)
    {
        Vector3 h = size * 0.5f;
        Vector3 P(int x, int y, int z) => t * (centre + new Vector3(x * h.X, y * h.Y, z * h.Z));
        int[][] edges =
        {
            new[] { -1, -1, -1, 1, -1, -1 }, new[] { -1, 1, -1, 1, 1, -1 }, new[] { -1, -1, 1, 1, -1, 1 }, new[] { -1, 1, 1, 1, 1, 1 },
            new[] { -1, -1, -1, -1, 1, -1 }, new[] { 1, -1, -1, 1, 1, -1 }, new[] { -1, -1, 1, -1, 1, 1 }, new[] { 1, -1, 1, 1, 1, 1 },
            new[] { -1, -1, -1, -1, -1, 1 }, new[] { 1, -1, -1, 1, -1, 1 }, new[] { -1, 1, -1, -1, 1, 1 }, new[] { 1, 1, -1, 1, 1, 1 },
        };
        foreach (int[] e in edges)
        {
            Line(mesh, P(e[0], e[1], e[2]), P(e[3], e[4], e[5]), colour);
        }
    }
}
#endif
