#if TOOLS
using System.Collections.Generic;
using System.Linq;
using DillControl.Planet;
using DillControl.Vehicles;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's main screen (docs/decisions/0047): the chosen asset in a
/// 3D view of its own. Every asset is listed in <see cref="AssetList"/>, a
/// walker's moves play in <see cref="TimelineBox"/>, and what it is, what it
/// costs and the resources it is tuned by are in <see cref="TuneBox"/>, an
/// inspector whose Save writes the shared resource after naming who else
/// uses it. The plugin puts those three in docks of their own (the left and
/// right tab groups and the bottom panel), so the view gets the screen.
/// </summary>
[Tool]
public partial class AssetLabPanel : HSplitContainer
{
    // Made by _Ready in this load of the code. A code reload restores what
    // Godot can keep of the old instance, never this, so the old one, which
    // runs a frame before the plugin builds anew, does nothing.
    private sealed class Built { }
    private Built _built;

    private bool Live => _built != null;

    public LabPreview Preview { get; private set; }

    /// <summary>The asset list, for a dock: not a child of the panel.</summary>
    public Control AssetList { get; private set; }

    /// <summary>The play bar, the timeline and the timing row, for the
    /// bottom panel: not a child of the panel.</summary>
    public Control TimelineBox { get; private set; }

    /// <summary>Info and Parameters, for a dock: not a child of the panel.</summary>
    public Control TuneBox { get; private set; }

    /// <summary>Raised on every asset shown: whether it has a timeline (a walker).</summary>
    public event System.Action<bool> TimelineWanted;
    public string Current { get; private set; } = "";
    public AssetInfo Info { get; private set; }
    public List<Resource> Resources { get; private set; } = new();

    private LineEdit _filter;
    private Tree _tree;
    private RichTextLabel _info;
    private OptionButton _resources;
    private EditorInspector _inspector;
    private Label _status;
    private OptionButton _look;
    private ConfirmationDialog _confirm;
    private readonly List<PlanetBiome> _biomes = new();

    private const string MetaSection = "asset_lab";

