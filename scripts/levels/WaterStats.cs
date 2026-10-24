using Godot;

namespace DillControl.Levels;

/// <summary>
/// How shallow water plays (docs/decisions/0045): wading slows the player
/// and the horde, the deeper the more, up to <see cref="FullSlowDepthM"/>;
/// stepping in splashes.
/// </summary>
[Tool]
[GlobalClass]
public partial class WaterStats : Resource
{
    public const string DefaultPath = "res://resources/levels/water_default.tres";

    /// <summary>At this depth or more, wading is at its slowest.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.05,suffix:m")]
    public float FullSlowDepthM { get; set; } = 1.0f;

    /// <summary>The player's walking and running speed at full depth, a share.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")]
    public float PlayerWadeShare { get; set; } = 0.55f;

    /// <summary>A pickle's speed at full depth, a share.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.05")]
    public float HordeWadeShare { get; set; } = 0.5f;

    /// <summary>Added to a flow cell's cost at full depth, so the crowd goes round a pond when the way round is short.</summary>
    [Export(PropertyHint.Range, "0,20,1")]
    public int HordeFlowCost { get; set; } = 4;

    /// <summary>Faster than this, in m/s, stepping in splashes.</summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:m/s")]
    public float SplashMinSpeed { get; set; } = 1.5f;
}
