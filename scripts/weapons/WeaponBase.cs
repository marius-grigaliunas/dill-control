using Godot;

namespace DillControl.Weapons;

/// <summary>
/// What every weapon in a hand has in common, and nothing more.
///
/// It exists because two weapons cannot both poll <see cref="Input"/> in their
/// own <c>_Process</c> and expect one click to mean one action. A holstered
/// weapon has its processing switched off entirely, so "which weapon reads the
/// trigger" is answered by the scene tree rather than by a flag every weapon
/// has to remember to check.
///
/// It reports outward the same way <see cref="HitscanWeapon"/> always did: a
/// local <see cref="Fired"/> signal carrying camera kick, and nothing that
/// reaches for the player, the HUD or the camera rig.
/// </summary>
public abstract partial class WeaponBase : Node3D
{
    [ExportGroup("Scene Wiring")]
    /// <summary>Where attacks are aimed from. The camera, not the muzzle: an
    /// attack that starts at the model and a crosshair at screen centre
    /// disagree at close range, and players trust the crosshair.</summary>
    [Export]
    public Camera3D AimCamera { get; set; }

    /// <summary>The mesh root that gets kicked, swung or lowered.</summary>
    [Export]
    public Node3D ViewModel { get; set; }

    /// <summary>
    /// Local signal. Carries camera kick in radians plus its response curve, so
    /// the listener does not have to read a stats resource to interpret it.
    /// Named for recoil because that was its first use; a melee swing wants the
    /// same thing under a different name.
    /// </summary>
    [Signal]
    public delegate void FiredEventHandler(Vector2 recoilRadians, float recoverSpeed, float snappiness);

    /// <summary>
    /// Recoil as thrust: a world-space impulse added to the owner's velocity.
    /// Zero for anything that does not push. The loadout re-emits it the same
    /// way it re-emits Fired.
    /// </summary>
    [Signal]
    public delegate void ThrustEventHandler(Vector3 impulse);

    /// <summary>True while this is the weapon in the player's hands.</summary>
    public bool IsEquipped { get; private set; }

    /// <summary>Cone half-angle in degrees. Zero for anything that does not spread.</summary>
    public virtual float CurrentSpreadDegrees => 0.0f;

    public virtual bool IsReloading => false;

    /// <summary>
    /// False for weapons with nothing to count. The HUD hides its ammo readout
    /// rather than the weapon inventing a fake magazine to publish.
    /// </summary>
    public virtual bool UsesAmmo => false;

    public virtual string DisplayName => Name;

    /// <summary>
    /// Set by a subclass that could not configure itself -- a missing stats
    /// resource, typically. Equipping will not revive it, because a weapon that
    /// switched itself off would crash the moment its trigger was read.
    /// </summary>
    protected bool IsBroken { get; set; }

    /// <summary>
    /// Raised when a weapon starts or stops aiming, carrying the numbers the
    /// camera needs so the rig never has to know what a WeaponStats is. Same
    /// shape as Fired: the loadout re-emits it and the player wires it once.
    /// </summary>
    [Signal]
    public delegate void AimStateChangedEventHandler(
        bool aiming, float fovScale, float transitionSpeed);

    /// <summary>True while this weapon is being aimed down.</summary>
    public virtual bool IsAiming => false;

    protected readonly RandomNumberGenerator Rng = new();

    /// <summary>
    /// Locomotion state, pushed in by the owner each frame. A weapon may need
    /// it but must never go looking up the tree for a player to ask.
    /// </summary>
    protected bool OwnerMoving;
    protected bool OwnerAirborne;
    protected bool OwnerCrouched;

    /// <summary>
    /// Which way is up, and how hard gravity pulls, where the owner is
    /// standing. Pushed down each frame like everything else here.
    ///
    /// A hitscan shot and a melee swing are camera-relative and do not care,
    /// but anything that launches something with an arc does: on a planet
    /// "down" is a different direction at every point on the surface, and a
    /// projectile that assumed world down would curve sideways.
    /// </summary>
    protected Vector3 OwnerUp = Vector3.Up;

