using DillControl.Mission;
using Godot;

namespace DillControl.UI;

/// <summary>
/// What a nest contract has left to do (<see cref="ColonyMission"/>): its
/// goals top right, each ticked when met, in a plate sized to its words; a
/// card when it is cleared and when every pickle is dead. How the colony
/// stands (asleep, waking, hanging, regrowing) is for whoever is testing it:
/// a plain debug line bottom left, not part of the game's look.
///
/// PLACEHOLDER, like <see cref="MissionHud"/>: drawn immediate-mode from a
/// <see cref="Readout"/> the mission hands it every frame; it reads nothing
/// itself.
/// </summary>
public partial class ColonyHud : Control
{
    public struct Readout
    {
        public ColonyMission.Phase Phase;
        public PodField.ColonyState Colony;
        public bool Bonus;
        public float Time;
        /// <summary>Seconds since it was cleared, or below zero.</summary>
        public float CardTime;
        public float CardSeconds;
        public int Pods;
        public int PodsLeft;
        public int Starting;
        public int Alive;
        public int AliveAllowed;
        public int Hanging;
        public int Regrowing;
        public int Dropped;
    }

    private const float Margin = 28.0f;
    private const float Pad = 18.0f;
    private const int TitleSize = 18;
    private const int LineSize = 22;
    private const int DebugSize = 14;

    private Readout _r;

    public void Show(in Readout readout)
    {
        _r = readout;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_r.Pods == 0)
        {
            return;
        }
        DrawGoals();
        DrawDebug();
        if (_r.CardTime >= 0.0f && _r.CardTime < _r.CardSeconds)
        {
            Card(_r.Bonus ? "NEST WIPED OUT" : "NEST CLEARED", _r.Bonus ? "Every pickle dead: the bonus is yours." : "Every pod destroyed.");
        }
    }

    // The goals, top right: a box ticked when met, what to do, and how far.
    private void DrawGoals()
    {
        (string Goal, string Left, bool Met, bool Bonus)[] lines =
        {
            ("DESTROY EVERY POD", $"{PickleStyle.Count(_r.PodsLeft)} left", _r.PodsLeft == 0, false),
            ($"LEAVE {PickleStyle.Count(_r.AliveAllowed)} PICKLES AT MOST", $"{PickleStyle.Count(_r.Alive)} alive", _r.Alive <= _r.AliveAllowed, false),
            ("BONUS: KILL THEM ALL", $"{PickleStyle.Count(_r.Alive)} to go", _r.Bonus, true),
        };
        float box = LineSize * 0.8f, gap = 12.0f, spacing = LineSize * 1.45f;
        float goals = 0.0f, lefts = 0.0f;
        foreach ((string goal, string left, bool _, bool _) in lines)
        {
            goals = Mathf.Max(goals, PickleStyle.Measure(goal, LineSize));
            lefts = Mathf.Max(lefts, PickleStyle.Measure(left, LineSize));
        }
        string title = _r.Phase == ColonyMission.Phase.Cleared ? "CONTRACT DONE" : "CONTRACT";
        float width = Pad + box + gap + goals + (gap * 2.5f) + lefts + Pad;
        float height = Pad + TitleSize + 10.0f + (spacing * lines.Length) + (Pad * 0.4f);
        var plate = new Rect2(new Vector2(Size.X - Margin - width, Margin), new Vector2(width, height));
        PickleStyle.Plate(this, plate, PickleStyle.Ink with { A = 0.8f }, 0.0f, 3.0f);

        float x = plate.Position.X + Pad;
        float y = plate.Position.Y + Pad + TitleSize;
        PickleStyle.Text(this, new Vector2(x, y), title, TitleSize, PickleStyle.BrineSour);
        y += 10.0f;
        foreach ((string goal, string left, bool met, bool bonus) in lines)
        {
            y += spacing;
            Color colour = met ? PickleStyle.Brine : bonus ? PickleStyle.Cream with { A = 0.7f } : PickleStyle.Cream;
            var square = new Rect2(new Vector2(x, y - box), new Vector2(box, box));
            DrawRect(square.Grow(2.0f), PickleStyle.Ink);
            DrawRect(square, met ? PickleStyle.Brine : PickleStyle.Cream with { A = 0.25f });
            PickleStyle.Text(this, new Vector2(x + box + gap, y), goal, LineSize, colour);
            float leftWidth = PickleStyle.Measure(left, LineSize);
            PickleStyle.Text(this, new Vector2(plate.End.X - Pad - leftWidth, y), left, LineSize, colour);
        }
    }

    // For testing: how the colony stands, plain and small, bottom left.
    private void DrawDebug()
    {
        string state = _r.Colony switch
        {
            PodField.ColonyState.Asleep => "asleep",
            PodField.ColonyState.Waking => "waking",
            _ => "awake",
        };
        string line = System.FormattableString.Invariant(
            $"debug  colony {state}  {Mathf.FloorToInt(_r.Time / 60.0f)}:{Mathf.FloorToInt(_r.Time % 60.0f):00}   hanging {_r.Hanging:N0}   regrowing {_r.Regrowing:N0}   dropped {_r.Dropped:N0}   pods {_r.PodsLeft:N0}/{_r.Pods:N0}");
        Font font = ThemeDB.FallbackFont;
        Vector2 size = font.GetStringSize(line, HorizontalAlignment.Left, -1, DebugSize);
        var at = new Vector2(Margin, Size.Y - Margin);
        DrawRect(new Rect2(at - new Vector2(6, size.Y), size + new Vector2(12, 8)), new Color(0, 0, 0, 0.45f));
        DrawString(font, at, line, HorizontalAlignment.Left, -1, DebugSize, new Color(0.75f, 0.78f, 0.72f));
    }

    private void Card(string title, string line)
    {
        Vector2 centre = Size * 0.5f;
        Rect2 card = new(centre - new Vector2(330.0f, 110.0f), new Vector2(660.0f, 220.0f));
        PickleStyle.Plate(this, card, PickleStyle.Ink with { A = 0.85f });
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -55.0f), title, 54, PickleStyle.Brine);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 0.0f), line, 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 44.0f),
            $"Pickles left  {PickleStyle.Count(_r.Alive)} of {PickleStyle.Count(_r.Starting)}", 24, PickleStyle.Cream);
    }
}
