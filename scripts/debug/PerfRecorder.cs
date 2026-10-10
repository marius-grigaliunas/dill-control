using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Godot;
using DillControl.Core;

namespace DillControl.Debug;

/// <summary>
/// Records every frame's profile to a file between two presses of a key, so a
/// hitch can be diagnosed afterwards from what actually happened rather than
/// from one snapshot taken whenever someone thought to press U.
///
/// The one-shot report prints the frame it happened to land on. A destruction
/// spike is one to five frames long, and nobody can press a key on the frame a
/// tower comes down -- so the snapshot showed 45 ms of voxel carve in three
/// reports out of five and could not say how many frames it lasted, what came
/// before it, or whether the physics spike that followed was the same event.
/// A recording answers all three.
///
/// Output is one CSV per recording under <c>perflogs/</c> in the project
/// folder, named for the wall-clock time the recording started, plus a summary
/// written next to it and echoed to the console. The summary is the file to
/// hand on: the machine and whether it was on battery, the worst frames each
/// with what it was spent on and what happened in it, which section caused
/// the spikes, and how the counts (loose pieces, heaps, bodies, draws) grew.
/// perflogs/ carries a .gdignore so Godot never tries to import a CSV as a
/// translation table, which it otherwise would. An exported build, whose res://
/// is read-only, falls back to user://perflogs.
///
/// Columns are the engine's own timers and counts, every
/// <see cref="FrameProfiler"/> section, and every <see cref="PerfLog"/>
/// counter; the events column is <see cref="PerfLog.Event"/>s and
/// <see cref="Mark"/>s.
///
/// Per-frame cost while recording is one reused StringBuilder and a buffered
/// write; while not recording it is one bool check. A small REC label in the
/// corner says it is running. It lives in debug/ because it ships with the
/// project and not with the game.
/// </summary>
public sealed partial class PerfRecorder : Node
{
    public const string FolderName = "perflogs";

    private const double BudgetMs = 1000.0 / 60.0;

    /// <summary>A frame this much over budget is a spike: what is felt, not
    /// the jitter of a frame held to 60 Hz landing either side of 16.7 ms.</summary>
    private const double SpikeMs = BudgetMs * 1.5;

    private const int WorstKept = 15;

    private readonly List<string> _columnNames = new();
    private readonly List<Func<double>> _columnSources = new();
    private readonly StringBuilder _row = new(1024);
    private readonly StringBuilder _events = new(64);

    private StreamWriter _writer;
    private string _path;
    private ulong _startUsec;
    private ulong _lastSampleUsec;
    private int _frames;
    private int _overBudget;
    private int _spikes;
    private double _totalMs;
    private double[] _sectionWorst = Array.Empty<double>();
    private double[] _sectionTotal = Array.Empty<double>();
    private double[] _sectionOnHitches = Array.Empty<double>();
    private long[] _sectionAllocated = Array.Empty<long>();
    private int[] _sectionBlamed = Array.Empty<int>();
    private int[] _aggregateBlamed = Array.Empty<int>();
    private int _gc0;
    private int _gc1;
    private int _gc2;
    private int _gc2Frames;
    private double _physicsWorst;
    private double _physicsOnHitches;
    private long _allocated;
    private int _activeColumns;
    private double[] _columnValues = Array.Empty<double>();
    private double[] _columnFirst = Array.Empty<double>();
    private double[] _columnPeak = Array.Empty<double>();
    private readonly Dictionary<string, int> _eventKinds = new();
    private readonly List<(double ms, int frame, string detail)> _worst = new();
    private string _powerAtStart = string.Empty;

    private CanvasLayer _overlay;
    private Label _label;
    private double _labelTimer;
    private double _savedTimer;

    public bool IsRecording => _writer != null;

    public string CurrentPath => _path;

    /// <summary>
    /// Adds a counter sampled every recorded frame -- agents, bodies, voxels.
    /// Registered once at setup; a closure per column, never per frame.
    /// Prefer <see cref="PerfLog.Counter"/> from game code.
    /// </summary>
    public void AddColumn(string name, Func<double> source)
    {
        _columnNames.Add(name);
        _columnSources.Add(source);
    }

