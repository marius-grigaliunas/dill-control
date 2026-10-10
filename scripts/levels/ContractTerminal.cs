using Godot;
using DillControl.Mission;
using DillControl.Player;
using DillControl.UI;

namespace DillControl.Levels;

/// <summary>
/// The computer in the player's ship. Walk up to it and press interact, and
/// the contracts on offer come up on the <see cref="ContractBoard"/>.
/// </summary>
[GlobalClass]
public partial class ContractTerminal : Node3D
{
    /// <summary>The volume the player has to be in to use it.</summary>
    [Export]
    public Area3D Reach { get; set; }

    [Export]
    public ContractBoard Board { get; set; }

    /// <summary>"F  contracts", shown while the player is in reach.</summary>
    [Export]
    public Label3D Prompt { get; set; }

    private bool _inReach;

    public override void _Ready()
    {
        if (Reach != null)
        {
            Reach.BodyEntered += body => SetInReach(body, true);
            Reach.BodyExited += body => SetInReach(body, false);
        }
        if (Prompt != null)
        {
            Prompt.Visible = false;
        }
    }

    private void SetInReach(Node3D body, bool inReach)
    {
        if (body is not PlayerController)
        {
            return;
        }
        _inReach = inReach;
        if (Prompt != null)
        {
            Prompt.Visible = inReach;
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_inReach || Board == null || Board.IsOpen || !@event.IsActionPressed("interact"))
        {
            return;
        }

        if (Session.Instance == null)
        {
            GD.PushError("ContractTerminal: no Session autoload; no contracts to show.");
            return;
        }

        Board.Open(Session.Instance.Offers);
        GetViewport().SetInputAsHandled();
    }

    /// <summary>For a test harness: take the first contract on the open board.</summary>
    public void BenchTake() => Board?.BenchTakeFirst();

    /// <summary>For a test harness: open the board from anywhere.</summary>
    public void BenchOpen()
    {
        if (Board != null && Session.Instance != null)
        {
            Board.Open(Session.Instance.Offers);
        }
    }
}
