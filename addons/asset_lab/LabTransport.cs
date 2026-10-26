#if TOOLS
using System.Collections.Generic;
using DillControl.Vehicles;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// What the asset lab's timeline plays on a walker: a clip of moves, each
/// starting at a time and blending in, with play, pause, loop and a time of
/// its own that a scrub sets. A single move is a clip of one; the combo is
/// chained as <see cref="MechSlasher"/> chains it in play. A recorded gait
/// cycle (<see cref="LabRecording"/>) is a clip with no moves. No clip: only
/// the walk runs, and pause holds that too.
/// </summary>
public sealed class LabTransport
{
    public readonly record struct Segment(MechMove Move, float Start, float Blend);

    public string Clip { get; private set; } = "";
    public List<Segment> Segments { get; private set; } = new();

    /// <summary>A recorded gait cycle, when that is the clip.</summary>
    public LabRecording Recording { get; private set; }

    /// <summary>Whether there is anything on the timeline.</summary>
    public bool HasClip => Segments.Count > 0 || Recording != null;
    public float Length { get; private set; }
    public float Time;
    public bool Playing = true;
    public bool Loop = true;

    /// <summary>Frames a second the step buttons move by: the clip's own.</summary>
    public float Fps => Recording != null ? LabRecording.Fps : Segments.Count > 0 ? Segments[0].Move.Fps : 30.0f;

    public void Load(string clip, List<Segment> segments)
    {
        Clip = clip;
        Segments = segments;
        Recording = null;
        Length = 0.0f;
        foreach (Segment segment in segments)
        {
            Length = Mathf.Max(Length, segment.Start + segment.Move.Length);
        }
        Time = 0.0f;
        Playing = true;
    }

    public void Load(string clip, LabRecording recording)
    {
        Load(clip, new List<Segment>());
        Recording = recording;
        Length = recording.Length;
    }

    /// <summary>The same moves again, after their timing was edited:
    /// where the playhead is, and whether it plays, are kept.</summary>
    public void Rebuild(List<Segment> segments)
    {
        float time = Time;
        bool playing = Playing;
        Load(Clip, segments);
        Time = Mathf.Min(time, Length);
        Playing = playing;
    }

    /// <summary>A move's own blend, else the one it would be played with.</summary>
    private static float BlendOf(MechMove move, float fallback) => move.Blend >= 0.0f ? move.Blend : fallback;

    public static List<Segment> Single(MechMove move) => new() { new Segment(move, 0.0f, BlendOf(move, 0.08f)) };

    /// <summary>The combo as it flows with the button held: each stroke
    /// once the last one's blow has landed and <see cref="MechSlashStats.ChainAfterHit"/>
    /// has passed, blending in over <see cref="MechSlashStats.ChainBlend"/>.</summary>
    public static List<Segment> Combo(MechSlashStats stats)
    {
        var segments = new List<Segment>();
        float start = 0.0f;
        foreach (MechMove move in stats.Combo)
        {
            if (move == null)
            {
                continue;
            }
            segments.Add(new Segment(move, start, BlendOf(move, segments.Count == 0 ? 0.08f : stats.ChainBlend)));
            float hit = move.EventTime("hit");
            start += (hit >= 0.0f ? hit : move.Length * 0.6f) + stats.ChainAfterHit;
        }
        return segments;
    }

    /// <summary>Runs the clock on by <paramref name="dt"/>, looping or
    /// stopping at the end.</summary>
    public void Advance(float dt)
    {
        if (!HasClip)
        {
            return;
        }
        Time += dt;
        if (Time >= Length)
        {
            if (Loop && Length > 0.0f)
            {
                Time %= Length;
            }
            else
            {
                Time = Length;
                Playing = false;
            }
        }
    }

    /// <summary>The move playing at <paramref name="time"/>, the latest to
    /// have started, and how far into it; null past its end.</summary>
    public Segment? At(float time, out float into)
    {
        into = 0.0f;
        for (int i = Segments.Count - 1; i >= 0; i--)
        {
            if (time >= Segments[i].Start)
            {
                into = time - Segments[i].Start;
                return into <= Segments[i].Move.Length ? Segments[i] : null;
            }
        }
        return null;
    }
}
#endif
