#if TOOLS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DillControl.Planet;
using DillControl.Vehicles;
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
/// A mech walks here (<see cref="MechWalker.LabMode"/>): on a treadmill, the
/// ground sliding under it, steered by WASD (Q and E turn, Shift sprints)
/// once the view has been clicked, or by <see cref="Drive"/>.
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
    private MeshInstance3D _ground;
    private List<(Node3D Node, DamageBake Bake)> _damageParts = new();

    /// <summary>The walker shown, if the asset is one.</summary>
    public MechWalker Walker { get; private set; }

    /// <summary>How it is steered besides the keys, in its own frame:
    /// x right, y forward, each -1 to 1; <see cref="DriveTurn"/> turns it
    /// (+ to its right). Set by the bridge.</summary>
    public Vector2 Drive;
    public float DriveTurn;
    public bool DriveSprint;

    /// <summary>The share of its top speed the keys walk it at.</summary>
    public float WalkSpeed = 1.0f;

    /// <summary>How fast its time runs: 1 is the game's, 0 holds it.</summary>
    public float TimeScale = 1.0f;

    private readonly HashSet<Key> _held = new();
    private Vector2 _pressedAt;
    private bool _dragged;

    /// <summary>What the last click on a walker's part did, for the tab to show.</summary>
    public string LastPick { get; private set; } = "";

    /// <summary>The ground's tiles, metres: the belt is seen moving by them.</summary>
    private const float Tile = 2.0f;

    public override void _Ready()
    {
        Stretch = true;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.All;
        FocusExited += () => _held.Clear();

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

        // Tiles of two close greys, so a treadmill's belt is seen moving.
        Image tiles = Image.CreateEmpty(2, 2, false, Image.Format.Rgb8);
        tiles.Fill(new Color(0.38f, 0.37f, 0.34f));
        tiles.SetPixel(0, 0, new Color(0.34f, 0.33f, 0.30f));
        tiles.SetPixel(1, 1, new Color(0.34f, 0.33f, 0.30f));
        _ground = new MeshInstance3D
        {
            Name = "Ground",
            Mesh = new PlaneMesh { Size = new Vector2(400.0f, 400.0f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = ImageTexture.CreateFromImage(tiles),
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
                Uv1Scale = new Vector3(400.0f / (Tile * 2.0f), 400.0f / (Tile * 2.0f), 1.0f),
                Roughness = 1.0f,
            },
        };
        _viewport.AddChild(_ground);

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
        Walker = null;
        if (scene != null)
        {
            Node made = scene.Instantiate();
            Asset = made as Node3D ?? new Node3D { Name = "Asset" };
            if (Asset != made)
            {
                Asset.AddChild(made);
            }
            // Before it enters the tree: its _Ready sets it up to walk here.
            Walker = Asset as MechWalker ?? Asset.FindChildren("*", nameof(CharacterBody3D), true, false).OfType<MechWalker>().FirstOrDefault();
            if (Walker != null)
            {
                Walker.LabMode = true;
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
        _damageParts = DamageParts();
        _ground.Position = Vector3.Zero;
        DrawOverlay();
    }

    public override void _Process(double delta)
    {
        if (Walker == null || !IsInstanceValid(Walker))
        {
            return;
        }
        float Held(Key key) => _held.Contains(key) ? 1.0f : 0.0f;
        var keys = new Vector2(Held(Key.D) - Held(Key.A), Held(Key.W) - Held(Key.S));
        Vector2 steer = (keys * WalkSpeed + Drive).LimitLength(1.0f);
        float turn = Mathf.Clamp(Held(Key.E) - Held(Key.Q) + DriveTurn, -1.0f, 1.0f);

        // Its front is +Z, so its right is -X.
        Basis basis = Walker.GlobalBasis;
        Walker.MoveIntent = basis * new Vector3(-steer.X, 0.0f, steer.Y);
        Walker.FaceDirection = Mathf.Abs(turn) > 0.01f ? basis.Z.Rotated(Vector3.Up, -Mathf.Sign(turn) * 1.5f) : Vector3.Zero;
        Walker.Sprint = DriveSprint || _held.Contains(Key.Shift);

        Walker.LabStep(Mathf.Min((float)delta, 1.0f / 20.0f) * TimeScale);
        Vector3 belt = Walker.BeltTravel;
        _ground.Position = new Vector3(Mathf.PosMod(belt.X, Tile * 2.0f), 0.0f, Mathf.PosMod(belt.Z, Tile * 2.0f));
        if (_damageMap && _damageParts.Count > 0)
        {
            DrawOverlay();
        }
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

    // Guarded: after a reload of the code the editor sets properties back
    // before _Ready has made the nodes.
    public bool Ink
    {
        get => _ink?.Visible ?? true;
        set
        {
            if (_ink != null)
            {
                _ink.Visible = value;
            }
        }
    }

    public bool DamageMap
    {
        get => _damageMap;
        set
        {
            _damageMap = value;
            if (_overlay != null)
            {
                DrawOverlay();
            }
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
            if (_sun == null)
            {
                return;
            }
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
                _dragged |= motion.Position.DistanceTo(_pressedAt) > 4.0f;
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
        else if (@event is InputEventKey key && Walker != null && WalkKey(key.PhysicalKeycode))
        {
            if (key.Pressed)
            {
                _held.Add(key.PhysicalKeycode);
            }
            else
            {
                _held.Remove(key.PhysicalKeycode);
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release)
        {
            // A click, not a drag to orbit: a walker's limb clicked comes off,
            // or goes back on.
            if (!_dragged && Walker != null)
            {
                Toggle(PartAt(release.Position));
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true } button)
        {
            GrabFocus();
            if (button.ButtonIndex == MouseButton.Left)
            {
                _pressedAt = button.Position;
                _dragged = false;
            }
            if (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown)
            {
                Distance *= button.ButtonIndex == MouseButton.WheelUp ? 0.9f : 1.1f;
                PlaceCamera();
                AcceptEvent();
            }
        }
    }

    /// <summary>Takes off or puts back the walker's limb that
    /// <paramref name="part"/> is in. Returns what happened, in words.</summary>
    public string Toggle(Node3D part)
    {
        if (Walker == null || part == null)
        {
            return LastPick = part == null ? "" : $"{part.Name}: not a walker";
        }
        bool? on = Walker.LabToggleLimb(part);
        LastPick = on switch
        {
            null => $"{part.Name} is not a limb",
            true => $"{part.Name} back on",
            false => $"{part.Name} off",
        };
        return LastPick;
    }

    /// <summary>The visible voxel part under a point of the view, found by
    /// walking the camera's ray through each part's cells; null for none.</summary>
    public Node3D PartAt(Vector2 point)
    {
        Vector3 from = _camera.ProjectRayOrigin(point);
        Vector3 along = _camera.ProjectRayNormal(point);
        Node3D best = null;
        float nearest = float.MaxValue;
        foreach ((Node3D node, DamageBake _) in _damageParts)
        {
            if (!node.IsVisibleInTree())
            {
                continue;
            }
            float voxel = VoxelSizeOf(node);
            Vector3I dims = node.GetMeta(VoxelMeta.Dims).AsVector3I();
            Vector3 origin = node.GetMeta(VoxelMeta.Origin).AsVector3();
            byte[] cells = node.GetMeta(VoxelMeta.Cells).AsByteArray();
            Transform3D inverse = node.GlobalTransform.AffineInverse();
            float step = voxel * 0.4f;
            float far = Mathf.Min(nearest, from.DistanceTo(Target) + Bounds.Size.Length());
            for (float t = 0.0f; t < far; t += step)
            {
                Vector3 v = VoxSpace.FromGodot(inverse * (from + along * t) / voxel) + origin;
                int x = Mathf.FloorToInt(v.X), y = Mathf.FloorToInt(v.Y), z = Mathf.FloorToInt(v.Z);
                if (x < 0 || y < 0 || z < 0 || x >= dims.X || y >= dims.Y || z >= dims.Z)
                {
                    continue;
                }
                if (cells[x + dims.X * (y + dims.Y * z)] != 0)
                {
                    nearest = t;
                    best = node;
                    break;
                }
            }
        }
        return best;
    }

    private static bool WalkKey(Key key) => key is Key.W or Key.A or Key.S or Key.D or Key.Q or Key.E or Key.Shift;

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

    /// <summary>The overlay again, after parts were moved (a pose from Blender).</summary>
    public void Redraw() => DrawOverlay();

    private void DrawOverlay()
    {
        var mesh = (ImmediateMesh)_overlay.Mesh;
        mesh.ClearSurfaces();
        List<(Node3D Node, DamageBake Bake)> parts = _damageParts;
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
            if (!node.IsVisibleInTree())
            {
                continue;
            }
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
