using Godot;
using DillControl.Mission;

namespace DillControl.UI;

/// <summary>
/// The way home on screen (<see cref="PodExtraction"/>): the words once the
/// contract is cleared, a marker over the pod from the moment it is sent, and
/// the card when it has gone.
///
/// PLACEHOLDER, like <see cref="ColonyHud"/>: drawn immediate-mode from a
/// <see cref="Readout"/> handed to it every frame.
/// </summary>
public partial class ExtractionHud : Control
{
    public struct Readout
    {
        public PodExtraction.Phase Phase;
        public float Countdown;
        public Vector3? PodAt;
        public float Time;
        public int Starting;
        public int Alive;
        public bool Bonus;
    }

    private Readout _r;

    public void Show(in Readout readout)
    {
        _r = readout;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 top = new(Size.X * 0.5f, 120.0f);
        switch (_r.Phase)
        {
            case PodExtraction.Phase.Counting:
                PickleStyle.TextCentred(this, top, "CONTRACT CLEARED", 48, PickleStyle.Brine);
                PickleStyle.TextCentred(this, top + new Vector2(0.0f, 46.0f),
                    System.FormattableString.Invariant($"Pod sent in {Mathf.CeilToInt(_r.Countdown)}"), 26, PickleStyle.Cream);
                break;
            case PodExtraction.Phase.Inbound:
                PickleStyle.TextCentred(this, top, "POD INBOUND", 48, PickleStyle.BrineSour);
                DrawPod("POD INBOUND");
                break;
            case PodExtraction.Phase.Open:
                PickleStyle.TextCentred(this, top, "GET TO THE POD", 48, PickleStyle.Brine);
                DrawPod("POD HOME");
                break;
            case PodExtraction.Phase.Leaving:
                PickleStyle.TextCentred(this, top, "EXTRACTING", 48, PickleStyle.Brine);
                break;
            case PodExtraction.Phase.Done:
                DrawCard();
                break;
        }
    }

    private void DrawPod(string label)
    {
        if (_r.PodAt is Vector3 at)
        {
            PickleStyle.Marker(this, Size, GetViewport().GetCamera3D(), at, label, PickleStyle.BrineSour);
        }
    }

    private void DrawCard()
    {
        Vector2 centre = Size * 0.5f;
        Rect2 card = new(centre - new Vector2(330.0f, 150.0f), new Vector2(660.0f, 300.0f));
        PickleStyle.Plate(this, card, PickleStyle.Ink with { A = 0.85f });
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -95.0f), "CONTRACT COMPLETE", 54, PickleStyle.Brine);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -40.0f), "The company thanks you for your service.", 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 10.0f),
            $"Cucumbers left  {PickleStyle.Count(_r.Alive)} of {PickleStyle.Count(_r.Starting)}{(_r.Bonus ? "     ALL CULLED" : "")}", 24, PickleStyle.Cream);
        int seconds = Mathf.FloorToInt(_r.Time);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 48.0f), $"Time  {seconds / 60:D2}:{seconds % 60:D2}", 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 110.0f), "R  another go", 26, PickleStyle.BrineSour);
    }
}
