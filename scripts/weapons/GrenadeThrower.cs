using Godot;
using DillControl.Audio;

namespace DillControl.Weapons;

/// <summary>
/// The hand that cooks and throws. Not a <see cref="WeaponBase"/>, because a
/// grenade is not a slot -- it borrows the hands and gives them back, which is
/// why there is no third weapon and no inventory.
///
/// It reports outward the same way a weapon does: a camera kick on
/// <see cref="Fired"/> and a <see cref="HandsBusy"/> flag. The loadout, which
/// owns what is in the hands, decides what to do with both. This node never
/// reaches for the weapon it displaces.
/// </summary>
public partial class GrenadeThrower : Node3D
{
    [ExportGroup("Data")]
    [Export]
    public GrenadeStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public Camera3D AimCamera { get; set; }

    /// <summary>Shown only while cooking. Hidden the rest of the time.</summary>
    [Export]
    public Node3D ViewModel { get; set; }

    /// <summary>Where the thrown body starts. Ahead of the camera, so a throw
    /// from against a wall does not spawn inside it.</summary>
    [Export]
    public Node3D ThrowPoint { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene GrenadeScene { get; set; }

    /// <summary>Camera kick, same shape as a weapon's so the loadout can
    /// forward it without caring which of its children sent it.</summary>
    [Signal]
    public delegate void FiredEventHandler(Vector2 recoilRadians, float recoverSpeed, float snappiness);

    /// <summary>True while the hands are on a grenade rather than a weapon.</summary>
    [Signal]
    public delegate void HandsBusyEventHandler(bool busy);

    [Signal]
    public delegate void CountChangedEventHandler(int count);

    /// <summary>Grenades left.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Which way is up where the thrower is standing, pushed in by the owner.
    /// Pushed rather than pulled, and not taken from the camera, which pitches:
    /// the throw arc has to lift away from the ground, not away from the
    /// crosshair.
    /// </summary>
    private Vector3 _ownerUp = Vector3.Up;

    public void SetOwnerUp(Vector3 up)
    {
        _ownerUp = up;
    }

    /// <summary>True from the press to the end of the throw recovery.</summary>
    public bool IsBusy => _cooking || _recoveryRemaining > 0.0f;

    /// <summary>Seconds the current grenade has been cooking. Zero when idle.</summary>
    public float CookElapsed => _cooking ? _cookElapsed : 0.0f;

    /// <summary>Fuse left on the grenade in hand, for the HUD to show.</summary>
    public float FuseRemaining =>
        _cooking && Stats != null ? Mathf.Max(0.0f, Stats.FuseTime - _cookElapsed) : 0.0f;

    private bool _cooking;
    private float _cookElapsed;
    private float _cooldownRemaining;
    private float _recoveryRemaining;

    public override void _Ready()
    {
        if (Stats == null)
        {
            GD.PushError("GrenadeThrower has no GrenadeStats assigned.");
            SetProcess(false);
            return;
        }

        AimCamera ??= GetViewport().GetCamera3D();
        Count = Stats.CarriedCount;
        SetViewModelVisible(false);
    }

    /// <summary>
    /// Puts grenades back, as a fraction of the full carry. Same terms as
    /// <see cref="WeaponBase.Resupply"/>, though the thrower is not a
    /// WeaponBase -- it borrows the hands rather than occupying a slot.
    /// </summary>
    public bool Resupply(float fraction)
    {
        if (Stats == null || fraction <= 0.0f || Count >= Stats.CarriedCount)
        {
            return false;
        }

        Count = Mathf.Min(Stats.CarriedCount, Count + Mathf.CeilToInt(Stats.CarriedCount * fraction));
        EmitSignal(SignalName.CountChanged, Count);
        return true;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        _cooldownRemaining = Mathf.Max(0.0f, _cooldownRemaining - dt);

        if (_recoveryRemaining > 0.0f)
        {
            _recoveryRemaining -= dt;
            if (_recoveryRemaining <= 0.0f)
            {
                _recoveryRemaining = 0.0f;
                EmitSignal(SignalName.HandsBusy, false);
            }
        }

        if (_cooking)
        {
            TickCook(dt);
        }

        HandleInput();
    }

    private void HandleInput()
    {
        if (Input.MouseMode != Input.MouseModeEnum.Captured)
        {
            // Releasing the mouse mid-cook would otherwise leave a live grenade
            // in hand with no way to let go of it.
            if (_cooking)
            {
                Throw();
            }
            return;
        }

        if (!_cooking
            && Input.IsActionJustPressed("grenade")
            && Count > 0
            && _cooldownRemaining <= 0.0f
            && _recoveryRemaining <= 0.0f)
        {
            StartCook();
            return;
        }

        if (_cooking && !Input.IsActionPressed("grenade"))
        {
            Throw();
        }
    }

    private void TickCook(float dt)
    {
        _cookElapsed += dt;

        if (Stats.AllowCookOff && _cookElapsed >= Stats.FuseTime)
        {
            // Held too long. The grenade goes off where it is, which is the
            // cost that makes cooking a decision rather than a free timer.
            CookOff();
        }
    }

    // ---------------- Cook and throw ----------------

    private void StartCook()
    {
        _cooking = true;
        _cookElapsed = 0.0f;
        SetViewModelVisible(true);
        EmitSignal(SignalName.HandsBusy, true);
    }

    private void Throw()
    {
        float fuseRemaining = Mathf.Max(0.0f, Stats.FuseTime - _cookElapsed);
        Vector3 direction = AimDirection();
        Vector3 velocity = (direction + (_ownerUp * Stats.ThrowUpBias)).Normalized() * Stats.ThrowSpeed;

        SpawnGrenade(SpawnPosition(), velocity, fuseRemaining);
        ApplyCameraKick();
        FinishThrow();
    }

    private void CookOff()
    {
        // Detonates in hand: zero fuse, no throw velocity, at the player.
        SpawnGrenade(SpawnPosition(), Vector3.Zero, 0.0f);
        FinishThrow();
    }

    private void FinishThrow()
    {
        _cooking = false;
        _cookElapsed = 0.0f;
        _cooldownRemaining = Stats.ThrowCooldown;
        _recoveryRemaining = Stats.ThrowRecovery;

        AudioDirector.Play(Sfx.GrenadeThrow);

        Count = Mathf.Max(0, Count - 1);
        EmitSignal(SignalName.CountChanged, Count);

        SetViewModelVisible(false);
    }

    private void SpawnGrenade(Vector3 position, Vector3 velocity, float fuseRemaining)
    {
        if (GrenadeScene == null)
        {
            GD.PushWarning("GrenadeThrower has no GrenadeScene assigned; nothing was thrown.");
            return;
        }

        Grenade grenade = GrenadeScene.Instantiate<Grenade>();
        // Parented to the level, not the weapon, or it would ride the view.
        GetTree().CurrentScene.AddChild(grenade);
        grenade.Launch(Stats, position, velocity, fuseRemaining, this, _ownerUp);
    }

    private Vector3 AimDirection() =>
        AimCamera != null ? -AimCamera.GlobalBasis.Z.Normalized() : -GlobalBasis.Z.Normalized();

    private Vector3 SpawnPosition() =>
        ThrowPoint != null ? ThrowPoint.GlobalPosition : GlobalPosition;

    private void ApplyCameraKick()
    {
        float yawSign = GD.Randf() < 0.5f ? 1.0f : -1.0f;
        float yaw = Mathf.DegToRad(Stats.ThrowKickYawDegrees) * yawSign;
        float pitch = Mathf.DegToRad(Stats.ThrowKickPitchDegrees);

        EmitSignal(SignalName.Fired, new Vector2(yaw, pitch), Stats.KickRecoverSpeed, Stats.KickSnappiness);
    }

    private void SetViewModelVisible(bool visible)
    {
        if (ViewModel != null)
        {
            ViewModel.Visible = visible;
        }
    }
}
