using Godot;

namespace DillControl.Weapons;

/// <summary>
/// The gravity manipulator as a weapon: slot 8, a bulky two-handed emitter,
/// hold the trigger to take hold of something and let go to throw it.
///
/// It was a bare key (V) wired up in the spike director, which read as a
/// debug feature rather than as something the player carries. Being a
/// <see cref="WeaponBase"/> gives it everything the other seven already have
/// for free -- a viewmodel, a slot, camera kick through the loadout's one
/// Fired connection, the HUD's weapon name, and "holstered means it cannot
/// read the trigger" answered by the scene tree.
///
/// LMB held grabs, released throws. A click that takes hold of nothing punts
/// instead, so a crowd too heavy to lift can still be shoved. RMB drops what
/// is held gently, or punts when nothing is.
///
/// Reaching the horde still goes through <see cref="Manipulator"/>'s
/// delegates. This scene is instanced inside the player at runtime, so the
/// level cannot export a path to it; it joins <see cref="GroupName"/> and the
/// file that knows both sides finds it there -- the same group push
/// AirstrikeDesignator and WeaponLoadout already use for the same reason.
/// </summary>
public partial class GravityGun : WeaponBase
{
    public const string GroupName = "dill_gravity_gun";

    [ExportGroup("Data")]
    [Export]
    public GravityGunStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>Where the tether is drawn from.</summary>
    [Export]
    public Node3D MuzzlePoint { get; set; }

    /// <summary>PLACEHOLDER tether: a unit box stretched from the emitter to
    /// whatever is held, the same arrangement as the bike's cannon beam.</summary>
    [Export]
    public MeshInstance3D Beam { get; set; }

    /// <summary>The emitter's glowing core, brightened while holding.</summary>
    [Export]
    public MeshInstance3D Core { get; set; }

    public Manipulator Manipulator { get; private set; }

    public override string DisplayName => Stats?.DisplayName ?? "Manipulator";

    protected override float ViewModelRecoverSpeed => Stats?.ViewModelRecoverSpeed ?? 6.0f;

    private bool _triggerHeld;
    private bool _grabbedThisPress;
    private ulong _pressedAt;
    private StandardMaterial3D _coreMaterial;
    private float _coreBaseEnergy = 1.0f;

    public override void _Ready()
    {
        base._Ready();

        if (Stats == null)
        {
            GD.PushError("GravityGun has no GravityGunStats assigned.");
            IsBroken = true;
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }

        Manipulator = new Manipulator { Name = "Manipulator" };
        AddChild(Manipulator);
        PushStats();

        if (Core?.MaterialOverride is StandardMaterial3D material)
        {
            // Duplicated so a second instance of the scene does not pulse too.
            _coreMaterial = (StandardMaterial3D)material.Duplicate();
            Core.MaterialOverride = _coreMaterial;
            _coreBaseEnergy = _coreMaterial.EmissionEnergyMultiplier;
        }

        if (Beam != null)
        {
            Beam.Visible = false;
            Beam.TopLevel = true;
        }

        AddToGroup(GroupName);
    }

    /// <summary>Every frame, so the tuning panel reaches a held body live.</summary>
    private void PushStats()
    {
        Manipulator.Range = Stats.Range;
        Manipulator.HoldDistance = Stats.HoldDistance;
        Manipulator.ServoStrength = Stats.ServoStrength;
        Manipulator.MaxHoldSpeed = Stats.MaxHoldSpeed;
        Manipulator.LaunchSpeed = Stats.LaunchSpeed;
        Manipulator.MaxGrabMass = Stats.MaxGrabMass;
        Manipulator.PuntRadius = Stats.PuntRadius;
        Manipulator.PuntDistance = Stats.PuntDistance;
        Manipulator.PuntImpulse = Stats.PuntImpulse;
        Manipulator.Impact = new ThrownImpact.Tuning(
            Stats.ImpactDamagePerSpeed,
            Stats.ImpactMinSpeed,
            Stats.ImpactReferenceMass,
            Stats.ImpactMaxTargets,
            Stats.ImpactLifetime);
    }

    /// <summary>
    /// Physics frame, and deliberately not switched off by a holster: the
    /// servo drives a rigid body's velocity, which belongs to the physics step.
    /// A holster releases whatever is held first, so off-hand this is a no-op.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (Manipulator == null)
        {
            return;
        }

