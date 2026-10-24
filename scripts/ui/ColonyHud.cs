using DillControl.Mission;
using Godot;

namespace DillControl.UI;

/// <summary>
/// What a nest contract has left to do (<see cref="ColonyMission"/>): the
/// pods left, the pickles alive against what the nest may be cleared with,
/// how many still hang, and whether the colony sleeps. A card when it is
/// cleared, and when every pickle is dead.
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
    }

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
        DrawPlate();
        if (_r.CardTime >= 0.0f && _r.CardTime < _r.CardSeconds)
        {
            Card(_r.Bonus ? "NEST WIPED OUT" : "NEST CLEARED", _r.Bonus ? "Every pickle dead: the bonus is yours." : "Every pod destroyed.");
        }
    }

    private void DrawPlate()
    {
        bool podsDone = _r.PodsLeft == 0;
        bool fewEnough = _r.Alive <= _r.AliveAllowed;
        Rect2 plate = new(new Vector2(28.0f, 24.0f), new Vector2(400.0f, 176.0f));
        PickleStyle.Plate(this, plate, PickleStyle.Ink with { A = 0.72f }, 10.0f, 3.0f);
        float x = plate.Position.X + 22.0f;
        float y = plate.Position.Y + 34.0f;
        string goal = _r.Phase == ColonyMission.Phase.Cleared
            ? _r.Bonus ? "WIPED OUT" : "CLEARED: KILL THE REST FOR THE BONUS"
            : "DESTROY EVERY POD";
        PickleStyle.Text(this, new Vector2(x, y), goal, 22, _r.Phase == ColonyMission.Phase.Cleared ? PickleStyle.Brine : PickleStyle.Cream);
        y += 32.0f;
        PickleStyle.Text(this, new Vector2(x, y), $"PODS LEFT  {PickleStyle.Count(_r.PodsLeft)} / {PickleStyle.Count(_r.Pods)}", 20,
            podsDone ? PickleStyle.Brine : PickleStyle.Cream);
        y += 28.0f;
        PickleStyle.Text(this, new Vector2(x, y), $"PICKLES ALIVE  {PickleStyle.Count(_r.Alive)}  (at most {PickleStyle.Count(_r.AliveAllowed)})", 20,
            fewEnough ? PickleStyle.Brine : PickleStyle.Cream);
        y += 28.0f;
        PickleStyle.Text(this, new Vector2(x, y), $"STILL HANGING  {PickleStyle.Count(_r.Hanging)}", 20, PickleStyle.Cream);
        y += 30.0f;
        string colony = _r.Colony switch
        {
            PodField.ColonyState.Asleep => "THE COLONY SLEEPS",
            PodField.ColonyState.Waking => "THE COLONY IS WAKING",
            _ => System.FormattableString.Invariant($"THE COLONY IS AWAKE  {Mathf.FloorToInt(_r.Time / 60.0f)}:{Mathf.FloorToInt(_r.Time % 60.0f):00}"),
        };
        PickleStyle.Text(this, new Vector2(x, y), colony, 20, _r.Colony == PodField.ColonyState.Asleep ? PickleStyle.BrineDeep : PickleStyle.BrineSour);
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
