using Godot;
using DillControl.Core;

namespace DillControl.Debug;

/// <summary>
/// Closes each frame for the profiler and owns the <c>U</c> recording.
///
/// <see cref="FrameProfiler"/> needs someone to call <c>EndFrame</c> once a
/// frame, and <see cref="PerfRecorder"/> needs someone to call <c>Sample</c>
/// straight after it. In the lab that was the debug overlay, which tied two
/// instruments to a panel; here it is this node, which <c>LevelRoot</c> adds to
/// every level, so every level can be recorded whether or not it has a HUD.
///
/// Runs last in process order, after <see cref="PhysicsStepProbe"/>'s last
/// probe, so the frame it publishes is complete.
/// </summary>
public partial class PerfDriver : Node
{
    public PerfRecorder Recorder { get; private set; }

    public override void _Ready()
    {
        ProcessPriority = int.MaxValue;
        AddChild(PhysicsStepProbe.Create());
        Recorder = new PerfRecorder { Name = "PerfRecorder" };
        AddChild(Recorder);
        if (SinkProbe.Wanted(OS.GetCmdlineUserArgs()))
        {
            AddChild(new SinkProbe { Name = "SinkProbe" });
        }
    }

    private readonly double[] _benchTotals = new double[FrameProfiler.MaxSections];
    private int _benchFrames;

    public override void _Process(double delta)
    {
        FrameProfiler.EndFrame();
        Recorder.Sample(delta);

        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            _benchTotals[i] += FrameProfiler.Milliseconds(i);
        }
        _benchFrames++;
    }

    /// <summary>
    /// Each profiler section's mean since the last call, for <c>bench.gd</c>'s
    /// per-phase report, then starts again. Godot's own TIME_PROCESS monitor
    /// read 114-381 ms a frame against 9-11 ms frames when the bench asked it
    /// from its MainLoop, so the bench reads these instead.
    /// </summary>
    public string TakeBenchReport()
    {
        System.Text.StringBuilder line = new();
        for (int i = 0; i < FrameProfiler.Count; i++)
        {
            double mean = _benchFrames > 0 ? _benchTotals[i] / _benchFrames : 0.0;
            line.Append(System.FormattableString.Invariant($"{FrameProfiler.NameOf(i)}={mean:F2}  "));
            _benchTotals[i] = 0.0;
        }
        _benchFrames = 0;
        return line.ToString().TrimEnd();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("perf_record"))
        {
            Recorder.Toggle();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.B })
        {
            Voxel.VoxelPart.ToggleChunkTint();
        }
    }
}
