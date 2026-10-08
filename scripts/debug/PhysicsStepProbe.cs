using System.Diagnostics;
using Godot;
using DillControl.Core;

namespace DillControl.Debug;

/// <summary>
/// Times the physics engine's own step -- Jolt's simulation, which no script
/// can wrap -- and reports it to the <see cref="FrameProfiler"/> as
/// "physics step [engine]", so the overlay, the <c>U</c> recorder and the
/// bench see it frame by frame.
///
/// DECISION -- measured from the outside rather than read from Godot's
/// physics monitor. That monitor holds one value across a whole run of
/// frames (a recording showed it flat at 54 ms for forty frames running at
/// 15), so it cannot say which frame paid. Godot runs a physics tick as every
/// script's _PhysicsProcess, then the engine's step, then the frame's
/// _Process; so the time from the last physics callback to whatever runs
/// next -- the next tick's first physics callback, or the frame's first
/// process callback -- is the step, plus the engine's sync and query flush on
/// either side of it, and nothing of this project's. Three instances do it:
/// one first and one last in physics order, one first in process order.
/// </summary>
public partial class PhysicsStepProbe : Node
{
    private enum Role
    {
        PhysicsFirst,
        PhysicsLast,
        ProcessFirst,
        ProcessLast,
    }

    private Role _role;

    private static long _lastScriptsEnd;
    private static long _scriptsStart;
    private static bool _pending;
    private static double _accumulated;
    private static double _physicsScripts;
    private static int _section = -1;
    private static int _physicsScriptsSection = -1;
    private static int _processScriptsSection = -1;
    private static int _renderSection = -1;
    private static long _processEnd;
    private static bool _processEnded;

    /// <summary>The three probes, ready to add under any node that lives as long as the level.</summary>
    public static Node Create()
    {
        _section = FrameProfiler.Section("physics step [engine]");

        // The other two thirds of a frame's CPU: every script's physics
        // callbacks together, and every script's process callbacks together
        // -- this project's code whether or not anyone wrapped it in a
        // section. What is left of the frame after these and the step is the
        // renderer and the wait on the GPU.
        _physicsScriptsSection = FrameProfiler.Section("physics scripts [all]");
        _processScriptsSection = FrameProfiler.Section("process scripts [all]");

        // From the end of a frame's scripts to the start of the next frame:
        // the engine's own late work, drawing the frame, and any wait on the
        // GPU or the swap chain. Large here and small in "render cpu" means
        // the CPU is waiting for the GPU.
        _renderSection = FrameProfiler.Section("render and wait [engine]");
        _pending = false;
        _accumulated = 0.0;

        Node holder = new() { Name = "PhysicsStepProbe" };
        holder.AddChild(new PhysicsStepProbe
        {
            Name = "PhysicsFirst",
            _role = Role.PhysicsFirst,
            ProcessPhysicsPriority = int.MinValue,
        });
        holder.AddChild(new PhysicsStepProbe
        {
            Name = "PhysicsLast",
            _role = Role.PhysicsLast,
            ProcessPhysicsPriority = int.MaxValue,
        });
        holder.AddChild(new PhysicsStepProbe
        {
            Name = "ProcessFirst",
            _role = Role.ProcessFirst,
            ProcessPriority = int.MinValue,
        });
        holder.AddChild(new PhysicsStepProbe
        {
            Name = "ProcessLast",
            _role = Role.ProcessLast,
            // One short of the last, so PerfDriver -- which publishes the
            // frame -- runs after it and the process total lands in the
            // frame it belongs to rather than the next one.
            ProcessPriority = int.MaxValue - 1,
        });
        return holder;
    }

    public override void _Ready()
    {
        SetPhysicsProcess(_role is Role.PhysicsFirst or Role.PhysicsLast);
        SetProcess(_role is Role.ProcessFirst or Role.ProcessLast);
    }

    public override void _PhysicsProcess(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        CloseFrame(now);
        if (_role == Role.PhysicsFirst)
        {
            Close();
            _scriptsStart = now;
            return;
        }

        _physicsScripts += (now - _scriptsStart) * 1000.0 / Stopwatch.Frequency;
        _lastScriptsEnd = now;
        _pending = true;
    }

    public override void _Process(double delta)
    {
        long now = Stopwatch.GetTimestamp();
        if (_role == Role.ProcessLast)
        {
            FrameProfiler.AddExternal(_processScriptsSection, (now - _scriptsStart) * 1000.0 / Stopwatch.Frequency);
            _processEnd = now;
            _processEnded = true;
            return;
        }

        CloseFrame(now);
        Close();
        FrameProfiler.AddExternal(_section, _accumulated);
        FrameProfiler.AddExternal(_physicsScriptsSection, _physicsScripts);
        _accumulated = 0.0;
        _physicsScripts = 0.0;
        _scriptsStart = Stopwatch.GetTimestamp();
    }

    private static void CloseFrame(long now)
    {
        if (!_processEnded)
        {
            return;
        }

        _processEnded = false;
        FrameProfiler.AddExternal(_renderSection, (now - _processEnd) * 1000.0 / Stopwatch.Frequency);
    }

    private static void Close()
    {
        if (!_pending)
        {
            return;
        }

        _pending = false;
        _accumulated += (Stopwatch.GetTimestamp() - _lastScriptsEnd) * 1000.0 / Stopwatch.Frequency;
    }
}
