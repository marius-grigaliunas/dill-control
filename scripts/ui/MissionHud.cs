using Godot;
using DillControl.Mission;

namespace DillControl.UI;

/// <summary>
/// What a drop-and-extract contract has left to do (decision 0026): the cull,
/// the jar, the clones and the clock in a plate top left, a marker over the
/// jar until it cracks and over the pod home once it is sent, and the big
/// words between bodies and at the end.
///
/// PLACEHOLDER, like <see cref="PlayerHud"/>: the information is meant to
/// survive the test, the look is not. Drawn immediate-mode from a
/// <see cref="Readout"/> the mission hands it every frame; it reads nothing
/// itself.
/// </summary>
public partial class MissionHud : Control
{
    public struct Readout
    {
        public ExtractionMission.Phase Phase;
        public float PhaseTime;
        public float Time;
        public int Killed;
        public int Target;
        public int Alive;
        public float CoreLeft;
        public float CoreTarget;
        public bool Cracked;
        public int ClonesLeft;
        public int Clones;
        public int Body;
        public float PrintSeconds;
        public bool JobDone;
        public float ExtractionIn;
        public bool ExtractionOpen;
        public Vector3? ExtractionAt;
        public Vector3? JarAt;
        public Vector3? PlayerAt;
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

        if (_r.Phase is ExtractionMission.Phase.Won or ExtractionMission.Phase.Lost)
        {
            DrawBanner();
            return;
        }

        DrawPlate();
        if (!_r.Cracked && _r.JarAt is Vector3 jar)
        {
            DrawMarker(jar + (Vector3.Up * 20.0f), "JAR", PickleStyle.Lid);
        }
        if (_r.ExtractionAt is Vector3 pod)
        {
            DrawMarker(pod + (Vector3.Up * 4.0f), _r.ExtractionOpen ? "POD HOME" : "POD INBOUND", PickleStyle.BrineSour);
        }
        DrawBanner();
    }

    private void DrawPlate()
    {
        bool culled = _r.Killed >= _r.Target;
        Rect2 plate = new(new Vector2(28.0f, 24.0f), new Vector2(360.0f, _r.JobDone ? 176.0f : 146.0f));
        PickleStyle.Plate(this, plate, PickleStyle.Ink with { A = 0.72f }, 10.0f, 3.0f);

        float x = plate.Position.X + 22.0f;
        float y = plate.Position.Y + 34.0f;
        PickleStyle.Text(this, new Vector2(x, y), "CULL HALF, CRACK THE JAR", 22, PickleStyle.Cream);
        y += 32.0f;
        PickleStyle.Text(this, new Vector2(x, y),
            $"PICKLES  {PickleStyle.Count(_r.Killed)} / {PickleStyle.Count(_r.Target)}", 20,
            culled ? PickleStyle.Brine : PickleStyle.Cream);
        y += 28.0f;
        string jar = _r.Cracked
            ? "JAR  CRACKED"
            : System.FormattableString.Invariant($"JAR CORE  {_r.CoreLeft * 100.0f:F0}%  (crack at {_r.CoreTarget * 100.0f:F0}%)");
        PickleStyle.Text(this, new Vector2(x, y), jar, 20, _r.Cracked ? PickleStyle.Brine : PickleStyle.Cream);
        y += 28.0f;
        PickleStyle.Text(this, new Vector2(x, y),
            $"BODY {Mathf.Min(_r.Body, _r.Clones)} OF {_r.Clones}     {Clock(_r.Time)}", 20, PickleStyle.Cream);

        if (_r.JobDone)
        {
            y += 30.0f;
            string line = _r.ExtractionAt == null
                ? System.FormattableString.Invariant($"POD SENT IN {Mathf.Max(0.0f, _r.ExtractionIn):F0} S")
                : _r.PlayerAt is Vector3 me && _r.ExtractionAt is Vector3 pod
                    ? System.FormattableString.Invariant($"GET TO THE POD  {me.DistanceTo(pod):F0} M")
                    : "GET TO THE POD";
            PickleStyle.Text(this, new Vector2(x, y), line, 22, PickleStyle.BrineSour);
        }
    }

    private void DrawBanner()
    {
        Vector2 centre = Size * 0.5f;
        switch (_r.Phase)
        {
            case ExtractionMission.Phase.Landing:
                if (_r.Body <= 1)
                {
                    PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -120.0f), "INBOUND", 56, PickleStyle.Brine);
                    PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -64.0f),
                        "Cull half the nest. Crack the jar. Get to the pod.", 26, PickleStyle.Cream);
                }
                else
                {
                    PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -120.0f), $"CLONE {_r.Body} OF {_r.Clones}", 56,
                        PickleStyle.Brine);
                }
                break;

            case ExtractionMission.Phase.Down:
                PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -40.0f), "PICKLED!", 72, PickleStyle.Lid);
                PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 30.0f),
                    System.FormattableString.Invariant(
                        $"Printing clone {_r.Body + 1} of {_r.Clones}... {Mathf.Max(0.0f, _r.PrintSeconds - _r.PhaseTime):F0}"),
                    28, PickleStyle.Cream);
                break;

            case ExtractionMission.Phase.Leaving:
                PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -120.0f), "EXTRACTING", 56, PickleStyle.Brine);
                break;

            case ExtractionMission.Phase.Won:
                EndCard("CONTRACT COMPLETE", PickleStyle.Brine, "The company thanks you for your service.");
                break;

            case ExtractionMission.Phase.Lost:
                EndCard("CONTRACT VOID", PickleStyle.Lid, "The company has stopped printing you.");
                break;
        }
    }

    private void EndCard(string title, Color colour, string line)
    {
        Vector2 centre = Size * 0.5f;
        Rect2 card = new(centre - new Vector2(330.0f, 150.0f), new Vector2(660.0f, 300.0f));
        PickleStyle.Plate(this, card, PickleStyle.Ink with { A = 0.85f });
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -95.0f), title, 54, colour);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, -40.0f), line, 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 10.0f),
            $"Pickles culled  {PickleStyle.Count(_r.Killed)}     Left  {PickleStyle.Count(_r.Alive)}", 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 48.0f),
            $"Bodies used  {Mathf.Min(_r.Body, _r.Clones)} of {_r.Clones}     Time  {Clock(_r.Time)}", 24, PickleStyle.Cream);
        PickleStyle.TextCentred(this, centre + new Vector2(0.0f, 110.0f), "R  another go", 26, PickleStyle.BrineSour);
    }

    /// <summary>A diamond over a world point with its name and distance; pinned to the screen's edge when off it.</summary>
    private void DrawMarker(Vector3 world, string label, Color colour) =>
        PickleStyle.Marker(this, Size, GetViewport().GetCamera3D(), world, label, colour);

    private static string Clock(float seconds)
    {
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60:D2}:{s % 60:D2}";
    }
}
