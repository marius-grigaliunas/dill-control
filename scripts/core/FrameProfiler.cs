using System.Diagnostics;
using System.Runtime.CompilerServices;
using Godot;

namespace DillControl.Core;

/// <summary>
/// Named per-frame timings, for attributing a blown frame budget to a
/// subsystem instead of guessing at it.
///
/// A section is registered once and thereafter addressed by an int, so the hot
/// path never touches a string or a dictionary. Measuring is a
/// <c>using</c> over a <c>ref struct</c>: C# binds <c>using</c> on a ref struct
/// by pattern rather than through IDisposable, so there is no box, no
/// allocation and no virtual call. A plain struct behind an IDisposable
/// <c>using</c> would box on every scope, which is the trap this avoids.
///
/// Disabled, a scope costs one static bool load and a predicted branch.
/// Enabled, it costs two Stopwatch.GetTimestamp() calls -- roughly 20 ns each.
/// Stopwatch rather than Time.GetTicksUsec() on purpose: the latter is a Godot
/// interop call and about ten times slower, which is enough to distort the
/// very numbers being measured.
/// </summary>
public static class FrameProfiler
{
    /// <summary>
    /// Sections there is room for. A section registered past it gets id -1
    /// and is silently never timed -- which is how the rocket's own blast
    /// sections read zero for a day after six weapon and horde sections were
    /// added ahead of them. Generous on purpose; an unused slot costs a few
    /// bytes. If this is ever reached, raise it rather than going without.
    /// </summary>
    public const int MaxSections = 64;

    /// <summary>Master switch. False makes every scope a predicted branch.</summary>
    public static bool Enabled { get; set; } = true;

    private static readonly string[] Names = new string[MaxSections];
    private static readonly bool[] Background = new bool[MaxSections];
    private static readonly double[] Accumulating = new double[MaxSections];
    private static readonly double[] Reported = new double[MaxSections];
    private static int _count;

    private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

    public static int Count => _count;

    public static string NameOf(int section) =>
        section >= 0 && section < _count ? Names[section] : string.Empty;

    /// <summary>
    /// True for work that does not happen on the frame's critical path, such as
    /// a worker-thread rebuild. Reported so it can be seen, but never added to
    /// the frame total -- counting a background job against the frame it
    /// happened to finish in is how a profiler starts lying.
    /// </summary>
    public static bool IsBackground(int section) =>
        section >= 0 && section < _count && Background[section];

    /// <summary>Milliseconds this section took during the last completed frame.</summary>
    public static double Milliseconds(int section) =>
        section >= 0 && section < _count ? Reported[section] : 0.0;

    /// <summary>
    /// Registers a section and returns its id. Call once, in _Ready, and keep
    /// the int. Registering the same name twice returns the same id so a
    /// subsystem that is rebuilt at runtime does not leak slots.
    /// </summary>
    public static int Section(string name, bool background = false)
    {
        for (int i = 0; i < _count; i++)
        {
            if (Names[i] == name)
            {
                return i;
            }
        }

        if (_count >= MaxSections)
        {
            GD.PushWarning($"FrameProfiler: no room for section '{name}'; raise MaxSections.");
            return -1;
        }

        Names[_count] = name;
        Background[_count] = background;
        return _count++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Scope Begin(int section) =>
        new(section, Enabled ? Stopwatch.GetTimestamp() : 0L);

    /// <summary>
    /// Publishes this frame's accumulated times and starts the next frame.
    /// Called once per frame by whoever owns the overlay.
    /// </summary>
    public static void EndFrame()
    {
        for (int i = 0; i < _count; i++)
        {
            Reported[i] = Accumulating[i];
            Accumulating[i] = 0.0;
        }
    }

    /// <summary>Adds a measurement taken elsewhere, such as on a worker thread.</summary>
    public static void AddExternal(int section, double milliseconds)
    {
        if (section >= 0 && section < _count)
        {
            Accumulating[section] += milliseconds;
        }
    }

    /// <summary>Replaces rather than accumulates -- for a value sampled at its own rate.</summary>
    public static void SetExternal(int section, double milliseconds)
    {
        if (section >= 0 && section < _count)
        {
            Accumulating[section] = milliseconds;
        }
    }

    public readonly ref struct Scope
    {
        private readonly int _section;
        private readonly long _start;

        internal Scope(int section, long start)
        {
            _section = section;
            _start = start;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (!Enabled || _section < 0)
            {
                return;
            }
            Accumulating[_section] += (Stopwatch.GetTimestamp() - _start) * MsPerTick;
        }
    }
}
