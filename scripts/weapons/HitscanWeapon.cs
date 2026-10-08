using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// A hitscan weapon. Everything it does is driven by its
/// <see cref="WeaponStats"/> -- there is no balance number in this file.
///
/// It reports outward through signals and never reaches for the player, the
/// HUD or the camera. Recoil is emitted as <see cref="WeaponBase.Fired"/> and
/// whoever owns the weapon decides what to do with it; ammo goes on the event
/// bus because the HUD is not something a gun should know about.
/// </summary>
public partial class HitscanWeapon : WeaponBase
{
    [ExportGroup("Data")]
    [Export]
    public WeaponStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>Where the tracer and muzzle flash are drawn from.</summary>
    [Export]
    public Node3D MuzzlePoint { get; set; }

    [Export]
    public Node3D MuzzleFlash { get; set; }

    [ExportGroup("Effect Scenes")]
    [Export]
    public PackedScene TracerScene { get; set; }

    [Export]
    public PackedScene ImpactScene { get; set; }

    /// <summary>
    /// Drawn for explosive rounds (<see cref="WeaponStats.SplashRadius"/>).
    /// Unused, and may be left empty, on a gun without them.
    /// </summary>
    [Export]
    public PackedScene ExplosionScene { get; set; }

    [Signal]
    public delegate void ReloadStartedEventHandler(float duration);

    [Signal]
    public delegate void ReloadFinishedEventHandler();

    /// <summary>Local signal: true when the shot landed on a Hitbox.</summary>
    [Signal]
    public delegate void HitConfirmedEventHandler(bool weakPoint, Vector3 position);

    public int MagazineAmmo { get; private set; }
    public int ReserveAmmo { get; private set; }

    public override bool IsReloading => _isReloading;

    /// <summary>Current cone half-angle in degrees, including all multipliers.</summary>
    public override float CurrentSpreadDegrees => _spreadDegrees;

    public override bool UsesAmmo => true;

    public override string DisplayName => Stats?.DisplayName ?? Name;

    protected override float ViewModelRecoverSpeed => Stats?.ViewmodelRecoverSpeed ?? 12.0f;

    public override bool IsAiming => _aiming;

    private bool _aiming;

    /// <summary>0 at the hip, 1 fully aimed. Drives the viewmodel only; the
    /// camera runs its own blend so the two cannot desynchronise on a switch.</summary>
    private float _aimBlend;

    private bool _isReloading;
    private float _spreadDegrees;
    private float _cooldown;
    private float _reloadRemaining;
    private float _accumulatedSpread;
    private float _flashRemaining;
    private int _shotCounter;

    public override void _Ready()
    {
        base._Ready();

        // Touched here so both sections exist before a perf recording starts:
        // the recorder fixes its columns when it begins, and a section first
        // registered on the first shot would never appear in it.
        _ = SectionTrace + SectionSplash;

        if (Stats == null)
        {
            GD.PushError("HitscanWeapon has no WeaponStats assigned.");
            IsBroken = true;
            SetProcess(false);
            return;
        }

        MagazineAmmo = Stats.MagazineSize;
        ReserveAmmo = Stats.ReserveAmmo;

        if (MuzzleFlash != null)
        {
            MuzzleFlash.Visible = false;
        }

        // Deferred: the HUD is an autoload-adjacent sibling that may not be
        // ready when the player scene instances during the level's _Ready.
        CallDeferred(MethodName.PublishAmmo);
    }

    protected override void TickWeapon(float dt)
    {
        TickCooldowns(dt);
        TickSpread(dt);
    }

    private void TickCooldowns(float dt)
    {
        _cooldown = Mathf.Max(0.0f, _cooldown - dt);

        if (_flashRemaining > 0.0f)
        {
            _flashRemaining -= dt;
            if (_flashRemaining <= 0.0f && MuzzleFlash != null)
            {
                MuzzleFlash.Visible = false;
            }
        }

        if (!_isReloading)
        {
            return;
        }

        _reloadRemaining -= dt;
        if (_reloadRemaining <= 0.0f)
        {
            FinishReload();
        }
    }

    private void TickSpread(float dt)
    {
        _accumulatedSpread = Mathf.Max(0.0f, _accumulatedSpread - (Stats.SpreadRecoveryPerSecond * dt));
        _spreadDegrees = Mathf.Min(
            (Stats.BaseSpreadDegrees + _accumulatedSpread)
                * StanceSpreadMultiplier()
                * AimSpreadMultiplier(),
            Stats.MaxSpreadDegrees);
    }