    /// <summary>
    /// Notes something that happened this frame -- a collapse, a blast -- in
    /// the row's events column, so a spike in the numbers sits next to its
    /// cause. Ignored when not recording.
    /// </summary>
    public void Mark(string text)
    {
        if (_writer == null)
        {
            return;
        }

        if (_events.Length > 0)
        {
            _events.Append(" | ");
        }
        _events.Append(text);
    }

    public override void _Ready()
    {
        // Engine counts every recording carries. Game systems add theirs
        // through PerfLog.Counter.
        // Not the physics monitors (active bodies, pairs, islands): under
        // Jolt they read zero.
        AddColumn("objects_drawn", () => Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame));
        AddColumn("nodes", () => Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        AddColumn("orphan_nodes", () => Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount));
        AddColumn("video_mb", () => Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0));

        _overlay = new CanvasLayer { Name = "RecordingIndicator", Layer = 120, Visible = false };
        _label = new Label
        {
            AnchorLeft = 1.0f,
            AnchorRight = 1.0f,
            OffsetLeft = -560.0f,
            OffsetRight = -16.0f,
            OffsetTop = 12.0f,
            HorizontalAlignment = HorizontalAlignment.Right,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.AddThemeColorOverride("font_color", new Color(1.0f, 0.35f, 0.3f));
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 6);
        _overlay.AddChild(_label);
        AddChild(_overlay);
    }

    public override void _Process(double delta)
    {
        if (_savedTimer > 0.0)
        {
            _savedTimer -= delta;
            if (_savedTimer <= 0.0 && !IsRecording)
            {
                _overlay.Visible = false;
            }
        }
    }

    public void Toggle()
    {
        if (IsRecording)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    public void Start()
    {
        if (IsRecording)
        {
            return;
        }

        string folder = ResolveFolder();
        string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        _path = Path.Combine(folder, $"perf_{stamp}.csv");

        try
        {
            _writer = new StreamWriter(_path, false, new UTF8Encoding(false), 1 << 16);
        }
        catch (Exception exception)
        {
            GD.PushError($"PerfRecorder could not open {_path}: {exception.Message}");
            _writer = null;
            return;
        }

        // The counters game systems registered, after the recorder's own.
        _activeColumns = _columnNames.Count;
        foreach ((string name, Func<double> source) in PerfLog.RegisteredCounters)
        {
            if (!_columnNames.Contains(name))
            {
                _columnNames.Add(name);
                _columnSources.Add(source);
            }
        }
        _activeColumns = _columnNames.Count;

        _frames = 0;
        _overBudget = 0;
        _spikes = 0;
        _totalMs = 0.0;
        _physicsWorst = 0.0;
        _physicsOnHitches = 0.0;
        _sectionWorst = new double[FrameProfiler.MaxSections];
        _sectionTotal = new double[FrameProfiler.MaxSections];
        _sectionOnHitches = new double[FrameProfiler.MaxSections];
        _sectionAllocated = new long[FrameProfiler.MaxSections];
        _sectionBlamed = new int[FrameProfiler.MaxSections];
        _aggregateBlamed = new int[FrameProfiler.MaxSections];
        _columnValues = new double[_activeColumns];
        _columnFirst = new double[_activeColumns];
        _columnPeak = new double[_activeColumns];
        _eventKinds.Clear();
        _worst.Clear();
        _events.Clear();
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        _gc2Frames = 0;
        _allocated = GC.GetTotalAllocatedBytes(false);
        _startUsec = Time.GetTicksUsec();
        _lastSampleUsec = 0;
        _powerAtStart = PowerState();
        PerfLog.SetRecording(true);

        WriteHeader();
        _overlay.Visible = true;
        _savedTimer = 0.0;
        UpdateLabel();
        GD.Print($"[perf] recording to {_path} -- press U again to stop");
    }

    private void WriteHeader()
    {
        // The window, not the viewport's visible rect: under canvas_items
        // stretch the rect is the UI base, while the 3D pass renders
        // at the window's own size times the 3D scale.
        Vector2I size = DisplayServer.WindowGetSize();

        _writer.WriteLine(Inv($"# dill control perf recording  started {DateTime.Now:yyyy-MM-dd HH:mm:ss}  scene {GetTree().CurrentScene?.SceneFilePath}"));
        foreach (string line in MachineLines(size))
        {
            _writer.WriteLine("# " + line);
        }
        _writer.WriteLine("# render_cpu/render_gpu are the engine's timers for the last frame it finished rendering, so they trail the frame_ms column by one row.");

        _row.Clear();
        // gc columns are collections that happened this frame, by generation.
        // A gen-2 collection is a full, blocking one, and a frame over budget
        // with nothing profiled in it is very often exactly that.
        _row.Append("frame,t_ms,frame_ms,fps,render_cpu_ms,render_gpu_ms,physics_ms,draws,primitives,gc0,gc1,gc2,managed_mb,alloc_kb");
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            _row.Append(',').Append(ColumnSafe(FrameProfiler.NameOf(i)));
        }
        for (int c = 0; c < _activeColumns; c++)
        {
            _row.Append(',').Append(ColumnSafe(_columnNames[c]));
        }
        _row.Append(",events");
        _writer.WriteLine(_row.ToString());
    }

    /// <summary>What it ran on: the same numbers mean different things on a
    /// plugged-in desktop and a laptop on battery.</summary>
    private List<string> MachineLines(Vector2I size)
    {
        return new List<string>
        {
            Inv($"window {size.X}x{size.Y}  scale {GetViewport().Scaling3DScale:F2}  budget {BudgetMs:F2} ms  vsync {DisplayServer.WindowGetVsyncMode()}  max_fps {Engine.MaxFps}  physics {Engine.PhysicsTicksPerSecond} Hz  engine {Engine.GetVersionInfo()["string"]}"),
            Inv($"cpu {OS.GetProcessorName()} ({OS.GetProcessorCount()} threads)  gpu {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()})  os {OS.GetName()} {OS.GetVersion()}"),
            Inv($"memory {OS.GetMemoryInfo()["physical"].AsInt64() / (1024 * 1024 * 1024.0):F1} GB  power {_powerAtStart}"),
        };
    }

    /// <summary>
    /// One row. Called once a frame by PerfDriver, straight after FrameProfiler.EndFrame, so
    /// the section times are the frame that just finished.
    /// </summary>
    public void Sample(double frameDeltaSeconds)
    {
        if (_writer == null)
        {
            return;
        }

        // Wall clock between two samples, not the engine's delta. The delta is
        // what the frame was *given*, which the engine is free to smooth; a
        // recording made to find hitches has to report what the frame took.
        ulong nowUsec = Time.GetTicksUsec();
        double frameMs = _lastSampleUsec == 0
            ? frameDeltaSeconds * 1000.0
            : (nowUsec - _lastSampleUsec) / 1000.0;
        _lastSampleUsec = nowUsec;
        Rid viewport = GetViewport().GetViewportRid();
        double renderCpu = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport);
        double renderGpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
        double physics = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        double draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        double elapsedMs = (Time.GetTicksUsec() - _startUsec) / 1000.0;

        PerfLog.TakeEvents(_events);
        bool hitch = frameMs > BudgetMs;
        bool spike = frameMs > SpikeMs;

        _row.Clear();
        _row.Append(_frames).Append(',');
        AppendNumber(elapsedMs, "F1");
        AppendNumber(frameMs, "F3");
        AppendNumber(Engine.GetFramesPerSecond(), "F0");
        AppendNumber(renderCpu, "F3");
        AppendNumber(renderGpu, "F3");
        AppendNumber(physics, "F3");
        AppendNumber(draws, "F0");
        AppendNumber(primitives, "F0");

        int gc0 = GC.CollectionCount(0);
        int gc1 = GC.CollectionCount(1);
        int gc2 = GC.CollectionCount(2);
        bool fullCollection = gc2 != _gc2;
        _row.Append(gc0 - _gc0).Append(',').Append(gc1 - _gc1).Append(',').Append(gc2 - _gc2).Append(',');
        if (fullCollection)
        {
            _gc2Frames++;
        }
        _gc0 = gc0;
        _gc1 = gc1;
        _gc2 = gc2;
        AppendNumber(GC.GetTotalMemory(false) / (1024.0 * 1024.0), "F1");
        // Bytes allocated this frame, every thread: what feeds the collector.
        long allocated = GC.GetTotalAllocatedBytes(false);
        AppendNumber((allocated - _allocated) / 1024.0, "F0");
        _allocated = allocated;

        int blamedSection = -1, blamedAggregate = -1;
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            double ms = FrameProfiler.Milliseconds(i);
            AppendNumber(ms, "F3");

            _sectionTotal[i] += ms;
            _sectionWorst[i] = Math.Max(_sectionWorst[i], ms);
            bool background = FrameProfiler.IsBackground(i);
            if (hitch && !background)
            {
                _sectionOnHitches[i] += ms;
            }
            if (spike && !background)
            {
                if (IsAggregate(i))
                {
                    if (blamedAggregate < 0 || ms > FrameProfiler.Milliseconds(blamedAggregate))
                    {
                        blamedAggregate = i;
                    }
                }
                else if (blamedSection < 0 || ms > FrameProfiler.Milliseconds(blamedSection))
                {
                    blamedSection = i;
                }
            }
            _sectionAllocated[i] += FrameProfiler.Allocated(i);
        }

        for (int c = 0; c < _activeColumns; c++)
        {
            double value = _columnSources[c]();
            _columnValues[c] = value;
            if (_frames == 0)
            {
                _columnFirst[c] = value;
            }
            _columnPeak[c] = Math.Max(_columnPeak[c], value);
            AppendNumber(value, "F0");
        }

        // Quoted: an event text may contain a comma.
        _row.Append('"').Append(_events.Replace("\"", "'")).Append('"');
        _writer.WriteLine(_row);

        _totalMs += frameMs;
        _physicsWorst = Math.Max(_physicsWorst, physics);
        if (hitch)
        {
            _overBudget++;
            _physicsOnHitches += physics;
        }
        if (spike)
        {
            _spikes++;
            if (blamedSection >= 0 && FrameProfiler.Milliseconds(blamedSection) >= 1.0)
            {
                _sectionBlamed[blamedSection]++;
            }
            if (blamedAggregate >= 0)
            {
                _aggregateBlamed[blamedAggregate]++;
            }
        }
        if (_events.Length > 0)
        {
            TallyEvents();
        }
        if (_worst.Count < WorstKept || frameMs > _worst[^1].ms)
        {
            KeepWorst(frameMs, renderGpu, physics, draws, fullCollection);
        }

        _frames++;
        _events.Clear();

        _labelTimer -= frameDeltaSeconds;
        if (_labelTimer <= 0.0)
        {
            _labelTimer = 0.25;
            UpdateLabel();
        }
    }

    /// <summary>The probe's totals (physics step, scripts, render and wait),
    /// as against the sections they contain.</summary>
    private static bool IsAggregate(int section)
    {
        string name = FrameProfiler.NameOf(section);
        return name.EndsWith("[engine]", StringComparison.Ordinal) || name.EndsWith("[all]", StringComparison.Ordinal);
    }

    /// <summary>Counts each kind of event: its text up to the first space or colon.</summary>
    private void TallyEvents()
    {
        foreach (string one in _events.ToString().Split(" | "))
        {
            int end = one.IndexOfAny(new[] { ' ', ':', '=' });
            string kind = end > 0 ? one[..end] : one;
            _eventKinds[kind] = _eventKinds.TryGetValue(kind, out int n) ? n + 1 : 1;
        }
    }

    /// <summary>
    /// A frame among the worst so far, with what it was spent on: the
    /// probe's totals, the biggest sections inside them, the counts, and what
    /// happened in it. Built only for a frame that makes the list.
    /// </summary>
    private void KeepWorst(double frameMs, double renderGpu, double physics, double draws, bool fullCollection)
    {
        var parts = new List<(double ms, int i)>();
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            double ms = FrameProfiler.Milliseconds(i);
            if (ms >= 0.5 && !FrameProfiler.IsBackground(i))
            {
                parts.Add((ms, i));
            }
        }
        parts.Sort((a, b) => b.ms.CompareTo(a.ms));

        var detail = new StringBuilder();
        detail.Append(Inv($"#{_frames}  t={(Time.GetTicksUsec() - _startUsec) / 1e6:F1}s  {frameMs:F1} ms  gpu {renderGpu:F1}  physics {physics:F1}  draws {draws:F0}"));
        if (fullCollection)
        {
            detail.Append("  FULL GC");
        }
        detail.Append("\n      totals:");
        foreach ((double ms, int i) in parts)
        {
            if (IsAggregate(i))
            {
                detail.Append(Inv($" {FrameProfiler.NameOf(i)} {ms:F1}"));
            }
        }
        detail.Append("\n      inside:");
        int shown = 0;
        foreach ((double ms, int i) in parts)
        {
            if (!IsAggregate(i) && shown++ < 6)
            {
                detail.Append(Inv($" {FrameProfiler.NameOf(i)} {ms:F1}"));
            }
        }
        if (shown == 0)
        {
            detail.Append(" nothing profiled over 0.5 ms (engine, GC or the OS)");
        }
        detail.Append("\n      counts:");
        for (int c = 0; c < _activeColumns; c++)
        {
            if (_columnValues[c] != 0.0)
            {
                detail.Append(Inv($" {_columnNames[c]}={_columnValues[c]:F0}"));
            }
        }
        if (_events.Length > 0)
        {
            string events = _events.ToString();
            detail.Append("\n      events: ").Append(events.Length > 400 ? events[..400] + " ..." : events);
        }

        _worst.Add((frameMs, _frames, detail.ToString()));
        _worst.Sort((a, b) => b.ms.CompareTo(a.ms));
        if (_worst.Count > WorstKept)
        {
            _worst.RemoveAt(_worst.Count - 1);
        }
    }

    private void UpdateLabel()
    {
        if (!IsRecording)
        {
            return;
        }
        double seconds = (Time.GetTicksUsec() - _startUsec) / 1e6;
        double worst = _worst.Count > 0 ? _worst[0].ms : 0.0;
        _label.Text = Inv($"● REC {seconds:F0} s   spikes {_spikes}   worst {worst:F0} ms   [U] stop");
    }

    public void Stop()
    {
        if (_writer == null)
        {
            return;
        }

        List<string> summary = BuildSummary();

        _writer.Flush();
        _writer.Dispose();
        _writer = null;
        PerfLog.SetRecording(false);

        string summaryPath = Path.ChangeExtension(_path, null) + "_summary.txt";
        try
        {
            File.WriteAllLines(summaryPath, summary);
        }
        catch (Exception exception)
        {
            GD.PushError($"PerfRecorder could not write {summaryPath}: {exception.Message}");
        }

        foreach (string line in summary)
        {
            GD.Print("[perf] " + line);
        }
        GD.Print($"[perf] saved {_path}");

        if (IsInstanceValid(_label))
        {
            _label.Text = $"saved perflogs/{Path.GetFileName(summaryPath)}";
            _savedTimer = 5.0;
        }
    }

    /// <summary>
    /// The numbers worth reading before opening the CSV: how often the budget
    /// was missed, the worst frames and what happened on each, which sections
    /// the spikes were spent in, and how the counts grew. The section that is
    /// small on average and huge on hitches is exactly what a mean hides.
    /// </summary>
    private List<string> BuildSummary()
    {
        string powerNow = PowerState();
        List<string> lines = new()
        {
            $"recording {Path.GetFileName(_path)}  scene {GetTree().CurrentScene?.SceneFilePath}",
        };
        lines.AddRange(MachineLines(DisplayServer.WindowGetSize()));
        if (powerNow != _powerAtStart)
        {
            lines.Add($"power at the end: {powerNow}");
        }
        if (_powerAtStart.StartsWith("ON BATTERY", StringComparison.Ordinal) || powerNow.StartsWith("ON BATTERY", StringComparison.Ordinal))
        {
            lines.Add("WARNING: on battery Windows holds frames to 60 Hz and clocks the GPU down; means read ~16.7 ms whatever is on screen. Spikes still count.");
        }

        lines.Add(string.Empty);
        lines.Add(Inv($"{_frames} frames over {_totalMs / 1000.0:F1} s  mean {(_frames > 0 ? _totalMs / _frames : 0.0):F2} ms  over budget {_overBudget} ({Pct(_overBudget)})  spikes over {SpikeMs:F0} ms {_spikes} ({Pct(_spikes)})"));
        lines.Add(Inv($"full (gen-2) garbage collections during the recording: {_gc2Frames}"));
        lines.Add(Inv($"physics [engine]  worst {_physicsWorst:F2} ms  on hitches {(_overBudget > 0 ? _physicsOnHitches / _overBudget : 0.0):F2} ms avg"));

        lines.Add(string.Empty);
        lines.Add($"what the spikes were spent on (frames over {SpikeMs:F0} ms, by their biggest part):");
        AddBlame(lines, _aggregateBlamed);
        AddBlame(lines, _sectionBlamed);

        lines.Add(string.Empty);
        lines.Add($"worst {_worst.Count} frames:");
        foreach ((_, _, string detail) in _worst)
        {
            lines.Add("  " + detail);
        }

        lines.Add(string.Empty);
        lines.Add("counts             first      peak       last");
        for (int c = 0; c < _activeColumns; c++)
        {
            lines.Add(Inv($"{_columnNames[c],-18}{_columnFirst[c],6:F0}{_columnPeak[c],10:F0}{_columnValues[c],11:F0}"));
        }

        if (_eventKinds.Count > 0)
        {
            lines.Add(string.Empty);
            var kinds = new List<KeyValuePair<string, int>>(_eventKinds);
            kinds.Sort((a, b) => b.Value.CompareTo(a.Value));
            var events = new StringBuilder("events: ");
            foreach (KeyValuePair<string, int> kind in kinds)
            {
                events.Append(Inv($"{kind.Key}={kind.Value}  "));
            }
            lines.Add(events.ToString().TrimEnd());
        }

        lines.Add(string.Empty);
        lines.Add("section               mean ms   worst ms   avg on hitches   KB/frame allocated");
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            if (_sectionWorst[i] < 0.005)
            {
                continue;
            }

            string name = FrameProfiler.NameOf(i);
            double mean = _frames > 0 ? _sectionTotal[i] / _frames : 0.0;
            double onHitch = _overBudget > 0 ? _sectionOnHitches[i] / _overBudget : 0.0;
            double kb = _frames > 0 ? _sectionAllocated[i] / 1024.0 / _frames : 0.0;
            lines.Add(Inv($"{name,-20}{mean,9:F3}{_sectionWorst[i],11:F3}{onHitch,13:F3}{kb,16:F1}"));
        }

        lines.Add(string.Empty);
        lines.Add($"every frame: {Path.GetFileName(_path)}");
        return lines;
    }

    private void AddBlame(List<string> lines, int[] blamed)
    {
        var order = new List<int>();
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            if (blamed[i] > 0)
            {
                order.Add(i);
            }
        }
        order.Sort((a, b) => blamed[b].CompareTo(blamed[a]));
        foreach (int i in order)
        {
            lines.Add(Inv($"  {FrameProfiler.NameOf(i),-26}{blamed[i],5} spikes"));
        }
    }

    private string Pct(int count) => Inv($"{(_frames > 0 ? 100.0 * count / _frames : 0.0):F1}%");

    private void AppendNumber(double value, string format)
    {
        _row.Append(value.ToString(format, CultureInfo.InvariantCulture)).Append(',');
    }

    private static string ColumnSafe(string name) => name.Replace(',', ' ').Replace(' ', '_');

    // ---------------- Power ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>Plugged in or on battery: on battery every number is
    /// different (see CLAUDE.md, "Measure plugged in").</summary>
    private static string PowerState()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "unknown";
        }
        try
        {
            if (!GetSystemPowerStatus(out SystemPowerStatus status))
            {
                return "unknown";
            }
            string battery = status.BatteryLifePercent <= 100 ? $" ({status.BatteryLifePercent}%)" : string.Empty;
            return status.ACLineStatus switch
            {
                0 => "ON BATTERY" + battery,
                1 => "plugged in" + battery,
                _ => "unknown",
            };
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// The project folder while running from source, where the logs sit next
    /// to the code they are about; user:// in an exported build, where res:// is
    /// read-only.
    /// </summary>
    private static string ResolveFolder()
    {
        string folder = OS.HasFeature("editor") || OS.HasFeature("debug")
            ? ProjectSettings.GlobalizePath("res://" + FolderName)
            : ProjectSettings.GlobalizePath("user://" + FolderName);

        try
        {
            Directory.CreateDirectory(folder);
            string ignore = Path.Combine(folder, ".gdignore");
            if (!File.Exists(ignore))
            {
                File.WriteAllText(ignore, string.Empty);
            }
        }
        catch (Exception)
        {
            folder = ProjectSettings.GlobalizePath("user://" + FolderName);
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    public override void _ExitTree()
    {
        // Quitting mid-recording still leaves a readable file behind.
        Stop();
    }

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
