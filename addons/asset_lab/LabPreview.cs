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
    // Made by _Ready in this load of the code. A code reload restores what
    // Godot can keep of the old instance, never this, so the old one, which
    // runs a frame before the plugin builds anew, does nothing.
    private sealed class Built { }
    private Built _built;

    private bool Live => _built != null;

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

    /// <summary>How fast its time runs: 1 is the game's.</summary>
    public float TimeScale = 1.0f;

    /// <summary>Walks it forward at <see cref="WalkSpeed"/> with no key held.</summary>
    public bool Treadmill;

    /// <summary>The timeline: the clip of moves it plays, and pause.</summary>
    public readonly LabTransport Transport = new();

    // A step's worth of time to run while paused, from the step buttons.
    private float _stepRun;

    // The fists' paths through the clip, in the walker's own space so a
    // turn carries them; and see-through poses either side of the playhead.
    private bool _trails = true;
    private bool _ghosts;
    private MeshInstance3D _trailMesh;
    private readonly List<MeshInstance3D> _ghostPool = new();
    private StandardMaterial3D _ghostBefore;
    private StandardMaterial3D _ghostAfter;

    /// <summary>Poses either side of the playhead, and how far apart, in frames.</summary>
    private const int GhostCount = 3;
    private const int GhostEvery = 4;

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

        _ghostBefore = GhostMaterial(new Color(0.35f, 0.65f, 1.0f, 0.22f));
        _ghostAfter = GhostMaterial(new Color(1.0f, 0.6f, 0.25f, 0.22f));

        SetLook(null);
        PlaceCamera();
        _built = new Built();
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
        _trailMesh = null;
        foreach (MeshInstance3D ghost in _ghostPool)
        {
            ghost.Visible = false;
        }
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
        if (!Live || Walker == null || !IsInstanceValid(Walker))
        {
            return;
        }
        float Held(Key key) => _held.Contains(key) ? 1.0f : 0.0f;
        var keys = new Vector2(Held(Key.D) - Held(Key.A), Held(Key.W) - Held(Key.S));
        Vector2 steer = (keys * WalkSpeed + Drive + (Treadmill ? new Vector2(0.0f, WalkSpeed) : Vector2.Zero)).LimitLength(1.0f);
        float turn = Mathf.Clamp(Held(Key.E) - Held(Key.Q) + DriveTurn, -1.0f, 1.0f);

        // Its front is +Z, so its right is -X.
        Basis basis = Walker.GlobalBasis;
        Walker.MoveIntent = basis * new Vector3(-steer.X, 0.0f, steer.Y);
        Walker.FaceDirection = Mathf.Abs(turn) > 0.01f ? basis.Z.Rotated(Vector3.Up, -Mathf.Sign(turn) * 1.5f) : Vector3.Zero;
        Walker.Sprint = DriveSprint || _held.Contains(Key.Shift);

        float run = Transport.Playing ? Mathf.Min((float)delta, 1.0f / 20.0f) * TimeScale : _stepRun;
        _stepRun = 0.0f;
        if (Transport.Segments.Count > 0)
        {
            if (Transport.Playing)
            {
                Transport.Advance(run);
            }
            // Put where the clock says, a frame back, so the step plays the
            // last bit and passes its markers.
            LabTransport.Segment? at = Transport.At(Transport.Time, out float into);
            Walker.LabSeekMove(at?.Move, into - run, at?.Blend ?? 0.0f);
        }
        Walker.LabStep(run);
        DrawGhosts();
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

    /// <summary>The fists' paths through the clip, with a cross where each
    /// blow lands. Drawn again on a new clip or a limb taken off.</summary>
    public bool Trails
    {
        get => _trails;
        set
        {
            _trails = value;
            RebuildTrails();
        }
    }

    /// <summary>See-through poses before (blue) and after (orange) the playhead.</summary>
    public bool Ghosts
    {
        get => _ghosts;
        set
        {
            _ghosts = value;
            if (!value)
            {
                foreach (MeshInstance3D ghost in _ghostPool)
                {
                    ghost.Visible = false;
                }
            }
        }
    }

    /// <summary>Poses the walker at <paramref name="time"/> into the clip,
    /// as the playhead there would.</summary>
    private void PoseAt(float time)
    {
        LabTransport.Segment? at = Transport.At(time, out float into);
        Walker.LabSeekMove(at?.Move, into, at?.Blend ?? 0.0f);
        Walker.LabStep(0.0f);
    }

    /// <summary>Samples the whole clip for the fists' paths, then puts the
    /// walker back where the playhead is.</summary>
    public void RebuildTrails()
    {
        if (!Live || Walker == null || !IsInstanceValid(Walker))
        {
            return;
        }
        if (_trailMesh == null || !IsInstanceValid(_trailMesh))
        {
            _trailMesh = new MeshInstance3D
            {
                Name = "LabTrails",
                Mesh = new ImmediateMesh(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = _overlay.MaterialOverride,
            };
            Walker.AddChild(_trailMesh);
        }
        var mesh = (ImmediateMesh)_trailMesh.Mesh;
        mesh.ClearSurfaces();
        if (!_trails || Transport.Segments.Count == 0 || Walker.Arms.Count == 0)
        {
            return;
        }
        int arms = Walker.Arms.Count;
        int samples = Mathf.Max(2, Mathf.CeilToInt(Transport.Length * Transport.Fps / 2.0f));
        var paths = new List<Vector3>[arms];
        for (int a = 0; a < arms; a++)
        {
            paths[a] = new List<Vector3>();
        }
        Transform3D toWalker = Walker.GlobalTransform.AffineInverse();
        for (int s = 0; s <= samples; s++)
        {
            PoseAt(Transport.Length * s / samples);
            for (int a = 0; a < arms; a++)
            {
                if (Walker.PartAlive(Walker.Arms[a]))
                {
                    paths[a].Add(toWalker * Walker.StrokeTip(a));
                }
            }
        }
        // Where each blow lands: the swinging fists at the move's 'hit'.
        var hits = new List<Vector3>();
        foreach (LabTransport.Segment segment in Transport.Segments)
        {
            float hit = segment.Move.EventTime("hit");
            if (hit < 0.0f)
            {
                continue;
            }
            PoseAt(segment.Start + hit);
            foreach (int a in Walker.MoveArms())
            {
                hits.Add(toWalker * Walker.StrokeTip(a));
            }
        }
        PoseAt(Transport.Time);

        Color[] hues = { new(1.0f, 0.55f, 0.15f), new(0.25f, 0.85f, 1.0f) };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        for (int a = 0; a < arms; a++)
        {
            List<Vector3> path = paths[a];
            for (int i = 1; i < path.Count; i++)
            {
                // Faint at the start of the clip, full at its end.
                Color colour = hues[a % hues.Length] * new Color(1, 1, 1, Mathf.Lerp(0.35f, 1.0f, (float)i / path.Count));
                Line(mesh, path[i - 1], path[i], colour);
            }
        }
        foreach (Vector3 hit in hits)
        {
            Cross(mesh, hit, 0.35f, new Color(1.0f, 0.2f, 0.2f));
        }
        mesh.SurfaceEnd();
    }

    /// <summary>The moving parts posed either side of the playhead, each
    /// drawn see-through with its own mesh.</summary>
    private void DrawGhosts()
    {
        int used = 0;
        if (_ghosts && Transport.Segments.Count > 0)
        {
            var now = new Dictionary<Node3D, Transform3D>();
            foreach ((Node3D node, DamageBake _) in _damageParts)
            {
                now[node] = node.GlobalTransform;
            }
            float every = GhostEvery / Transport.Fps;
            for (int k = -GhostCount; k <= GhostCount; k++)
            {
                float time = Transport.Time + k * every;
                if (k == 0 || time < 0.0f || time > Transport.Length)
                {
                    continue;
                }
                PoseAt(time);
                foreach ((Node3D node, DamageBake _) in _damageParts)
                {
                    // Only what moves: what stays put would sit on itself.
                    if (node is not MeshInstance3D part || !part.IsVisibleInTree()
                        || part.GlobalTransform.Origin.DistanceTo(now[node].Origin) + (part.GlobalBasis.Z - now[node].Basis.Z).Length() < 0.03f)
                    {
                        continue;
                    }
                    if (used == _ghostPool.Count)
                    {
                        var made = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                        _viewport.AddChild(made);
                        _ghostPool.Add(made);
                    }
                    MeshInstance3D ghost = _ghostPool[used++];
                    ghost.Mesh = part.Mesh;
                    ghost.GlobalTransform = part.GlobalTransform;
                    ghost.MaterialOverride = k < 0 ? _ghostBefore : _ghostAfter;
                    ghost.Transparency = 0.25f + 0.6f * (Mathf.Abs(k) - 1) / GhostCount;
                    ghost.Visible = true;
                }
            }
            PoseAt(Transport.Time);
        }
        for (int i = used; i < _ghostPool.Count; i++)
        {
            _ghostPool[i].Visible = false;
        }
    }

    private static StandardMaterial3D GhostMaterial(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoColor = colour,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// <summary>Holds the clip at <paramref name="time"/> seconds, paused.</summary>
    public void Seek(float time)
    {
        Transport.Playing = false;
        Transport.Time = Mathf.Clamp(time, 0.0f, Transport.Length);
    }

    /// <summary>Paused, moves on (or back) <paramref name="frames"/> of the
    /// clip's frames. The walk only goes forward.</summary>
    public void StepFrames(int frames)
    {
        Transport.Playing = false;
        float seconds = frames / Transport.Fps;
        Transport.Time = Mathf.Clamp(Transport.Time + seconds, 0.0f, Transport.Length);
        _stepRun = Mathf.Max(seconds, 0.0f);
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
        RebuildTrails();
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
