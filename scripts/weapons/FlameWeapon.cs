using System.Collections.Generic;
using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// A continuous cone of fire. The first weapon here whose trigger is held
/// rather than pulled, so what a player controls is how long something stays
/// inside the cone rather than how accurately they clicked.
///
/// DECISION -- one shape query per tick, then a cone filter, then one
/// line-of-sight ray per survivor. This is the same two-query structure
/// <see cref="Blast"/> uses and for the same reason: an overlap alone burns
/// through walls, which makes cover worthless. The obvious alternative --
/// spraying N jittered rays down the cone every frame -- is strictly more
/// rays for a worse answer, because it samples the cone rather than covering
/// it and a thin target between two rays takes nothing.
///
/// DECISION -- damage is a rate resolved on a timer, not per frame. Damage per
/// tick is DamagePerSecond times the elapsed slice, so what a target actually
/// takes does not change with frame rate or with TickInterval; that number only
/// trades smoothness against how often the query runs.
///
/// The damage it deals is <b>not</b> flagged as splash. It is direct damage
/// that happens to reach several targets, and the horde's hit proxies drop
/// splash deliberately -- see HordeHitProxy.ApplyDamage.
/// </summary>
public partial class FlameWeapon : WeaponBase
{
    [Export]
    public FlamethrowerStats Stats { get; set; }

    /// <summary>Where the flame is drawn from. The cone is still measured from
    /// the camera, like every other weapon's aim in this project.</summary>
    [Export]
    public Node3D MuzzlePoint { get; set; }

    /// <summary>PLACEHOLDER particle stream, emitting while firing. Built and
    /// sized in code from the stats; see <see cref="BuildFlameVisual"/>.</summary>
    [Export]
    public GpuParticles3D FlameVisual { get; set; }

    /// <summary>Lights the ground in front of the nozzle while firing, and only
    /// then.</summary>
    [Export]
    public OmniLight3D FlameLight { get; set; }

    public override bool UsesAmmo => true;

    public override string DisplayName => Stats?.DisplayName ?? Name;

    protected override float ViewModelRecoverSpeed => Stats?.ViewModelRecoverSpeed ?? 14.0f;

    /// <summary>Seconds of fuel left.</summary>
    public float Fuel { get; private set; }

    private float _tickTimer;
    private float _quietTimer;
    private bool _firing;

    /// <summary>Reused per tick so a held trigger does not allocate.</summary>
    private readonly Dictionary<IDamageable, (Hitbox Box, Node3D Node, Vector3 Point, float Distance)> _targets = new();

    public override void _Ready()
    {
        base._Ready();

        if (Stats == null)
        {
            GD.PushError($"{Name} has no FlamethrowerStats assigned.");
            IsBroken = true;
            return;
        }

        Fuel = Stats.FuelSeconds;
        BuildFlameVisual();
        SetFlameVisible(false);
    }

    /// <summary>
    /// A world-space stream of billboards, sized from the stats the damage
    /// query actually tests against.
    ///
    /// DECISION -- particles, not a cone mesh. The cone was a CylinderMesh laid
    /// along the aim, and a first-person camera looks straight down its axis:
    /// seen end-on, a cone is a disc. Fixing its orientation and then nesting a
    /// brighter core inside it produced, reported both times, "just an orange
    /// orb" -- because that is what any solid of revolution is when viewed
    /// along its own axis, whichever way round it points. A stream of puffs
    /// reads as a jet from any angle, and emitted in world space it trails and
    /// curls as the view sweeps, which is the "river of fire" a mesh rigidly
    /// parented to the camera can never show.
    ///
    /// The envelope is still the query's: puffs travel
    /// <see cref="FlamethrowerStats.Range"/> in their lifetime and grow to the
    /// cone's end radius as they get there, so what is drawn is where the fire
    /// reaches. FlameVisualScale pulls the drawing in if the true volume is
    /// more than the eye wants.
    ///
    /// Alpha-blended rather than additive, for the reason the explosion's
    /// fireball already records: without contrast an additive puff over a
    /// bright sky desaturates to a pale smear. A white-yellow head cooling
    /// through orange to a dark red and a thin grey tail is what reads as heat.
    /// </summary>
    private void BuildFlameVisual()
    {
        if (FlameVisual == null)
        {
            return;
        }
        _flameProcess = BuildJet(FlameVisual, Stats.Range * Stats.FlameVisualScale, Stats.ConeDegrees);
    }

