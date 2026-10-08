using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// The bazooka. One rocket in the tube, a slow reload, and a warhead that takes
/// a corner off a building.
///
/// It is a <see cref="WeaponBase"/> like the carbine and the machete, which
/// means the loadout, the HUD ammo readout, the camera kick wiring and the
/// holster-stops-processing rule all apply to it with no new code anywhere.
/// The only thing genuinely new here is that its projectile is a node in the
/// world rather than an instant ray, so a shot can be dodged, led, and watched.
/// </summary>
public partial class RocketLauncher : WeaponBase
{
    [ExportGroup("Data")]
    [Export]
    public RocketStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>
    /// Where the rocket is seen to come from. Visual only -- the shot is aimed
    /// from the camera, because a muzzle-origin shot and a centre-screen
    /// crosshair disagree at close range and players trust the crosshair.
    /// </summary>
    [Export]
    public Node3D MuzzlePoint { get; set; }

    [Export]
    public Node3D MuzzleFlash { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene RocketScene { get; set; }

    private int _inTube;
    private int _volleyLeft;
    private int _volleyIndex;
    private float _volleyTimer;
    private int _reserve;
    private float _cooldown;
    private float _reloadRemaining;
    private float _flashRemaining;

    public override bool UsesAmmo => true;

    public override bool IsReloading => _reloadRemaining > 0.0f;

    public override string DisplayName => Stats?.DisplayName ?? "Launcher";

    protected override float ViewModelRecoverSpeed => Stats?.ViewModelRecoverSpeed ?? 7.0f;

    public override void _Ready()
    {
        base._Ready();

        if (Stats == null)
        {
            GD.PushError("RocketLauncher has no RocketStats assigned.");
            IsBroken = true;
            return;
        }

        if (RocketScene == null)
        {
            GD.PushError("RocketLauncher has no RocketScene assigned; it would fire nothing.");
            IsBroken = true;
            return;
        }

        _inTube = Stats.MagazineSize;
        _reserve = Stats.ReserveAmmo;

        if (MuzzleFlash != null)
        {
            MuzzleFlash.Visible = false;
        }
    }

    protected override void TickWeapon(float dt)
    {
        if (_cooldown > 0.0f)
        {
            _cooldown = Mathf.Max(0.0f, _cooldown - dt);
        }

        if (_flashRemaining > 0.0f)
        {
            _flashRemaining -= dt;
            if (_flashRemaining <= 0.0f && MuzzleFlash != null)
            {
                MuzzleFlash.Visible = false;
            }
        }

        if (_volleyLeft > 0)
        {
            _volleyTimer -= dt;
            if (_volleyTimer <= 0.0f)
            {
                _volleyTimer += Stats.VolleyInterval;
                _volleyLeft--;
                LaunchOne(_volleyIndex++);
            }
        }

        if (_reloadRemaining > 0.0f)
        {
            _reloadRemaining -= dt;
            if (_reloadRemaining <= 0.0f)
            {
                FinishReload();
            }
        }
    }

    private bool _aiming;

    public override bool IsAiming => _aiming;

    private void SetAiming(bool aiming)
    {
        if (aiming == _aiming)
        {
            return;
        }

        _aiming = aiming;
        EmitSignal(SignalName.AimStateChanged, _aiming, Stats.AimFovScale, Stats.AimTransitionSpeed);
    }

    protected override void HandleWeaponInput()
    {
        if (Input.IsActionJustPressed("reload"))
        {
            BeginReload();
        }

        SetAiming(Input.IsActionPressed("alt_fire"));

        // Semi-automatic on purpose. Held-trigger rockets would empty the
        // reserve in the time it takes to notice, and the whole cost model of
        // this weapon is that each shot is a decision.
        if (Input.IsActionJustPressed("fire"))
        {
            Fire();
        }
    }

    private void Fire()
    {
        if (IsReloading || _cooldown > 0.0f || AimCamera == null)
        {
            return;
        }

        if (_inTube <= 0)
        {
            BeginReload();
            return;
        }

        _inTube--;
        _cooldown = Stats.FireInterval;

        _volleyLeft = Mathf.Max(1, Stats.RocketsPerShot) - 1;
        _volleyIndex = 1;
        _volleyTimer = Stats.VolleyInterval;
        LaunchOne(0);
        PublishStatus();
    }

    /// <summary>
    /// One rocket of a shot. Index 0 flies straight down the sight line; the
    /// rest sit on a ring of <see cref="RocketStats.VolleySpreadDegrees"/>
    /// around it, stepped by the golden angle so any number of them spread
    /// evenly without a table. Each is aimed from where the camera is now,
    /// not where it was when the trigger went.
    /// </summary>
    private void LaunchOne(int index)
    {
        if (AimCamera == null || !IsInsideTree())
        {
            return;
        }

        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 direction = -AimCamera.GlobalBasis.Z.Normalized();
        if (index > 0 && Stats.VolleySpreadDegrees > 0.0f)
        {
            float roll = index * 2.39996f;
            Vector3 axis = (AimCamera.GlobalBasis.X * Mathf.Cos(roll)) + (AimCamera.GlobalBasis.Y * Mathf.Sin(roll));
            direction = direction.Rotated(axis.Normalized(), Mathf.DegToRad(Stats.VolleySpreadDegrees)).Normalized();
        }

        Rocket rocket = RocketScene.Instantiate<Rocket>();

        // Parented to the level rather than to the weapon: the rocket outlives
        // a weapon switch, and a viewmodel-parented projectile would be dragged
        // along by the camera.
        GetTree().CurrentScene.AddChild(rocket);

        rocket.Launch(
            Stats,
            this,
            origin + (direction * Stats.SpawnOffset),
            direction,
            OwnerUp,
            OwnerGravity,
            ShooterRid());

        ShowFlash();
        AudioDirector.Play(Sfx.RocketFire);
        AddViewModelKick(Stats.ViewModelKickBack, Stats.ViewModelKickPitch);

        // Camera kick goes out the same way every other weapon's does, so the
        // player wires it once and nothing is rewired on a switch.
        EmitSignal(
            SignalName.Fired,
            new Vector2(
                Mathf.DegToRad(Stats.KickPitchDegrees),
                Mathf.DegToRad((Rng.Randf() - 0.5f) * 2.0f * Stats.KickYawDegrees)),
            Stats.KickRecoverSpeed,
            Stats.KickSnappiness);
    }

    /// <summary>
    /// The shooter's own collider, so the rocket's first sweep cannot detonate
    /// against the body that fired it. Walks up to the first CollisionObject3D
    /// ancestor, which is the player capsule -- the weapon is several nodes
    /// below it and has no collider of its own.
    /// </summary>
    private Rid ShooterRid()
    {
        for (Node node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is CollisionObject3D body)
            {
                return body.GetRid();
            }
        }
        return default;
    }

