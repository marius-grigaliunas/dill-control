using Godot;

namespace DillControl.Core;

/// <summary>
/// A collider that passes all its damage to one receiver it shares with other
/// colliders: a building's sections each have a body of their own, and the
/// building takes the damage once (docs/decisions/0015). Like a
/// <see cref="Hitbox"/>, but a body, so it can be stood on.
/// </summary>
public interface IDamageRelay
{
    IDamageable Receiver { get; }

    /// <summary>Adds the bodies of every collider relaying to the same
    /// receiver: an area query that has found one need not find the rest
    /// (a tower is a dozen section bodies, a heap a hundred tiles).</summary>
    void AddSiblings(Godot.Collections.Array<Rid> into) { }
}

/// <summary>What a collider's damage goes to: the one rule every weapon uses,
/// so an area hit counts a receiver once however many of its colliders it
/// touches.</summary>
public static class DamageReceiver
{
    public static IDamageable Of(GodotObject collider) => collider switch
    {
        Hitbox box => box.Receiver,
        IDamageRelay relay => relay.Receiver,
        IDamageable direct => direct,
        _ => null,
    };
}
