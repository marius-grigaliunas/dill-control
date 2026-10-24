namespace DillControl.Horde;

/// <summary>
/// A ground where some cells are slow going (shallow water, decision 0045).
/// Read from the horde's parallel passes, so it is an array lookup, made
/// when the ground was.
/// </summary>
public interface IGroundPace
{
    /// <summary>The share of its speed a pickle keeps in a flow cell: 1 on dry ground.</summary>
    float PaceAt(int cell);
}
