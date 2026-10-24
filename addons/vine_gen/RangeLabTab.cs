#if TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DillControl.Core;
using DillControl.Foliage;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The Vines tool's range lab (docs/handoffs/vine-tool.md, "The tool"): pick a
/// kind, roll N seeds of it and see each one as it rolls, before any re-roll:
/// valid or not and why. Mark vines Good or Bad (the vine verdict log,
/// <see cref="VineLogs"/>); each setting's bar shows where valid, failed,
/// good and bad vines fall inside the kind's range, so a range can be
/// narrowed from evidence. Ranges are edited in place, rolled again, and
/// Saved to the kind's .tres.
/// </summary>
[Tool]
public partial class RangeLabTab : HSplitContainer
{
    private const int ThumbWidth = 150;
    private const int ThumbHeight = 200;

    private sealed class Entry
    {
        public int Seed;
        public VineParams Params;
        public VineGenerator.Result Vine;
        public VineValidator.Verdict Verdict;
        public NestPreview.Vine Preview;
        public bool? Judged;
        public PanelContainer Card;
        public TextureRect Thumb;
        public StyleBoxFlat Style;
    }

    private VineBakeStats _stats;
    private readonly List<VineKind> _kinds = new();
    private OptionButton _kind;
    private SpinBox _count;
    private SpinBox _seed;
    private Button _roll;
    private Button _saveRanges;
    private Label _summary;
    private LineEdit _note;
    private GridContainer _ranges;
    private GridContainer _cards;
    private readonly Dictionary<string, (Control Lo, Control Hi, SpreadBar Bar)> _rows = new();
    private bool _filling;

    private List<Entry> _entries = new();
    private Task<List<Entry>> _work;
    private int _rendering = -1;
    private int _renderFrames;
    private int _drawnAt;
    private Entry _selected;

    private SubViewport _thumbs;
    private Node3D _holder;
    private Camera3D _camera;
    private Material _voxelMaterial;
    private Material _leafMaterial;

    public override void _Ready()
    {
        if (GetChildCount() > 0)
            return;
        _voxelMaterial = GD.Load<Material>(VineBaker.Material);
        _leafMaterial = GD.Load<Material>(VineLeaves.MaterialPath);
        SplitOffset = 520;

        var leftScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(480, 0) };
        AddChild(leftScroll);
        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftScroll.AddChild(left);

        var kindRow = new HBoxContainer();
        kindRow.AddChild(new Label { Text = "Kind", CustomMinimumSize = new Vector2(60, 0) });
        _kind = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _kind.ItemSelected += _ => FillRanges();
        kindRow.AddChild(_kind);
        var reload = new Button { Text = "Reload", TooltipText = "Read the kinds again from their files, forgetting unsaved edits." };
        reload.Pressed += LoadKinds;
        kindRow.AddChild(reload);
        left.AddChild(kindRow);

