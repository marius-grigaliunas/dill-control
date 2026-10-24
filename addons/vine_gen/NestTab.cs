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
///
/// After a play the tab shows how it went (<see cref="NestPlay.Result"/>).
/// Good or Bad, with a note, goes to the verdict log (<see cref="VineLogs"/>)
/// with what the nest measured, planned again if it was not generated here;
/// a Good nest can be Saved (<see cref="NestPreset"/>) and loaded back from
/// the Saved list.
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
    private CheckBox _ammo;
    private CheckBox _bike;
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
    // Which nest the shown layout is: kit, seed, population.
    private (string Kit, ulong Seed, int Population) _layoutKey;

    private OptionButton _saved;
    private readonly List<string> _savedPaths = new();
    private Label _played;
    private LineEdit _note;
    private Button _good;
    private Button _bad;
    private Button _save;
    private NestPlay.Result _playResult;
    private bool _awaitingPlay;
    private bool _sawPlaying;
    private bool? _pendingVerdict;
    private Task<NestLayout> _verdictPlan;
    private (string Kit, ulong Seed, int Population) _goodKey;
    private (string Kit, ulong Seed, int Population) _generatingKey;
    private string _presets = NestPreset.Folder;

    private (string Kit, ulong Seed, int Population) Key() => (Kit()?.ResourcePath ?? "", (ulong)_seed.Value, (int)_population.Value);

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
        var testing = new HBoxContainer();
        _ammo = new CheckBox { Text = "Infinite ammo", ButtonPressed = true, TooltipText = "For testing: every weapon kept full (magazines still reload)." };
        _bike = new CheckBox { Text = "Hoverbike", ButtonPressed = true, TooltipText = "For testing: a hoverbike parked beside you at the start." };
        testing.AddChild(_ammo);
        testing.AddChild(_bike);
        column.AddChild(testing);

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

        column.AddChild(new HSeparator());
        column.AddChild(new Label { Text = "Verdict" });
        _played = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = "Not played yet. Play it, then judge it; or judge what Generate showed." };
        column.AddChild(_played);
        _note = new LineEdit { PlaceholderText = "A note: what was good or bad about it", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_note);
        var verdicts = new HBoxContainer();
        _good = Button("Good", "Log this nest as good, with its measures and how the play went.", () => Verdict(true));
        _bad = Button("Bad", "Log this nest as bad. Bad nests are kept as carefully as good ones.", () => Verdict(false));
        _save = Button("Save", "Keep this Good nest: resources/mission/nests/, loadable from Saved.", Save);
        _save.Disabled = true;
        foreach (Button b in new[] { _good, _bad, _save })
        {
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            verdicts.AddChild(b);
        }
        column.AddChild(verdicts);
        _saved = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = "A saved nest: its kit, seed and population into the fields." };
        _saved.ItemSelected += LoadSaved;
        column.AddChild(Row("Saved", _saved));

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
        VineLogs.Folder = "user://vine-logs-check";
        _presets = "user://nests-check";
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        // The nest the last play result (if any) is of, else 5,000.
        _playResult = NestPlay.ReadResult();
        _seed.Value = _playResult?.Seed ?? 1;
        _population.Value = _playResult?.Population ?? 5000;
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
            GetTree().Root.GetTexture().GetImage().SavePng(_checkPng.Replace(".png", "_editor.png"));
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
        _note.Text = "check";
        Verdict(true);
        Save();
        LoadSavedList();
        LoadSaved(_saved.ItemCount - 1);
        GD.Print($"[nesttab] {_status.Text}");
        GD.Print($"[nesttab] log: {ProjectSettings.GlobalizePath(VineLogs.Folder)}");
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
        LoadSavedList();
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
        _generatingKey = Key();
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
        WatchPlay();
        WatchVerdictPlan();
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
            _layoutKey = _generatingKey;
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
        if (_checkFrames > 0 || _awaitingPlay || _verdictPlan != null)
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
            InfiniteAmmo = _ammo.ButtonPressed,
            Bike = _bike.ButtonPressed,
        });
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Playing nest {(ulong)_seed.Value} at {(int)_population.Value:N0}.");
        NestPlay.ClearResult();
        _playResult = null;
        _awaitingPlay = true;
        _sawPlaying = false;
        SetProcess(true);
        EditorInterface.Singleton.PlayCustomScene(LabPath);
    }

    private void WatchPlay()
    {
        if (!_awaitingPlay)
            return;
        if (EditorInterface.Singleton.IsPlayingScene())
        {
            _sawPlaying = true;
            return;
        }
        if (!_sawPlaying)
            return;
        _awaitingPlay = false;
        _playResult = NestPlay.ReadResult();
        _played.Text = _playResult == null ? "Played, but no result came back (left before the nest loaded?)." : Played(_playResult);
        _status.Text = "Played. Judge it: Good or Bad, with a note.";
    }

    private static string Played(NestPlay.Result r)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Create(c, $"Played nest {r.Seed} at {r.Population:N0}{(r.Horde ? "" : ", no horde")} for {r.PlayedSeconds / 60:F0}:{r.PlayedSeconds % 60:00}\n")
            + string.Create(c, $"{r.Swings} swings, {r.GroundTouches} times on the ground, heart reached {(r.SecondsToHeart < 0 ? "never" : string.Create(c, $"after {r.SecondsToHeart:F0} s"))}\n")
            + string.Create(c, $"Pods destroyed {r.PodsDestroyed:N0} of {r.Pods:N0}; pickles left {r.PicklesLeft:N0} of {r.PicklesStarting:N0} ({r.KilledOnPods:N0} killed hanging)\n")
            + $"The colony {(r.Woke ? "woke" : "slept")}; {r.Outcome}{(r.Bonus ? ", with the bonus" : "")}; pickled {r.TimesPickled} times";
    }

    // Good or Bad, for the nest in the fields: with its measures (planned
    // again if the shown one is another nest) and the play, if it was this one.
    private void Verdict(bool good)
    {
        if (Kit() == null || _verdictPlan != null)
            return;
        _pendingVerdict = good;
        if (_layout != null && _layoutKey == Key())
        {
            WriteVerdict(_layout);
            return;
        }
        NestKit kit = Kit();
        VineGrower.Context context = VineGrower.Context.For(kit.BakeOrDefault(), kit.Kinds);
        (string _, ulong seed, int population) = Key();
        _verdictPlan = Task.Run(() => NestLayout.Plan(kit, context, seed, population));
        _good.Disabled = _bad.Disabled = true;
        _status.Text = "Planning the nest again for its measures...";
        SetProcess(true);
    }

    private void WatchVerdictPlan()
    {
        if (_verdictPlan == null || !_verdictPlan.IsCompleted)
            return;
        Task<NestLayout> plan = _verdictPlan;
        _verdictPlan = null;
        _good.Disabled = _bad.Disabled = false;
        if (plan.IsFaulted)
        {
            _status.Text = "Not logged: planning failed, " + plan.Exception?.GetBaseException().Message;
            return;
        }
        WriteVerdict(plan.Result);
    }

    private void WriteVerdict(NestLayout layout)
    {
        bool good = _pendingVerdict ?? false;
        _pendingVerdict = null;
        (string kit, ulong seed, int population) = Key();
        NestPlay.Result play = _playResult != null && _playResult.Kit == kit && _playResult.Seed == seed && _playResult.Population == population
            ? _playResult : null;
        var record = new Dictionary<string, object>
        {
            ["time"] = VineLogs.Now(),
            ["verdict"] = good ? "good" : "bad",
            ["note"] = _note.Text,
            ["kit"] = kit,
            ["seed"] = seed,
            ["population"] = population,
            ["horde"] = _horde.ButtonPressed,
            ["generator"] = VineLogs.Generator(),
            ["plan"] = PlanMeasures(layout),
            ["play"] = play == null ? null : PlayMeasures(play),
        };
        VineLogs.Append(VineLogs.NestVerdicts, record);
        _goodKey = good ? Key() : default;
        _save.Disabled = !good;
        _status.Text = $"Logged as {(good ? "good" : "bad")} in {VineLogs.Folder}/{VineLogs.NestVerdicts}{(play == null ? " (not played)" : "")}." + (good ? " Save keeps it." : "");
    }

    private static Dictionary<string, object> PlanMeasures(NestLayout layout)
    {
        List<Vector3> at = layout.Vines.Select(v => Vector3.Forward.Rotated(Vector3.Up, v.Bearing) * v.RadiusM).ToList();
        List<float> nearest = at.Count > 1 ? at.Select((p, i) => at.Where((_, j) => j != i).Min(q => q.DistanceTo(p))).ToList() : new List<float> { 0 };
        return new Dictionary<string, object>
        {
            ["vines"] = layout.Vines.Count,
            ["pods"] = layout.Pods,
            ["short"] = layout.Short,
            ["radius_m"] = Math.Round(layout.RadiusM, 1),
            ["slots"] = layout.Slots,
            ["slots_used"] = layout.Vines.Count,
            ["avenues"] = layout.Avenues.Length,
            ["shifted"] = layout.Shifted,
            ["kinds"] = layout.Vines.GroupBy(v => v.Kind?.Slug ?? "?").ToDictionary(g => g.Key, g => (object)new Dictionary<string, object>
            {
                ["vines"] = g.Count(),
                ["mean_pods"] = Math.Round(g.Average(v => v.Pods), 1),
            }),
            ["spacing_min_m"] = Math.Round(nearest.Min(), 1),
            ["spacing_mean_m"] = Math.Round(nearest.Average(), 1),
            ["rolls"] = layout.Vines.Sum(v => 1 + (v.Grown?.Attempt ?? 0)),
            ["rejected"] = layout.Vines.Sum(v => v.Grown?.Attempt ?? 0),
            ["reasons"] = layout.Vines.Where(v => v.Grown != null).SelectMany(v => v.Grown.Rejected).SelectMany(r => r.Reasons)
                .GroupBy(r => r).ToDictionary(g => g.Key, g => (object)g.Count()),
            ["grow_seconds"] = Math.Round(layout.GrowSeconds, 2),
        };
    }

    private static Dictionary<string, object> PlayMeasures(NestPlay.Result r) => new()
    {
        ["played_seconds"] = Math.Round(r.PlayedSeconds, 1),
        ["swings"] = r.Swings,
        ["seconds_to_heart"] = Math.Round(r.SecondsToHeart, 1),
        ["ground_touches"] = r.GroundTouches,
        ["pods"] = r.Pods,
        ["pods_destroyed"] = r.PodsDestroyed,
        ["pickles_starting"] = r.PicklesStarting,
        ["pickles_left"] = r.PicklesLeft,
        ["killed_on_pods"] = r.KilledOnPods,
        ["woke"] = r.Woke,
        ["outcome"] = r.Outcome,
        ["bonus"] = r.Bonus,
        ["times_pickled"] = r.TimesPickled,
    };

    // Only a nest just judged Good is kept.
    private void Save()
    {
        NestKit kit = Kit();
        if (kit == null || _goodKey != Key())
        {
            _status.Text = "Only a nest just judged Good can be saved.";
            return;
        }
        (string _, ulong seed, int population) = Key();
        var preset = new NestPreset
        {
            Kit = kit,
            Seed = (long)seed,
            Population = population,
            Horde = _horde.ButtonPressed,
            Note = _note.Text,
            Saved = VineLogs.Now(),
            Generator = VineLogs.Generator(),
        };
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_presets));
        string path = string.Create(CultureInfo.InvariantCulture, $"{_presets}/nest_{kit.ResourcePath.GetFile().GetBaseName().Replace("nest_kit_", "")}_{seed}_{population}.tres");
        Error error = ResourceSaver.Save(preset, path);
        _status.Text = error == Error.Ok ? $"Saved {path}." : $"Not saved: {error}.";
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        LoadSavedList();
    }

    private void LoadSavedList()
    {
        _savedPaths.Clear();
        _saved.Clear();
        _saved.AddItem("(none)");
        if (!DirAccess.DirExistsAbsolute(_presets))
            return;
        foreach (string file in DirAccess.GetFilesAt(_presets))
        {
            if (!file.EndsWith(".tres"))
                continue;
            _savedPaths.Add($"{_presets}/{file}");
            _saved.AddItem(file.GetBaseName());
        }
    }

    private void LoadSaved(long index)
    {
        if (index < 1 || index > _savedPaths.Count)
            return;
        if (ResourceLoader.Load(_savedPaths[(int)index - 1], cacheMode: ResourceLoader.CacheMode.Replace) is not NestPreset preset)
            return;
        int kit = _kits.FindIndex(k => k.ResourcePath == preset.Kit?.ResourcePath);
        if (kit >= 0)
        {
            _kit.Selected = kit;
            KitChanged();
        }
        _seed.Value = preset.Seed;
        _population.Value = preset.Population;
        _horde.ButtonPressed = preset.Horde;
        _note.Text = preset.Note;
        _status.Text = $"Loaded {preset.ResourcePath.GetFile()}: Generate to see it, Play to play it.";
    }
}
#endif
