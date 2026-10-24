#if TOOLS
using System.Collections.Generic;
using System.Globalization;
using DillControl.Core;
using DillControl.Foliage;
using DillControl.Voxel;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// One vine: pick a kind and a seed, see the vine it grows and what it is
/// judged against, change a setting for this one vine if it needs it, and
/// Bake: a .vox imported at the bake's voxel size and a wrapper scene that
/// drops into any level (<see cref="VineBaker"/>), for hand-made set pieces.
/// Kinds are <c>resources/foliage/vine_kind_*.tres</c>; how a vine is baked
/// and judged is <c>vine_bake.tres</c>. The Vines dock and the Vines tool's
/// Vine tab each hold one.
/// </summary>
[Tool]
public partial class VinePanel : VBoxContainer
{
    private VineBakeStats _stats;
    private readonly List<VineKind> _kinds = new();
    private VineParams _params;
    private VineGenerator.Result _vine;
    private VineGrower.Grown _grown;
    private bool _custom;
    private bool _filling;

    private OptionButton _kind;
    private SpinBox _seed;
    private SpinBox _pool;
    private Label _report;
    private Label _status;
    private Button _bake;
    private GridContainer _fields;
    private readonly Dictionary<string, Control> _editors = new();
    private Timer _regrow;

    private SubViewport _viewport;
    private Camera3D _camera;
    private MeshInstance3D _mesh;
    private MultiMeshInstance3D _pods;
    private Node3D _leaves;
    private Material _material;
    private SubViewportContainer _previewBox;
    private float _yaw = 0.7f, _pitch = 0.2f, _distance = 160.0f, _height = 50.0f, _lookAt = 25.0f;

    public override void _Ready()
    {
        if (GetChildCount() > 0)
            return;
        _material = GD.Load<Material>("res://art/materials/mat_voxel_toon.tres");
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);

