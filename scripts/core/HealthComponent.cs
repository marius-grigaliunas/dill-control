using Godot;

namespace DillControl.Core;

/// <summary>
/// Holds health and implements <see cref="IDamageable"/>. Attach it to
/// anything shootable and parent that thing's <see cref="Hitbox"/> colliders
/// under it (or point them at it explicitly).
///
/// It reports through local signals for whoever owns it, and pushes one event
/// onto <see cref="EventBus"/> for the listeners it cannot name -- HUD
/// hitmarkers, debug readouts.
/// </summary>
[GlobalClass]
public partial class HealthComponent : Node, IDamageable
{
    [Export(PropertyHint.Range, "1,1000,1,or_greater")]
    public float MaxHealth { get; set; } = 100.0f;

    /// <summary>Local signal: this node's owner cares, nobody else does.</summary>
    [Signal]
    public delegate void HealthChangedEventHandler(float current, float max);

    [Signal]
    public delegate void DiedEventHandler();

    public float CurrentHealth { get; private set; }

    public bool IsAlive => CurrentHealth > 0.0f;

    public override void _Ready()
    {
        CurrentHealth = MaxHealth;
    }

    public void ApplyDamage(DamageInfo info)
    {
        if (!IsAlive)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0.0f, CurrentHealth - info.Amount);

        Node3D owner = GetOwnerNode3D();

        info.HealthFraction = MaxHealth > 0.0f ? CurrentHealth / MaxHealth : 0.0f;
        info.TargetPosition = owner?.GlobalPosition ?? info.Position;
        info.Killed = !IsAlive;

        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
        EventBus.EmitDamageDealt(owner, info);

        if (!IsAlive)
        {
            EmitSignal(SignalName.Died);
        }
    }

    /// <summary>Resets to full. Used by the graybox target's respawn timer.</summary>
    public void Revive()
    {
        CurrentHealth = MaxHealth;
        EmitSignal(SignalName.HealthChanged, CurrentHealth, MaxHealth);
    }

    private Node3D GetOwnerNode3D()
    {
        for (Node node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is Node3D spatial)
            {
                return spatial;
            }
        }
        return null;
    }
}
