#if TOOLS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DillControl.Core;
using DillControl.Foliage;
using DillControl.Mission;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool's Nest tab (docs/handoffs/vine-tool.md, "The tool"): pick a
/// kit, a seed and a population, Generate to see the nest the game would
/// grow from them and what it measures, and Play to load straight into it.
///
/// Generate plans with the game's own <see cref="NestLayout"/> on all cores,
/// as <see cref="NestBuilder"/> does behind the loading screen, and stands it
/// on a flat disc (<see cref="NestPreview"/>). Play hands the kit, seed,
/// population and horde to <c>lvl_nest_lab</c> (<see cref="NestPlay"/>);
/// it needs no Generate first, and writes nothing else.
/// </summary>
[Tool]
public partial class NestTab : HSplitContainer
{
    public const string LabPath = "res://scenes/levels/lvl_nest_lab.tscn";
    private const string KitFolder = "res://resources/mission";
    private const string KitPrefix = "nest_kit_";
    private const int PlantedPerFrame = 4;

    private readonly List<NestKit> _kits = new();
    private OptionButton _kit;
    private SpinBox _seed;
    private SpinBox _population;
    private CheckBox _horde;
    private Button _generate;
    private Button _play;
    private Label _metrics;
    private Label _status;

    private SubViewport _viewport;
    private SubViewportContainer _previewBox;
    private Camera3D _camera;
    private Node3D _nest;
    private MeshInstance3D _ground;
    private Material _voxelMaterial;
    private Material _leafMaterial;
    private float _yaw = 0.6f, _pitch = 0.9f, _distance = 900.0f;
    private Vector3 _target;

    private Task<(NestLayout Layout, List<NestPreview.Vine> Vines, double PreviewSeconds, long Memory)> _work;
    private readonly Stopwatch _clock = new();
    private List<NestPreview.Vine> _toPlant;
    private int _planted;
    private NestLayout _layout;
    private MultiMesh _pods;
    private int _podsDrawn;

    public override void _Ready()
    {
        if (GetChildCount() > 0)
            return;
        _voxelMaterial = GD.Load<Material>(VineBaker.Material);
        _leafMaterial = GD.Load<Material>(VineLeaves.MaterialPath);
        SplitOffset = 340;

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(320, 0) };
        AddChild(scroll);
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(column);