    private float StanceSpreadMultiplier()
    {
        if (OwnerAirborne)
        {
            return Stats.AirborneSpreadMultiplier;
        }
        if (OwnerCrouched)
        {
            return Stats.CrouchSpreadMultiplier;
        }
        return OwnerMoving ? Stats.MovingSpreadMultiplier : 1.0f;
    }

    /// <summary>
    /// Aiming scales whatever stance already produced rather than replacing it,
    /// so aiming from a sprint is still worse than aiming from a crouch.
    /// </summary>
    private float AimSpreadMultiplier() => _aiming ? Stats.AimSpreadMultiplier : 1.0f;

    protected override void HandleWeaponInput()
    {
        // Held, not toggled. The melee weapon reads the same button as its
        // stab; only the weapon in hand is asked, so they never collide.
        SetAiming(Input.IsActionPressed("alt_fire"));

        if (Input.IsActionJustPressed("reload"))
        {
            TryStartReload();
        }

        bool triggerDown = Stats.FireMode == WeaponFireMode.Automatic
            ? Input.IsActionPressed("fire")
            : Input.IsActionJustPressed("fire");

        if (triggerDown)
        {
            TryFire();
        }
    }

    private void SetAiming(bool aiming)
    {
        if (aiming == _aiming)
        {
            return;
        }

        _aiming = aiming;
        EmitSignal(
            SignalName.AimStateChanged, _aiming, Stats.AimFovScale, Stats.AimTransitionSpeed);
    }

    protected override void OnHolstered()
    {
        base.OnHolstered();
        // A weapon going away must not leave the camera zoomed in.
        SetAiming(false);
        _aimBlend = 0.0f;
    }

    protected override void TickViewModel(float dt)
    {
        base.TickViewModel(dt);

        if (ViewModel == null)
        {
            return;
        }

        _aimBlend = Mathf.MoveToward(
            _aimBlend, _aiming ? 1.0f : 0.0f, Stats.AimTransitionSpeed * dt);

        if (_aimBlend <= 0.0f)
        {
            return;
        }

        // Base has already written the hip pose including the recoil kick.
        // The aimed pose is the same kick applied on the sightline instead.
        Vector3 hip = ViewModel.Position;
        Vector3 aimed = hip + new Vector3(
            -ViewModelRestPosition.X,
            Stats.AimViewmodelRise,
            Stats.AimViewmodelPush);

        ViewModel.Position = hip.Lerp(aimed, _aimBlend);
    }

    // ---------------- Firing ----------------

    public void TryFire()
    {
        if (_isReloading || _cooldown > 0.0f)
        {
            return;
        }

        if (MagazineAmmo <= 0)
        {
            // A reload that cannot start is an empty gun, and an empty gun that
            // makes no sound is indistinguishable from a broken one.
            if (ReserveAmmo <= 0)
            {
                AudioDirector.Play(Sfx.DryFire);
            }

            TryStartReload();
            return;
        }

        Fire();
    }

    private void Fire()
    {
        _cooldown = Stats.ShotInterval;
        MagazineAmmo--;
        _shotCounter++;

        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 forward = -AimCamera.GlobalBasis.Z.Normalized();

        // Each pellet rolls its own offset inside the cone, but only the first
        // draws a tracer -- SpawnTracer is per trace, so a ten-pellet shell
        // would otherwise paint ten of them down the same corridor.
        int pellets = Mathf.Max(1, Stats.PelletsPerShot);
        for (int pellet = 0; pellet < pellets; pellet++)
        {
            TraceShot(origin, ApplySpread(forward), pellet == 0);
        }

        ApplyRecoil();
        if (Stats.RecoilThrust > 0.0f)
        {
            EmitSignal(SignalName.Thrust, -forward * Stats.RecoilThrust);
        }

        ShowMuzzleFlash();
        PlayFireSound();
        PublishAmmo();

        // Once per shot, not once per pellet: a shell would otherwise max out
        // the cone on its very first trigger pull.
        _accumulatedSpread = Mathf.Min(
            _accumulatedSpread + Stats.SpreadPerShotDegrees,
            Stats.MaxSpreadDegrees);
    }