    /// <summary>
    /// Sets an emitter up as the flame jet along its local -Z, reaching
    /// <paramref name="length"/> metres in a cone of <paramref name="coneDegrees"/>.
    /// Shared with anything else that throws flame (a mech's flamer), so every
    /// flame in the game is drawn the same way. Returns the process material,
    /// whose gravity the owner points up as the flame's lift.
    /// </summary>
    public static ParticleProcessMaterial BuildJet(GpuParticles3D visual, float length, float coneDegrees)
    {
        length = Mathf.Max(0.5f, length);
        float endRadius = Mathf.Tan(Mathf.DegToRad(coneDegrees)) * length;

        // Damping bleeds speed over the flight, so the launch speed is set a
        // little over range/lifetime for the puffs to actually arrive.
        ParticleProcessMaterial process = BuildFlameProcess();
        process.InitialVelocityMin = length / FlameLifetime * 1.05f;
        process.InitialVelocityMax = length / FlameLifetime * 1.3f;

        // The spread is narrower than the cone and the growth fills the rest:
        // a puff's centre travelling at the full half-angle would draw its own
        // disc beyond the edge the query tests.
        process.Spread = coneDegrees * 0.6f;
        process.ScaleMin = Mathf.Max(0.4f, endRadius * 0.5f);
        process.ScaleMax = Mathf.Max(0.5f, endRadius * 0.8f);

        visual.ProcessMaterial = process;
        visual.DrawPass1 = BuildFlameQuad();
        visual.Amount = FlameParticles;
        visual.Lifetime = FlameLifetime;
        visual.Randomness = 0.35f;
        visual.LocalCoords = false;
        visual.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        visual.Emitting = false;

        // Culling needs a box that holds the whole stream wherever the view
        // has swept it; a tight one blinks the flame off at the edges.
        visual.VisibilityAabb = new Aabb(
            new Vector3(-length, -length, -length * 1.5f),
            new Vector3(length * 2.0f, length * 2.0f, length * 3.0f));
        return process;
    }

    /// <summary>Particles alive at once. Over the lifetime that is ~190 puffs
    /// a second -- a stream, not a spray.</summary>
    private const int FlameParticles = 96;

    /// <summary>Seconds a puff lives, which with the range fixes its speed.</summary>
    private const float FlameLifetime = 0.5f;

    /// <summary>Upward acceleration on the puffs, in m/s², along the local up.</summary>
    public const float FlameLift = 4.0f;

    private ParticleProcessMaterial _flameProcess;

