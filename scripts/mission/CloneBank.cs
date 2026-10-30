using Godot;
using DillControl.Levels;
using DillControl.Player;
using DillControl.UI;

namespace DillControl.Mission;

/// <summary>
/// Death is cloning (decision 0053). The player's health running out is a body
/// lost: the next clone is printed and comes down in the company's pod, the way
/// the first did (<see cref="PodLanding"/> hears the new body and drops it).
/// Nothing the body carries is lost. Losing the last body voids the contract;
/// <c>reload</c> starts it again.
///
/// It reads the player and the level, and changes the player in one way: a body
/// that is down is stowed, so it stops acting while the next is printed.
/// </summary>
[GlobalClass]
public partial class CloneBank : Node
{
    [Export] public CloneRules Rules { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export] public LevelRoot Level { get; set; }

    public enum Phase { Alive, Printing, Void }

    public Phase Current { get; private set; } = Phase.Alive;

    /// <summary>Which body this is, from one.</summary>
    public int Body { get; private set; }

    private PlayerController _player;
    private CloneHud _hud;
    private float _phaseTime;
    private float _sinceBody;

    public override void _Ready()
    {
        Rules ??= GD.Load<CloneRules>(CloneRules.DefaultPath);
        if (Level == null || Rules == null)
        {
            GD.PushError($"CloneBank '{Name}' needs its Level and Rules; no clones.");
            SetProcess(false);
            return;
        }
        Level.PlayerSpawned += OnPlayerSpawned;

        var layer = new CanvasLayer { Name = "CloneHud", Layer = 7 };
        _hud = new CloneHud { Name = "Hud" };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_hud);
        AddChild(layer);
    }

    public override void _ExitTree()
    {
        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
        Body++;
        _sinceBody = 0.0f;
        SetPhase(Phase.Alive);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _phaseTime += dt;
        _sinceBody += dt;

        switch (Current)
        {
            case Phase.Alive:
                if (_player != null && IsInstanceValid(_player) && (_player.Health?.IsPickled ?? false))
                {
                    _player.SetStowed(true);
                    SetPhase(Body >= Rules.Clones ? Phase.Void : Phase.Printing);
                }
                break;
            case Phase.Printing:
                if (_phaseTime >= Rules.PrintSeconds)
                {
                    Level.RespawnPlayer();
                }
                break;
            case Phase.Void:
                if (Input.IsActionJustPressed("reload"))
                {
                    GetTree().ReloadCurrentScene();
                }
                break;
        }

        _hud.Show(new CloneHud.Readout
        {
            Phase = Current,
            PhaseTime = _phaseTime,
            Body = Body,
            Clones = Rules.Clones,
            PrintSeconds = Rules.PrintSeconds,
            FlashTime = _sinceBody,
            FlashSeconds = Rules.FlashSeconds,
        });
    }

    private void SetPhase(Phase phase)
    {
        Current = phase;
        _phaseTime = 0.0f;
        GD.Print(System.FormattableString.Invariant($"[clones] {phase}, body {Body} of {Rules.Clones}"));
    }
}
