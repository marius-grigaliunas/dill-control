using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// folder, named for the wall-clock time the recording started, plus a short
/// summary written next to it and echoed to the console. perflogs/ carries a
/// .gdignore so Godot never tries to import a CSV as a translation table, which
/// it otherwise would. An exported build, whose res:// is read-only, falls
/// back to user://perflogs.
///
/// Per-frame cost while recording is one reused StringBuilder and a buffered
/// write; while not recording it is one bool check. It lives in debug/ because
/// it ships with the project and not with the game.
/// </summary>
public sealed partial class PerfRecorder : Node
{
    public const string FolderName = "perflogs";

    private const double BudgetMs = 1000.0 / 60.0;

    private readonly List<string> _columnNames = new();
    private readonly List<Func<double>> _columnSources = new();
    private readonly StringBuilder _row = new(512);
    private readonly StringBuilder _events = new(64);

    private StreamWriter _writer;
    private string _path;
    private ulong _startUsec;
    private ulong _lastSampleUsec;
    private int _frames;
    private int _overBudget;
    private double _totalMs;
    private double _worstMs;
    private int _worstFrame;
    private string _worstEvents = string.Empty;
    private double[] _sectionWorst = Array.Empty<double>();
    private double[] _sectionTotal = Array.Empty<double>();
    private double[] _sectionOnHitches = Array.Empty<double>();
    private int _gc0;
    private int _gc1;
    private int _gc2;
    private int _gc2Frames;
    private double _physicsWorst;
    private double _physicsOnHitches;
    private long _allocated;

    public bool IsRecording => _writer != null;

    public string CurrentPath => _path;

    /// <summary>
    /// Adds a counter sampled every recorded frame -- agents, bodies, voxels.
    /// Registered once at setup; a closure per column, never per frame.
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

        _frames = 0;
        _overBudget = 0;
        _totalMs = 0.0;
        _worstMs = 0.0;
        _worstFrame = -1;
        _worstEvents = string.Empty;
        _physicsWorst = 0.0;
        _physicsOnHitches = 0.0;
        _sectionWorst = new double[FrameProfiler.MaxSections];
        _sectionTotal = new double[FrameProfiler.MaxSections];
        _sectionOnHitches = new double[FrameProfiler.MaxSections];
        _events.Clear();
        _gc0 = GC.CollectionCount(0);
        _gc1 = GC.CollectionCount(1);
        _gc2 = GC.CollectionCount(2);
        _gc2Frames = 0;
        _allocated = GC.GetTotalAllocatedBytes(false);
        _startUsec = Time.GetTicksUsec();
        _lastSampleUsec = 0;

        WriteHeader();
        GD.Print($"[perf] recording to {_path} -- press U again to stop");
    }

    private void WriteHeader()
    {
        Rid viewport = GetViewport().GetViewportRid();
        // The window, not the viewport's visible rect: under canvas_items
        // stretch the rect is the UI base, while the 3D pass renders
        // at the window's own size times the 3D scale.
        Vector2I size = DisplayServer.WindowGetSize();

        _writer.WriteLine(Inv($"# dill control perf recording  started {DateTime.Now:yyyy-MM-dd HH:mm:ss}  scene {GetTree().CurrentScene?.SceneFilePath}"));
        _writer.WriteLine(Inv($"# viewport {size.X:F0}x{size.Y:F0}  scale {GetViewport().Scaling3DScale:F2}  budget {BudgetMs:F2} ms  vsync {DisplayServer.WindowGetVsyncMode()}  engine {Engine.GetVersionInfo()["string"]}"));
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
        foreach (string name in _columnNames)
        {
            _row.Append(',').Append(ColumnSafe(name));
        }
        _row.Append(",events");
        _writer.WriteLine(_row.ToString());
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

        bool hitch = frameMs > BudgetMs;

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
        _row.Append(gc0 - _gc0).Append(',').Append(gc1 - _gc1).Append(',').Append(gc2 - _gc2).Append(',');
        if (gc2 != _gc2)
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

        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            double ms = FrameProfiler.Milliseconds(i);
            AppendNumber(ms, "F3");

            _sectionTotal[i] += ms;
            _sectionWorst[i] = Math.Max(_sectionWorst[i], ms);
            if (hitch && !FrameProfiler.IsBackground(i))
            {
                _sectionOnHitches[i] += ms;
            }
        }

        foreach (Func<double> source in _columnSources)
        {
            AppendNumber(source(), "F0");
        }

        // Quoted: an event text may one day contain a comma.
        _row.Append('"').Append(_events.Replace("\"", "'")).Append('"');
        _writer.WriteLine(_row);

        _frames++;
        _totalMs += frameMs;
        _physicsWorst = Math.Max(_physicsWorst, physics);
        if (hitch)
        {
            _overBudget++;
            _physicsOnHitches += physics;
        }

        if (frameMs > _worstMs)
        {
            _worstMs = frameMs;
            _worstFrame = _frames - 1;
            _worstEvents = _events.ToString();
        }

        _events.Clear();
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
    }

    /// <summary>
    /// The numbers worth reading before opening the CSV: how often the budget
    /// was missed, the worst frame and what happened on it, and which sections
    /// the missed frames were spent in. The last is the one that names a
    /// culprit -- a section that is small on average and huge on hitches is
    /// exactly what a mean hides.
    /// </summary>
    private List<string> BuildSummary()
    {
        List<string> lines = new()
        {
            $"recording {Path.GetFileName(_path)}",
            Inv($"{_frames} frames over {_totalMs / 1000.0:F1} s  mean {(_frames > 0 ? _totalMs / _frames : 0.0):F2} ms  over budget {_overBudget} ({(_frames > 0 ? 100.0 * _overBudget / _frames : 0.0):F1}%)"),
            Inv($"worst frame #{_worstFrame}  {_worstMs:F2} ms  events: {(_worstEvents.Length > 0 ? _worstEvents : "none")}"),
            Inv($"full (gen-2) garbage collections during the recording: {_gc2Frames}"),
            Inv($"physics [engine]  worst {_physicsWorst:F2} ms  on hitches {(_overBudget > 0 ? _physicsOnHitches / _overBudget : 0.0):F2} ms avg"),
            "section               mean ms   worst ms   avg on hitches",
        };

        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            if (_sectionWorst[i] < 0.005)
            {
                continue;
            }

            string name = FrameProfiler.NameOf(i);
            double mean = _frames > 0 ? _sectionTotal[i] / _frames : 0.0;
            double onHitch = _overBudget > 0 ? _sectionOnHitches[i] / _overBudget : 0.0;
            lines.Add(Inv($"{name,-20}{mean,9:F3}{_sectionWorst[i],11:F3}{onHitch,13:F3}"));
        }

        return lines;
    }

    private void AppendNumber(double value, string format)
    {
        _row.Append(value.ToString(format, CultureInfo.InvariantCulture)).Append(',');
    }

    private static string ColumnSafe(string name) => name.Replace(',', ' ').Replace(' ', '_');

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