        _kit = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = $"A {KitFolder}/{KitPrefix}*.tres: the kinds and the layout." };
        _kit.ItemSelected += _ => KitChanged();
        column.AddChild(Row("Kit", _kit, Button("Reload", "Read the kits again.", LoadKits)));

        _seed = new SpinBox { MinValue = 0, MaxValue = int.MaxValue, Step = 1, Rounded = true, Value = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "The nest's seed: the same seed, kit and population are the same nest on every machine." };
        column.AddChild(Row("Seed", _seed, Button("Roll", "A new seed.", () => _seed.Value = new SeededRandom(Time.GetTicksUsec()).RangeInt(1, int.MaxValue))));

        _population = new SpinBox { MinValue = 1000, MaxValue = 25000, Step = 100, Rounded = true, Value = 5000, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "Pickles, one pod each: the nest grows vines until their pods cover it." };
        column.AddChild(Row("Population", _population));

        _horde = new CheckBox { Text = "With the horde", ButtonPressed = true,
            TooltipText = "Off: the pickles hang, and nothing drops when the colony wakes." };
        column.AddChild(_horde);

        var buttons = new HBoxContainer();
        _generate = Button("Generate", "Plan the nest and stand it here, as the game would grow it.", Generate);
        _generate.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_generate);
        _play = Button("Play", "Load straight into this nest (lvl_nest_lab), free to swing, shoot and fell.", Play);
        _play.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_play);
        column.AddChild(buttons);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_status);
        column.AddChild(new HSeparator());
        _metrics = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_metrics);

        AddChild(BuildPreview());
        // As the dock: the editor has settled before C# resources resolve.
        Callable.From(LoadKits).CallDeferred();
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("nesttab-check="))
                _checkPng = argument.Substring("nesttab-check=".Length);
        }
        if (_checkPng != null)
            Callable.From(StartCheck).CallDeferred();
    }

    // For checking the tab in the editor (--editor -- nesttab-check=<png>):
    // a 5,000 nest generated, its preview saved from above and beside the
    // vine furthest out, its metrics printed, and the editor quit. The tab
    // is shown first: a hidden one's preview is not drawn.
    private string _checkPng;
    private int _checkFrames = -1;

    private void StartCheck()
    {
        EditorInterface.Singleton.SetMainScreenEditor("Vines");
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _population.Value = 5000;
        Generate();
    }

    private bool _checkClose;

    private void FinishCheck()
    {
        Image image = _viewport.GetTexture().GetImage();
        if (!_checkClose)
        {
            // Then from beside the vine furthest out.
            image.SavePng(_checkPng);
            _checkClose = true;
            Node3D far = _nest.GetChildren().OfType<Node3D>().OrderByDescending(n => n.Position.Length()).First();
            _target = far.Position + new Vector3(0, 40, 0);
            _distance = 160;
            _pitch = 0.25f;
            PlaceCamera();
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
            _checkFrames = 20;
            return;
        }
        image.SavePng(_checkPng.Replace(".png", "_far_vine.png"));
        GD.Print($"[nesttab] {_status.Text}\n{_metrics.Text}\n[nesttab] {_nest.GetChildCount()} nodes, {_podsDrawn} pods drawn, preview -> {_checkPng}");
        GetTree().Quit();
    }

    private static HBoxContainer Row(string label, params Control[] controls)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(90, 0) });
        foreach (Control control in controls)
            row.AddChild(control);
        return row;
    }

    private static Button Button(string text, string tip, Action pressed)
    {
        var button = new Button { Text = text, TooltipText = tip };
        button.Pressed += pressed;
        return button;
    }

    private Control BuildPreview()
    {
        _previewBox = new SubViewportContainer
        {
            Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Drag: turn. Wheel: zoom. Right-drag: move across. Double-click: the whole nest.\nLime: a pod's pickle. The disc is the nest's ground; the planet is the game's.",
        };
        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa2X };
        _previewBox.AddChild(_viewport);
        _previewBox.GuiInput += PreviewInput;
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.62f, 0.72f, 0.82f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
            AmbientLightEnergy = 0.6f,
        };
        _viewport.AddChild(new WorldEnvironment { Environment = environment });
        var sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.2f, DirectionalShadowMaxDistance = 1500 };
        sun.RotationDegrees = new Vector3(-55, 30, 0);
        _viewport.AddChild(sun);
        _ground = new MeshInstance3D
        {
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.47f, 0.36f) },
        };
        _viewport.AddChild(_ground);
        _camera = new Camera3D { Fov = 50, Far = 6000 };
        _viewport.AddChild(_camera);
        PlaceCamera();
        return _previewBox;
    }

    private void PreviewInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion)
        {
            if ((motion.ButtonMask & MouseButtonMask.Left) != 0)
            {
                _yaw -= motion.Relative.X * 0.01f;
                _pitch = Mathf.Clamp(_pitch + (motion.Relative.Y * 0.01f), 0.02f, 1.55f);
            }
            else if ((motion.ButtonMask & MouseButtonMask.Right) != 0)
            {
                var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
                var forward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw));
                _target -= ((right * motion.Relative.X) + (forward * motion.Relative.Y)) * _distance * 0.002f;
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
                _distance = Mathf.Max(10, _distance * 0.85f);
            else if (button.ButtonIndex == MouseButton.WheelDown)
                _distance = Mathf.Min(5000, _distance / 0.85f);
            else if (button.ButtonIndex == MouseButton.Left && button.DoubleClick)
                Frame();
            else if (button.ButtonIndex != MouseButton.Left && button.ButtonIndex != MouseButton.Right)
                return;
            PlaceCamera();
            _previewBox.AcceptEvent();
        }
    }

    private void Frame()
    {
        float radius = _layout?.RadiusM ?? 400.0f;
        _target = new Vector3(0, 40, 0);
        _distance = Mathf.Max(200, radius * 2.4f);
        _pitch = 0.9f;
    }

    private void PlaceCamera()
    {
        var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch));
        // Set, not LookAt: it is placed before it is in the tree.
        Vector3 from = _target + (offset * _distance);
        _camera.Transform = new Transform3D(Basis.LookingAt(_target - from, Vector3.Up), from);
    }

    private void LoadKits()
    {
        string selected = Kit()?.ResourcePath;
        _kits.Clear();
        _kit.Clear();
        foreach (string file in DirAccess.GetFilesAt(KitFolder))
        {
            if (!file.StartsWith(KitPrefix) || !file.EndsWith(".tres"))
                continue;
            if (ResourceLoader.Load($"{KitFolder}/{file}", cacheMode: ResourceLoader.CacheMode.Replace) is not NestKit kit)
                continue;
            _kits.Add(kit);
            _kit.AddItem(kit.DisplayName);
            if (kit.ResourcePath == selected)
                _kit.Selected = _kits.Count - 1;
        }
        if (_kits.Count > 0 && _kit.Selected < 0)
            _kit.Selected = 0;
        _status.Text = _kits.Count == 0 ? $"No kits read from {KitFolder}/{KitPrefix}*.tres: build the project, then Reload." : "";
        KitChanged();
    }

    private NestKit Kit() => _kit != null && _kit.Selected >= 0 && _kit.Selected < _kits.Count ? _kits[_kit.Selected] : null;

    private void KitChanged()
    {
        NestKit kit = Kit();
        _generate.Disabled = _play.Disabled = kit == null;
        if (kit == null)
            return;
        _population.MinValue = kit.PopulationClamp.X;
        _population.MaxValue = kit.PopulationClamp.Y;
    }

    private void Generate()
    {
        NestKit kit = Kit();
        if (kit == null || _work != null)
            return;
        // Read here; everything after runs on the other cores (no engine calls there).
        VineBakeStats stats = kit.BakeOrDefault();
        VineGrower.Context context = VineGrower.Context.For(stats, kit.Kinds);
        ulong seed = (ulong)_seed.Value;
        int population = (int)_population.Value;
        _generate.Disabled = true;
        _clock.Restart();
        _work = Task.Run(() =>
        {
            long before = GC.GetTotalMemory(false);
            NestLayout layout = NestLayout.Plan(kit, context, seed, population);
            var preview = Stopwatch.StartNew();
            List<NestPreview.Vine> vines = NestPreview.Build(layout, kit, stats);
            return (layout, vines, preview.Elapsed.TotalSeconds, GC.GetTotalMemory(false) - before);
        });
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_work != null)
        {
            if (!_work.IsCompleted)
            {
                _status.Text = string.Create(CultureInfo.InvariantCulture, $"Growing the vines on every core... {_clock.Elapsed.TotalSeconds:F0} s");
                return;
            }
            if (_work.IsFaulted)
            {
                _status.Text = "Generate failed: " + _work.Exception?.GetBaseException().Message;
                GD.PushError(_work.Exception?.GetBaseException().ToString());
                _work = null;
                _generate.Disabled = false;
                return;
            }
            var (layout, vines, previewSeconds, memory) = _work.Result;
            _work = null;
            Clear(layout);
            _toPlant = vines;
            _planted = 0;
            _metrics.Text = Metrics(layout, previewSeconds, memory);
        }
        if (_toPlant != null)
        {
            for (int n = 0; n < PlantedPerFrame && _planted < _toPlant.Count; n++)
                Plant(_toPlant[_planted++]);
            _status.Text = $"Standing the vines: {_planted} of {_toPlant.Count}";
            if (_planted >= _toPlant.Count)
            {
                _pods.VisibleInstanceCount = _podsDrawn;
                _status.Text = string.Create(CultureInfo.InvariantCulture, $"Generated in {_clock.Elapsed.TotalSeconds:F1} s. Play loads this nest.");
                _toPlant = null;
                _generate.Disabled = false;
                if (_checkPng != null)
                {
                    EditorInterface.Singleton.SetMainScreenEditor("Vines");
                    _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
                    _checkFrames = 30;
                }
            }
            return;
        }
        if (_checkFrames > 0 && --_checkFrames == 0)
        {
            FinishCheck();
            return;
        }
        if (_checkFrames > 0)
            return;
        SetProcess(false);
    }

    // The last nest gone, the ground sized for this one and the camera on it.
    private void Clear(NestLayout layout)
    {
        _nest?.QueueFree();
        _nest = new Node3D { Name = "Nest" };
        _viewport.AddChild(_nest);
        _layout = layout;
        float radius = layout.RadiusM + 80.0f;
        _ground.Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = 0.2f, RadialSegments = 96 };
        _ground.Position = new Vector3(0, -0.1f, 0);
        _pods = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CapsuleMesh { Radius = 0.35f, Height = 1.8f, RadialSegments = 6, Rings = 1 },
            InstanceCount = Math.Max(1, layout.Pods),
            VisibleInstanceCount = 0,
        };
        _podsDrawn = 0;
        _nest.AddChild(new MultiMeshInstance3D
        {
            Multimesh = _pods,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.62f, 0.18f), DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Toon },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        Frame();
        PlaceCamera();
    }

    private void Plant(NestPreview.Vine vine)
    {
        var root = new Node3D { Name = vine.Planned.Data?.Name ?? $"Vine{vine.Planned.Index}", Transform = vine.Place };
        if (vine.Mesh != null)
        {
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, vine.Mesh.ToGodot());
            vine.Mesh.Release();
            vine.Mesh = null;
            mesh.SurfaceSetMaterial(0, _voxelMaterial);
            root.AddChild(new MeshInstance3D { Mesh = mesh, Position = vine.MeshOffset });
        }
        if (vine.Leaves != null)
            root.AddChild(VineLeaves.Drawing(vine.Leaves, _leafMaterial, 0.35f));
        _nest.AddChild(root);
        foreach (Vector3 pod in vine.Pods)
        {
            if (_podsDrawn < _pods.InstanceCount)
                _pods.SetInstanceTransform(_podsDrawn++, new Transform3D(Basis.Identity, vine.Place * (pod - new Vector3(0, 0.9f, 0))));
        }
    }

    private static string Metrics(NestLayout layout, double previewSeconds, long memory)
    {
        var text = new StringBuilder();
        CultureInfo c = CultureInfo.InvariantCulture;
        text.AppendLine(string.Create(c, $"{layout.Vines.Count} vines, {layout.Pods:N0} pods for {layout.Population:N0} pickles{(layout.Short > 0 ? $" ({layout.Short:N0} short)" : "")}"));
        text.AppendLine("Kinds: " + layout.KindSummary());
        text.AppendLine(string.Create(c, $"Footprint {layout.RadiusM:F0} m from the heart, {layout.Vines.Count} of {layout.Slots} slots, {layout.Avenues.Length} avenues{(layout.Shifted ? ", shifted to high-pod kinds" : "")}"));
        List<Vector3> at = layout.Vines.Select(v => Vector3.Forward.Rotated(Vector3.Up, v.Bearing) * v.RadiusM).ToList();
        if (at.Count > 1)
        {
            var nearest = at.Select((p, i) => at.Where((_, j) => j != i).Min(q => q.DistanceTo(p))).ToList();
            text.AppendLine(string.Create(c, $"Spacing: nearest neighbours {nearest.Min():F0} m at least, {nearest.Average():F0} m on average"));
        }
        int rolls = layout.Vines.Sum(v => 1 + (v.Grown?.Attempt ?? 0));
        int rejected = layout.Vines.Sum(v => v.Grown?.Attempt ?? 0);
        text.AppendLine(string.Create(c, $"Rejected rolls: {rejected} of {rolls} ({(rolls > 0 ? rejected / (double)rolls : 0):P0})"));
        foreach (var reason in layout.Vines.Where(v => v.Grown != null).SelectMany(v => v.Grown.Rejected).SelectMany(r => r.Reasons)
                     .GroupBy(r => r).OrderByDescending(g => g.Count()).Take(4))
            text.AppendLine(string.Create(c, $"  {reason.Key}: {reason.Count()}"));
        text.AppendLine(string.Create(c, $"Grown in {layout.GrowSeconds:F1} s on every core; meshed for here in {previewSeconds:F1} s"));
        text.Append(string.Create(c, $"Memory: about {memory / 1048576.0:F0} MB managed (the game builds collision and chunks too: more)"));
        return text.ToString();
    }

    private void Play()
    {
        NestKit kit = Kit();
        if (kit == null)
            return;
        NestPlay.Write(new NestPlay.Request
        {
            Kit = kit.ResourcePath,
            Seed = (ulong)_seed.Value,
            Population = (int)_population.Value,
            Horde = _horde.ButtonPressed,
        });
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Playing nest {(ulong)_seed.Value} at {(int)_population.Value:N0}.");
        EditorInterface.Singleton.PlayCustomScene(LabPath);
    }
}
#endif