    private static ParticleProcessMaterial BuildFlameProcess()
    {
        Gradient colour = new();
        // Over 1.0 at the head on purpose: with glow on, the hot end of the
        // jet is the part that blooms, and the cooling tail is the part that
        // does not. The texture has to be HDR or those values clip to white.
        colour.SetColor(0, new Color(2.6f, 2.0f, 1.1f, 0.95f));
        colour.SetColor(1, new Color(0.18f, 0.15f, 0.13f, 0.0f));
        colour.AddPoint(0.15f, new Color(2.0f, 1.05f, 0.25f, 0.9f));
        colour.AddPoint(0.42f, new Color(1.0f, 0.36f, 0.04f, 0.8f));
        colour.AddPoint(0.72f, new Color(0.5f, 0.1f, 0.02f, 0.5f));

        // Small at the nozzle, so the puffs nearest the camera -- the ones that
        // would fill the screen -- are the smallest ones drawn.
        Curve growth = new();
        growth.AddPoint(new Vector2(0.0f, 0.03f));
        growth.AddPoint(new Vector2(0.35f, 0.4f));
        growth.AddPoint(new Vector2(1.0f, 1.0f));

        return new ParticleProcessMaterial
        {
            Direction = new Vector3(0.0f, 0.0f, -1.0f),
            Gravity = Vector3.Zero,
            DampingMin = 4.0f,
            DampingMax = 8.0f,
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            AngularVelocityMin = -160.0f,
            AngularVelocityMax = 160.0f,
            HueVariationMin = -0.02f,
            HueVariationMax = 0.02f,
            ColorRamp = new GradientTexture1D { Gradient = colour, UseHdr = true },
            ScaleCurve = new CurveTexture { Curve = growth },
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.04f,
        };
    }

