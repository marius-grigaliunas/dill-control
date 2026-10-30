using Godot;
using DillControl.Mission;

namespace DillControl.UI;

/// <summary>
/// The clones on screen (<see cref="CloneBank"/>): which body this is, the
/// words between bodies, the card when the last is lost, and the cloning lab's
/// flashes (decision 0054) as each body begins its fall. The flashes are
/// deliberately unexplained: white cuts and bars, a few frames each, no words.
///
/// PLACEHOLDER, like <see cref="ColonyHud"/>: drawn immediate-mode from a
/// <see cref="Readout"/> handed to it every frame.
/// </summary>
public partial class CloneHud : Control
{
    public struct Readout
    {
        public CloneBank.Phase Phase;
        public float PhaseTime;
        public int Body;
        public int Clones;
        public float PrintSeconds;
        /// <summary>Seconds since the current body was made.</summary>
        public float FlashTime;
        public float FlashSeconds;
    }

    private Readout _r;

    public void Show(in Readout readout)
    {
        _r = readout;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_r.Clones == 0)
        {
            return;
        }
        if (_r.Phase == CloneBank.Phase.Void)
        {
            DrawVoid();
            return;
        }
        DrawBodies();
        if (_r.Phase == CloneBank.Phase.Printing)
        {
            Vector2 centre = Size * 0.5f;
            PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -40.0f), "BODY LOST", 72, PickleStyle.Lid);
            PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 30.0f),
                System.FormattableString.Invariant(
                    $"Printing clone {_r.Body + 1} of {_r.Clones}... {Mathf.Max(0.0f, _r.PrintSeconds - _r.PhaseTime):F0}"),
                28, PickleStyle.Cream);
        }
        else
        {
            DrawFlashes();
        }
    }

    /// <summary>Top left: a pip for each body, the ones spent dark.</summary>
    private void DrawBodies()
    {
        Rect2 plate = new(new Vector2(28.0f, 24.0f), new Vector2(112.0f + (_r.Clones * 26.0f), 44.0f));
        PickleStyle.Plate(this, plate, PickleStyle.Ink with { A = 0.72f }, 8.0f, 3.0f);
        PickleStyle.Text(this, plate.Position + new Vector2(18.0f, 29.0f), "BODY", 20, PickleStyle.Cream);
        for (int i = 0; i < _r.Clones; i++)
        {
            Color pip = i < _r.Body - 1
                ? PickleStyle.Lid with { A = 0.5f }
                : i == _r.Body - 1 ? PickleStyle.Brine : PickleStyle.Cream with { A = 0.35f };
            PickleStyle.Disc(this, plate.Position + new Vector2(102.0f + (i * 26.0f), 22.0f), 8.0f, pip);
        }
    }

    /// <summary>
    /// White cuts, fading out as they flicker, with a bar or two of the lab's
    /// green across them. Hashed from the tick of the flicker, so it is the
    /// same every time.
    /// </summary>
    private void DrawFlashes()
    {
        if (_r.FlashSeconds <= 0.0f || _r.FlashTime >= _r.FlashSeconds)
        {
            return;
        }
        float t = _r.FlashTime / _r.FlashSeconds;
        float fade = 1.0f - t;
        uint tick = (uint)Mathf.FloorToInt(_r.FlashTime * 24.0f);
        uint hash = tick * 2654435761u;
        bool on = (((hash >> 7) & 3u) != 0u) || t < 0.15f;
        if (on)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), PickleStyle.Cream with { A = 0.9f * fade });
        }
        for (uint i = 0; i < 3; i++)
        {
            uint bar = (tick + (i * 7u)) * 2246822519u;
            float y = ((bar >> 5) & 1023u) / 1023.0f * Size.Y;
            float height = 6.0f + (((bar >> 15) & 63u) * 1.5f);
            DrawRect(new Rect2(0.0f, y, Size.X, height), PickleStyle.BrineDeep with { A = 0.7f * fade });
        }
    }

    private void DrawVoid()
    {
        Vector2 centre = Size * 0.5f;
        Rect2 card = new(centre - new Vector2(330.0f, 120.0f), new Vector2(660.0f, 240.0f));
        PickleStyle.Plate(this, card, PickleStyle.Ink with { A = 0.85f });
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -62.0f), "CONTRACT VOID", 54, PickleStyle.Lid);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -8.0f), "The company has stopped printing you.", 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 70.0f), "R  another go", 26, PickleStyle.BrineSour);
    }
}