    /// <summary>Earned upgrades, pushed by the loadout. See <see cref="PackageBonus"/>.</summary>
    public PackageBonus Bonus { get; set; } = PackageBonus.None;
    protected Vector3 OwnerGravity = Vector3.Down * 9.8f;

    /// <summary>
    /// How fast the owner is moving, pushed down each frame. The machete reads
    /// it so a cut landed off the end of a swing lands with the swing's speed
    /// behind it; nothing else does yet.
    /// </summary>
    protected Vector3 OwnerVelocity;

    protected Vector3 ViewModelRestPosition;
    protected Vector3 ViewModelKick;
    protected float ViewModelPitchKick;

    /// <summary>
    /// The viewmodel's size on screen, as a scale on the authored model. The
    /// placeholder guns were built big and dead centre-right; at 0.82 and
    /// nudged down and right they leave the middle of the screen to the
    /// crowd, which is what is being aimed at.
    /// </summary>
    [ExportGroup("Viewmodel Placement")]
    [Export(PropertyHint.Range, "0.3,1.5,0.01")]
    public float ViewModelScale { get; set; } = 0.82f;

    /// <summary>Added to the authored position after scaling: right, up, back.</summary>
    [Export]
    public Vector3 ViewModelOffset { get; set; } = new(0.035f, -0.04f, 0.0f);

    // The cartoon half of recoil: the kick springs back past rest and
    // settles, and the gun squashes along its barrel for an instant.
    private Vector3 _kickVelocity;
    private float _pitchVelocity;
    private float _rollKick;
    private float _rollVelocity;
    private float _squash;
    private float _squashVelocity;

    /// <summary>
    /// 0 to 1, how far into the reload pose the gun is: down, rolled and
    /// tipped away while <see cref="IsReloading"/>, and back up when it is
    /// done. Eased in, then released into the same spring as the recoil so the
    /// gun snaps back into the hands with a bounce. A fast reload with no
    /// motion read as a number refilling rather than a gun being reloaded.
    /// </summary>
    private float _reloadPose;

    /// <summary>Damping ratio of the return. Under one overshoots; 0.45 bounces once.</summary>
    private const float KickDamping = 0.45f;

    /// <summary>How fast the viewmodel settles back to rest. Subclasses read it
    /// from their own stats resource.</summary>
    protected virtual float ViewModelRecoverSpeed => 12.0f;

    public override void _Ready()
    {
        Rng.Randomize();
        if (ViewModel != null)
        {
            ViewModel.Scale = Vector3.One * ViewModelScale;
            ViewModelRestPosition = (ViewModel.Position * ViewModelScale) + ViewModelOffset;
            ViewModel.Position = ViewModelRestPosition;
        }
        AimCamera ??= GetViewport().GetCamera3D();
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        TickWeapon(dt);
        TickViewModel(dt);

        if (IsEquipped && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            HandleWeaponInput();
        }
    }

    /// <summary>Per-frame timers and state. Runs whether or not input is accepted.</summary>
    protected virtual void TickWeapon(float dt)
    {
    }

    /// <summary>
    /// Reads the trigger. Only ever called on the equipped weapon with the
    /// mouse captured, so implementations do not repeat either check.
    /// </summary>
    protected abstract void HandleWeaponInput();

