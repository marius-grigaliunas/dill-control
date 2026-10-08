using DillControl.Weapons;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A mech's two guns: the turret's rapid explosive rounds and the hull
/// cannon's shells, and how it picks targets on its own. Read by
/// <see cref="MechGunner"/>.
/// </summary>
[GlobalClass]
public partial class MechGunStats : Resource
{
    [ExportGroup("Turret gun")]
    [Export(PropertyHint.Range, "0.02,2,0.01,suffix:s")]
    public float TurretFireInterval { get; set; } = 0.11f;

    [Export(PropertyHint.Range, "0,10,0.1,suffix:°")]
    public float TurretSpreadDegrees { get; set; } = 1.2f;

    [Export(PropertyHint.Range, "10,400,5,suffix:m")]
    public float TurretRange { get; set; } = 140.0f;

    /// <summary>Each round goes off where it lands, through the same
    /// <see cref="DillControl.Core.Blast"/> every explosive uses, so it reaches
    /// the whole horde, not only the agents with borrowed colliders.</summary>
    [Export(PropertyHint.Range, "0,500,1")]
    public float RoundDamage { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float RoundBlastRadius { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,10,0.1,suffix:m")]
    public float RoundInnerRadius { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0,40,0.5")]
    public float RoundImpulse { get; set; } = 6.0f;

    /// <summary>A round's explosion is drawn at most this often; the ones
    /// between still do their damage and show an impact flash. The bike gun's
    /// rule, for the same reason: a drawn blast every 0.11 s is a fill cost.
    /// At 0.15 the fighter firing both arms ran 17 ms frames.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float ExplosionEffectInterval { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0.1,10,0.1,suffix:m")]
    public float ExplosionEffectRadius { get; set; } = 2.2f;

    [Export(PropertyHint.Range, "50,1000,10,suffix:m/s")]
    public float TracerSpeed { get; set; } = 260.0f;

    [ExportGroup("Hull cannon")]
    /// <summary>The shell: a rocket that flies straight and hits hard.</summary>
    [Export]
    public RocketStats CannonShell { get; set; }

    [Export(PropertyHint.Range, "0.1,10,0.05,suffix:s")]
    public float CannonFireInterval { get; set; } = 1.4f;

    /// <summary>The hull gun is fixed to the body: aimed further off its nose
    /// than this, it fires straight ahead instead.</summary>
    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float CannonTraverseDegrees { get; set; } = 35.0f;

    [ExportGroup("Targeting")]
    /// <summary>How far it looks for pickles on its own.</summary>
    [Export(PropertyHint.Range, "5,200,1,suffix:m")]
    public float AutoRange { get; set; } = 55.0f;

    /// <summary>How often it looks for a new target. The search walks the
    /// horde's spatial hash out to AutoRange, so it is not done every frame.</summary>
    [Export(PropertyHint.Range, "0.05,2,0.05,suffix:s")]
    public float AutoScanInterval { get; set; } = 0.25f;

    /// <summary>The turret fires once it points within this of its target.</summary>
    [Export(PropertyHint.Range, "0.5,45,0.5,suffix:°")]
    public float AutoAimToleranceDegrees { get; set; } = 8.0f;

    /// <summary>The hull cannon fires on its own when a target is within this
    /// of the nose and further off than CannonMinRange.</summary>
    [Export(PropertyHint.Range, "0,90,1,suffix:°")]
    public float AutoCannonConeDegrees { get; set; } = 20.0f;

    [Export(PropertyHint.Range, "0,100,1,suffix:m")]
    public float CannonMinRange { get; set; } = 12.0f;

    /// <summary>On its own, it holds fire at anything this close to the player.</summary>
    [Export(PropertyHint.Range, "0,30,0.5,suffix:m")]
    public float PlayerSafetyRadius { get; set; } = 7.0f;
}
