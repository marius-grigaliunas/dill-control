using DillControl.Core;
using DillControl.Horde;
using DillControl.UI;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// A nest contract's goal (docs/decisions/0034): destroy every pod, and leave
/// at most <see cref="ColonyRules.MaxAliveShare"/> of the colony's pickles
/// alive. Killing every one earns the bonus, before or after it is cleared.
///
/// A pickle is alive hanging full on its pod or dropped into the horde
/// (<see cref="HordeManager.SpawnOrigin.Pod"/>); one still regrowing is not
/// yet. It reads <see cref="Pods"/> and the horde and hands
/// <see cref="ColonyHud"/> what to show every frame.
/// </summary>
[GlobalClass]
public partial class ColonyMission : Node
{
    [Export] public ColonyRules Rules { get; set; }
    [ExportGroup("Scene Wiring")]
    [Export] public PodField Pods { get; set; }
    [Export] public HordeDirector Horde { get; set; }

    public enum Phase
    {
        /// <summary>Not yet harmed: nothing has dropped.</summary>
        Asleep,
        Fighting,
        /// <summary>Every pod destroyed and few enough left.</summary>
        Cleared,
    }

    public Phase Current { get; private set; } = Phase.Asleep;
    /// <summary>Every pickle of the colony killed.</summary>
    public bool Bonus { get; private set; }
    public int Starting { get; private set; }
    public int Alive { get; private set; }
    public int PodsLeft { get; private set; }
    /// <summary>The most alive the nest may be cleared with.</summary>
    public int AliveAllowed => Mathf.FloorToInt((Rules?.MaxAliveShare ?? 0.2f) * Starting);

    private ColonyHud _hud;
    private float _time;
    private float _clearedAt = -1.0f;

    public override void _Ready()
    {
        Rules ??= GD.Load<ColonyRules>(ColonyRules.DefaultPath);
        if (Pods == null)
        {
            GD.PushError($"ColonyMission '{Name}' needs its PodField; no contract.");
            SetProcess(false);
            return;
        }
        var layer = new CanvasLayer { Name = "ColonyHud", Layer = 5 };
        _hud = new ColonyHud { Name = "Hud" };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_hud);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        if (Pods.Count == 0)
        {
            // Still planting.
            return;
        }
        if (Current == Phase.Asleep)
        {
            // Pods are added as the vines are planted, all before it wakes.
            Starting = Pods.Count;
        }
        HordeManager horde = Horde != null && IsInstanceValid(Horde) ? Horde.Manager : null;
        Alive = Pods.Full + (horde?.ExtraCount(HordeManager.SpawnOrigin.Pod) ?? 0);
        PodsLeft = Pods.Count - Pods.Destroyed;

        if (Current == Phase.Asleep && Pods.Colony != PodField.ColonyState.Asleep)
        {
            Current = Phase.Fighting;
        }
        if (Current != Phase.Asleep)
        {
            _time += (float)delta;
        }
        if (Current != Phase.Cleared && PodsLeft == 0 && Alive <= AliveAllowed)
        {
            Current = Phase.Cleared;
            _clearedAt = _time;
            Report("cleared");
        }
        if (!Bonus && Current != Phase.Asleep && Alive == 0 && PodsLeft == 0)
        {
            Bonus = true;
            _clearedAt = _time;
            Report("bonus");
        }

        _hud?.Show(new ColonyHud.Readout
        {
            Phase = Current,
            Colony = Pods.Colony,
            Bonus = Bonus,
            Time = _time,
            CardTime = _clearedAt >= 0.0f ? _time - _clearedAt : -1.0f,
            CardSeconds = Rules.CardSeconds,
            Pods = Pods.Count,
            PodsLeft = PodsLeft,
            Starting = Starting,
            Alive = Alive,
            AliveAllowed = AliveAllowed,
            Hanging = Pods.Full,
            Regrowing = Pods.Regrowing,
            Dropped = Pods.Dropped,
        });
    }

    private void Report(string what)
    {
        string line = System.FormattableString.Invariant(
            $"[colony] {what} after {_time:F0} s: {PodsLeft} pods left, {Alive} of {Starting} pickles alive");
        GD.Print(line);
        if (PerfLog.Recording)
        {
            PerfLog.Event($"colony-{what}");
        }
    }
}