        var kindRow = new HBoxContainer();
        kindRow.AddChild(new Label { Text = "Kind" });
        _kind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "A vine_kind_*.tres: every setting a range." };
        _kind.ItemSelected += _ => Reroll();
        kindRow.AddChild(_kind);
        var reload = new Button { Text = "Reload", TooltipText = "Read the kinds and vine_bake.tres again." };
        reload.Pressed += LoadKinds;
        kindRow.AddChild(reload);
        column.AddChild(kindRow);

        var seedRow = new HBoxContainer();
        seedRow.AddChild(new Label { Text = "Seed" });
        _seed = new SpinBox { MinValue = 0, MaxValue = 9999999, Step = 1, Rounded = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _seed.ValueChanged += _ => Reroll();
        seedRow.AddChild(_seed);
        var roll = new Button { Text = "Roll", TooltipText = "A new seed: the same kind, a different vine." };
        roll.Pressed += () => _seed.Value = new SeededRandom(Time.GetTicksUsec()).RangeInt(0, 9999999);
        seedRow.AddChild(roll);
        column.AddChild(seedRow);

        var poolRow = new HBoxContainer();
        poolRow.AddChild(new Label { Text = "Pool vine" });
        _pool = new SpinBox { MinValue = 0, MaxValue = 7, Step = 1, Rounded = true, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "The kind's Nth pool seed, from vine_bake.tres's archetype seed." };
        poolRow.AddChild(_pool);
        var usePool = new Button { Text = "Use" };
        usePool.Pressed += () =>
        {
            if (Kind() is VineKind kind)
                _seed.Value = kind.PoolSeed(_stats.ArchetypeSeed, (int)_pool.Value);
        };
        poolRow.AddChild(usePool);
        column.AddChild(poolRow);

        column.AddChild(BuildPreview());

        _report = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_report);

        var bakeRow = new HBoxContainer();
        _bake = new Button { Text = "Bake", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Write art/voxels/prp_vine_<kind>_<seed>.vox and scenes/props/prp_vine_<kind>_<seed>.tscn." };
        _bake.Pressed += Bake;
        bakeRow.AddChild(_bake);
        var reset = new Button { Text = "Back to the kind", TooltipText = "Forget changed settings: roll them from the kind again." };
        reset.Pressed += Reroll;
        bakeRow.AddChild(reset);
        column.AddChild(bakeRow);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_status);

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Settings (rolled; change one for this vine only)" });
        _fields = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_fields);
        BuildFields();

        _regrow = new Timer { OneShot = true, WaitTime = 0.2 };
        _regrow.Timeout += Grow;
        AddChild(_regrow);

        // At editor start the plugin enters before C# resources resolve their
        // scripts: read the kinds once the editor has settled.
        Callable.From(LoadKinds).CallDeferred();
    }

    private Control BuildPreview()
    {
        var container = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(0, 420), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Drag: turn. Wheel: zoom. Right-drag: up and down the vine. Double-click: the whole vine.\nThe grey disc (60 m) is the scene origin: the foot stands on it. Orange: pods." };
        _previewBox = container;
        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X };
        container.AddChild(_viewport);
        container.GuiInput += PreviewInput;

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.55f, 0.62f, 0.66f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
            AmbientLightEnergy = 0.6f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.2f };
        sun.RotationDegrees = new Vector3(-50, 30, 0);
        _viewport.AddChild(sun);
        var ground = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 30, BottomRadius = 30, Height = 0.1f, RadialSegments = 48 },
            Position = new Vector3(0, -0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.4f, 0.36f) },
        };
        _viewport.AddChild(ground);
        _mesh = new MeshInstance3D();
        _viewport.AddChild(_mesh);
        _pods = new MultiMeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.55f, 0.15f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        _viewport.AddChild(_pods);
        _camera = new Camera3D { Fov = 50, Far = 2000 };
        _viewport.AddChild(_camera);
        return container;
    }

    private void PreviewInput(InputEvent e)
    {
        // Claimed here, or the dock's scroll container takes the wheel.
        if (e is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                _yaw -= motion.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.01f, -0.3f, 1.5f);
            }
            else if ((motion.ButtonMask & MouseButtonMask.Right) != 0)
            {
                _lookAt = Mathf.Clamp(_lookAt + motion.Relative.Y * _distance * 0.003f, 0, _height * 1.2f);
            }
            else
            {
                return;
            }
            PlaceCamera();
            _previewBox.AcceptEvent();
        }
        else if (e is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp)
                _distance = Mathf.Max(5, _distance * 0.85f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Min(2000, _distance / 0.85f);
            else if (button.ButtonIndex == MouseButton.Left && button.DoubleClick)
                Frame();
            else if (button.ButtonIndex != MouseButton.Left && button.ButtonIndex != MouseButton.Right)
                return;
            PlaceCamera();
            _previewBox.AcceptEvent();
        }
    }

    /// <summary>The whole vine in view, from the side.</summary>
    private void Frame()
    {
        _lookAt = _height * 0.5f;
        _distance = Mathf.Max(20, _height * 1.6f);
        _pitch = 0.15f;
    }

    private void PlaceCamera()
    {
        var target = new Vector3(0, _lookAt, 0);
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        _camera.Position = target + offset * _distance;
        _camera.LookAt(target);
    }

    private void BuildFields()
    {
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            _fields.AddChild(new Label { Text = field.Key });
            Control editor;
            if (field.Bool)
            {
                var check = new CheckBox();
                check.Toggled += on => Changed(field, on ? 1 : 0);
                editor = check;
            }
            else
            {
                var spin = new SpinBox
                {
                    MinValue = field.Min, MaxValue = field.Max, Step = field.Integer ? 1 : field.Step,
                    Rounded = field.Integer, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                };
                spin.ValueChanged += v => Changed(field, v);
                editor = spin;
            }
            _editors[field.Key] = editor;
            _fields.AddChild(editor);
        }
    }

    private void LoadKinds()
    {
        _stats = ResourceLoader.Load(VineBakeStats.DefaultPath, cacheMode: ResourceLoader.CacheMode.Replace) as VineBakeStats;
        string selected = Kind()?.KindName;
        _kinds.Clear();
        _kind.Clear();
        foreach (string file in DirAccess.GetFilesAt(VineKind.Folder))
        {
            if (!file.StartsWith(VineKind.Prefix) || !file.EndsWith(".tres"))
                continue;
            var kind = ResourceLoader.Load($"{VineKind.Folder}/{file}", cacheMode: ResourceLoader.CacheMode.Replace) as VineKind;
            if (kind == null)
                continue;
            _kinds.Add(kind);
            _kind.AddItem(kind.KindName);
            if (kind.KindName == selected)
                _kind.Selected = _kinds.Count - 1;
        }
        if (_kinds.Count == 0 || _stats == null)
            GD.PushWarning($"Vines dock: read {_kinds.Count} kinds and {(_stats == null ? "no" : "the")} bake settings. Build the project, then Reload.");
        if (_kinds.Count > 0 && _stats != null && _seed.Value == 0)
            _seed.SetValueNoSignal(_kinds[_kind.Selected < 0 ? 0 : _kind.Selected].PoolSeed(_stats.ArchetypeSeed, 0));
        Reroll();
    }

    private VineKind Kind() => _kind != null && _kind.Selected >= 0 && _kind.Selected < _kinds.Count ? _kinds[_kind.Selected] : null;

    /// <summary>The settings from the kind and the seed, nothing changed by hand.</summary>
    private void Reroll()
    {
        VineKind kind = Kind();
        if (kind == null || _stats == null)
        {
            _report.Text = $"No kinds or bake settings read: {VineKind.Folder}/{VineKind.Prefix}*.tres and {VineBakeStats.DefaultPath}. Build the project, then Reload.";
            _bake.Disabled = true;
            return;
        }
        _pool.MaxValue = kind.PoolSize - 1;
        ulong t0 = Time.GetTicksMsec();
        _grown = VineGrower.Grow(kind, (int)_seed.Value, _stats);
        _growMs = Time.GetTicksMsec() - t0;
        _params = _grown.Vine.Params.Copy();
        _custom = false;
        _filling = true;
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            Control editor = _editors[field.Key];
            if (editor is CheckBox check)
                check.ButtonPressed = field.Get(_params) != 0;
            else if (editor is SpinBox spin)
                spin.Value = field.Get(_params);
            (double lo, double hi) = field.Bool ? (kind.ChanceOf(field.Key), 0) : kind.RangeOf(field);
            editor.TooltipText = field.Bool
                ? string.Create(CultureInfo.InvariantCulture, $"Chance in this kind: {lo:0.##}")
                : string.Create(CultureInfo.InvariantCulture, $"This kind's range: {lo} to {hi}");
            editor.Modulate = Colors.White;
        }
        _filling = false;
        Show();
    }

    private void Changed(VineParams.Field field, double value)
    {
        if (_filling || _params == null)
            return;
        field.Set(_params, value);
        _custom = true;
        _editors[field.Key].Modulate = new Color(1.0f, 0.8f, 0.4f);
        _regrow.Start();
    }

    private ulong _growMs;

    /// <summary>Settings changed by hand: grown and judged once, never re-rolled.</summary>
    private void Grow()
    {
        if (_params == null || _stats == null)
            return;
        ulong t0 = Time.GetTicksMsec();
        int asked = _grown?.Seed ?? (int)_seed.Value;
        _grown = VineGrower.FromSettings(_params, _stats);
        _grown.Seed = asked;
        _growMs = Time.GetTicksMsec() - t0;
        Show();
    }

    private void Show()
    {
        _vine = _grown.Vine;
        // Only valid vines are baked (docs/handoffs/vine-tool.md).
        _bake.Disabled = !_grown.Ok;
        _bake.Text = _custom ? "Bake (changed settings)" : "Bake";
        _report.Text = Report(_grown, _growMs);
        ShowVine();
    }

    private string Report(VineGrower.Grown grown, ulong ms)
    {
        VineGenerator.Result vine = grown.Vine;
        if (vine.Voxels.Count == 0)
            return vine.Error ?? "Nothing grew.";
        VineGenerator.Counts c = vine.Counts;
        VineValidator.Verdict verdict = grown.Verdict;
        string name = VineBaker.NameOf(Kind(), grown.Seed, _custom);
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{name}\n{c.Total:N0} voxels of {_stats.VoxelBudget:N0}, {c.Bodies} bodies at rest\n"
            + $"{vine.BoundsX}x{vine.BoundsY}x{vine.BoundsZ} voxels, {vine.BoundsZ * vine.VoxelSize:0.#} m tall\n"
            + $"{c.Markers} pods ({c.PodsLeftOut} left out: {c.PodsBlocked} blocked, {c.PodsGrounded} too low, {c.PodsCrowded} crowded), nearest two {c.PodSpacingM:0.00} m apart\n"
            + $"core {c.Core:N0}, {c.Leaves} leaves ({c.LeavesLeftOut} left out), bridged {c.Bridged}\n"
            + $"lean {verdict.LeanDeg:0.0} deg, spine bends {verdict.KinkDeg:0.00} deg at most, tip sags {verdict.SagM:0.0} m\n"
            + $"grown in {ms} ms");
        if (grown.Attempt > 0)
        {
            text += string.Create(CultureInfo.InvariantCulture, $"\nAttempt {grown.Attempt} (rolled from seed {(int)vine.Params.Seed}); skipped: ")
                + string.Join("; ", grown.Rejected.ConvertAll(r => string.Join(", ", r.Reasons)));
        }
        foreach (string detail in verdict.Details)
            text += "\nNot valid: " + detail;
        if (!grown.Ok)
            text += _custom ? "\nCannot bake: change the settings back, or Reroll." : "\nCannot bake: no valid vine in this kind's attempts; its ranges need narrowing.";
        if (FileAccess.FileExists($"{_stats.VoxelFolder}/{name}.vox"))
            text += "\nBaked before: baking again replaces its .vox and keeps its scene.";
        return text;
    }

    /// <summary>Meshed as the importer meshes it, standing where it will stand.</summary>
    private void ShowVine()
    {
        if (_vine.Voxels.Count == 0 || _vine.BoundsX > 256 || _vine.BoundsY > 256 || _vine.BoundsZ > 256)
        {
            _mesh.Mesh = null;
            _pods.Multimesh = null;
            _leaves?.QueueFree();
            _leaves = null;
            return;
        }
        VinePalette palette = VineBaker.PaletteFor(Kind(), _stats, _vine);
        VoxFile file = VoxFile.Load(VineBaker.Encode(_vine, palette));
        VoxFile.Model model = file.Models[0];
        float vs = (float)_vine.VoxelSize;
        Vector3 centre = VoxSpace.Centre(model.Size);
        Godot.Collections.Array arrays = VoxMesher.Build(model, file.Palette, file.Emission, centre, vs, 255);
        var mesh = new ArrayMesh();
        if (arrays != null)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(0, _material);
        }
        _mesh.Mesh = mesh;
        (int X, int Y, int Z) t = VineBaker.FootTranslation(_vine);
        var translation = new Vector3(t.X, t.Y, t.Z);
        _mesh.Position = VoxSpace.ToGodot(translation) * vs;

        var pods = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new BoxMesh { Size = new Vector3(0.6f, 0.9f, 0.6f) },
            InstanceCount = _vine.Markers.Count,
        };
        for (int i = 0; i < _vine.Markers.Count; i++)
        {
            VineGenerator.Marker m = _vine.Markers[i];
            Vector3 at = translation + new Vector3(m.X + 0.5f, m.Y + 0.5f, m.Z + 0.5f) - centre;
            pods.SetInstanceTransform(i, new Transform3D(Basis.Identity, VoxSpace.ToGodot(at) * vs));
        }
        _pods.Multimesh = pods;

        // Leaves are meshes: drawn as the game draws them, from the same set.
        _leaves?.QueueFree();
        _leaves = VineLeaves.Drawing(VineLeafSet.Of(_vine, Kind(), palette),
            GD.Load<Material>(VineLeaves.MaterialPath), 0.35f);
        _viewport.AddChild(_leaves);

        float height = _vine.BoundsZ * vs;
        if (Mathf.Abs(height - _height) > 1.0f)
        {
            _height = height;
            Frame();
        }
        PlaceCamera();
    }

    private void Bake()
    {
        VineKind kind = Kind();
        if (kind == null || _grown == null || !_grown.Ok)
            return;
        VineBaker.Baked baked = VineBaker.Bake(kind, _grown.Seed, _stats, _custom ? _params : null, _custom);
        if (!baked.Grown.Ok)
        {
            _status.Text = "Not baked: " + string.Join(" ", baked.Grown.Verdict.Details);
            return;
        }
        _status.Text = $"Baked {baked.Name}: {baked.VoxPath}" + (baked.SceneWritten ? $" and {baked.ScenePath}." : $"; kept {baked.ScenePath}.");
        EditorFileSystem files = EditorInterface.Singleton.GetResourceFilesystem();
        string scene = baked.ScenePath;
        void Select()
        {
            files.FilesystemChanged -= Select;
            EditorInterface.Singleton.SelectFile(scene);
        }
        files.FilesystemChanged += Select;
        files.Scan();
        _report.Text = Report(_grown, 0);
    }
}
#endif
