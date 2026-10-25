namespace DillControl.Vehicles;

/// <summary>
/// A vehicle the player can be put into or taken out of at once, by a test
/// harness rather than by walking up and pressing interact: the asset lab
/// (docs/decisions/0047) seats the player in whatever it spawned.
/// </summary>
public interface IRideable
{
    bool CanRide { get; }

    bool IsRidden { get; }

    /// <summary>Into the seat from anywhere, once the level has a player.</summary>
    void BoardNow();

    /// <summary>Out of the seat, beside it.</summary>
    void LeaveNow();
}