    /// <summary>
    /// Offsets the aim direction by a random point inside the spread cone.
    /// Uniform over the cone's disc, not over its angle, so shots do not
    /// cluster in the centre more than the cone implies.
    /// </summary>
    private Vector3 ApplySpread(Vector3 forward)
    {
        if (CurrentSpreadDegrees <= 0.0f)
        {
            return forward;
        }

        float maxAngle = Mathf.DegToRad(CurrentSpreadDegrees);
        float angle = maxAngle * Mathf.Sqrt(Rng.Randf());
        float roll = Rng.Randf() * Mathf.Tau;

        Vector3 right = AimCamera.GlobalBasis.X.Normalized();
        Vector3 up = AimCamera.GlobalBasis.Y.Normalized();
        Vector3 offsetAxis = (right * Mathf.Cos(roll)) + (up * Mathf.Sin(roll));

        return forward.Rotated(offsetAxis.Cross(forward).Normalized(), angle).Normalized();
    }

    /// <summary>
    /// One ray, and then up to <see cref="WeaponStats.PenetrationCount"/> more
    /// from each hit onwards. At the default of 0 this is exactly the single
    /// ray it always was, which is what lets every existing weapon keep its
    /// behaviour without touching a .tres.
    /// </summary>
    private static readonly int SectionTrace = FrameProfiler.Section("hitscan trace");

