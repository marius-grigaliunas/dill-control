using Godot;

namespace DillControl.Core;

/// <summary>
/// The single autoloaded event bus, registered as <c>EventBus</c>.
///
/// SCOPE RULE -- read before adding anything here.
/// A signal belongs on this bus only if the sender cannot reasonably know its
/// listeners AND the traffic crosses unrelated subsystems. Parent-child and
/// sibling communication uses local signals on the node itself. If you can
/// name the listener, it does not go here.
///
/// Every signal below is justified in <c>docs/decisions/0006-event-bus.md</c>.
/// Adding one means adding a paragraph there first.
/// </summary>
public partial class EventBus : Node
{
    /// <summary>
    /// Singleton access. Assigned in <see cref="_EnterTree"/> so it is valid
    /// before any scene node runs <c>_Ready</c>.
    /// </summary>
    public static EventBus Instance { get; private set; }

    /// <summary>
    /// Something took damage. Emitted by <see cref="Hitbox"/> and by direct
    /// <see cref="IDamageable"/> hits. Listened to by HUD hitmarkers, damage
    /// numbers and the debug overlay -- none of which the weapon should know about.
    /// </summary>
    [Signal]
    public delegate void DamageDealtEventHandler(Node3D target, DamageInfo info);

    /// <summary>
    /// The equipped weapon's ammo counts changed. Emitted on fire, on reload
    /// completion and on equip. Listened to by the HUD.
    /// </summary>
    [Signal]
    public delegate void AmmoChangedEventHandler(int inMagazine, int magazineSize, int reserve);

    /// <summary>
    /// The player's locomotion state changed. <paramref name="state"/> is a
    /// <see cref="PlayerState"/> cast to int -- Godot signals cannot marshal
    /// arbitrary enums. Listened to by the debug overlay, and later by audio
    /// (footstep sets) and weapon spread.
    /// </summary>
    [Signal]
    public delegate void PlayerStateChangedEventHandler(int state);

    /// <summary>
    /// An explosion happened. Emitted once, from <see cref="Blast.ApplyDamage"/>,
    /// rather than at each weapon's own detonation site.
    ///
    /// A weapon (Grenade, Rocket) genuinely cannot name this listener.
    /// <c>DillControl.Horde.HordeManager</c> represents thousands of agents with
    /// no physical collider at all, so <see cref="Blast.ApplyDamage"/>'s
    /// sphere-overlap query can never find them, and a weapon that named the
    /// horde would make every weapon depend on it. Listened to by the horde and
    /// by the player, for camera shake.
    /// </summary>
    [Signal]
    public delegate void AreaBlastEventHandler(BlastInfo blast);

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // -- Typed emit helpers. Call these instead of EmitSignal with a string. --

    public static void EmitDamageDealt(Node3D target, DamageInfo info)
    {
        Instance?.EmitSignal(SignalName.DamageDealt, target, info);
    }

    public static void EmitAmmoChanged(int inMagazine, int magazineSize, int reserve)
    {
        Instance?.EmitSignal(SignalName.AmmoChanged, inMagazine, magazineSize, reserve);
    }

    public static void EmitPlayerStateChanged(PlayerState state)
    {
        Instance?.EmitSignal(SignalName.PlayerStateChanged, (int)state);
    }

    public static void EmitAreaBlast(BlastInfo blast)
    {
        Instance?.EmitSignal(SignalName.AreaBlast, blast);
    }
}

/// <summary>Player locomotion states, carried by <see cref="EventBus.PlayerStateChanged"/>.</summary>
public enum PlayerState
{
    Idle,
    Walking,
    Sprinting,
    Crouching,
    CrouchWalking,
    Airborne,

    /// <summary>
    /// Under jetpack thrust. Appended rather than inserted: the state crosses
    /// the bus as an int, so renumbering the existing entries would silently
    /// change what every listener sees.
    /// </summary>
    Flying,

    /// <summary>On the grapple line, off the ground. Appended for the same reason.</summary>
    Grappling,
}
