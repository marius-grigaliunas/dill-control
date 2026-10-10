using Godot;
using DillControl.Mission;

namespace DillControl.UI;

/// <summary>
/// One contract on the ship's computer: the planet's designation, what kind
/// of planet, how bad the infestation is, and a button to take it.
/// </summary>
[GlobalClass]
public partial class ContractCard : PanelContainer
{
    [Export]
    public Label DesignationLabel { get; set; }

    [Export]
    public Label PlanetLabel { get; set; }

    [Export]
    public Label HordeLabel { get; set; }

    [Export]
    public Label NestLabel { get; set; }

    [Export]
    public Button AcceptButton { get; set; }

    /// <summary>Local signal: this card's contract was taken.</summary>
    [Signal]
    public delegate void ChosenEventHandler(ContractCard card);

    public ContractSpec Contract { get; private set; }

    public override void _Ready()
    {
        if (AcceptButton != null)
        {
            AcceptButton.Pressed += () => EmitSignal(SignalName.Chosen, this);
        }
        if (DesignationLabel != null)
        {
            DesignationLabel.LabelSettings = new LabelSettings
            {
                Font = PickleStyle.Font,
                FontSize = 40,
                FontColor = PickleStyle.Brine,
                OutlineSize = PickleStyle.OutlineFor(40),
                OutlineColor = PickleStyle.Ink,
            };
        }
    }

    public void Fill(ContractSpec contract)
    {
        Contract = contract;
        if (DesignationLabel != null)
        {
            DesignationLabel.Text = contract.Designation;
        }
        if (PlanetLabel != null)
        {
            PlanetLabel.Text = System.FormattableString.Invariant(
                $"{Capitalise(contract.SizeClass)} {contract.Archetype?.DisplayName.ToLowerInvariant() ?? "planet"}, {contract.Planet.Radius:F0} m\n{contract.Biome?.DisplayName ?? "Unknown"}");
        }
        if (HordeLabel != null)
        {
            HordeLabel.Text = System.FormattableString.Invariant(
                $"{contract.Tier?.DisplayName ?? "Infested"}\nabout {PickleStyle.Count(contract.Tier?.AgentCount ?? 0)} pickles");
        }
        if (NestLabel != null)
        {
            NestLabel.Text = contract.Kit?.DisplayName ?? "";
        }
    }

    private static string Capitalise(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
