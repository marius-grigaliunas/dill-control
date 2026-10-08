namespace DillControl.Core;

/// <summary>
/// Implemented by anything that can be hurt. The weapon never checks what it
/// hit -- it asks whether the thing implements this, and hands it a
/// <see cref="DamageInfo"/>.
///
/// A <see cref="Hitbox"/> sits in front of an implementer and scales the
/// damage on the way through, so weak-point versus body-shot tuning is a value
/// on a collider, never a branch in weapon code.
/// </summary>
public interface IDamageable
{
    /// <summary>True while this thing can still take damage.</summary>
    bool IsAlive { get; }

    /// <summary>Applies one damage event. Implementers own their own death logic.</summary>
    void ApplyDamage(DamageInfo info);
}
