#if TOOLS
using Godot;

namespace DillControl.Editor;

/// <summary>
/// The asset lab's timeline under the view: each move of the clip as a bar
/// from where it starts, its markers on it ('hit' red, 'shake' orange, a
/// sound blue), and the playhead. Click or drag to scrub; it pauses there.
/// A marker dragged moves to another frame of its move (<see cref="MarkerMoved"/>).
/// </summary>
[Tool]
public partial class LabTimeline : Control
{
    public LabPreview Preview;

    private const float Margin = 8.0f;
    private const float RowHeight = 14.0f;

    /// <summary>A marker was dragged: the move, which of its events, and
    /// its new time into the move, on one of its frames.</summary>
    public event System.Action<Vehicles.MechMove, int, float> MarkerMoved;

    private int _dragSegment = -1;
    private int _dragEvent = -1;

    private int Rows => Mathf.Min(Transport.Segments.Count, 2);

    private float RowTop(int segment) => 4.0f + (segment % Rows) * (RowHeight + 2.0f);

    /// <summary>The marker under a point of the timeline, if any.</summary>
    private bool MarkerAt(Vector2 point, out int segment, out int index)
    {
        for (segment = 0; segment < Transport.Segments.Count; segment++)
        {
            LabTransport.Segment s = Transport.Segments[segment];
            float top = RowTop(segment);
            if (point.Y < top - 3.0f || point.Y > top + RowHeight + 3.0f)
            {
                continue;
            }
            for (index = 0; index < s.Move.EventNames.Length && index < s.Move.EventTimes.Length; index++)
            {
                if (Mathf.Abs(point.X - X(s.Start + s.Move.EventTimes[index])) <= 5.0f)
                {
                    return true;
                }
            }
        }
        segment = index = -1;
        return false;
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 46);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
    }

    private LabTransport Transport => Preview?.Transport;

    private float X(float time) => Margin + (Size.X - Margin * 2.0f) * (Transport.Length > 0.0f ? time / Transport.Length : 0.0f);

    private float TimeAt(float x) => Transport.Length * Mathf.Clamp((x - Margin) / Mathf.Max(Size.X - Margin * 2.0f, 1.0f), 0.0f, 1.0f);

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.1f, 0.1f, 0.12f));
        // Not laid out yet: no width to draw on (and the tick spacing below
        // would never reach 4 px).
        if (Size.X < Margin * 4.0f)
        {
            return;
        }
        if (Transport == null || !Transport.HasClip)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(Margin, 28), "No clip: pick a move, the combo or a cycle to play it here.", HorizontalAlignment.Left, -1, 13, new Color(0.6f, 0.6f, 0.6f));
            return;
        }
        Font font = ThemeDB.FallbackFont;
        if (Transport.Recording is LabRecording recording)
        {
            // One bar for the cycle, a tick where each step comes down
            // (left feet green, right yellow, crutch arms blue).
            DrawRect(new Rect2(X(0.0f), 4.0f, X(Transport.Length) - X(0.0f), RowHeight), new Color(0.3f, 0.35f, 0.45f));
            DrawString(font, new Vector2(X(0.0f) + 3, 15), $"{Transport.Clip}, {recording.Steps.Count - 1} steps", HorizontalAlignment.Left, -1, 11, Colors.White);
            foreach ((Node3D node, float time) in recording.Steps)
            {
                string name = node.Name;
                Color tick = name.StartsWith("Arm") ? new Color(0.4f, 0.7f, 1.0f) : name.EndsWith("L") ? new Color(0.5f, 1.0f, 0.4f) : new Color(1.0f, 0.9f, 0.3f);
                float x = X(time);
                DrawLine(new Vector2(x, 2.0f), new Vector2(x, 6.0f + RowHeight * 2.0f), tick, 2.0f);
                DrawString(font, new Vector2(x + 2, 8.0f + RowHeight * 2.0f), name, HorizontalAlignment.Left, -1, 10, tick);
            }
        }
        float rowHeight = RowHeight;
        for (int i = 0; i < Transport.Segments.Count; i++)
        {
            LabTransport.Segment segment = Transport.Segments[i];
            float y = RowTop(i);
            float x0 = X(segment.Start), x1 = X(segment.Start + segment.Move.Length);
            var colour = i % 2 == 0 ? new Color(0.25f, 0.4f, 0.55f) : new Color(0.3f, 0.5f, 0.35f);
            DrawRect(new Rect2(x0, y, x1 - x0, rowHeight), colour);
            DrawRect(new Rect2(x0, y, X(segment.Start + segment.Blend) - x0, rowHeight), colour.Lightened(0.25f));
            DrawString(font, new Vector2(x0 + 3, y + 11), ShortName(segment.Move), HorizontalAlignment.Left, x1 - x0 - 4, 11, Colors.White);
            for (int e = 0; e < segment.Move.EventNames.Length && e < segment.Move.EventTimes.Length; e++)
            {
                string name = segment.Move.EventNames[e];
                Color mark = name == "hit" ? new Color(1.0f, 0.25f, 0.2f) : name == "shake" ? new Color(1.0f, 0.6f, 0.1f) : new Color(0.4f, 0.7f, 1.0f);
                float x = X(segment.Start + segment.Move.EventTimes[e]);
                bool held = i == _dragSegment && e == _dragEvent;
                DrawLine(new Vector2(x, y - 2), new Vector2(x, y + rowHeight + 2), held ? Colors.White : mark, held ? 3.0f : 2.0f);
            }
        }
        // Frame ticks along the bottom, one a frame when there is room.
        float bottom = Size.Y - 10.0f;
        float frame = 1.0f / Transport.Fps;
        float perFrame = X(frame) - X(0.0f);
        float every = frame * Mathf.Max(1.0f, Mathf.Ceil(4.0f / Mathf.Max(perFrame, 1e-3f)));
        for (float t = 0.0f; t <= Transport.Length + 1e-4f; t += every)
        {
            DrawLine(new Vector2(X(t), bottom), new Vector2(X(t), bottom + 4), new Color(0.5f, 0.5f, 0.5f));
        }
        float head = X(Transport.Time);
        DrawLine(new Vector2(head, 0), new Vector2(head, Size.Y), Colors.White, 2.0f);
    }

    /// <summary>A move's name without its mech's.</summary>
    public static string ShortName(Vehicles.MechMove move)
    {
        string name = move.ResourcePath.GetFile().GetBaseName();
        return move.Mech != "" && name.StartsWith(move.Mech + "_") ? name.Substring(move.Mech.Length + 1) : name;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Transport == null || !Transport.HasClip)
        {
            return;
        }
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (!button.Pressed)
            {
                _dragSegment = _dragEvent = -1;
            }
            else if (MarkerAt(button.Position, out _dragSegment, out _dragEvent))
            {
                Transport.Playing = false;
            }
            else
            {
                Preview.Seek(TimeAt(button.Position.X));
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            if (_dragSegment >= 0 && _dragSegment < Transport.Segments.Count)
            {
                LabTransport.Segment segment = Transport.Segments[_dragSegment];
                float into = Mathf.Clamp(TimeAt(motion.Position.X) - segment.Start, 0.0f, segment.Move.Length);
                into = Mathf.Round(into * segment.Move.Fps) / segment.Move.Fps;
                MarkerMoved?.Invoke(segment.Move, _dragEvent, into);
                Preview.Seek(segment.Start + into);
            }
            else
            {
                Preview.Seek(TimeAt(motion.Position.X));
            }
            AcceptEvent();
        }
    }
}
#endif