    public override void _Ready()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) };
        _filter = new LineEdit { PlaceholderText = "Filter assets", ClearButtonEnabled = true };
        _filter.TextChanged += _ => FillTree();
        left.AddChild(_filter);
        _tree = new Tree { HideRoot = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _tree.ItemSelected += () =>
        {
            if (_tree.GetSelected()?.GetMetadata(0).AsString() is string path && path != "")
            {
                Open(path);
            }
        };
        left.AddChild(_tree);
        AssetList = left;

        var middleAndRight = new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(middleAndRight);

        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var bar = new HBoxContainer();
        bar.AddChild(Button("Frame", () => Preview.Frame()));
        var ink = new CheckBox { Text = "Ink", ButtonPressed = true };
        ink.Toggled += on => Preview.Ink = on;
        bar.AddChild(ink);
        var map = new CheckBox { Text = "Damage map", ButtonPressed = true };
        map.Toggled += on => Preview.DamageMap = on;
        bar.AddChild(map);
        _look = new OptionButton();
        _look.AddItem("Lab light");
        foreach (string file in DirAccess.GetFilesAt("res://resources/planet/biomes").Where(f => f.EndsWith(".tres")))
        {
            if (GD.Load<Resource>($"res://resources/planet/biomes/{file}") is PlanetBiome biome)
            {
                _biomes.Add(biome);
                _look.AddItem(file.GetBaseName().Replace("biome_", ""));
            }
        }
        _look.ItemSelected += index => Preview.SetLook(index == 0 ? null : _biomes[(int)index - 1]);
        bar.AddChild(_look);
        bar.AddChild(new Label { Text = "Sun" });
        var sun = new HSlider { MinValue = 2, MaxValue = 90, Value = 45, CustomMinimumSize = new Vector2(100, 0) };
        sun.ValueChanged += value => Preview.SunElevation = (float)value;
        bar.AddChild(sun);
        bar.AddChild(new VSeparator());
        var board = new CheckBox { Text = "Ride", ButtonPressed = true, TooltipText = "Test puts you in its seat if it is a vehicle, or steers a walker" };
        bar.AddChild(board);
        bar.AddChild(Button("Test", async () =>
        {
            if (Current != "")
            {
                await AssetLabCommands.StopAndWait();
                AssetLabCommands.Play(Current, board.ButtonPressed);
            }
        }));
        middle.AddChild(bar);
        // A walker walks in the view, on a treadmill, without Test.
        _walkBar = new HBoxContainer { Visible = false };
        _walkBar.AddChild(new Label
        {
            Text = "Walk speed",
            TooltipText = "Click the view, then WASD walks it, Q/E turn, Shift sprints.\nClick a leg or an arm to take it off, again to put it back.",
            MouseFilter = MouseFilterEnum.Stop,
        });
        var speed = new HSlider { MinValue = 0.05, MaxValue = 1, Step = 0.05, Value = 1, CustomMinimumSize = new Vector2(120, 0), TooltipText = "The share of its top speed WASD and the treadmill walk it at" };
        speed.ValueChanged += value =>
        {
            Preview.WalkSpeed = (float)value;
            Rerecord();
        };
        _walkBar.AddChild(speed);
        var treadmill = new CheckBox { Text = "Treadmill", TooltipText = "Walks forward at that speed with no key held" };
        treadmill.Toggled += on => Preview.Treadmill = on;
        _walkBar.AddChild(treadmill);
        _walkBar.AddChild(Button("Reset", ResetAsset, "Puts it back as it was built: every limb on, standing still"));
        // Text that changes never widens the bar: that pushed the editor about.
        _stance = new Label { ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _walkBar.AddChild(_stance);
        middle.AddChild(_walkBar);
        Preview = new LabPreview();
        Preview.LimbsChanged += Rerecord;
        middle.AddChild(Preview);

        // Its moves, played, paused, scrubbed and slowed.
        _playBar = new HBoxContainer { Visible = false };
        _clips = new OptionButton { TooltipText = "What plays: the walk alone, the combo as it flows in play, or one move (read fresh from disk)" };
        _clips.ItemSelected += index => LoadClip(_clips.GetItemText((int)index));
        _playBar.AddChild(_clips);
        _playBar.AddChild(Button("|◀", () => Preview.StepFrames(-1)));
        _play = Button("Pause", () =>
        {
            LabTransport transport = Preview.Transport;
            if (!transport.Playing && transport.HasClip && transport.Time >= transport.Length)
            {
                transport.Time = 0.0f;
            }
            transport.Playing = !transport.Playing;
        });
        _play.CustomMinimumSize = new Vector2(60, 0);
        _playBar.AddChild(_play);
        _playBar.AddChild(Button("▶|", () => Preview.StepFrames(1)));
        _loop = new CheckBox { Text = "Loop", ButtonPressed = true };
        _loop.Toggled += on => Preview.Transport.Loop = on;
        _playBar.AddChild(_loop);
        _playBar.AddChild(new Label { Text = "Time" });
        _timeScale = new HSlider { MinValue = 0.1, MaxValue = 1, Step = 0.05, Value = 1, CustomMinimumSize = new Vector2(100, 0), TooltipText = "How fast time runs: 1 is the game's" };
        _timeScale.ValueChanged += value => Preview.TimeScale = (float)value;
        _playBar.AddChild(_timeScale);
        _trails = new CheckBox { Text = "Trails", ButtonPressed = true, TooltipText = "The fists' paths through the clip, a red cross where each blow lands" };
        _trails.Toggled += on => Preview.Trails = on;
        _playBar.AddChild(_trails);
        _ghosts = new CheckBox { Text = "Ghosts", TooltipText = "See-through poses before (blue) and after (orange) the playhead" };
        _ghosts.Toggled += on => Preview.Ghosts = on;
        _playBar.AddChild(_ghosts);
        _clock = new Label { ClipText = true, CustomMinimumSize = new Vector2(230, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _playBar.AddChild(_clock);
        var timelineBox = new VBoxContainer { CustomMinimumSize = new Vector2(0, 110) };
        timelineBox.AddChild(_playBar);
        _timeline = new LabTimeline { Preview = Preview, Visible = false };
        _timeline.MarkerMoved += SetMarker;
        timelineBox.AddChild(_timeline);

        // The timing of the move under the playhead, edited here and saved
        // to its .tres; poses stay in Blender, which reads the timing back.
        _timingBar = new HBoxContainer { Visible = false };
        _timingName = new Label();
        _timingBar.AddChild(_timingName);
        _timingBar.AddChild(new Label { Text = "Speed ×" });
        _speed = new SpinBox { MinValue = 0.25, MaxValue = 4, Step = 0.05, Value = 1, TooltipText = "Plays it faster or slower: its frames and markers keep their places" };
        _speed.ValueChanged += value => { if (TimingMove != null) SetSpeed(TimingMove, (float)value); };
        _timingBar.AddChild(_speed);
        _timingBar.AddChild(new Label { Text = "Blend" });
        _blend = new SpinBox { MinValue = -0.01, MaxValue = 1, Step = 0.01, Value = -0.01, Suffix = "s", TooltipText = "Seconds it eases in and out over; below 0 the player's (the combo's chain blend)" };
        _blend.ValueChanged += value => { if (TimingMove != null) SetBlend(TimingMove, value < 0 ? -1.0f : (float)value); };
        _timingBar.AddChild(_blend);
        _timingBar.AddChild(Button("Save timing", () => SaveTiming()));
        _timingBar.AddChild(Button("Revert", RevertTiming));
        _timingStatus = new Label { TooltipText = "Drag a marker on the timeline to move it", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _timingBar.AddChild(_timingStatus);
        timelineBox.AddChild(_timingBar);

        // How the whole body goes with the move under the playhead: worked
        // out from its fists, or set here and saved with its timing.
        _bodyBar = new HBoxContainer { Visible = false };
        _bodyBar.AddChild(new Label { Text = "Body:" });
        _bodyAuto = new CheckBox { Text = "Auto", TooltipText = "Worked out from how its fists move at the hit, in the mech's Strikes tuning. Edit a number to set it by hand." };
        _bodyAuto.Toggled += on => { if (TimingMove != null) SetBody(TimingMove, on ? null : BodyOf(TimingMove), on); };
        _bodyBar.AddChild(_bodyAuto);
        (string Name, double Min, double Max, double Step, string Suffix, string Tip)[] fields =
        {
            ("Lunge", 0, 3, 0.05, "m", "How far the body goes toward the blow at the hit"),
            ("Drop", -1, 2, 0.05, "m", "How far it drops its weight at the hit; below 0 it drives up"),
            ("Lean", -45, 45, 1, "°", "How far it leans into the blow; below 0 back"),
            ("Twist", 0, 90, 1, "°", "How far a one-armed blow turns the hips, striking side forward"),
            ("Step", 0, 4, 0.05, "m", "How far the foot opposite the striking arm steps in, landing on the hit"),
            ("Windup", 0, 1, 0.05, "", "Share of all that the wind-up goes the other way first"),
        };
        for (int i = 0; i < fields.Length; i++)
        {
            int field = i;
            _bodyBar.AddChild(new Label { Text = fields[i].Name });
            var spin = new SpinBox { MinValue = fields[i].Min, MaxValue = fields[i].Max, Step = fields[i].Step, Suffix = fields[i].Suffix, TooltipText = fields[i].Tip };
            spin.ValueChanged += value => SetBodyField(field, (float)value);
            _bodySpins[i] = spin;
            _bodyBar.AddChild(spin);
        }
        timelineBox.AddChild(_bodyBar);
        TimelineBox = timelineBox;
        middleAndRight.AddChild(middle);

        // What it is on top, scrolling in its own box; the parameters below
        // get the rest, and the split between them drags.
        // Two sections that fold: what it is, and its parameters. Both open,
        // the split between them drags; one folded, the other has the room.
        var right = new VSplitContainer { CustomMinimumSize = new Vector2(280, 0), SplitOffset = 220 };
        var infoScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 80), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        _info = new RichTextLabel { BbcodeEnabled = true, FitContent = true, SelectionEnabled = true, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        infoScroll.AddChild(_info);
        var tune = new VBoxContainer { CustomMinimumSize = new Vector2(0, 200), SizeFlagsVertical = SizeFlags.ExpandFill };
        _resources = new OptionButton();
        _resources.ItemSelected += index => _inspector.Edit(index >= 0 && index < Resources.Count ? Resources[(int)index] : null);
        tune.AddChild(_resources);
        _inspector = new EditorInspector { SizeFlagsVertical = SizeFlags.ExpandFill };
        tune.AddChild(_inspector);
        var buttons = new HBoxContainer();
        buttons.AddChild(Button("Save", AskSave));
        buttons.AddChild(Button("Revert", Revert));
        tune.AddChild(buttons);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        tune.AddChild(_status);
        _infoSection = new Section("Info", infoScroll, "info_open");
        _tuneSection = new Section("Parameters", tune, "tune_open");
        _infoSection.Toggled += () => ArrangeSections(right);
        _tuneSection.Toggled += () => ArrangeSections(right);
        right.AddChild(_infoSection);
        right.AddChild(_tuneSection);
        ArrangeSections(right);
        TuneBox = right;

        _confirm = new ConfirmationDialog { Title = "Save a shared resource", OkButtonText = "Save" };
        _confirm.Confirmed += Save;
        AddChild(_confirm);

        _built = new Built();
        FillTree();
        string last = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata(MetaSection, "asset", "").AsString();
        if (last != "" && ResourceLoader.Exists(last))
        {
            CallDeferred(MethodName.Open, last);
        }
    }

    private HBoxContainer _walkBar;
    private Label _stance;
    private HBoxContainer _playBar;
    private OptionButton _clips;
    private Button _play;
    private CheckBox _loop;
    private HSlider _timeScale;
    private CheckBox _trails;
    private CheckBox _ghosts;
    private Label _clock;

    /// <summary>The trails and ghosts ticks, set as the bridge sets them.</summary>
    public void ShowTrails(bool on) => _trails.ButtonPressed = on;

    public void ShowGhosts(bool on) => _ghosts.ButtonPressed = on;
    private LabTimeline _timeline;

    private HBoxContainer _timingBar;
    private Label _timingName;
    private SpinBox _speed;
    private SpinBox _blend;
    private Label _timingStatus;
    private MechMove _shownTiming;
    private HBoxContainer _bodyBar;
    private CheckBox _bodyAuto;
    private readonly SpinBox[] _bodySpins = new SpinBox[6];

    /// <summary>The body numbers <paramref name="move"/> plays with: its
    /// own, or those the walker works out from its fists.</summary>
    public MechMove.BodyNumbers BodyOf(MechMove move) =>
        move.BodyAuto && Preview.Walker != null ? Preview.Walker.AutoBody(move) : move.Body;

    /// <summary>Sets how the body goes with <paramref name="move"/>:
    /// <paramref name="auto"/> from its fists, else these numbers (null keeps
    /// what it has). Saved with Save timing.</summary>
    public void SetBody(MechMove move, MechMove.BodyNumbers? numbers, bool auto)
    {
        BaseOf(move);
        if (numbers is MechMove.BodyNumbers set)
        {
            move.Body = set;
        }
        move.BodyAuto = auto;
        TimingChanged();
        ShowBody(move);
    }

    private void SetBodyField(int field, float value)
    {
        MechMove move = TimingMove;
        if (move == null)
        {
            return;
        }
        MechMove.BodyNumbers n = BodyOf(move);
        n = field switch
        {
            0 => n with { Lunge = value },
            1 => n with { Drop = value },
            2 => n with { LeanDegrees = value },
            3 => n with { TwistDegrees = value },
            4 => n with { Step = value },
            _ => n with { Windup = value },
        };
        SetBody(move, n, false);
    }

    private void ShowBody(MechMove move)
    {
        MechMove.BodyNumbers n = BodyOf(move);
        float[] values = { n.Lunge, n.Drop, n.LeanDegrees, n.TwistDegrees, n.Step, n.Windup };
        for (int i = 0; i < values.Length; i++)
        {
            _bodySpins[i].SetValueNoSignal(values[i]);
        }
        _bodyAuto.SetPressedNoSignal(move.BodyAuto);
    }

    // Moves whose timing was edited and not saved: each with its frame rate
    // and markers before the speed was changed, so speed is always against those.
    private readonly Dictionary<MechMove, (float Fps, float[] Times)> _timingBase = new();

    /// <summary>The move whose timing the row edits: the one under the playhead.</summary>
    public MechMove TimingMove
    {
        get
        {
            LabTransport transport = Preview.Transport;
            if (transport.Segments.Count == 0)
            {
                return null;
            }
            return transport.At(transport.Time, out _)?.Move ?? transport.Segments[^1].Move;
        }
    }

    private (float Fps, float[] Times) BaseOf(MechMove move)
    {
        if (!_timingBase.TryGetValue(move, out var timing))
        {
            timing = (move.Fps, (float[])move.EventTimes.Clone());
            _timingBase[move] = timing;
        }
        return timing;
    }

    public float SpeedOf(MechMove move) => _timingBase.TryGetValue(move, out var timing) ? move.Fps / timing.Fps : 1.0f;

    /// <summary>Plays it <paramref name="speed"/> times as fast: its frame
    /// rate up, its markers' times down, each against what was saved.</summary>
    public void SetSpeed(MechMove move, float speed)
    {
        var timing = BaseOf(move);
        move.Fps = timing.Fps * speed;
        move.EventTimes = timing.Times.Select(t => t / speed).ToArray();
        TimingChanged();
    }

    public void SetBlend(MechMove move, float blend)
    {
        BaseOf(move);
        move.Blend = blend;
        TimingChanged();
    }

    /// <summary>Puts marker <paramref name="index"/> at <paramref name="time"/>
    /// seconds into the move.</summary>
    public void SetMarker(MechMove move, int index, float time)
    {
        var timing = BaseOf(move);
        float speed = move.Fps / timing.Fps;
        float[] times = (float[])move.EventTimes.Clone();
        times[index] = time;
        move.EventTimes = times;
        timing.Times[index] = time * speed;
        TimingChanged();
    }

    /// <summary>The clip laid out again from the edited moves.</summary>
    private void TimingChanged()
    {
        LabTransport transport = Preview.Transport;
        if (transport.Clip == ComboClip && Resources.OfType<MechSlashStats>().FirstOrDefault() is MechSlashStats stats)
        {
            transport.Rebuild(LabTransport.Combo(stats));
        }
        else if (transport.Segments.Count > 0)
        {
            transport.Rebuild(LabTransport.Single(transport.Segments[0].Move));
        }
        Preview.RebuildTrails();
        _timingStatus.Text = "  unsaved: " + string.Join(", ", _timingBase.Keys.Select(LabTimeline.ShortName));
    }

    /// <summary>Writes every edited move's .tres, stamped so the Blender
    /// add-on knows to read the timing back. Returns the files written.</summary>
    public List<string> SaveTiming()
    {
        var saved = new List<string>();
        string stamp = System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        foreach (MechMove move in _timingBase.Keys)
        {
            move.TimingStamp = stamp;
            Error error = ResourceSaver.Save(move, move.ResourcePath);
            if (error != Error.Ok)
            {
                _timingStatus.Text = $"  could not save {move.ResourcePath}: {error}";
                return saved;
            }
            saved.Add(move.ResourcePath);
        }
        _timingBase.Clear();
        _shownTiming = null;
        _timingStatus.Text = saved.Count == 0 ? "  nothing changed" : $"  saved {saved.Count}; in Blender, Read Timing before exporting again";
        return saved;
    }

    /// <summary>Every edited move back as saved.</summary>
    public void RevertTiming()
    {
        foreach (MechMove move in _timingBase.Keys)
        {
            Fresh(move.ResourcePath);
        }
        _timingBase.Clear();
        _shownTiming = null;
        TimingChanged();
        _timingStatus.Text = "  as saved";
    }

    public const string WalkOnly = "Walk only";
    public const string ComboClip = "Combo";
    public const string WalkCycle = "Walk cycle";
    public const string SprintCycle = "Sprint cycle";

    public override void _Process(double delta)
    {
        if (!Live || !_walkBar.Visible || Preview.Walker == null || !IsInstanceValid(Preview.Walker))
        {
            return;
        }
        string text = $"  {Preview.Walker.CurrentStance}" + (Preview.LastPick != "" ? $"  ({Preview.LastPick})" : "");
        if (_stance.Text != text)
        {
            _stance.Text = text;
        }
        LabTransport transport = Preview.Transport;
        _play.Text = transport.Playing ? "Pause" : "Play";
        string clock = !transport.HasClip
            ? ""
            : $"  {transport.Time:0.00} / {transport.Length:0.00} s  frame {Mathf.RoundToInt(transport.Time * transport.Fps)}";
        if (_clock.Text != clock)
        {
            _clock.Text = clock;
        }
        _timeline.QueueRedraw();
        _timingBar.Visible = transport.Segments.Count > 0;
        _bodyBar.Visible = _timingBar.Visible;
        MechMove move = TimingMove;
        if (move != _shownTiming)
        {
            _shownTiming = move;
            if (move != null)
            {
                _timingName.Text = $"Timing of {LabTimeline.ShortName(move)}:";
                _speed.SetValueNoSignal(SpeedOf(move));
                _blend.SetValueNoSignal(move.Blend < 0.0f ? -0.01 : move.Blend);
                ShowBody(move);
            }
        }
    }

    /// <summary>The walker's own moves, by file: those posed on its .vox.</summary>
    public List<string> MovesOf(MechWalker walker)
    {
        string mech = MechName(walker);
        const string folder = "res://resources/vehicles/moves";
        return DirAccess.GetFilesAt(folder)
            .Where(f => f.EndsWith(".tres") && f.StartsWith(mech + "_"))
            .Select(f => $"{folder}/{f}")
            .ToList();
    }

    private static string MechName(MechWalker walker) => walker?.Model?.SceneFilePath.GetFile().GetBaseName() ?? "";

    /// <summary>Plays a clip on the walker: <see cref="WalkOnly"/>,
    /// <see cref="ComboClip"/>, or a move by its short name. Moves are read
    /// fresh from disk, so a re-export from Blender shows. False if there is
    /// no such clip.</summary>
    public bool LoadClip(string clip)
    {
        MechWalker walker = Preview.Walker;
        if (walker == null)
        {
            return false;
        }
        if (clip == WalkCycle || clip == SprintCycle)
        {
            // Recorded now, with what is left of it, at the walk speed set.
            LabRecording recording = LabRecording.Record(walker, new Vector2(0.0f, Preview.WalkSpeed), clip == SprintCycle);
            Preview.Transport.Load(clip, recording);
            Preview.Transport.Loop = _loop.ButtonPressed;
            Preview.RebuildTrails();
            SelectClip(clip);
            _timeline.Visible = true;
            return true;
        }
        List<LabTransport.Segment> segments;
        if (clip == WalkOnly)
        {
            segments = new();
        }
        else if (clip == ComboClip)
        {
            if (Resources.OfType<MechSlashStats>().FirstOrDefault() is not MechSlashStats stats)
            {
                return false;
            }
            foreach (MechMove move in stats.Combo)
            {
                // Fresh from disk, unless its timing is being edited here.
                if (move != null && !_timingBase.ContainsKey(move))
                {
                    Fresh(move.ResourcePath);
                }
            }
            segments = LabTransport.Combo(stats);
        }
        else
        {
            string mech = MechName(walker);
            string path = MovesOf(walker).FirstOrDefault(p => p.GetFile().GetBaseName() == $"{mech}_{clip}" || p.GetFile().GetBaseName() == clip);
            bool edited = _timingBase.Keys.Any(m => m.ResourcePath == path);
            if (path == null || (edited ? ResourceLoader.Load(path) : Fresh(path)) is not MechMove move)
            {
                return false;
            }
            segments = LabTransport.Single(move);
        }
        walker.LabSeekMove(null, 0.0f, 0.0f);
        Preview.Transport.Load(clip, segments);
        Preview.Transport.Loop = _loop.ButtonPressed;
        Preview.RebuildTrails();
        SelectClip(clip);
        _timeline.Visible = segments.Count > 0;
        return true;
    }

    private void SelectClip(string clip)
    {
        for (int i = 0; i < _clips.ItemCount; i++)
        {
            if (_clips.GetItemText(i) == clip)
            {
                _clips.Select(i);
            }
        }
    }

    /// <summary>A recorded cycle made again, after what it was recorded
    /// with (limbs, speed) changed.</summary>
    private void Rerecord()
    {
        string clip = Preview.Transport.Clip;
        if (Preview.Transport.Recording != null && (clip == WalkCycle || clip == SprintCycle))
        {
            LoadClip(clip);
        }
    }

    /// <summary>
    /// A move as its file has it now (a re-export from Blender shows). Read
    /// into the loaded one, which keeps a property the file leaves out, so
    /// the lab's own fields go back to their defaults first.
    /// </summary>
    private static MechMove Fresh(string path)
    {
        if (ResourceLoader.HasCached(path) && ResourceLoader.Load(path) is MechMove cached)
        {
            cached.Blend = -1.0f;
            cached.TimingStamp = "";
            cached.BodyAuto = true;
            cached.Body = default;
        }
        return ResourceLoader.Load(path, "", ResourceLoader.CacheMode.Replace) as MechMove;
    }

    /// <summary>The clips the walker shown has, for the list.</summary>
    private void FillClips()
    {
        _clips.Clear();
        _clips.AddItem(WalkOnly);
        _clips.AddItem(WalkCycle);
        if (Preview.Walker.Tuning.SprintSpeed > Preview.Walker.Tuning.MaxSpeed)
        {
            _clips.AddItem(SprintCycle);
        }
        if (Resources.OfType<MechSlashStats>().FirstOrDefault() is MechSlashStats { Combo.Count: > 0 })
        {
            _clips.AddItem(ComboClip);
        }
        string mech = MechName(Preview.Walker);
        foreach (string path in MovesOf(Preview.Walker))
        {
            _clips.AddItem(path.GetFile().GetBaseName().Substring(mech.Length + 1));
        }
        LoadClip(WalkOnly);
    }

    private Section _infoSection;
    private Section _tuneSection;

    private void ArrangeSections(SplitContainer split)
    {
        bool info = _infoSection.Open, tune = _tuneSection.Open;
        split.Collapsed = !(info && tune);
        _infoSection.SizeFlagsVertical = info && !tune ? SizeFlags.ExpandFill : SizeFlags.Fill;
        _tuneSection.SizeFlagsVertical = tune ? SizeFlags.ExpandFill : SizeFlags.Fill;
    }

    /// <summary>A heading that folds what is under it away and back,
    /// remembered for the project.</summary>
    private sealed partial class Section : VBoxContainer
    {
        private readonly Button _header;
        private readonly Control _body;
        private readonly string _key;

        public event System.Action Toggled;

        public bool Open => _body.Visible;

        public Section() { }

        public Section(string title, Control body, string key)
        {
            _body = body;
            _key = key;
            _header = new Button { Flat = true, Alignment = HorizontalAlignment.Left, Text = title };
            _header.Pressed += Flip;
            AddChild(_header);
            AddChild(body);
            body.Visible = EditorInterface.Singleton.GetEditorSettings().GetProjectMetadata(MetaSection, key, true).AsBool();
            Label(title);
        }

        private void Flip()
        {
            _body.Visible = !_body.Visible;
            EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata(MetaSection, _key, _body.Visible);
            Label(_header.Text.Substring(2));
            Toggled?.Invoke();
        }

        private void Label(string title) => _header.Text = (_body.Visible ? "▾ " : "▸ ") + title;
    }

    private static Button Button(string text, System.Action pressed, string tooltip = "")
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += pressed;
        return button;
    }

    private void FillTree()
    {
        _tree.Clear();
        TreeItem root = _tree.CreateItem();
        string filter = _filter.Text.ToLowerInvariant();
        var groups = new Dictionary<string, TreeItem>();
        foreach (AssetCatalog.Asset asset in AssetCatalog.All())
        {
            if (filter != "" && !asset.Path.ToLowerInvariant().Contains(filter))
            {
                continue;
            }
            if (!groups.TryGetValue(asset.Group, out TreeItem group))
            {
                group = _tree.CreateItem(root);
                group.SetText(0, asset.Group);
                group.SetSelectable(0, false);
                groups[asset.Group] = group;
            }
            TreeItem item = _tree.CreateItem(group);
            item.SetText(0, asset.Name);
            item.SetTooltipText(0, asset.Path);
            item.SetMetadata(0, asset.Path);
            if (asset.Path == Current)
            {
                item.Select(0);
            }
        }
    }

    /// <summary>Shows the asset at <paramref name="path"/> and reads it.</summary>
    public void Open(string path)
    {
        var scene = ResourceLoader.Load<PackedScene>(path);
        if (scene == null)
        {
            _status.Text = $"{path} is not a scene or an imported model.";
            return;
        }
        Current = path;
        EditorInterface.Singleton.GetEditorSettings().SetProjectMetadata(MetaSection, "asset", path);
        Preview.Show(scene);
        _walkBar.Visible = Preview.Walker != null;
        _playBar.Visible = Preview.Walker != null;
        _timeline.Visible = false;
        _timingBar.Visible = false;
        _bodyBar.Visible = false;
        _shownTiming = null;
        Preview.Transport.Load("", new List<LabTransport.Segment>());
        Preview.TimeScale = (float)_timeScale.Value;
        Info = AssetInfo.Of(path, Preview.Asset, Preview.Bounds);
        _info.Text = Info.Text();
        Resources = AssetCatalog.ResourcesOf(Preview.Asset);
        _resources.Clear();
        foreach (Resource resource in Resources)
        {
            _resources.AddItem(resource.ResourcePath.GetFile());
        }
        _inspector.Edit(Resources.Count > 0 ? Resources[0] : null);
        _status.Text = "";
        if (Preview.Walker != null)
        {
            FillClips();
        }
        TimelineWanted?.Invoke(Preview.Walker != null);
    }

    /// <summary>The asset shown, built again from its scene: limbs taken
    /// off come back, and a walker stands still where it started.</summary>
    public void ResetAsset()
    {
        if (Current != "")
        {
            Open(Current);
        }
    }

    private Resource Edited => _inspector.GetEditedObject() as Resource;

    private void AskSave()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        List<string> others = SharedWith(resource.ResourcePath);
        if (others.Count == 0)
        {
            Save();
            return;
        }
        _confirm.DialogText = $"{resource.ResourcePath.GetFile()} also tunes:\n  "
            + string.Join("\n  ", others.Select(o => o.GetFile())) + "\n\nSave it for all of them?";
        _confirm.PopupCentered();
    }

    /// <summary>Who uses a resource besides the asset shown.</summary>
    public List<string> SharedWith(string path) => AssetCatalog.UsersOf(path).Where(u => u != Current).ToList();

    private void Save()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        Error error = ResourceSaver.Save(resource, resource.ResourcePath);
        _status.Text = error == Error.Ok ? $"Saved {resource.ResourcePath}." : $"Could not save {resource.ResourcePath}: {error}.";
    }

    private void Revert()
    {
        Resource resource = Edited;
        if (resource == null)
        {
            return;
        }
        ResourceLoader.Load(resource.ResourcePath, "", ResourceLoader.CacheMode.Replace);
        _inspector.Edit(null);
        _inspector.Edit(resource);
        _status.Text = $"{resource.ResourcePath.GetFile()} as saved.";
    }
}
#endif