    protected virtual void TickViewModel(float dt)
    {
        if (ViewModel == null)
        {
            return;
        }

        // A damped spring rather than an exponential ease: the gun snaps back
        // past rest and settles, which is what reads as cartoon recoil. The
        // stats' recover speed sets how fast; the damping sets the bounce.
        float omega = ViewModelRecoverSpeed * 1.3f;
        float stiffness = omega * omega;
        float damping = 2.0f * KickDamping * omega;
        float step = Mathf.Min(dt, 1.0f / 30.0f);

        _kickVelocity += ((-stiffness * ViewModelKick) - (damping * _kickVelocity)) * step;
        ViewModelKick += _kickVelocity * step;
        _pitchVelocity += ((-stiffness * ViewModelPitchKick) - (damping * _pitchVelocity)) * step;
        ViewModelPitchKick += _pitchVelocity * step;
        _rollVelocity += ((-stiffness * _rollKick) - (damping * _rollVelocity)) * step;
        _rollKick += _rollVelocity * step;

        // The squash is quicker and bouncier than the kick: a beat of rubber.
        float squashOmega = omega * 1.8f;
        _squashVelocity += ((-squashOmega * squashOmega * _squash) - (2.0f * 0.35f * squashOmega * _squashVelocity)) * step;
        _squash += _squashVelocity * step;

        bool reloading = IsReloading;
        float before = _reloadPose;
        _reloadPose = Mathf.MoveToward(_reloadPose, reloading ? 1.0f : 0.0f, dt * (reloading ? 9.0f : 6.0f));
        if (before > 0.5f && !reloading && _reloadPose <= 0.5f)
        {
            // The magazine is home: a little upward pop into the spring.
            _kickVelocity += new Vector3(0.0f, 0.6f, 0.0f);
            _squash = Mathf.Min(1.0f, _squash + 0.5f);
        }
        float pose = _reloadPose * _reloadPose * (3.0f - (2.0f * _reloadPose));
        Vector3 dip = new Vector3(0.02f, -0.11f, 0.03f) * pose;

        ViewModel.Position = ViewModelRestPosition + ViewModelKick + dip;
        ViewModel.Rotation = new Vector3(
            ViewModelPitchKick - (0.45f * pose),
            0.25f * pose,
            _rollKick + (0.55f * pose));
        float bulge = 1.0f + (0.12f * _squash);
        ViewModel.Scale = new Vector3(bulge, bulge, 1.0f - (0.18f * _squash)) * ViewModelScale;
    }

    protected void AddViewModelKick(float back, float pitchDegrees)
    {
        ViewModelKick += new Vector3(0.0f, 0.0f, back);
        ViewModelPitchKick += Mathf.DegToRad(pitchDegrees);
        _rollKick += Mathf.DegToRad(pitchDegrees) * Rng.RandfRange(-0.4f, 0.4f);

        // Bigger kicks squash harder, saturating so a rocket does not fold the
        // tube in half.
        _squash = Mathf.Min(1.0f, _squash + Mathf.Clamp(back * 9.0f, 0.15f, 1.0f));
    }

    /// <summary>
    /// Called by the owner each frame with the state a weapon may need.
    /// Pushed, never pulled.
    /// </summary>
    public void SetOwnerLocomotion(bool moving, bool airborne, bool crouched)
    {
        OwnerMoving = moving;
        OwnerAirborne = airborne;
        OwnerCrouched = crouched;
    }

    /// <summary>Called by the owner each frame with the local gravity frame.</summary>
    public void SetOwnerFrame(Vector3 up, Vector3 gravity)
    {
        OwnerUp = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
        OwnerGravity = gravity;
    }

    /// <summary>Called by the owner each frame with its own velocity.</summary>
    public void SetOwnerVelocity(Vector3 velocity)
    {
        OwnerVelocity = velocity;
    }

    // ---------------- Equip ----------------

    public void Equip()
    {
        IsEquipped = true;
        Visible = !IsBroken;
        SetProcess(!IsBroken);
        OnEquipped();
        PublishStatus();
    }

    public void Holster()
    {
        IsEquipped = false;
        Visible = false;
        // Nothing to tick and, more to the point, no trigger to read.
        SetProcess(false);
        OnHolstered();
    }

    protected virtual void OnEquipped()
    {
    }

    protected virtual void OnHolstered()
    {
    }

    /// <summary>
    /// Re-announces whatever the HUD needs after a switch. The carbine re-emits
    /// its real ammo count; a weapon with nothing to count does nothing.
    /// </summary>
    public virtual void PublishStatus()
    {
    }

    /// <summary>
    /// Puts ammunition back, as a fraction of this weapon's own full reserve.
    ///
    /// A fraction rather than a count, because the weapons do not share a unit:
    /// the carbine carries 180 rounds, the bazooka 12 and the flamethrower a
    /// number of seconds. One supply order granting "half a reserve" means
    /// something for all three; one granting "40 rounds" means nothing for two
    /// of them and would need a table mapping orders to weapons.
    ///
    /// Returns true if anything was actually added, so a caller can tell a
    /// resupply that did something from one that found everything full.
    /// </summary>
    public virtual bool Resupply(float fraction) => false;
}
