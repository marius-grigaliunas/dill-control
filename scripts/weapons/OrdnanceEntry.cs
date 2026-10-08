using Godot;

namespace DillControl.Weapons;

/// <summary>
/// One thing the ship can be asked to drop: what it is called, what it costs,
/// how the salvo is shaped, and what each round in it does when it lands.
///
/// It is a pair of existing resources plus a price, and that is the whole
/// implementation. <see cref="AirstrikeStats"/> already carries a full
/// <see cref="RocketStats"/> as its ordnance and <c>Airstrike.Begin</c> already
/// takes both, so five ordnance types arrive as ten .tres files and not one
/// line changed in the strike itself.
/// </summary>
[GlobalClass]
public partial class OrdnanceEntry : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Round";

    /// <summary>
    /// Billed against the payout, not paid up front. Every round is spent ore,
    /// which is what keeps fire support a decision rather than a button -- the
    /// greed curve pays for the fireworks.
    /// </summary>
    [Export(PropertyHint.Range, "0,5000,5")]
    public float Cost { get; set; } = 120.0f;

    [ExportGroup("Salvo")]
    /// <summary>
    /// How the salvo is shaped: lead time, bomb count, interval, scatter and
    /// release height. Its Charges, Cooldown and MaxRange are ignored -- those
    /// belong to the designator holding it, not to the round being called.
    /// </summary>
    [Export]
    public AirstrikeStats Profile { get; set; }

    /// <summary>
    /// What one round of it is. Damage, blast, and whether it craters, floods
    /// or burns.
    /// </summary>
    [Export]
    public RocketStats Warhead { get; set; }
}
