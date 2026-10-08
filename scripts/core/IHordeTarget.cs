using Godot;

namespace DillControl.Core;

/// <summary>
/// Something on the player's side that the horde goes for besides the player:
/// an ally mech. Pickles near one steer at it, bite it, strike it and spit at
/// it. It joins <see cref="GroupName"/> and the horde finds it there, so the
/// horde never names what it is attacking, and the ally never names the horde.
/// </summary>
public interface IHordeTarget
{
    public const string GroupName = "dill_horde_targets";

    /// <summary>False while it cannot be attacked (destroyed, say).</summary>
    bool IsTargetable { get; }

    /// <summary>The ground under its middle.</summary>
    Vector3 TargetBase { get; }

    /// <summary>How far from <see cref="TargetBase"/> its body reaches sideways.</summary>
    float TargetRadius { get; }

    /// <summary>How tall it is.</summary>
    float TargetHeight { get; }

    /// <summary>A pickle reached it: a bite, a strike, a spit. From where, how hard.</summary>
    void TakeHordeHit(Vector3 from, float damage, Vector3 impulse);
}
