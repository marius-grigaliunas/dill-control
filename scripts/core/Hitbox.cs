using Godot;

namespace DillControl.Core;

/// <summary>
/// A collider that scales incoming damage before passing it to its receiver.
///
/// This is the whole weak-point mechanic: an alligator's skull gets a Hitbox
/// with a high multiplier and <see cref="IsWeakPoint"/> set, its flank gets one
/// with a low multiplier, and the weapon stays ignorant of both. Retuning the
/// risk-reward of aiming for the head in month 2 means editing a number in the
/// inspector, not editing weapon code.
///
/// Lives on physics layer <see cref="GameLayers.Hitbox"/> so hitscan queries
/// can include it without every ray colliding with body capsules twice.
/// </summary>
[GlobalClass]
public partial class Hitbox : Area3D
{
    /// <summary>
    /// Incoming damage is multiplied by this. 1.0 is a plain body shot.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.05,or_greater")]
    public float DamageMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Marks this hitbox as a weak point. Purely presentational as far as
    /// damage goes -- <see cref="DamageMultiplier"/> does the actual work --
    /// but it lets the HUD play a distinct hitmarker and lets audio pick a
    /// different impact sound.
    /// </summary>
    [Export]
    public bool IsWeakPoint { get; set; }

    /// <summary>
    /// The node that actually holds health. Must implement
    /// <see cref="IDamageable"/>. Left empty, the hitbox walks up its own
    /// ancestors and uses the first implementer it finds, which is the normal
    /// case for a hitbox parented under the thing it protects.
    /// </summary>
    [Export]
    public Node DamageReceiver { get; set; }

    private IDamageable _receiver;

    /// <summary>
    /// The physics layer this hitbox sits on: <see cref="GameLayers.Hitbox"/>
    /// for every authored target, <see cref="GameLayers.HordeHitbox"/> for the
    /// horde's borrowed proxies. Set before the node enters the tree.
    /// </summary>
    public int Layer { get; set; } = GameLayers.Hitbox;

    /// <summary>
    /// The resolved receiver, exposed so area damage can de-duplicate. One
    /// target carries several hitboxes over a single health pool, and a blast
    /// or a swing arc that overlaps two of them must decide between them rather
    /// than apply damage twice.
    /// </summary>
    public IDamageable Receiver => _receiver;

    public override void _Ready()
    {
        // Hitboxes are query targets, not overlap detectors. They never need
        // to run their own collision checks.
        Monitoring = false;
        Monitorable = true;

        CollisionLayer = GameLayers.Mask(Layer);
        CollisionMask = 0;

        _receiver = ResolveReceiver();

        if (_receiver == null)
        {
            GD.PushWarning($"Hitbox '{GetPath()}' found no IDamageable receiver. Hits on it will be ignored.");
        }
    }

    private IDamageable ResolveReceiver()
    {
        if (DamageReceiver is IDamageable exported)
        {
            return exported;
        }

        for (Node node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is IDamageable found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Applies damage through this hitbox. The caller supplies the base amount
    /// and impact data; the hitbox scales it and forwards it on.
    /// </summary>
    public void TakeHit(DamageInfo info)
    {
        if (_receiver == null || !_receiver.IsAlive)
        {
            return;
        }

        info.BaseAmount = info.Amount;
        info.Multiplier = DamageMultiplier;
        info.Amount *= DamageMultiplier;
        info.IsWeakPoint = IsWeakPoint;

        _receiver.ApplyDamage(info);
    }
}