        var rollRow = new HBoxContainer();
        rollRow.AddChild(new Label { Text = "Seeds" });
        _count = new SpinBox { MinValue = 4, MaxValue = 200, Step = 1, Rounded = true, Value = 48, TooltipText = "How many seeds to roll." };
        rollRow.AddChild(_count);
        rollRow.AddChild(new Label { Text = "from" });
        _seed = new SpinBox { MinValue = 0, MaxValue = int.MaxValue, Step = 1, Rounded = true, Value = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "The seeds are drawn from this one: the same number from the same one is the same set." };
        rollRow.AddChild(_seed);
        _roll = new Button { Text = "Roll", TooltipText = "Grow every seed with the ranges as they are now, and judge each." };
        _roll.Pressed += Roll;
        rollRow.AddChild(_roll);
        left.AddChild(rollRow);

        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddChild(_summary);
        _note = new LineEdit { PlaceholderText = "A note for the next Good or Bad", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddChild(_note);

        left.AddChild(new HSeparator());
        var rangesHead = new HBoxContainer();
        rangesHead.AddChild(new Label { Text = "Ranges  (bar: grey failed, pale valid, green good, red bad; shaded: the kind's range)", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart });
        _saveRanges = new Button { Text = "Save ranges", Disabled = true, TooltipText = "Write the edited ranges to the kind's .tres." };
        _saveRanges.Pressed += SaveRanges;
        rangesHead.AddChild(_saveRanges);
        left.AddChild(rangesHead);
        _ranges = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddChild(_ranges);
        BuildRanges();

        var rightScroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(rightScroll);
        _cards = new GridContainer { Columns = 6 };
        rightScroll.AddChild(_cards);
        rightScroll.Resized += () => _cards.Columns = Math.Max(1, (int)(rightScroll.Size.X / (ThumbWidth + 12)));

        BuildThumbViewport();
        Callable.From(LoadKinds).CallDeferred();
        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            if (argument.StartsWith("rangelab-check="))
                _checkPrefix = argument.Substring("rangelab-check=".Length);
        }
    }

    // Not in a container: it draws when asked, shown or not.
    private void BuildThumbViewport()
    {
        _thumbs = new SubViewport
        {
            OwnWorld3D = true, Size = new Vector2I(ThumbWidth, ThumbHeight), Msaa3D = Viewport.Msaa.Msaa2X,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_thumbs);
        _thumbs.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.62f, 0.72f, 0.82f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.6f, 0.65f, 0.7f),
                AmbientLightEnergy = 0.6f,
            },
        });
        var sun = new DirectionalLight3D { LightEnergy = 1.2f };
        sun.RotationDegrees = new Vector3(-50, 30, 0);
        _thumbs.AddChild(sun);
        _thumbs.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 20, BottomRadius = 20, Height = 0.1f, RadialSegments = 32 },
            Position = new Vector3(0, -0.05f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.47f, 0.36f) },
        });
        _holder = new Node3D();
        _thumbs.AddChild(_holder);
        _camera = new Camera3D { Fov = 40, Far = 1000 };
        _thumbs.AddChild(_camera);
    }

    private void BuildRanges()
    {
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            _ranges.AddChild(new Label { Text = field.Key, CustomMinimumSize = new Vector2(120, 0) });
            Control lo, hi;
            if (field.Bool)
            {
                lo = new SpinBox { MinValue = 0, MaxValue = 1, Step = 0.01, TooltipText = "Chance a vine of this kind has it." };
                ((SpinBox)lo).ValueChanged += _ => Edited(field);
                hi = new Control();
            }
            else
            {
                lo = Spin(field, "least");
                hi = Spin(field, "most");
            }
            _ranges.AddChild(lo);
            _ranges.AddChild(hi);
            var bar = new SpreadBar { Min = (float)field.Min, Max = (float)field.Max, CustomMinimumSize = new Vector2(140, 18), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            _ranges.AddChild(bar);
            _rows[field.Key] = (lo, hi, bar);
        }
    }

    private SpinBox Spin(VineParams.Field field, string end)
    {
        var spin = new SpinBox
        {
            MinValue = field.Min, MaxValue = field.Max, Step = field.Integer ? 1 : field.Step, Rounded = field.Integer,
            CustomMinimumSize = new Vector2(80, 0), TooltipText = $"The {end} a vine of this kind rolls.",
        };
        spin.ValueChanged += _ => Edited(field);
        return spin;
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
            if (ResourceLoader.Load($"{VineKind.Folder}/{file}", cacheMode: ResourceLoader.CacheMode.Replace) is not VineKind kind)
                continue;
            _kinds.Add(kind);
            _kind.AddItem(kind.KindName);
            if (kind.KindName == selected)
                _kind.Selected = _kinds.Count - 1;
        }
        if (_kinds.Count > 0 && _kind.Selected < 0)
            _kind.Selected = 0;
        _saveRanges.Disabled = true;
        FillRanges();
        if (_checkPrefix != null)
            Roll();
    }

    private VineKind Kind() => _kind != null && _kind.Selected >= 0 && _kind.Selected < _kinds.Count ? _kinds[_kind.Selected] : null;

    private void FillRanges()
    {
        VineKind kind = Kind();
        if (kind == null)
            return;
        _filling = true;
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            (Control lo, Control hi, SpreadBar bar) = _rows[field.Key];
            if (field.Bool)
            {
                ((SpinBox)lo).Value = kind.ChanceOf(field.Key);
                continue;
            }
            (double a, double b) = kind.RangeOf(field);
            ((SpinBox)lo).Value = a;
            ((SpinBox)hi).Value = b;
            bar.Lo = (float)a;
            bar.Hi = (float)b;
            bar.QueueRedraw();
        }
        _filling = false;
        Spread();
    }

    // A range edited: into the kind in memory, so the next Roll uses it.
    private void Edited(VineParams.Field field)
    {
        VineKind kind = Kind();
        if (_filling || kind == null)
            return;
        (Control lo, Control hi, SpreadBar bar) = _rows[field.Key];
        string property = char.ToUpperInvariant(field.Key[0]) + field.Key.Substring(1);
        if (field.Bool)
        {
            kind.Set(property + "Chance", (float)((SpinBox)lo).Value);
        }
        else
        {
            var range = new Vector2((float)((SpinBox)lo).Value, (float)((SpinBox)hi).Value);
            kind.Set(property, range);
            bar.Lo = range.X;
            bar.Hi = range.Y;
            bar.QueueRedraw();
        }
        _saveRanges.Disabled = false;
        _saveRanges.Text = "Save ranges *";
    }

    private void SaveRanges()
    {
        VineKind kind = Kind();
        if (kind == null)
            return;
        Error error = ResourceSaver.Save(kind, kind.ResourcePath);
        _summary.Text = error == Error.Ok
            ? $"Saved {kind.ResourcePath}. Baked vines and the golden vines are as they were: re-bake, then vinecheck.gd -- record, if the change is meant to move them."
            : $"Not saved: {error}.";
        _saveRanges.Disabled = error == Error.Ok;
        _saveRanges.Text = "Save ranges";
    }

    private void Roll()
    {
        VineKind kind = Kind();
        if (kind == null || _stats == null || _work != null)
            return;
        // Read here, with the edits; the rolls run on every core.
        VineGrower.Context context = VineGrower.Context.For(_stats, new[] { kind });
        VineKind.Ranges ranges = context.Kinds[kind];
        VineBakeStats stats = _stats;
        int count = (int)_count.Value;
        uint from = (uint)_seed.Value;
        _roll.Disabled = true;
        _summary.Text = "Rolling...";
        foreach (Node card in _cards.GetChildren())
            card.QueueFree();
        _selected = null;
        _work = Task.Run(() =>
        {
            var made = new Entry[count];
            Parallel.For(0, count, i =>
            {
                int seed = (int)(SeededRandom.Hash(from, "lab:" + i) % 10_000_000UL);
                VineParams p = kind.Roll(seed, context.VoxelSize, ranges);
                VineGenerator.Result vine = VineGenerator.Generate(p, context.Limits);
                VineValidator.Verdict verdict = VineValidator.Check(vine, stats, full: true, context.Spine);
                bool drawable = vine.Voxels.Count > 0 && vine.BoundsX <= 256 && vine.BoundsY <= 256 && vine.BoundsZ <= 256;
                made[i] = new Entry
                {
                    Seed = seed, Params = p, Vine = vine, Verdict = verdict,
                    Preview = drawable ? NestPreview.One(vine, kind, stats) : null,
                };
            });
            return made.ToList();
        });
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_work != null)
        {
            if (!_work.IsCompleted)
                return;
            if (_work.IsFaulted)
            {
                _summary.Text = "Rolling failed: " + _work.Exception?.GetBaseException().Message;
                _work = null;
                _roll.Disabled = false;
                return;
            }
            _entries = _work.Result;
            _work = null;
            foreach (Entry entry in _entries)
                _cards.AddChild(MakeCard(entry));
            Summarise();
            Spread();
            if (_checkPrefix != null)
            {
                EditorInterface.Singleton.SetMainScreenEditor("Vines");
                if (GetParent() is TabContainer tabs)
                    tabs.CurrentTab = GetIndex();
            }
            _rendering = 0;
            _renderFrames = 0;
        }
        if (_rendering >= 0)
        {
            RenderStep();
            return;
        }
        SetProcess(false);
    }

    // One thumbnail at a time: stood up and asked for in one frame, read once
    // a frame has really been drawn since (the editor draws only when
    // something changes: reading sooner, or with the tab hidden, would take
    // the last vine's picture).
    private void RenderStep()
    {
        if (_rendering >= _entries.Count)
        {
            _rendering = -1;
            _roll.Disabled = false;
            Summarise();
            if (_checkPrefix != null)
                FinishCheck();
            return;
        }
        Entry entry = _entries[_rendering];
        if (_renderFrames == 0)
        {
            foreach (Node child in _holder.GetChildren())
                child.QueueFree();
            if (entry.Preview == null)
            {
                _rendering++;
                return;
            }
            NestPreview.Vine vine = entry.Preview;
            if (vine.Mesh != null)
            {
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, vine.Mesh.ToGodot());
                vine.Mesh.Release();
                vine.Mesh = null;
                mesh.SurfaceSetMaterial(0, _voxelMaterial);
                _holder.AddChild(new MeshInstance3D { Mesh = mesh, Position = vine.MeshOffset });
            }
            if (vine.Leaves != null)
                _holder.AddChild(VineLeaves.Drawing(vine.Leaves, _leafMaterial, 0.35f));
            float height = (float)(entry.Vine.BoundsZ * entry.Vine.VoxelSize);
            var target = new Vector3(0, height * 0.48f, 0);
            Vector3 from = target + (new Vector3(0.75f, 0.12f, 0.65f).Normalized() * height * 1.55f);
            _camera.Transform = new Transform3D(Basis.LookingAt(target - from, Vector3.Up), from);
            _thumbs.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
            _drawnAt = Engine.GetFramesDrawn();
        }
        _summary.Text = $"Drawing {_rendering + 1} of {_entries.Count}...";
        _renderFrames++;
        if (Engine.GetFramesDrawn() - _drawnAt < 2)
        {
            // Nothing else may be changing: ask the editor for a frame.
            EditorInterface.Singleton.GetBaseControl().QueueRedraw();
            return;
        }
        entry.Thumb.Texture = ImageTexture.CreateFromImage(_thumbs.GetTexture().GetImage());
        _renderFrames = 0;
        _rendering++;
    }

    private Control MakeCard(Entry entry)
    {
        entry.Style = new StyleBoxFlat { BgColor = new Color(0.16f, 0.17f, 0.19f), BorderColor = new Color(0.3f, 0.3f, 0.3f) };
        entry.Style.SetBorderWidthAll(3);
        entry.Card = new PanelContainer();
        entry.Card.AddThemeStyleboxOverride("panel", entry.Style);
        var column = new VBoxContainer();
        entry.Card.AddChild(column);
        entry.Thumb = new TextureRect
        {
            CustomMinimumSize = new Vector2(ThumbWidth, ThumbHeight), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Stop,
            TooltipText = "Click: mark its settings on the bars.",
        };
        entry.Thumb.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                Select(entry);
        };
        column.AddChild(entry.Thumb);
        bool ok = entry.Verdict.Ok;
        string reasons = string.Join(", ", entry.Verdict.Reasons);
        column.AddChild(new Label
        {
            Text = $"{entry.Seed}  {(ok ? "valid" : "FAILED")}",
            Modulate = ok ? new Color(0.75f, 1.0f, 0.7f) : new Color(1.0f, 0.6f, 0.55f),
        });
        column.AddChild(new Label
        {
            Text = ok ? string.Create(CultureInfo.InvariantCulture, $"{entry.Vine.Counts.Markers} pods, {entry.Vine.BoundsZ * entry.Vine.VoxelSize:F0} m") : reasons,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(ThumbWidth, 0),
            TooltipText = string.Join("\n", entry.Verdict.Details),
        });
        var buttons = new HBoxContainer();
        var good = new Button { Text = "Good", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        good.Pressed += () => Judge(entry, true);
        var bad = new Button { Text = "Bad", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bad.Pressed += () => Judge(entry, false);
        buttons.AddChild(good);
        buttons.AddChild(bad);
        column.AddChild(buttons);
        return entry.Card;
    }

    private void Select(Entry entry)
    {
        if (_selected != null)
            _selected.Style.BorderColor = BorderOf(_selected);
        _selected = entry;
        entry.Style.BorderColor = new Color(1.0f, 0.85f, 0.3f);
        Spread();
    }

    private static Color BorderOf(Entry entry) => entry.Judged switch
    {
        true => new Color(0.4f, 0.85f, 0.35f),
        false => new Color(0.9f, 0.3f, 0.25f),
        _ => new Color(0.3f, 0.3f, 0.3f),
    };

    private void Judge(Entry entry, bool good)
    {
        VineKind kind = Kind();
        entry.Judged = good;
        entry.Style.BorderColor = entry == _selected ? new Color(1.0f, 0.85f, 0.3f) : BorderOf(entry);
        var settings = new Dictionary<string, object>();
        foreach (VineParams.Field field in VineParams.Fields)
            settings[field.Key] = field.Get(entry.Params);
        VineGenerator.Counts c = entry.Vine.Counts;
        VineValidator.Verdict v = entry.Verdict;
        VineLogs.Append(VineLogs.VineVerdicts, new Dictionary<string, object>
        {
            ["time"] = VineLogs.Now(),
            ["verdict"] = good ? "good" : "bad",
            ["note"] = _note.Text,
            ["kind"] = kind?.ResourcePath ?? "",
            ["seed"] = entry.Seed,
            ["attempt"] = 0,
            ["generator"] = VineLogs.Generator(),
            ["valid"] = v.Ok,
            ["reasons"] = v.Reasons,
            ["settings"] = settings,
            ["measures"] = new Dictionary<string, object>
            {
                ["voxels"] = c.Total,
                ["pods"] = c.Markers,
                ["leaves"] = c.Leaves,
                ["height_m"] = Math.Round(entry.Vine.BoundsZ * entry.Vine.VoxelSize, 2),
                ["lean_deg"] = Math.Round(v.LeanDeg, 2),
                ["kink_deg"] = Math.Round(v.KinkDeg, 3),
                ["sag_m"] = Math.Round(v.SagM, 2),
            },
        });
        _note.Text = "";
        Summarise();
        Spread();
    }

    private void Summarise()
    {
        if (_entries.Count == 0)
            return;
        int valid = _entries.Count(e => e.Verdict.Ok);
        var text = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
            $"{_entries.Count} rolled: {valid} valid on their own roll ({valid / (double)_entries.Count:P0}); {_entries.Count(e => e.Judged == true)} good, {_entries.Count(e => e.Judged == false)} bad"));
        foreach (var reason in _entries.SelectMany(e => e.Verdict.Reasons).GroupBy(r => r).OrderByDescending(g => g.Count()))
            text.Append(string.Create(CultureInfo.InvariantCulture, $"\n  {reason.Key}: {reason.Count()}"));
        if (_rendering < 0)
            _summary.Text = text.ToString();
    }

    // Every setting's bar: where each rolled vine's value falls.
    private void Spread()
    {
        foreach (VineParams.Field field in VineParams.RangeFields)
        {
            SpreadBar bar = _rows[field.Key].Bar;
            bar.Points.Clear();
            foreach (Entry entry in _entries)
            {
                Color colour = entry.Judged switch
                {
                    true => new Color(0.35f, 0.9f, 0.3f),
                    false => new Color(0.95f, 0.3f, 0.25f),
                    _ => entry.Verdict.Ok ? new Color(0.85f, 0.88f, 0.8f, 0.7f) : new Color(0.45f, 0.45f, 0.45f, 0.8f),
                };
                bar.Points.Add(((float)field.Get(entry.Params), colour, entry == _selected));
            }
            bar.QueueRedraw();
        }
    }

    // For checking the lab in the editor (--editor -- rangelab-check=<prefix>):
    // the first kind's seeds rolled and drawn, the first few thumbnails saved
    // as <prefix>_N.png, the first judged Good into a scratch log
    // (user://vine-logs-check), a summary printed, and the editor quit.
    private string _checkPrefix;

    private void FinishCheck()
    {
        VineLogs.Folder = "user://vine-logs-check";
        if (_entries.Count > 0)
            Judge(_entries[0], true);
        for (int i = 0; i < Math.Min(4, _entries.Count); i++)
            (_entries[i].Thumb.Texture as ImageTexture)?.GetImage()?.SavePng($"{_checkPrefix}_{i}.png");
        GD.Print($"[rangelab] {_summary.Text}\n[rangelab] {_entries.Count(e => e.Thumb.Texture != null)} thumbnails drawn");
        GetTree().Quit();
    }
}

/// <summary>A setting's whole field as a bar, the kind's range shaded, and a
/// tick for each rolled vine's value in its colour; a selected one taller.</summary>
[Tool]
public partial class SpreadBar : Control
{
    public float Min, Max, Lo, Hi;
    public readonly List<(float Value, Color Colour, bool Selected)> Points = new();

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.1f, 0.1f, 0.12f));
        float span = Mathf.Max(Max - Min, 1e-6f);
        float x0 = (Lo - Min) / span * size.X, x1 = (Hi - Min) / span * size.X;
        DrawRect(new Rect2(x0, 0, Mathf.Max(2, x1 - x0), size.Y), new Color(0.3f, 0.36f, 0.45f));
        foreach ((float value, Color colour, bool selected) in Points)
        {
            float x = (value - Min) / span * size.X;
            DrawRect(selected ? new Rect2(x - 1.5f, 0, 3, size.Y) : new Rect2(x - 1, size.Y * 0.25f, 2, size.Y * 0.5f),
                selected ? new Color(1.0f, 0.85f, 0.3f) : colour);
        }
    }
}
#endif
