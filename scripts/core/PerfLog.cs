using System;
using System.Collections.Generic;
using System.Text;

namespace DillControl.Core;

/// <summary>
/// What game code tells the <c>U</c> recording (<c>Debug.PerfRecorder</c>)
/// besides the section times: things that happened this frame (a collapse,
/// a slow section handed over) and counts worth watching grow (loose pieces,
/// rubble heaps). It lives in core so a voxel or a horde can report without
/// knowing a recorder exists.
///
/// Not recording, an event is one bool test: build its text inside an
/// <c>if (PerfLog.Recording)</c>, never before it, or every frame pays for a
/// string nobody reads.
///
/// Main thread only.
/// </summary>
public static class PerfLog
{
    private static readonly StringBuilder Pending = new(128);
    private static readonly List<(string name, Func<double> source)> Counters = new();

    /// <summary>True while a recording is running.</summary>
    public static bool Recording { get; private set; }

    /// <summary>Notes something that happened this frame, for the row's events column.</summary>
    public static void Event(string text)
    {
        if (!Recording)
        {
            return;
        }
        if (Pending.Length > 0)
        {
            Pending.Append(" | ");
        }
        Pending.Append(text);
    }

    /// <summary>
    /// A count sampled every recorded frame. Register once, from a class that
    /// exists at load: a recording's columns are fixed when it starts. A name
    /// registered twice keeps its first source.
    /// </summary>
    public static void Counter(string name, Func<double> source)
    {
        foreach ((string existing, _) in Counters)
        {
            if (existing == name)
            {
                return;
            }
        }
        Counters.Add((name, source));
    }

    public static IReadOnlyList<(string name, Func<double> source)> RegisteredCounters => Counters;

    /// <summary>For the recorder: starts or stops taking events.</summary>
    public static void SetRecording(bool recording)
    {
        Recording = recording;
        Pending.Clear();
    }

    /// <summary>For the recorder: this frame's events, appended to <paramref name="into"/>, and cleared.</summary>
    public static void TakeEvents(StringBuilder into)
    {
        if (Pending.Length == 0)
        {
            return;
        }
        if (into.Length > 0)
        {
            into.Append(" | ");
        }
        into.Append(Pending);
        Pending.Clear();
    }
}