    private static QuadMesh BuildFlameQuad()
    {
        Gradient falloff = new();
        falloff.SetColor(0, new Color(1.0f, 1.0f, 1.0f, 1.0f));
        falloff.SetColor(1, new Color(1.0f, 1.0f, 1.0f, 0.0f));
        falloff.AddPoint(0.45f, new Color(1.0f, 1.0f, 1.0f, 0.7f));

        StandardMaterial3D material = new()
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = falloff,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1.0f, 0.5f),
                Width = 64,
                Height = 64,
            },
        };

        return new QuadMesh { Size = Vector2.One, Material = material };
    }

    protected override void TickWeapon(float dt)
    {
        if (IsBroken)
        {
            return;
        }

        // Refill only after a quiet spell, so tapping the trigger is not the
        // same as holding it and a long burn actually costs something.
        if (!_firing)
        {
            _quietTimer += dt;
            if (_quietTimer >= Stats.RefillDelay)
            {
                Fuel = Mathf.Min(Fuel + (Stats.FuelRefillPerSecond * dt), Stats.FuelSeconds);
                PublishStatus();
            }
        }

        // Cleared every frame and re-raised by HandleWeaponInput, which only
        // runs on the equipped weapon with the mouse captured -- so holstering
        // or opening the pause menu stops the flame without a special case.
        if (_firing)
        {
            _firing = false;
        }
        else
        {
            SetFlameVisible(false);
        }
    }

    protected override void HandleWeaponInput()
    {
        if (IsBroken)
        {
            return;
        }

        if (!Input.IsActionPressed("fire") || Fuel <= 0.0f)
        {
            return;
        }

        _firing = true;
        _quietTimer = 0.0f;
        SetFlameVisible(true);

        // Hot gas rises along whatever up is here. World-space particles take
        // a world-space gravity, and on a planet that direction moves.
        if (_flameProcess != null)
        {
            _flameProcess.Gravity = OwnerUp * FlameLift;
        }

        float dt = (float)GetProcessDeltaTime();
        Fuel = Mathf.Max(Fuel - dt, 0.0f);
        _tickTimer += dt;

        if (_tickTimer < Stats.TickInterval)
        {
            return;
        }

        ResolveTick(_tickTimer);
        _tickTimer = 0.0f;
        PublishStatus();
    }

    /// <summary>
    /// Resolves one damage tick over <paramref name="slice"/> seconds.
    /// </summary>
    private void ResolveTick(float slice)
    {
        if (AimCamera == null)
        {
            return;
        }

        PhysicsDirectSpaceState3D space = GetWorld3D()?.DirectSpaceState;
        if (space == null)
        {
            return;
        }

        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 forward = -AimCamera.GlobalBasis.Z.Normalized();
        float half = Stats.Range * 0.5f;

        // A sphere covering the cone's reach. Cheaper than a cone shape and the
        // angle test below discards everything it over-selects. Centred half
        // way down the axis, it has to reach the far rim as well as the tip:
        // a radius of half the range left the outer third of a 26 degree cone
        // outside the query entirely.
        float cosine = Mathf.Cos(Mathf.DegToRad(Stats.ConeDegrees));
        float radius = Stats.Range * Mathf.Sqrt(Mathf.Max(0.25f, 1.25f - cosine));

        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = new SphereShape3D { Radius = radius },
            Transform = new Transform3D(Basis.Identity, origin + (forward * half)),
            // The horde's hitboxes only when the wash is off: with it on,
            // the wash burns every agent and the cone would skip them anyway.
            CollisionMask = Stats.HordeWashSpheres > 0 ? GameLayers.ExplosionMask : GameLayers.DirectAreaMask,
            CollideWithAreas = true,   // Hitboxes are monitorable areas.
            CollideWithBodies = true,
        };

        _targets.Clear();

        bool washing = Stats.HordeWashSpheres > 0;
        if (washing)
        {
            Wash(space, origin, forward, slice);
        }

        // Distinct colliders, not raw results: see Blast.DistinctOverlaps for
        // the boulder that used to fill every slot with its own triangles.
        foreach (GodotObject collider in Blast.DistinctOverlaps(space, query, Stats.MaxTargetsPerTick))
        {
            if (collider is not Node3D node)
            {
                continue;
            }

            Hitbox box = node as Hitbox;
            IDamageable receiver = DamageReceiver.Of(node);
            if (receiver == null)
            {
                continue;
            }

            // The wash already burned whatever takes area damage another way;
            // hitting it here as well would count every proxied agent twice.
            if (washing && receiver is ISplashExempt)
            {
                continue;
            }

            Vector3 point = receiver is IBlastSurface surface
                ? surface.NearestBlastPoint(origin)
                : node.GlobalPosition;

            Vector3 toTarget = point - origin;
            float distance = toTarget.Length();
            if (distance > Stats.Range || distance < 0.001f)
            {
                continue;
            }

            if (Mathf.RadToDeg(forward.AngleTo(toTarget / distance)) > Stats.ConeDegrees)
            {
                continue;
            }

            Rid ignore = node is CollisionObject3D body ? body.GetRid() : default;
            if (!Blast.HasLineOfSight(space, origin, point, ignore, default, receiver))
            {
                continue;
            }

            // One target carries several hitboxes over one health pool, so keep
            // the nearest and let it stand for the whole target -- the same
            // de-duplication by receiver the blast and the swing arc do.
            if (!_targets.TryGetValue(receiver, out var best) || distance < best.Distance)
            {
                _targets[receiver] = (box, node, point, distance);
            }
        }

        if (_targets.Count == 0)
        {
            return;
        }

        foreach ((IDamageable receiver, var target) in _targets)
        {
            // A fresh DamageInfo per target: Hitbox.TakeHit mutates what it is
            // handed, so a shared instance would compound multipliers.
            DamageInfo info = new()
            {
                Amount = Stats.DamagePerSecond * slice * Stats.FalloffMultiplierAt(target.Distance) * Bonus.DamageScale,
                Position = target.Point,
                Normal = (origin - target.Point).Normalized(),
                Direction = forward,
                Distance = target.Distance,
                Source = this,

                // Direct damage that happens to reach several targets, not
                // splash -- the horde's proxies drop splash on purpose.
                IsSplash = false,
            };

            if (target.Box != null)
            {
                target.Box.TakeHit(info);
            }
            else
            {
                receiver.ApplyDamage(info);
            }
        }

        EmitSignal(
            SignalName.Fired,
            new Vector2(Mathf.DegToRad(Stats.KickPitchPerTick), 0.0f),
            Stats.KickRecoverSpeed,
            Stats.KickSnappiness);

        AddViewModelKick(0.01f, 0.4f);
    }

    /// <summary>
    /// The horde half of a tick: <see cref="FlamethrowerStats.HordeWashSpheres"/>
    /// spheres down the cone's axis, each sized to the cone's width there, as
    /// sustained blasts on the bus -- the route a fire zone's tick already
    /// takes to every agent in its radius.
    /// </summary>
    private void Wash(PhysicsDirectSpaceState3D space, Vector3 origin, Vector3 forward, float slice)
    {
        int count = Stats.HordeWashSpheres;
        float tangent = Mathf.Tan(Mathf.DegToRad(Stats.ConeDegrees));
        for (int i = 0; i < count; i++)
        {
            float along = Stats.Range * (i + 0.6f) / count;
            float radius = Mathf.Max(1.0f, along * tangent);
            Blast.Settings blast = new()
            {
                Centre = origin + (forward * along),
                InnerRadius = radius,
                Radius = radius,
                Damage = Stats.DamagePerSecond * slice * Stats.FalloffMultiplierAt(along) * Bonus.DamageScale,
                MinMultiplier = 1.0f,
                Up = OwnerUp,
                Source = this,
                SelfImpulseScale = 0.0f,
                Sustained = true,
                NoCrater = true,
            };
            // The bus alone, not Blast.ApplyDamage: the cone's own pass above
            // already burns every ordinary target, and Blast's overlap would
            // burn them a second time. Only what listens for area damage --
            // the horde -- needs telling.
            EventBus.EmitAreaBlast(BlastInfo.From(blast));
        }
    }

    /// <summary>
    /// The flame's one on/off switch, visual and audio together.
    ///
    /// Hanging the loop off this rather than off the trigger is what makes it
    /// correct for free in the cases that are easy to miss: TickWeapon clears
    /// the flame every frame it was not re-raised, so running dry, holstering
    /// and opening the pause menu all stop the sound through the same path
    /// that already stops the cone.
    /// </summary>
    private void SetFlameVisible(bool visible)
    {
        if (FlameVisual != null && FlameVisual.Emitting != visible)
        {
            // Emitting, not Visible: puffs already in the air finish their
            // flight, so letting go of the trigger tails off instead of the
            // whole stream vanishing on one frame.
            FlameVisual.Emitting = visible;
        }

        if (FlameLight != null)
        {
            FlameLight.Visible = visible;
        }

        if (visible == (_flameLoop != null))
        {
            return;
        }

        if (visible)
        {
            _flameLoop = AudioDirector.Loop(Sfx.FlameLoop, this);
            return;
        }

        _flameLoop?.Stop();
        _flameLoop = null;
    }

    private SoundLoop _flameLoop;

    protected override void OnHolstered()
    {
        _firing = false;
        _tickTimer = 0.0f;
        SetFlameVisible(false);
    }

    /// <summary>
    /// Fuel shown through the ammo readout in tenths of a second, so the HUD
    /// has a draining number rather than nothing. A stopgap until there is a
    /// real fuel gauge -- the HUD has no concept of a continuous resource yet.
    /// </summary>
    /// <summary>
    /// Fuel, in the same fraction-of-full terms as everything else. The
    /// flamethrower already refills itself after a quiet spell, so this is the
    /// one weapon where a supply drop buys time rather than capability -- which
    /// is exactly what it should buy mid-fight.
    /// </summary>
    public override bool Resupply(float fraction)
    {
        if (Stats == null || fraction <= 0.0f || Fuel >= Stats.FuelSeconds)
        {
            return false;
        }

        Fuel = Mathf.Min(Stats.FuelSeconds, Fuel + (Stats.FuelSeconds * fraction));
        PublishStatus();
        return true;
    }

    public override void PublishStatus()
    {
        if (Stats == null)
        {
            return;
        }

        EventBus.EmitAmmoChanged(
            Mathf.RoundToInt(Fuel * 10.0f),
            Mathf.RoundToInt(Stats.FuelSeconds * 10.0f),
            0);
    }
}
