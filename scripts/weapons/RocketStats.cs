using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Everything tunable about a shoulder-fired rocket: how it is carried, how it
/// flies, and what the warhead does.
///
/// Same shape as <see cref="GrenadeStats"/> on purpose. Both feed
/// <see cref="Core.Blast.Settings"/>, so the blast half of this resource reads
/// identically and a number learned on one transfers to the other.
/// </summary>
[GlobalClass]
public partial class RocketStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Bunker Buster";

    [ExportGroup("Ammunition")]
    /// <summary>
    /// One in the tube. A magazine would make the launcher a machine gun that
    /// happens to explode, and the reload between shots is most of what stops
    /// it trivialising every problem in the level.
    /// </summary>
    [Export(PropertyHint.Range, "1,12,1")]
    public int MagazineSize { get; set; } = 1;

    [Export(PropertyHint.Range, "0,60,1")]
    public int ReserveAmmo { get; set; } = 12;

    [Export(PropertyHint.Range, "0.2,6,0.05,suffix:s")]
    public float ReloadTime { get; set; } = 1.35f;

    /// <summary>Minimum time between shots, for a launcher with more than one tube.</summary>
    [Export(PropertyHint.Range, "0.1,3,0.01,suffix:s")]
    public float FireInterval { get; set; } = 0.85f;

    [ExportGroup("Volley")]
    /// <summary>
    /// Rockets one trigger pull sends, and one round of ammunition pays for.
    /// One, the default, is the bazooka every existing .tres describes. More
    /// is a volley: the first leaves on the press and the rest follow
    /// <see cref="VolleyInterval"/> apart, fanned by <see cref="VolleySpreadDegrees"/>,
    /// each re-aimed at wherever the camera points as it leaves -- so a
    /// volley can be walked across a crowd.
    /// </summary>
    [Export(PropertyHint.Range, "1,12,1")]
    public int RocketsPerShot { get; set; } = 1;

    [Export(PropertyHint.Range, "0.01,0.5,0.01,suffix:s")]
    public float VolleyInterval { get; set; } = 0.06f;

    /// <summary>Half-angle of the fan the follow-up rockets are spread across.</summary>
    [Export(PropertyHint.Range, "0,15,0.1,suffix:deg")]
    public float VolleySpreadDegrees { get; set; } = 0.0f;

    [ExportGroup("Flight")]
    /// <summary>
    /// Speed at the muzzle. Deliberately well below the cruise speed: a rocket
    /// that leaves at full speed is indistinguishable from hitscan, and the
    /// visible acceleration away from the shooter is most of what makes one
    /// read as a rocket rather than a very fast bullet.
    /// </summary>
    [Export(PropertyHint.Range, "1,200,0.5,suffix:m/s")]
    public float MuzzleSpeed { get; set; } = 26.0f;

    [Export(PropertyHint.Range, "1,400,0.5,suffix:m/s")]
    public float CruiseSpeed { get; set; } = 78.0f;

    [Export(PropertyHint.Range, "1,600,1,suffix:m/s²")]
    public float Acceleration { get; set; } = 120.0f;

    /// <summary>
    /// Share of local gravity the motor does not cancel. Small but not zero, so
    /// a long shot drops enough to have to be led.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float GravityScale { get; set; } = 0.18f;

    [Export(PropertyHint.Range, "0.5,30,0.1,suffix:s")]
    public float MaxLifetime { get; set; } = 8.0f;

    /// <summary>
    /// How far in front of the camera the rocket appears. Far enough that it is
    /// never spawned inside the shooter's own capsule, which would detonate it
    /// on frame one.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,4,0.05,suffix:m")]
    public float SpawnOffset { get; set; } = 1.1f;

    [ExportGroup("Blast")]
    /// <summary>Applied on top of the blast when the rocket strikes a target directly.</summary>
    [Export(PropertyHint.Range, "0,500,1")]
    public float DirectHitDamage { get; set; } = 120.0f;

    [Export(PropertyHint.Range, "1,1000,1")]
    public float Damage { get; set; } = 320.0f;

    [Export(PropertyHint.Range, "0.1,30,0.1,suffix:m")]
    public float InnerRadius { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "0.5,60,0.1,suffix:m")]
    public float BlastRadius { get; set; } = 14.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinMultiplier { get; set; } = 0.32f;

    [Export(PropertyHint.Range, "0,80,0.5,suffix:m/s")]
    public float Impulse { get; set; } = 26.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ImpulseUpBias { get; set; } = 0.5f;

    /// <summary>
    /// Scales the shove the shooter takes from their own rocket. Left at 1 so
    /// rocket-jumping works, which is exactly the kind of thing a playground
    /// should let you find out about by trying it.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.05")]
    public float SelfImpulseScale { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    public float DebrisImpulseScale { get; set; } = 3.4f;

    [ExportGroup("Terrain Effect")]
    /// <summary>
    /// Crater dug where this warhead lands. Zero -- the default, and what every
    /// existing .tres says by omission -- leaves the ground alone, which is
    /// what the rifle-scale explosives should do.
    ///
    /// A crater is not decoration: it reroutes the horde. Dug when the planet
    /// arrives (M4); the flats leave the ground alone.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float CraterRadius { get; set; }

    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float CraterDepth { get; set; } = 2.6f;

    /// <summary>Seconds of burning ground left behind. Zero for everything but incendiary.</summary>
    [Export(PropertyHint.Range, "0,120,0.5,suffix:s")]
    public float FireSeconds { get; set; }

    [ExportGroup("Aim")]
    /// <summary>
    /// Field of view while RMB is held, as a share of the base. Every gun
    /// aims on RMB, the launcher included: a rocket is the shot where placing
    /// it matters most and the one that used to be fired from the hip only.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,1,0.01")]
    public float AimFovScale { get; set; } = 0.72f;

    [Export(PropertyHint.Range, "1,40,0.5")]
    public float AimTransitionSpeed { get; set; } = 12.0f;

    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,20,0.05,suffix:°")]
    public float KickPitchDegrees { get; set; } = 3.6f;

    [Export(PropertyHint.Range, "0,20,0.05,suffix:°")]
    public float KickYawDegrees { get; set; } = 0.9f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 4.5f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 14.0f;

    [ExportGroup("Viewmodel")]
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:m")]
    public float ViewModelKickBack { get; set; } = 0.14f;

    [Export(PropertyHint.Range, "0,30,0.1,suffix:°")]
    public float ViewModelKickPitch { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ViewModelRecoverSpeed { get; set; } = 7.0f;
}
