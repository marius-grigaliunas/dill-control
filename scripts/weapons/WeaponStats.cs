using Godot;
using DillControl.Audio;

namespace DillControl.Weapons;

public enum WeaponFireMode
{
    /// <summary>One shot per trigger pull.</summary>
    Semi,

    /// <summary>Continuous fire while held.</summary>
    Automatic,
}

/// <summary>
/// Everything about how a weapon behaves. The weapon script contains no
/// balance numbers at all -- it reads them from here, so retuning a gun is
/// editing a .tres and does not need a rebuild.
///
/// Damage per hit is <see cref="Damage"/> scaled by range falloff and then by
/// the <see cref="Core.Hitbox"/> multiplier at the impact point. This resource
/// deliberately holds no headshot multiplier: that number belongs on the
/// collider, not on the gun.
/// </summary>
[GlobalClass]
public partial class WeaponStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Unnamed Weapon";

    [Export]
    public WeaponFireMode FireMode { get; set; } = WeaponFireMode.Automatic;

    // ---------------- Damage ----------------

    [ExportGroup("Damage")]
    [Export(PropertyHint.Range, "1,500,0.5")]
    public float Damage { get; set; } = 24.0f;

    /// <summary>
    /// Rays per trigger pull. 1 is a rifle; 8 to 12 against a wide base spread
    /// is a shotgun. Each pellet carries the full <see cref="Damage"/>, so a
    /// shell's point-blank total is Damage * PelletsPerShot.
    /// </summary>
    [Export(PropertyHint.Range, "1,24,1")]
    public int PelletsPerShot { get; set; } = 1;

    /// <summary>
    /// How many further targets a shot passes through after the first. 0 is
    /// every gun this project had before this field existed. This is the first
    /// half of the ballistics work docs/game-mechanics.md section 6 describes,
    /// arriving early because a horde is the one thing penetration is obviously
    /// worth having against.
    /// </summary>
    [Export(PropertyHint.Range, "0,24,1")]
    public int PenetrationCount { get; set; } = 0;

    /// <summary>Damage share kept per target already penetrated.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float PenetrationDamageRetention { get; set; } = 0.65f;

    /// <summary>
    /// Whether penetration carries on through static World geometry. False
    /// stops the shot at the first wall, which is what a bullet does and what
    /// keeps a penetrating round from firing through the planet.
    /// </summary>
    [Export]
    public bool PenetratesWorld { get; set; } = false;

    /// <summary>
    /// A shot that skewers the horde along its whole length: every agent
    /// within this distance of the line takes <see cref="Damage"/>, out to
    /// the first wall (or <see cref="LineRange"/> if the round goes through
    /// walls). Penetration alone only ever reaches the agents wearing one of
    /// the horde's borrowed colliders -- the nearest ninety-six -- so a
    /// forty-deep lance into a crowd of thousands was stopping at a handful.
    /// 0 for an ordinary round.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.1,suffix:m")]
    public float LineRadius { get; set; } = 0.0f;

    [Export(PropertyHint.Range, "10,600,5,suffix:m")]
    public float LineRange { get; set; } = 250.0f;

    [Export(PropertyHint.Range, "10,1000,1,suffix:m")]
    public float MaxRange { get; set; } = 220.0f;

    /// <summary>Beyond this range damage starts dropping toward <see cref="FalloffMinMultiplier"/>.</summary>
    [Export(PropertyHint.Range, "1,500,0.5,suffix:m")]
    public float FalloffStart { get; set; } = 32.0f;

    /// <summary>At and beyond this range damage is at its floor.</summary>
    [Export(PropertyHint.Range, "1,500,0.5,suffix:m")]
    public float FalloffEnd { get; set; } = 95.0f;

    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float FalloffMinMultiplier { get; set; } = 0.55f;

    // ---------------- Rate and ammunition ----------------

    [ExportGroup("Rate And Ammunition")]
    [Export(PropertyHint.Range, "30,1400,1,suffix:rpm")]
    public float RoundsPerMinute { get; set; } = 620.0f;

    [Export(PropertyHint.Range, "1,200,1")]
    public int MagazineSize { get; set; } = 30;

    [Export(PropertyHint.Range, "0,600,1")]
    public int ReserveAmmo { get; set; } = 180;

    [Export(PropertyHint.Range, "0.1,6,0.05,suffix:s")]
    public float ReloadTime { get; set; } = 2.1f;

    /// <summary>
    /// Shaves time off a reload started with a round still chambered. Set to
    /// the same value as <see cref="ReloadTime"/> to disable the distinction.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,6,0.05,suffix:s")]
    public float TacticalReloadTime { get; set; } = 1.75f;

    // ---------------- Spread ----------------

    [ExportGroup("Spread")]
    /// <summary>Cone half-angle when standing still and freshly settled.</summary>
    [Export(PropertyHint.Range, "0,15,0.01,suffix:°")]
    public float BaseSpreadDegrees { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0,25,0.01,suffix:°")]
    public float MaxSpreadDegrees { get; set; } = 5.2f;

    /// <summary>Spread added by each shot, up to <see cref="MaxSpreadDegrees"/>.</summary>
    [Export(PropertyHint.Range, "0,5,0.01,suffix:°")]
    public float SpreadPerShotDegrees { get; set; } = 0.55f;

    /// <summary>Degrees of accumulated spread shed per second once firing stops.</summary>
    [Export(PropertyHint.Range, "0,30,0.05,suffix:°")]
    public float SpreadRecoveryPerSecond { get; set; } = 4.5f;

    /// <summary>Spread multiplier while moving at walk speed or faster.</summary>
    [Export(PropertyHint.Range, "1,6,0.05")]
    public float MovingSpreadMultiplier { get; set; } = 1.7f;

    [Export(PropertyHint.Range, "1,10,0.05")]
    public float AirborneSpreadMultiplier { get; set; } = 3.2f;

    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float CrouchSpreadMultiplier { get; set; } = 0.7f;

    // ---------------- Recoil ----------------

    [ExportGroup("Aim")]
    /// <summary>
    /// Field of view while aiming, as a fraction of MovementTuning.BaseFov.
    /// A little zoom, not a scope: 0.62 of 90 degrees is 56, which is about
    /// 1.6x and still wide enough to see something coming at you.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,1,0.01")]
    public float AimFovScale { get; set; } = 0.62f;

    /// <summary>
    /// How fast the whole aim pose moves -- field of view, viewmodel, all of
    /// it. Fast enough that aiming is a reflex rather than a commitment, since
    /// the accuracy it buys is the commitment.
    /// </summary>
    [Export(PropertyHint.Range, "1,40,0.5")]
    public float AimTransitionSpeed { get; set; } = 13.0f;

    /// <summary>
    /// Multiplies the spread that stance and fire rate already produced, rather
    /// than replacing it. Aiming while sprinting into the air should still be
    /// worse than aiming while crouched and still.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float AimSpreadMultiplier { get; set; } = 0.32f;

    /// <summary>How far the viewmodel lifts toward the sightline when aiming.</summary>
    [Export(PropertyHint.Range, "-0.2,0.2,0.001,suffix:m")]
    public float AimViewmodelRise { get; set; } = 0.014f;

    /// <summary>How far it draws back toward the eye. Positive is backwards.</summary>
    [Export(PropertyHint.Range, "-0.3,0.3,0.001,suffix:m")]
    public float AimViewmodelPush { get; set; } = 0.055f;

    [ExportGroup("Recoil")]
    /// <summary>Upward kick per shot, applied to the camera rig's offset node.</summary>
    [Export(PropertyHint.Range, "0,5,0.005,suffix:°")]
    public float RecoilPitchPerShot { get; set; } = 0.42f;

    /// <summary>Horizontal kick per shot, randomised within +/- this value.</summary>
    [Export(PropertyHint.Range, "0,5,0.005,suffix:°")]
    public float RecoilYawPerShot { get; set; } = 0.19f;

    /// <summary>Bias in the horizontal kick. -1 always left, +1 always right, 0 even.</summary>
    [Export(PropertyHint.Range, "-1,1,0.01")]
    public float RecoilYawBias { get; set; } = 0.15f;

    /// <summary>How fast accumulated recoil decays back to centre.</summary>
    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float RecoilRecoverSpeed { get; set; } = 6.5f;

    /// <summary>How fast the view chases the recoil target. High is snappy, low is springy.</summary>
    [Export(PropertyHint.Range, "1,60,0.5")]
    public float RecoilSnappiness { get; set; } = 24.0f;

    // ---------------- Viewmodel and effects ----------------

    [ExportGroup("Viewmodel And Effects")]
    /// <summary>Metres the weapon model is shoved backward per shot.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.001,suffix:m")]
    public float ViewmodelKickBack { get; set; } = 0.035f;

    [Export(PropertyHint.Range, "0,10,0.05,suffix:°")]
    public float ViewmodelKickPitch { get; set; } = 2.6f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ViewmodelRecoverSpeed { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "0.005,0.3,0.005,suffix:s")]
    public float MuzzleFlashDuration { get; set; } = 0.045f;

    /// <summary>Metres per second the tracer travels toward the impact point.</summary>
    [Export(PropertyHint.Range, "50,4000,10,suffix:m/s")]
    public float TracerSpeed { get; set; } = 900.0f;

    /// <summary>Shots between visible tracers. 1 draws one for every shot.</summary>
    [Export(PropertyHint.Range, "1,10,1")]
    public int TracerEveryNShots { get; set; } = 1;

    [ExportGroup("Audio")]
    /// <summary>
    /// Which bank slot this weapon fires with. It is here rather than on the
    /// weapon node because the rifle, the shotgun and the railgun are the same
    /// <c>HitscanWeapon</c> class distinguished only by their .tres -- so their
    /// sound has to be distinguished the same way, or the class needs a branch
    /// per weapon and the data-not-code rule is broken for audio alone.
    /// </summary>
    [Export]
    public Sfx FireSfx { get; set; } = Sfx.RifleFire;

    /// <summary>
    /// This gun's shot pitch, as a multiple of the shared event's. Several
    /// guns share one fire slot, and the same waveform from a light rifle and
    /// a heavy lance made them sound like the same weapon.
    /// </summary>
    [Export(PropertyHint.Range, "0.3,2.5,0.01")]
    public float FirePitch { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "-20,12,0.5,suffix:dB")]
    public float FireVolumeDb { get; set; } = 0.0f;

    /// <summary>
    /// A sound belonging to this weapon and no other. Set it and the bank slot
    /// above is ignored; leave it null and the shared one is used.
    /// </summary>
    [Export]
    public SoundEvent FireSound { get; set; }

    [Export]
    public Sfx ReloadStartSfx { get; set; } = Sfx.ReloadStart;

    [Export]
    public Sfx ReloadFinishSfx { get; set; } = Sfx.ReloadFinish;

    // ---------------- Momentum ----------------

    [ExportGroup("Momentum")]
    /// <summary>
    /// World-space push opposite the shot, in metres per second. Zero for
    /// every gun that existed before this field, so nothing changes unless a
    /// .tres asks for it. The shotgun is the one that does: fire down to hop,
    /// fire backwards while zipping to brake.
    /// </summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float RecoilThrust { get; set; } = 0.0f;

    /// <summary>
    /// Owner speed below which a shot is just a shot. Above sprint, so walking
    /// fire is unchanged.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float MomentumMinSpeed { get; set; } = 9.0f;

    /// <summary>Owner speed at which the full damage multiplier applies.</summary>
    [Export(PropertyHint.Range, "1,80,0.5,suffix:m/s")]
    public float MomentumFullSpeed { get; set; } = 30.0f;

    /// <summary>
    /// Damage at full speed, as a multiple of the shot's own. 1.35 is a
    /// nudge toward shooting while moving, not a second weapon.
    /// </summary>
    [Export(PropertyHint.Range, "1,4,0.01")]
    public float MomentumDamageMultiplier { get; set; } = 1.35f;

    // ---------------- Explosive rounds ----------------

    [ExportGroup("Explosive Rounds")]
    /// <summary>
    /// A small blast wherever a round lands, through <c>Blast</c> -- so it
    /// reaches the whole horde over <c>AreaBlast</c>, chews voxel bodies and
    /// shoves loose debris, with no branch anywhere for who fired it. Zero, the
    /// default, is an ordinary bullet, so every existing .tres is unchanged.
    ///
    /// This is what makes a gun clear a crowd rather than a queue: against
    /// twenty-five thousand pickles a round that kills one is a round that
    /// changes nothing on screen.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.1,suffix:m")]
    public float SplashRadius { get; set; } = 0.0f;

    /// <summary>Full splash damage inside this radius; falls off to the edge.</summary>
    [Export(PropertyHint.Range, "0,8,0.1,suffix:m")]
    public float SplashInnerRadius { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0,400,1")]
    public float SplashDamage { get; set; } = 0.0f;

    /// <summary>Splash damage left at the very edge, as a share.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SplashMinMultiplier { get; set; } = 0.4f;

    /// <summary>Shove on rigid bodies and the player inside the splash.</summary>
    [Export(PropertyHint.Range, "0,40,0.5")]
    public float SplashImpulse { get; set; } = 3.0f;

    /// <summary>
    /// Radius the drawn explosion is sized to. Bigger than the damage radius
    /// on purpose: a 2 m blast drawn at 2 m is a puff nobody notices.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,12,0.1,suffix:m")]
    public float SplashEffectRadius { get; set; } = 3.0f;

    /// <summary>
    /// Shortest gap between two drawn explosions. The rounds in between still
    /// deal their full splash and show the ordinary impact flash -- the bike
    /// gun's rule, for the same reason: an effect node per round at fourteen
    /// rounds a second is a hundred nodes alive through one burst.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01,suffix:s")]
    public float SplashEffectInterval { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "0,30,0.5,suffix:s")]
    public float SplashScorchSeconds { get; set; } = 2.0f;

    /// <summary>
    /// Most splashes one shot sets off, across all its penetration passes.
    /// Zero is unlimited. A round through twenty pickles is twenty blasts in
    /// one physics frame, each an overlap, a sight-line ray per candidate and
    /// a walk of the horde's hash: measured at 28-40 ms for the lance's
    /// first version, a hitch on every shot. Damage past the cap still lands
    /// as the direct hit.
    /// </summary>
    [Export(PropertyHint.Range, "0,64,1")]
    public int SplashMaxPerShot { get; set; } = 0;

    /// <summary>Seconds per shot, derived from <see cref="RoundsPerMinute"/>.</summary>
    public float ShotInterval => RoundsPerMinute > 0.0f ? 60.0f / RoundsPerMinute : 0.1f;

    /// <summary>Damage multiplier from range alone, before any hitbox multiplier.</summary>
    public float FalloffMultiplierAt(float distance)
    {
        if (distance <= FalloffStart || FalloffEnd <= FalloffStart)
        {
            return 1.0f;
        }
        float t = Mathf.Clamp((distance - FalloffStart) / (FalloffEnd - FalloffStart), 0.0f, 1.0f);
        return Mathf.Lerp(1.0f, FalloffMinMultiplier, t);
    }
}