        PushStats();
        Manipulator.Tick(AimCamera, (float)delta);
        TickVortex((float)delta);
    }

    // ---------------- Vortex ----------------

    private readonly System.Collections.Generic.List<long> _vortex = new();
    private readonly System.Collections.Generic.List<long> _gathered = new();
    private bool _vortexOn;
    private float _vortexAngle;
    private ulong _altPressedAt;

    /// <summary>The centre of the ball: ahead of the muzzle, where the player is looking.</summary>
    private Vector3 VortexCentre() =>
        AimCamera.GlobalPosition + (-AimCamera.GlobalBasis.Z.Normalized() * Stats.VortexDistance);

    /// <summary>
    /// While RMB is held: pull in what is near the centre, up to the cap and
    /// under the mass gate, and hold each on a spinning ring round it, on the
    /// same MoveAgent call the single grab uses. Agents that died or were
    /// refilled drop out on their own, since a stale handle moves nothing.
    /// </summary>
    private void TickVortex(float dt)
    {
        if (!_vortexOn || AimCamera == null || Manipulator.MoveAgent == null)
        {
            return;
        }

        Vector3 centre = VortexCentre();

        if (_vortex.Count < Stats.VortexMaxAgents && Manipulator.CollectAgents != null)
        {
            Manipulator.CollectAgents(centre, Stats.VortexPullRadius, Stats.VortexMaxAgents - _vortex.Count, _gathered);
            foreach (long handle in _gathered)
            {
                float mass = Manipulator.AgentMass?.Invoke(handle) ?? 0.0f;
                if (mass <= Stats.MaxGrabMass && !_vortex.Contains(handle))
                {
                    _vortex.Add(handle);
                }
            }
        }

        _vortexAngle += Mathf.Tau * Stats.VortexSpin * dt;
        Basis view = AimCamera.GlobalBasis;
        for (int i = _vortex.Count - 1; i >= 0; i--)
        {
            // A golden-angle shell: every agent its own place on the ball, the
            // whole ball turning about the sightline.
            float a = (i * 2.39996f) + _vortexAngle;
            float h = 1.0f - (2.0f * (i + 0.5f) / Mathf.Max(1, _vortex.Count));
            float r = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (h * h)));
            Vector3 local = new(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
            Vector3 target = centre + (view * local * Stats.VortexBallRadius);

            Vector3? at = Manipulator.AgentPosition?.Invoke(_vortex[i]);
            if (at == null)
            {
                _vortex.RemoveAt(i);
                continue;
            }

            // Eased in, so a pickle is dragged across the ground to the ball
            // rather than teleported onto it.
            Vector3 step = at.Value.Lerp(target, Mathf.Min(1.0f, dt * 6.0f));
            if (!Manipulator.MoveAgent(_vortex[i], step))
            {
                _vortex.RemoveAt(i);
            }
        }
    }

    private void StartVortex()
    {
        _vortexOn = true;
        _vortex.Clear();
    }

    /// <summary>Lets the ball go: every agent flung along the aim, fanned a little.</summary>
    private void ReleaseVortex(bool fling)
    {
        _vortexOn = false;
        if (AimCamera != null && Manipulator.ReleaseAgent != null)
        {
            Vector3 aim = -AimCamera.GlobalBasis.Z.Normalized();
            Basis view = AimCamera.GlobalBasis;
            foreach (long handle in _vortex)
            {
                Vector3 scatter = view * new Vector3(
                    (float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-0.4, 1.0), 0.0f) * Stats.VortexScatter;
                Manipulator.ReleaseAgent(handle, fling ? (aim * Stats.VortexLaunchSpeed) + scatter : Vector3.Zero);
            }
        }

        if (fling && _vortex.Count > 0)
        {
            Kick();
        }
        _vortex.Clear();
    }

    protected override void TickWeapon(float dt)
    {
        // The pause menu takes the mouse and HandleWeaponInput stops being
        // called, so the release would never arrive. Let go rather than carry
        // a crate through the menu.
        if (Input.MouseMode != Input.MouseModeEnum.Captured && Manipulator.IsHolding)
        {
            Manipulator.Release(AimCamera, false);
            _triggerHeld = false;
        }

        if (Input.MouseMode != Input.MouseModeEnum.Captured && _vortexOn)
        {
            ReleaseVortex(false);
        }

        UpdateTether();
    }

    protected override void HandleWeaponInput()
    {
        if (Input.IsActionJustPressed("fire"))
        {
            _triggerHeld = true;
            _pressedAt = Time.GetTicksMsec();
            _grabbedThisPress = Manipulator.TryGrab(AimCamera);

            if (!_grabbedThisPress && !string.IsNullOrEmpty(Manipulator.LastRefusal))
            {
                GD.Print($"[manipulator] {Manipulator.LastRefusal}");
            }
        }
        else if (_triggerHeld && !Input.IsActionPressed("fire"))
        {
            _triggerHeld = false;

            if (_grabbedThisPress && Manipulator.IsHolding)
            {
                Manipulator.Release(AimCamera, true);
                Kick();
            }
            else if (!_grabbedThisPress
                     && Time.GetTicksMsec() - _pressedAt <= (ulong)(Stats.PuntTapWindow * 1000.0f))
            {
                Manipulator.Punt(AimCamera);
                Kick();
            }

            _grabbedThisPress = false;
        }

        // RMB: drops what LMB is holding; otherwise a tap punts and a hold
        // spins up the vortex, which flings everything it gathered on release.
        if (Input.IsActionJustPressed("alt_fire"))
        {
            if (Manipulator.IsHolding)
            {
                Manipulator.Release(AimCamera, false);
                _grabbedThisPress = false;
            }
            else
            {
                _altPressedAt = Time.GetTicksMsec();
                StartVortex();
            }
        }
        else if (_vortexOn && !Input.IsActionPressed("alt_fire"))
        {
            bool tap = Time.GetTicksMsec() - _altPressedAt <= (ulong)(Stats.PuntTapWindow * 1000.0f);
            if (tap && _vortex.Count == 0)
            {
                _vortexOn = false;
                Manipulator.Punt(AimCamera);
                Kick();
            }
            else
            {
                ReleaseVortex(true);
            }
        }
    }

    private void Kick()
    {
        AddViewModelKick(Stats.ViewModelKickBack, Stats.ViewModelKickPitch);

        EmitSignal(
            SignalName.Fired,
            new Vector2(
                Mathf.DegToRad(Stats.KickPitchDegrees),
                Mathf.DegToRad((Rng.Randf() - 0.5f) * 2.0f * Stats.KickYawDegrees)),
            Stats.KickRecoverSpeed,
            Stats.KickSnappiness);
    }

    /// <summary>
    /// Redrawn from the emitter to the held thing every frame, so the pull
    /// reads as a connection rather than a crate that happens to float.
    /// </summary>
    private void UpdateTether()
    {
        bool holding = Manipulator.IsHolding;

        if (_coreMaterial != null)
        {
            float pulse = holding
                ? 3.0f + Mathf.Sin(Time.GetTicksMsec() * 0.02f)
                : 1.0f;
            _coreMaterial.EmissionEnergyMultiplier = _coreBaseEnergy * pulse;
        }

        if (Beam == null || MuzzlePoint == null)
        {
            return;
        }

        if (!holding || !IsEquipped)
        {
            Beam.Visible = false;
            return;
        }

        Vector3 from = MuzzlePoint.GlobalPosition;
        Vector3 to = Manipulator.HeldPosition;
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 0.05f)
        {
            Beam.Visible = false;
            return;
        }

        Vector3 direction = delta / length;
        Vector3 up = Mathf.Abs(direction.Dot(OwnerUp)) > 0.99f ? Vector3.Forward : OwnerUp;

        Beam.Visible = true;
        Beam.GlobalPosition = from + (delta * 0.5f);
        Beam.LookAt(to, up);
        Beam.Scale = new Vector3(Stats.BeamWidth, Stats.BeamWidth, length);
    }

    protected override void OnHolstered()
    {
        if (_vortexOn)
        {
            ReleaseVortex(false);
        }

        if (Manipulator != null && Manipulator.IsHolding)
        {
            Manipulator.Release(AimCamera, false);
        }

        _triggerHeld = false;
        _grabbedThisPress = false;

        if (Beam != null)
        {
            Beam.Visible = false;
        }
    }
}