    private void TraceShot(Vector3 origin, Vector3 direction, bool drawTracer)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionTrace);
        Vector3 end = origin + (direction * Stats.MaxRange);
        Vector3 from = origin;
        float damageScale = MomentumDamageScale();
        Vector3 tracerEnd = end;

        int passes = Mathf.Max(0, Stats.PenetrationCount) + 1;

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(from, end);
        query.CollisionMask = GameLayers.HitscanMask;
        query.CollideWithAreas = true;   // Hitboxes are Area3D.
        query.CollideWithBodies = true;  // World and props are bodies.

        // Everything already hit, so a penetrating round does not re-hit the
        // collider it just came out of on the next pass. Only allocated for a
        // round that can actually penetrate -- the single-ray case is every
        // other weapon in the project and fires often.
        Godot.Collections.Array<Rid> exclude = passes > 1 ? new() : null;

        int splashesLeft = Stats.SplashMaxPerShot > 0 ? Stats.SplashMaxPerShot : int.MaxValue;
        IDamageable lastReceiver = null;
        float lineScale = damageScale;
        Vector3 lineEnd = origin + (direction * Mathf.Min(Stats.LineRange, Stats.MaxRange));

        for (int pass = 0; pass < passes; pass++)
        {
            query.From = from;
            query.To = end;

            if (exclude != null)
            {
                query.Exclude = exclude;
            }

            Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
            if (result.Count == 0)
            {
                break;
            }

            Vector3 point = (Vector3)result["position"];
            Vector3 normal = (Vector3)result["normal"];
            GodotObject collider = (GodotObject)result["collider"];
            float distance = origin.DistanceTo(point);

            tracerEnd = point;
            bool splashes = Stats.SplashRadius > 0.0f && Stats.SplashDamage > 0.0f;
            if (!splashes)
            {
                SpawnImpact(point, normal);
            }

            // Only the pellet that draws a tracer announces the impact. The
            // sound event's own retrigger gate would collapse a shell's nine
            // hits anyway, but not spawning eight voices to have eight of them
            // refused is cheaper and says the same thing.
            if (drawTracer)
            {
                AudioDirector.PlayAt(SurfaceAudio.ImpactFor(collider), point);
            }

            DamageInfo info = new()
            {
                Amount = Stats.Damage * Stats.FalloffMultiplierAt(distance) * damageScale * Bonus.DamageScale,
                Position = point,
                Normal = normal,
                Direction = direction,
                Distance = distance,
                Source = this,
            };

            bool stoppedByWorld = false;

            // The splash goes off where the round stopped or passed through,
            // before the direct hit, so a direct hit's kill is not reported a
            // second time by a blast finding an agent already dead. A round
            // that penetrates detonates at every pass, which is the point of
            // carrying both: a line of pickles is a line of blasts.
            // One blast per thing hit, not per collider: a pickle's body and
            // head are two hitboxes the round passes through back to back.
            IDamageable receiver = (collider as Hitbox)?.Receiver ?? collider as IDamageable;
            bool sameTarget = receiver != null && receiver == lastReceiver;
            lastReceiver = receiver;
            if (splashes && !sameTarget && splashesLeft > 0)
            {
                splashesLeft--;
                Detonate(point, normal, damageScale);
            }
            else if (splashes)
            {
                SpawnImpact(point, normal);
            }

            if (collider is Hitbox hitbox)
            {
                hitbox.TakeHit(info);
                EmitSignal(SignalName.HitConfirmed, hitbox.IsWeakPoint, point);
            }
            else if (collider is IDamageable damageable)
            {
                // Something shootable with no hitbox in front of it. Rare, but
                // the interface is the contract, not the Hitbox node.
                damageable.ApplyDamage(info);
                EmitSignal(SignalName.HitConfirmed, false, point);
            }
            else
            {
                // Static geometry. A round that does not punch through walls
                // ends here, which is also what keeps a penetrating one from
                // firing straight through the planet.
                stoppedByWorld = !Stats.PenetratesWorld;
            }

            if (stoppedByWorld)
            {
                if (origin.DistanceTo(point) < origin.DistanceTo(lineEnd))
                {
                    lineEnd = point;
                }
                break;
            }

            if (collider is CollisionObject3D body)
            {
                exclude?.Add(body.GetRid());
            }

            damageScale *= Stats.PenetrationDamageRetention;

            // Step just past the surface, or the next ray starts on it and
            // reports the same contact again.
            from = point + (direction * 0.02f);
        }

        if (Stats.LineRadius > 0.0f)
        {
            // Through the horde's hash rather than its colliders, so every
            // pickle on the line is reached; marked sustained so it digs no
            // crater and shakes no camera at the muzzle it starts from.
            EventBus.EmitAreaBlast(new BlastInfo
            {
                Centre = origin,
                LineEnd = lineEnd,
                IsLine = true,
                InnerRadius = Stats.LineRadius,
                Radius = Stats.LineRadius,
                Damage = Stats.Damage * lineScale * Bonus.DamageScale,
                MinMultiplier = 1.0f,
                Up = OwnerUp,
                Source = this,
                Sustained = true,
                NoCrater = true,
            });
        }

        if (drawTracer)
        {
            SpawnTracer(tracerEnd);
        }
    }

    private ulong _lastSplashEffectMs;

    private static readonly int SectionSplash = FrameProfiler.Section("splash rounds");

    /// <summary>
    /// One explosive round's blast. Damage through <c>Blast.ApplyDamage</c>,
    /// which also tells the horde; a shove on loose bodies; and an explosion
    /// drawn at most once per <see cref="WeaponStats.SplashEffectInterval"/>.
    /// The player is not shoved by their own rounds -- a point-blank burst
    /// that threw you backwards would make the gun unusable at the one range
    /// the horde forces on you.
    /// </summary>
    private void Detonate(Vector3 point, Vector3 normal, float damageScale)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionSplash);
        Vector3 centre = point + (normal * 0.1f);
        Vector3 up = OwnerUp;

        Blast.Settings blast = new()
        {
            Centre = centre,
            InnerRadius = Stats.SplashInnerRadius,
            Radius = Stats.SplashRadius,
            Damage = Stats.SplashDamage * damageScale * Bonus.DamageScale,
            MinMultiplier = Stats.SplashMinMultiplier,
            Impulse = Stats.SplashImpulse,
            ImpulseUpBias = 0.3f,
            Up = up,
            Source = this,
            SelfImpulseScale = 0.0f,
            NoCrater = true,
        };

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);

        ulong now = Time.GetTicksMsec();
        bool draw = ExplosionScene != null
            && now - _lastSplashEffectMs >= (ulong)(Stats.SplashEffectInterval * 1000.0f);
        if (!draw)
        {
            SpawnImpact(point, normal);
            return;
        }

        _lastSplashEffectMs = now;
        Node3D explosion = ExplosionScene.Instantiate<Node3D>();
        if (explosion is ExplosionEffect effect)
        {
            // Before AddChild: _Ready sizes everything from what it is told.
            effect.ScorchSeconds = Stats.SplashScorchSeconds;
            effect.Minor = true;
            effect.Configure(centre, Stats.SplashEffectRadius, up, normal);
        }
        GetTree().CurrentScene.AddChild(explosion);
        explosion.GlobalPosition = centre;
    }

    private void ApplyRecoil()
    {
        float yawSign = Rng.Randf() < ((Stats.RecoilYawBias + 1.0f) * 0.5f) ? 1.0f : -1.0f;
        float yaw = Mathf.DegToRad(Stats.RecoilYawPerShot) * yawSign * Rng.RandfRange(0.35f, 1.0f);
        float pitch = Mathf.DegToRad(Stats.RecoilPitchPerShot) * Rng.RandfRange(0.8f, 1.0f);

        EmitSignal(SignalName.Fired, new Vector2(yaw, pitch), Stats.RecoilRecoverSpeed, Stats.RecoilSnappiness);

        AddViewModelKick(Stats.ViewmodelKickBack, Stats.ViewmodelKickPitch);
    }

    private void ShowMuzzleFlash()
    {
        if (MuzzleFlash == null)
        {
            return;
        }
        MuzzleFlash.Visible = true;
        MuzzleFlash.Rotation = new Vector3(0.0f, 0.0f, Rng.Randf() * Mathf.Tau);
        _flashRemaining = Stats.MuzzleFlashDuration;
    }

    // ---------------- Effects ----------------

    private void SpawnTracer(Vector3 endPoint)
    {
        if (TracerScene == null || MuzzlePoint == null)
        {
            return;
        }
        if (Stats.TracerEveryNShots > 1 && _shotCounter % Stats.TracerEveryNShots != 0)
        {
            return;
        }

        Tracer tracer = TracerScene.Instantiate<Tracer>();
        // Parented to the level, not the weapon, so it does not ride the view.
        GetTree().CurrentScene.AddChild(tracer);
        tracer.Launch(MuzzlePoint.GlobalPosition, endPoint, Stats.TracerSpeed);
    }

    /// <summary>
    /// Speed as a damage bonus, matching the machete's momentum fields so a
    /// zip into a wart hits harder than a standing shot. Below sprint it is 1.
    /// </summary>
    private float MomentumDamageScale()
    {
        if (Stats == null || Stats.MomentumDamageMultiplier <= 1.0f)
        {
            return 1.0f;
        }

        float speed = OwnerVelocity.Length();
        float span = Mathf.Max(Stats.MomentumFullSpeed - Stats.MomentumMinSpeed, 0.01f);
        float t = Mathf.Clamp((speed - Stats.MomentumMinSpeed) / span, 0.0f, 1.0f);
        return Mathf.Lerp(1.0f, Stats.MomentumDamageMultiplier, t);
    }

    /// <summary>
    /// The shot itself. Non-positional, because it happens in the player's own
    /// hands: a muzzle blast attenuated by its distance to the camera is a
    /// muzzle blast that gets quieter when you aim down the sights.
    /// </summary>
    private void PlayFireSound()
    {
        if (Stats == null)
        {
            return;
        }

        AudioDirector.Play(Stats.FireSfx, Stats.FireSound, Stats.FirePitch, Stats.FireVolumeDb);
    }

    /// <summary>
    /// One flash per pellet was ten omni lights for a ten-pellet shell.
    /// ImpactEffect.Spawn declines past its own live cap -- see the note there.
    /// </summary>
    private void SpawnImpact(Vector3 point, Vector3 normal) =>
        ImpactEffect.Spawn(ImpactScene, GetTree().CurrentScene, point, normal);

    // ---------------- Reloading ----------------

    public void TryStartReload()
    {
        if (_isReloading || ReserveAmmo <= 0 || MagazineAmmo >= Stats.MagazineSize)
        {
            return;
        }

        _isReloading = true;
        // A round still in the chamber means no bolt to cycle, so it is faster.
        _reloadRemaining = (MagazineAmmo > 0 ? Stats.TacticalReloadTime : Stats.ReloadTime) * Bonus.ReloadScale;
        AudioDirector.Play(Stats.ReloadStartSfx);
        EmitSignal(SignalName.ReloadStarted, _reloadRemaining);
    }

    private void FinishReload()
    {
        int wanted = Stats.MagazineSize - MagazineAmmo;
        int taken = Mathf.Min(wanted, ReserveAmmo);

        MagazineAmmo += taken;
        ReserveAmmo -= taken;
        _isReloading = false;
        _accumulatedSpread = 0.0f;

        AudioDirector.Play(Stats.ReloadFinishSfx);
        EmitSignal(SignalName.ReloadFinished);
        PublishAmmo();
    }

    /// <summary>
    /// Re-announces the magazine after a weapon switch, so the HUD shows the
    /// real count the moment the carbine comes back up.
    /// </summary>
    public override void PublishStatus() => PublishAmmo();

    public override bool Resupply(float fraction)
    {
        if (Stats == null || fraction <= 0.0f || ReserveAmmo >= Stats.ReserveAmmo)
        {
            return false;
        }

        // Rounded up, so a small grant against a large reserve is still worth
        // at least one round rather than silently nothing.
        int added = Mathf.CeilToInt(Stats.ReserveAmmo * fraction);
        ReserveAmmo = Mathf.Min(Stats.ReserveAmmo, ReserveAmmo + added);

        PublishAmmo();
        return true;
    }

    private void PublishAmmo()
    {
        if (Stats == null)
        {
            return;
        }
        EventBus.EmitAmmoChanged(MagazineAmmo, Stats.MagazineSize, ReserveAmmo);
    }
}
