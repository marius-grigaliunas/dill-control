using DillControl.Levels;
using DillControl.Weapons;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A walking ammunition store: while the player stands near it, it tops up
/// whatever they carry, a share at a time. The mobile storage bot's job.
///
/// It reaches the loadout through <see cref="WeaponLoadout.ResupplyGroupName"/>,
/// the group every resupply point uses, because the player's loadout lives in
/// a scene instanced at runtime and nothing in a level can name it. The level
/// is an <c>[Export]</c> only for the player's position.
/// </summary>
public partial class MechSupply : Node
{
    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public LevelRoot Level { get; set; }

    /// <summary>Shown over the bot while it is handing out ammunition.</summary>
    [Export]
    public Label3D Indicator { get; set; }

    [Export(PropertyHint.Range, "0.5,20,0.5,suffix:m")]
    public float Range { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:s")]
    public float Interval { get; set; } = 1.5f;

    /// <summary>Share of each weapon's full reserve handed over per interval.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float Fraction { get; set; } = 0.2f;

    private float _timer;
    private float _shown;

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _shown = Mathf.Max(0.0f, _shown - dt);
        if (Indicator != null)
        {
            Indicator.Visible = _shown > 0.0f;
        }

        if (Walker == null || Level?.Player is not Node3D player || !player.Visible)
        {
            return;
        }

        if (player.GlobalPosition.DistanceTo(Walker.GlobalPosition) > Range)
        {
            _timer = 0.0f;
            return;
        }

        _timer += dt;
        if (_timer < Interval)
        {
            return;
        }
        _timer = 0.0f;

        bool given = false;
        foreach (Node node in GetTree().GetNodesInGroup(WeaponLoadout.ResupplyGroupName))
        {
            if (node is WeaponLoadout loadout)
            {
                given |= loadout.Resupply(Fraction);
            }
        }
        if (given)
        {
            _shown = 1.2f;
        }
    }
}
