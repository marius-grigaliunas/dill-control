using System.Collections.Generic;
using Godot;
using DillControl.Mission;

namespace DillControl.UI;

/// <summary>
/// The ship's computer screen: the contracts on offer as cards, one of which
/// the player takes. Taking one hands it to the <see cref="Session"/> and goes
/// to the contract level, which builds it behind its own loading screen
/// (<c>docs/decisions/0021</c>).
///
/// Pauses the ship while it is up, so the player does not walk off under it.
/// </summary>
[GlobalClass]
public partial class ContractBoard : CanvasLayer
{
    [Export]
    public Control Root { get; set; }

    /// <summary>Where the cards go.</summary>
    [Export]
    public Container Cards { get; set; }

    /// <summary>One card; its root is a <see cref="ContractCard"/>.</summary>
    [Export]
    public PackedScene CardScene { get; set; }

    [Export]
    public Button CloseButton { get; set; }

    [Export(PropertyHint.File, "*.tscn")]
    public string ContractScene { get; set; } = "res://scenes/levels/lvl_contract.tscn";

    public bool IsOpen => Root != null && Root.Visible;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (Root != null)
        {
            Root.Visible = false;
        }
        if (CloseButton != null)
        {
            CloseButton.Pressed += Close;
        }
    }

    public void Open(IReadOnlyList<ContractSpec> offers)
    {
        if (Root == null || Cards == null || CardScene == null)
        {
            GD.PushError($"ContractBoard '{Name}' is missing its root, card container or card scene.");
            return;
        }

        foreach (Node old in Cards.GetChildren())
        {
            old.QueueFree();
        }
        foreach (ContractSpec offer in offers)
        {
            ContractCard card = CardScene.Instantiate<ContractCard>();
            Cards.AddChild(card);
            card.Fill(offer);
            card.Chosen += OnChosen;
        }

        Root.Visible = true;
        GetTree().Paused = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        if (Root != null)
        {
            Root.Visible = false;
        }
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // East face button backs out while the board is up. It is not
        // ui_cancel, so the same button can crouch once the board is closed.
        if (IsOpen && (@event.IsActionPressed("ui_cancel")
            || @event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B }))
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>For a test harness: take the first contract on the board.</summary>
    public void BenchTakeFirst()
    {
        if (IsOpen && Cards != null && Cards.GetChildCount() > 0 && Cards.GetChild(0) is ContractCard card)
        {
            OnChosen(card);
        }
    }

    private void OnChosen(ContractCard card)
    {
        Session.Instance?.Accept(card.Contract);
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        GetTree().ChangeSceneToFile(ContractScene);
    }
}