    private void ShowFlash()
    {
        if (MuzzleFlash == null)
        {
            return;
        }

        MuzzleFlash.Visible = true;
        _flashRemaining = 0.06f;
    }

    private void BeginReload()
    {
        if (IsReloading || _inTube >= Stats.MagazineSize || _reserve <= 0)
        {
            return;
        }

        _reloadRemaining = Stats.ReloadTime * Bonus.ReloadScale;
        PublishStatus();
    }

    private void FinishReload()
    {
        _reloadRemaining = 0.0f;

        int wanted = Stats.MagazineSize - _inTube;
        int taken = Mathf.Min(wanted, _reserve);
        _inTube += taken;
        _reserve -= taken;

        PublishStatus();
    }

    protected override void OnHolstered()
    {
        // A weapon going away must not leave the camera zoomed in.
        SetAiming(false);

        // A reload does not survive being put away. Otherwise the tube refills
        // itself while the machete is out, which makes the reload free.
        _reloadRemaining = 0.0f;

        if (MuzzleFlash != null)
        {
            MuzzleFlash.Visible = false;
        }
        _flashRemaining = 0.0f;
    }

    public override bool Resupply(float fraction)
    {
        if (Stats == null || fraction <= 0.0f || _reserve >= Stats.ReserveAmmo)
        {
            return false;
        }

        _reserve = Mathf.Min(Stats.ReserveAmmo, _reserve + Mathf.CeilToInt(Stats.ReserveAmmo * fraction));
        PublishStatus();
        return true;
    }

    public override void PublishStatus()
    {
        EventBus.EmitAmmoChanged(_inTube, Stats?.MagazineSize ?? 0, _reserve);
    }
}
