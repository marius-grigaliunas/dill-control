using Godot;
using DillControl.Audio;
using DillControl.Core;
using DillControl.Levels;
using DillControl.Player;
using DillControl.Weapons;

namespace DillControl.Vehicles;

/// <summary>
/// The Swampy-specific half of the hoverbike -- boarding, the cannon, and the
/// flight HUD -- composed against a <see cref="FlightRig"/> sibling rather
/// than knowing how to fly. See scripts/vehicles/flightrig/README.md for why
/// the split exists: the rig is a portable module, this is the one file that
/// knows both it and the rest of the game, the same role
/// scripts/ui/GamePauseMenu.cs plays for ModularMenu.
/// </summary>
public partial class HoverBikeSystems : Node, IRideable
{
    [ExportGroup("Data")]
    [Export]
    public HoverBikeStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public FlightRig Rig { get; set; }

    [Export]
    public Camera3D Camera { get; set; }

    /// <summary>Where a leaving pilot is put down. Outside the hull.</summary>
    [Export]
    public Marker3D ExitPoint { get; set; }

    /// <summary>Overlap that decides whether the board prompt is available.</summary>
    [Export]
    public Area3D BoardingZone { get; set; }

    [ExportGroup("Cannon")]
    /// <summary>Where the shot is seen to come from. Aimed from the camera,
    /// like every other weapon in the project -- a muzzle-origin shot and a
    /// centre-screen crosshair disagree at close range.</summary>
    [Export]
    public Node3D MuzzlePoint { get; set; }

    /// <summary>PLACEHOLDER beam: a unit box stretched from the muzzle to
    /// whatever the beam ray found, shown for as long as the trigger is held.
    /// Real particles and a bloom pass, month 3.</summary>
    [Export]
    public MeshInstance3D BeamVisual { get; set; }

    [Export]
    public PackedScene ImpactScene { get; set; }

    [ExportGroup("Minigun")]
    /// <summary>Alternating muzzles, the same pattern the rocket pod already
    /// uses -- a minigun with one visible barrel reads as a repeater with a
    /// slow rate; alternating sells the volume of fire for free.</summary>
    [Export]
    public Node3D MinigunMuzzlePort { get; set; }

    [Export]
    public Node3D MinigunMuzzleStarboard { get; set; }

    /// <summary>Same shared fx_tracer.tscn every hand weapon's HitscanWeapon
    /// uses -- there is nothing bike-specific about a tracer.</summary>
    [Export]
    public PackedScene TracerScene { get; set; }

    /// <summary>The same fx_explosion.tscn every rocket uses, drawn small for
    /// an explosive round. See HoverBikeStats.MinigunExplosive.</summary>
    [Export]
    public PackedScene ExplosionScene { get; set; }

    [ExportGroup("Cannon")]
    /// <summary>Alternating pod muzzles, matching the two pods on the hull.</summary>
    [Export]
    public Node3D PodMuzzlePort { get; set; }

    [Export]
    public Node3D PodMuzzleStarboard { get; set; }

    /// <summary>Where a bomb is released from. Under the hull, so a released
    /// bomb does not scrape along it on the way out.</summary>
    [Export]
    public Node3D BombPoint { get; set; }

    [Export]
    public PackedScene RocketScene { get; set; }

    [Export]
    public PackedScene BombScene { get; set; }

    /// <summary>
    /// The level, for its PlayerSpawned signal.
    ///
    /// Listened to rather than the level knowing about ships: the player is
    /// instanced at runtime so it cannot be an [Export], and this direction
    /// keeps LevelRoot -- which every level uses -- free of any dependency on
    /// a ship existing.
    /// </summary>
    [Export]
    public LevelRoot Level { get; set; }

    /// <summary>Local signal, for a HUD prompt outside this node that wants
    /// to know when boarding state changes.</summary>
    [Signal]
    public delegate void PilotedChangedEventHandler(bool piloted);

    public bool IsPiloted { get; private set; }

    /// <summary>
    /// What LMB fires. The bike's weapons are slots on one trigger, picked with
    /// the same number keys the hand loadout uses -- the loadout's processing
    /// is off while the player is stowed, so the keys are free. Slot order is
    /// the enum order, so a slot's number is its position here exactly as a
    /// hand weapon's number is its position in WeaponLoadout.
    ///
    /// Only the trigger weapons are slots. The rocket pod and the bomb bay keep
    /// their own buttons, because a slot you have to switch to is a slot you
    /// cannot fire alongside the gun.
    /// </summary>
    public enum ShipWeapon
    {
        Gun,
        Laser,
    }

    public ShipWeapon SelectedWeapon { get; private set; } = ShipWeapon.Gun;

    private static readonly StringName[] SlotActions = { "slot_1", "slot_2" };

    private PlayerController _player;
    private bool _playerInRange;

    /// <summary>Whether the player is inside the boarding zone. For the test harness.</summary>
    public bool PlayerInRange => _playerInRange;

    /// <summary>Whether a player has been handed over by the level yet. For the test harness.</summary>
    public bool HasPlayer => _player != null;
    private float _beamTimer;
    private float _podCooldown;
    private float _bombCooldown;
    private int _podRemaining;
    private int _bombRemaining;
    private bool _podAlternate;
    private float _minigunCooldown;
    private float _minigunQuietTimer;
    private int _minigunRemaining;
    private bool _minigunAlternate;
    private ulong _lastRoundExplosionMs;

    /// <summary>Rounds since the gun last dug; see MinigunCraterDepthPerRound.</summary>
    private int _roundsSinceDig;

    // The laser's melt: how long it has held, where, and when it last lit.
    private float _meltTimer;
    private float _igniteTimer;
    private string _weaponText;

    /// <summary>Owned, built in _Ready, and pushed to -- never read from.</summary>
    private HoverBikeHud _hud;

    public override void _Ready()
    {
        if (Rig == null || Stats == null)
        {
            GD.PushError("HoverBikeSystems needs both Rig and Stats assigned.");
            SetPhysicsProcess(false);
            return;
        }

        if (BoardingZone != null)
        {
            BoardingZone.CollisionLayer = 0;
            BoardingZone.CollisionMask = GameLayers.Mask(GameLayers.Player);
            BoardingZone.Monitoring = true;
            BoardingZone.BodyEntered += OnBoardingBodyEntered;
            BoardingZone.BodyExited += OnBoardingBodyExited;

            // The shape's own radius was hard-coded in the scene and
            // Stats.BoardRange was never actually read anywhere -- a tuning
            // value that could not tune anything. Driven from Stats now, so
            // the number in the inspector is the number that is true.
            foreach (Node child in BoardingZone.GetChildren())
            {
                if (child is CollisionShape3D { Shape: SphereShape3D sphere })
                {
                    sphere.Radius = Stats.BoardRange;
                }
            }
        }

        // Wired in the level scene normally. If the override is missing --
        // an editor re-save of the level once dropped it, and boarding then
        // silently did nothing because the bike never learned who the player
        // was -- find the level this bike stands in, the way a Hitbox finds
        // its receiver: by walking up for the type, never by a path.
        Level ??= FindLevel();

        BuildBeamVisual();

        if (Stats.LaserTrailSeconds > 0.0f)
        {
            _scorch = new ScorchTrail
            {
                Name = "ScorchTrail",
                Width = Stats.LaserTrailWidth,
                Lifetime = Stats.LaserTrailSeconds,
                EmberSeconds = Stats.LaserTrailEmberSeconds,
                Capacity = Stats.LaserTrailMarks,
            };
            AddChild(_scorch);
        }

        if (Level != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
            _player ??= Level.Player as PlayerController;
        }
        else
        {
            GD.PushWarning("HoverBikeSystems found no LevelRoot above it; boarding will never work.");
        }

        SetBeamVisible(false);

        _hud = new HoverBikeHud { Name = "Hud" };
        AddChild(_hud);
        _hud.SetLegend(
            "WASD  thrust        Space / Ctrl  up-down\n"
            + "Shift  boost         Q / E  roll left / right\n"
            + "LMB  fire slot       1  gun   2  laser\n"
            + "RMB  rocket pod      G  drop bomb\n"
            + "H  hover / cruise mode");
        UpdatePrompt();
    }

    public override void _ExitTree()
    {
        if (BoardingZone != null)
        {
            BoardingZone.BodyEntered -= OnBoardingBodyEntered;
            BoardingZone.BodyExited -= OnBoardingBodyExited;
        }

        if (Level != null)
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private LevelRoot FindLevel()
    {
        for (Node node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is LevelRoot level)
            {
                return level;
            }
        }
        return null;
    }

    private void OnPlayerSpawned(Node3D player)
    {
        _player = player as PlayerController;
    }

    private void OnBoardingBodyEntered(Node3D body)
    {
        if (body is PlayerController)
        {
            _playerInRange = true;
            UpdatePrompt();
        }
    }

    private void OnBoardingBodyExited(Node3D body)
    {
        if (body is PlayerController)
        {
            _playerInRange = false;
            UpdatePrompt();
        }
    }

    // ---------------- HUD pushes ----------------

    private void UpdatePrompt() =>
        _hud?.SetPrompt(IsPiloted, _playerInRange, Stats?.DisplayName);

    /// <summary>
    /// Pushes this frame's flight state at the HUD. Pitch and roll are measured
    /// against the <b>local</b> horizon, which on a planet is not the world one.
    ///
    /// They exist because of geodesic drift: holding a heading at 58 m/s on a
    /// 1200 m world rotates the ground under you at v/R = 2.8 degrees a second,
    /// so after ten seconds of doing nothing wrong the nose sits 28 degrees
    /// high relative to where level now is. Auto-levelling the hull is ruled
    /// out, so the answer is to make the drift legible -- the number tells you
    /// to nudge down before the view stops making sense.
    /// </summary>
    private void UpdateFlightHud(bool boosting)
    {
        if (_hud == null || !IsPiloted)
        {
            return;
        }

        Vector3 gravity = Rig.GetGravity();

        // Gravity's own up where there is a well; the radial otherwise, which
        // is the best available guess while drifting between fields.
        Vector3 up = gravity.LengthSquared() > 1e-6f
            ? -gravity.Normalized()
            : Rig.GlobalPosition.Normalized();

        Vector3 forward = -Rig.GlobalBasis.Z.Normalized();
        Vector3 right = Rig.GlobalBasis.X.Normalized();

        float pitch = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(forward.Dot(up), -1.0f, 1.0f)));
        float roll = -Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(right.Dot(up), -1.0f, 1.0f)));

        _hud.SetFlightState(
            Rig.Velocity.Length(),
            boosting,
            Rig.Mode,
            Rig.HoverHolding,
            Rig.GroundDistance,
            pitch,
            roll);
    }

    // ---------------- Boarding ----------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("interact"))
        {
            if (IsPiloted)
            {
                Disembark();
                GetViewport().SetInputAsHandled();
            }
            else if (_playerInRange && _player != null)
            {
                Board();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    /// <summary>Boards from anywhere, for a test harness. A no-op when already piloted or with no player.</summary>
    public void BoardNow()
    {
        if (!IsPiloted && _player != null)
        {
            Board();
        }
    }

    public bool CanRide => Rig != null;

    public bool IsRidden => IsPiloted;

    /// <summary>Gets off from anywhere, for a test harness.</summary>
    public void LeaveNow()
    {
        if (IsPiloted)
        {
            Disembark();
        }
    }

    private void Board()
    {
        IsPiloted = true;
        SelectedWeapon = ShipWeapon.Gun;
        _weaponText = null;
        _podRemaining = Stats.PodReserve;
        _bombRemaining = Stats.BombReserve;
        _minigunRemaining = Stats.MinigunReserve;
        _minigunQuietTimer = 0.0f;
        Rig.SetActive(true);
        _player.SetStowed(true);

        AudioDirector.PlayAt(Sfx.ShipBoard, Rig.GlobalPosition);
        _engineLoop ??= AudioDirector.Loop(Sfx.ShipEngineLoop, Rig);

        EmitSignal(SignalName.PilotedChanged, true);
        UpdatePrompt();
        _hud.SetFlightVisible(true);
        UpdateFlightHud(false);
    }

    private void Disembark()
    {
        IsPiloted = false;
        Rig.SetActive(false);
        _beamTimer = 0.0f;
        SetBeamVisible(false);

        if (_player != null)
        {
            if (ExitPoint != null)
            {
                _player.GlobalTransform = ExitPoint.GlobalTransform;
            }
            _player.SetStowed(false);
        }

        AudioDirector.PlayAt(Sfx.ShipDisembark, Rig.GlobalPosition);
        _engineLoop?.Stop();
        _engineLoop = null;

        EmitSignal(SignalName.PilotedChanged, false);
        UpdatePrompt();
        _hud.SetFlightVisible(false);
    }

    // ---------------- Cannon ----------------

    /// <summary>
    /// Gravity's own up where there is a well, the hull's otherwise. Every
    /// ordnance launch needs this and none of them should guess at it.
    /// </summary>
    private Vector3 LocalUp()
    {
        Vector3 gravity = Rig.GetGravity();
        return gravity.LengthSquared() > 1e-6f ? -gravity.Normalized() : Rig.GlobalBasis.Y;
    }

    /// <summary>
    /// A salvo of the same <see cref="Rocket"/> the bazooka fires, launched with
    /// the bike's own velocity folded in -- without that, a 120 m/s muzzle speed
    /// against a bike doing 104 still puts the warhead uncomfortably close to
    /// the thing that fired it.
    ///
    /// Aimed from the camera, like the cannon and like every weapon here: a
    /// muzzle-origin shot and a centre-screen crosshair disagree with each other.
    /// </summary>
    private void FirePod()
    {
        if (_podCooldown > 0.0f || Camera == null
            || Stats.PodOrdnance == null || RocketScene == null)
        {
            return;
        }

        if (Stats.PodReserve > 0 && _podRemaining <= 0)
        {
            return;
        }

        _podCooldown = Stats.PodFireInterval;
        if (Stats.PodReserve > 0)
        {
            _podRemaining--;
        }

        Node3D muzzle = (_podAlternate ? PodMuzzleStarboard : PodMuzzlePort) ?? MuzzlePoint;
        _podAlternate = !_podAlternate;

        Rocket rocket = RocketScene.Instantiate<Rocket>();
        (GetTree().CurrentScene ?? (Node)this).AddChild(rocket);
        rocket.Launch(
            Stats.PodOrdnance,
            Rig,
            muzzle.GlobalPosition,
            -Camera.GlobalBasis.Z.Normalized(),
            LocalUp(),
            Rig.GetGravity(),
            Rig.GetRid(),
            Rig.Velocity);
    }

    /// <summary>
    /// Released rather than thrown: the bike's own velocity plus a nudge clear
    /// of the hull, so the bomb falls along the arc you were already flying and
    /// leading a target is a matter of where you were going.
    /// </summary>
    private void ReleaseBomb()
    {
        if (_bombCooldown > 0.0f || Stats.BombOrdnance == null || BombScene == null)
        {
            return;
        }

        if (Stats.BombReserve > 0 && _bombRemaining <= 0)
        {
            return;
        }

        _bombCooldown = Stats.BombInterval;
        if (Stats.BombReserve > 0)
        {
            _bombRemaining--;
        }

        Vector3 up = LocalUp();
        Node3D point = BombPoint ?? MuzzlePoint;

        Grenade bomb = BombScene.Instantiate<Grenade>();
        (GetTree().CurrentScene ?? (Node)this).AddChild(bomb);
        bomb.Launch(
            Stats.BombOrdnance,
            point.GlobalPosition,
            Rig.Velocity - (up * Stats.BombReleaseSpeed),
            Stats.BombOrdnance.FuseTime,
            Rig,
            up);
    }

    // ---------------- Minigun ----------------

    /// <summary>
    /// Rapid hitscan with spread and a magazine -- deliberately a different
    /// weapon from the cannon rather than a change to it. TickCannon's own
    /// decision comment explains why the beam stays a single continuous ray;
    /// this is the "fast repeater" the beam was written specifically not to
    /// be, added alongside it rather than in place of it.
    ///
    /// One shot per call, resolved instantly like every hand-held
    /// HitscanWeapon's own shot -- there is no per-tick rate math here the way
    /// the beam and the flamethrower need, because a single hitscan shot is
    /// already frame-rate independent.
    /// </summary>
    private void FireMinigun()
    {
        if (_minigunCooldown > 0.0f || Camera == null)
        {
            return;
        }

        if (Stats.MinigunReserve > 0 && _minigunRemaining <= 0)
        {
            return;
        }

        _minigunCooldown = Stats.MinigunFireInterval;
        _minigunQuietTimer = 0.0f;
        if (Stats.MinigunReserve > 0)
        {
            _minigunRemaining--;
        }

        Node3D muzzle = (_minigunAlternate ? MinigunMuzzleStarboard : MinigunMuzzlePort) ?? MuzzlePoint;
        _minigunAlternate = !_minigunAlternate;

        Vector3 origin = Camera.GlobalPosition;
        Vector3 forward = -Camera.GlobalBasis.Z.Normalized();

        // Same per-shot spread roll WeaponStats' pellets already use: rolled
        // once per shot around the aim direction, not accumulated.
        float spreadRad = Mathf.DegToRad(Stats.MinigunSpreadDegrees);
        Vector3 direction = spreadRad > 0.0f
            ? forward.Rotated(RandomPerpendicular(forward), (float)GD.RandRange(0.0, spreadRad))
            : forward;

        Vector3 end = origin + (direction * Stats.MinigunRange);

        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, end);
        query.CollisionMask = GameLayers.HitscanMask;
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;

        Godot.Collections.Dictionary result = Rig.GetWorld3D().DirectSpaceState.IntersectRay(query);
        bool hit = result.Count > 0;
        Vector3 point = hit ? result["position"].AsVector3() : end;

        if (muzzle != null && TracerScene != null)
        {
            Tracer tracer = TracerScene.Instantiate<Tracer>();
            (GetTree().CurrentScene ?? (Node)this).AddChild(tracer);
            tracer.Launch(muzzle.GlobalPosition, point, MinigunTracerSpeed);
        }

        AudioDirector.PlayAt(Sfx.RifleFire, origin);

        if (!hit)
        {
            return;
        }

        Vector3 normal = result["normal"].AsVector3();

        DamageInfo info = new()
        {
            Amount = Stats.MinigunDamagePerShot,
            Position = point,
            Normal = normal,
            Direction = direction,
            Distance = origin.DistanceTo(point),
            Source = Rig,
            IsSplash = false,
        };

        switch (result["collider"].AsGodotObject())
        {
            case Hitbox hitbox:
                hitbox.TakeHit(info);
                break;
            case IDamageable damageable:
                damageable.ApplyDamage(info);
                break;
        }

        if (Stats.MinigunExplosive)
        {
            DetonateRound(point, normal);
        }
        else
        {
            SpawnImpact(point, normal);
        }
    }

    /// <summary>
    /// An explosive round: the direct hit above, then a small blast through the
    /// same <see cref="Blast"/> every rocket and grenade uses, so it reaches the
    /// whole horde over the bus, carves voxel bodies through IBlastSurface and
    /// throws loose debris, with no branch anywhere for who fired it. Its damage
    /// is a fraction of the pod's own warhead rather than a number of its own.
    ///
    /// No player impulse: the pilot is stowed, and a round going off against
    /// something at point blank should not shove the bike firing it.
    /// </summary>
    private void DetonateRound(Vector3 point, Vector3 normal)
    {
        float podDamage = Stats.PodOrdnance?.Damage ?? 260.0f;
        Vector3 centre = point + (normal * 0.1f);
        Vector3 up = LocalUp();

        // Only the drawn rounds dig, and each digs for every round since the
        // last one -- see MinigunCraterRadius.
        ulong now = Time.GetTicksMsec();
        bool draw = ExplosionScene != null
            && now - _lastRoundExplosionMs >= (ulong)(Stats.MinigunExplosionEffectInterval * 1000.0f);
        _roundsSinceDig++;
        float digDepth = 0.0f;
        if (draw)
        {
            digDepth = Mathf.Min(Stats.MinigunCraterDepthPerRound * _roundsSinceDig, Stats.MinigunCraterMaxDepth);
            _roundsSinceDig = 0;
        }

        Blast.Settings blast = new()
        {
            Centre = centre,
            InnerRadius = Stats.MinigunBlastInnerRadius,
            Radius = Stats.MinigunBlastRadius,
            Damage = podDamage * Stats.MinigunBlastDamageFraction,
            MinMultiplier = Stats.PodOrdnance?.MinMultiplier ?? 0.35f,
            Impulse = Stats.MinigunBlastImpulse,
            ImpulseUpBias = 0.3f,
            Up = up,
            Source = Rig,
            SelfImpulseScale = 0.0f,
            CraterRadius = digDepth > 0.0f ? Stats.MinigunCraterRadius : 0.0f,
            CraterDepth = digDepth,
        };

        PhysicsDirectSpaceState3D space = Rig.GetWorld3D().DirectSpaceState;
        Blast.ApplyDamage(space, blast);
        Blast.ApplyToRigidBodies(space, blast, 1.0f);

        // Drawn at most once per interval; the rounds in between still dealt
        // everything above and show the ordinary impact flash instead.
        if (!draw)
        {
            SpawnImpact(point, normal);
            return;
        }

        _lastRoundExplosionMs = now;
        Node3D explosion = ExplosionScene.Instantiate<Node3D>();
        if (explosion is ExplosionEffect effect)
        {
            // Before AddChild, for the reason Rocket.SpawnExplosion records.
            effect.ScorchSeconds = Stats.MinigunScorchSeconds;
            effect.Configure(centre, Stats.MinigunEffectRadius, up, normal);
        }
        (GetTree().CurrentScene ?? (Node)this).AddChild(explosion);
        explosion.GlobalPosition = centre;
    }

    /// <summary>No TracerStats to read here -- the bike has no per-shot
    /// resource the way a hand weapon's WeaponStats is, so this is a plain
    /// constant matching the hand weapons' own tracer speed.</summary>
    private const float MinigunTracerSpeed = 260.0f;

    /// <summary>Any axis perpendicular to <paramref name="forward"/> works for
    /// rolling a cone of spread around it; which one is arbitrary as long as it
    /// is not parallel.</summary>
    private static Vector3 RandomPerpendicular(Vector3 forward)
    {
        Vector3 axis = Mathf.Abs(forward.Dot(Vector3.Up)) > 0.99f ? Vector3.Right : Vector3.Up;
        return forward.Cross(axis).Normalized().Rotated(forward, (float)GD.RandRange(0.0, Mathf.Tau));
    }

    private void TickMinigun(float dt, bool firing)
    {
        if (_minigunCooldown > 0.0f)
        {
            _minigunCooldown -= dt;
        }

        if (firing)
        {
            FireMinigun();
            return;
        }

        // Refills only after a quiet spell, same rule FlameWeapon's fuel uses:
        // otherwise tapping the trigger behaves identically to holding it.
        _minigunQuietTimer += dt;
        if (Stats.MinigunReserve > 0
            && _minigunQuietTimer >= Stats.MinigunRefillDelay
            && _minigunRemaining < Stats.MinigunReserve)
        {
            _minigunRemaining = Mathf.Min(
                Stats.MinigunReserve,
                _minigunRemaining + Mathf.CeilToInt(Stats.MinigunRefillPerSecond * dt));
        }
    }

    /// <summary>
    /// One beam down the nose, held rather than pulled, so "drive it" and
    /// "shoot from it" are both true and the pilot's job while firing is to
    /// keep the nose on something rather than to click at it.
    ///
    /// DECISION -- a continuous beam, not a fast repeater. The cannon was a
    /// hitscan on a 0.09 s cooldown, which is a machine gun in everything but
    /// name: fixed damage per shot, a cooldown to tune, and a tracer whose
    /// travel time argues with a weapon meant to be instant. It is a rate now,
    /// resolved on a timer the same way FlameWeapon's cone is -- damage per
    /// tick is DamagePerSecond times the elapsed slice, so what a target takes
    /// does not change with frame rate or with CannonTickInterval. The beam
    /// mesh is redrawn every frame from the muzzle to whatever the ray found,
    /// so what you see is exactly what the cannon tested.
    ///
    /// It is a single ray, unlike the flamethrower's cone: a laser burns the
    /// first thing under the line and nothing else, and that difference is the
    /// whole reason to carry both.
    ///
    /// Routed through the same Hitbox / IDamageable / DamageInfo path every
    /// other weapon in the project uses, so the beam lights the HUD hitmarker
    /// and hurts the horde exactly like a bullet from the rifle does, with no
    /// branch anywhere in that path for who fired it.
    /// </summary>
    private void TickCannon(float dt, bool firing)
    {
        if (!firing || Camera == null)
        {
            _beamTimer = 0.0f;
            _meltTimer = 0.0f;
            _scorch?.Break();
            SetBeamVisible(false);
            return;
        }

        Vector3 origin = Camera.GlobalPosition;
        Vector3 direction = -Camera.GlobalBasis.Z.Normalized();
        Vector3 end = origin + (direction * Stats.CannonRange);
        _beamClock += dt;

        // A piercing beam looks straight through the horde's borrowed
        // colliders: the agents on the line are burned through the hash below,
        // and stopping on the first of them is what made it a pea-shooter.
        bool piercing = Stats.LaserPierceRadius > 0.0f;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(origin, end);
        query.CollisionMask = piercing
            ? GameLayers.Mask(GameLayers.World, GameLayers.Enemy, GameLayers.Prop, GameLayers.Hitbox)
            : GameLayers.HitscanMask;
        query.CollideWithAreas = true;
        query.CollideWithBodies = true;

        Godot.Collections.Dictionary result = Rig.GetWorld3D().DirectSpaceState.IntersectRay(query);
        bool hit = result.Count > 0;

        Vector3 point = hit ? result["position"].AsVector3() : end;
        Vector3 normal = hit ? result["normal"].AsVector3() : -direction;

        // Drawn every frame, damage resolved on the tick below. The beam has
        // to track the nose continuously or it reads as a strobe.
        ShowBeam(point, hit);

        _beamTimer += dt;
        if (_beamTimer < Stats.CannonTickInterval)
        {
            return;
        }

        float slice = _beamTimer;
        _beamTimer = 0.0f;

        // Holding the beam is felt through the seat, and it pushes back.
        _shakeTrauma = Mathf.Max(_shakeTrauma, Stats.LaserShakeTrauma);
        Rig.Velocity -= direction * (Stats.LaserRecoil * slice);

        if (piercing)
        {
            float reach = Mathf.Min(origin.DistanceTo(point), Stats.LaserPierceRange);
            EventBus.EmitAreaBlast(new BlastInfo
            {
                Centre = origin,
                LineEnd = origin + (direction * reach),
                IsLine = true,
                InnerRadius = Stats.LaserPierceRadius,
                Radius = Stats.LaserPierceRadius,
                Damage = Stats.CannonDamagePerSecond * slice,
                MinMultiplier = 1.0f,
                Up = LocalUp(),
                Source = Rig,
                // Direct damage arriving continuously, as the flame's wash is:
                // no crater, no shake, no thrown ground for anyone else listening.
                Sustained = true,
                NoCrater = true,
            });
        }

        if (!hit)
        {
            _meltTimer = 0.0f;
            _scorch?.Break();
            return;
        }

        // Char on the ground only: a building's face is melted away under the
        // beam, and a mark there would hang in the hole.
        if (result["collider"].AsGodotObject() is StaticBody3D and not Voxel.VoxelCollider)
        {
            _scorch?.Mark(point, normal);
        }
        else
        {
            _scorch?.Break();
        }

        if (!Vaporize(point, normal))
        {
            SpawnImpact(point, normal);
        }
        MeltGround(point, slice);

        DamageInfo info = new()
        {
            Amount = Stats.CannonDamagePerSecond * slice,
            Position = point,
            Normal = normal,
            Direction = direction,
            Distance = origin.DistanceTo(point),
            Source = Rig,

            // Direct damage that happens to arrive continuously, not splash --
            // the horde's proxies drop splash on purpose, and this has to reach
            // them. Same call the flame cone makes, for the same reason.
            IsSplash = false,
            IsSustained = true,

            // Melts what it lands on whatever the damage number says, armour
            // included -- see HoverBikeStats.LaserCarveRadius.
            MeltRadius = Stats.LaserCarveRadius,
        };

        switch (result["collider"].AsGodotObject())
        {
            case Hitbox hitbox:
                hitbox.TakeHit(info);
                break;
            case IDamageable damageable:
                damageable.ApplyDamage(info);
                break;
        }
    }

    /// <summary>
    /// The beam's landing drawn as a blast: the same ExplosionEffect the bike
    /// gun's rounds go off as, once per LaserEffectInterval, and not Silent, so it
    /// is heard as well as seen -- the sound's own retrigger rules thin it.
    /// Returns false between bursts, when the ordinary impact flash stands in.
    /// </summary>
    private bool Vaporize(Vector3 point, Vector3 normal)
    {
        ulong now = Time.GetTicksMsec();
        if (ExplosionScene == null
            || now - _lastLaserBurstMs < (ulong)(Stats.LaserEffectInterval * 1000.0f))
        {
            return false;
        }

        _lastLaserBurstMs = now;
        Vector3 centre = point + (normal * 0.1f);
        Node3D explosion = ExplosionScene.Instantiate<Node3D>();
        if (explosion is ExplosionEffect effect)
        {
            // Before AddChild, for the reason Rocket.SpawnExplosion records.
            effect.ScorchSeconds = Stats.LaserScorchSeconds;
            effect.Configure(centre, Stats.LaserEffectRadius, LocalUp(), normal);
        }
        (GetTree().CurrentScene ?? (Node)this).AddChild(explosion);
        explosion.GlobalPosition = centre;
        return true;
    }

    private ulong _lastLaserBurstMs;

    /// <summary>The burned path the beam leaves on the ground.</summary>
    private ScorchTrail _scorch;

    /// <summary>
    /// The laser burns and melts what it holds on. Asked for outright: the
    /// bike's firepower is the point of it, every weapon on it but the laser
    /// should dig, and the laser should "burn and maybe melt the ground".
    ///
    /// Announced on the fourth bus signal as a blast with no damage and a
    /// radius too small for the horde, the debris or the camera shake to act
    /// on, carrying only the two fields this needs: a shallow crater with no
    /// rubble, and a fire zone. That is the one route from this file to the
    /// terrain and the fire code without naming either, and it is the route
    /// the incendiary round already takes. Craters apply by 3D distance, so a
    /// beam held on a building or an agent in the air melts nothing below it.
    /// </summary>
    private void MeltGround(Vector3 point, float slice)
    {
        _meltTimer += slice;
        _igniteTimer -= slice;

        if (_meltTimer < Stats.LaserMeltInterval)
        {
            return;
        }

        bool ignite = Stats.LaserFireSeconds > 0.0f && _igniteTimer <= 0.0f;
        if (ignite)
        {
            _igniteTimer = Stats.LaserIgniteInterval;
        }

        EventBus.EmitAreaBlast(new BlastInfo
        {
            Centre = point,
            Radius = 0.1f,
            Damage = 0.0f,
            Up = LocalUp(),
            Source = Rig,
            CraterRadius = Stats.LaserMeltRadius,
            CraterDepth = Stats.LaserMeltDepthPerSecond * _meltTimer,
            FireSeconds = ignite ? Stats.LaserFireSeconds : 0.0f,
        });

        _meltTimer = 0.0f;
    }

    /// <summary>
    /// Stretches the placeholder beam box from the muzzle to wherever the ray
    /// ended. A unit box scaled on Z, exactly as Tracer does it, so the mesh
    /// never has to be re-authored for a new range. Scale is assigned after
    /// LookAt because LookAt writes the whole basis.
    /// </summary>
    private void ShowBeam(Vector3 end, bool landed)
    {
        if (BeamVisual == null || MuzzlePoint == null)
        {
            return;
        }

        Vector3 from = MuzzlePoint.GlobalPosition;
        Vector3 delta = end - from;
        float length = delta.Length();

        if (length < 0.05f)
        {
            SetBeamVisible(false);
            return;
        }

        Vector3 direction = delta / length;
        Vector3 up = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;

        BeamVisual.Visible = true;
        BeamVisual.GlobalPosition = from + (delta * 0.5f);
        BeamVisual.LookAt(end, up);
        // A flicker on the width, so a held beam boils rather than sitting
        // there as a solid rod. Free: it is one scale write a frame.
        float flicker = 1.0f + (0.12f * Mathf.Sin(_beamClock * 53.0f)) + (0.08f * Mathf.Sin(_beamClock * 31.0f));
        float width = Stats.CannonBeamWidth * flicker;
        BeamVisual.Scale = new Vector3(width, width, length);

        if (_beamGlow != null)
        {
            _beamGlow.Scale = new Vector3(Stats.LaserGlowScale, 1.0f, Stats.LaserGlowScale);
        }

        // The molten spot and its light belong to something being burned; a
        // beam into the open sky has neither.
        if (_beamSpot != null && _beamSpot.IsInsideTree())
        {
            float spot = Stats.CannonBeamWidth * Stats.LaserGlowScale * 0.9f * flicker;
            _beamSpot.Visible = landed;
            _beamSpot.GlobalPosition = end;
            _beamSpot.Scale = new Vector3(spot, spot, spot);
        }

        if (_beamVapor != null && _beamVapor.IsInsideTree())
        {
            _beamVapor.Emitting = landed;
            if (landed)
            {
                Vector3 localUp = LocalUp();
                _beamVapor.GlobalTransform = new Transform3D(BasisAlongUp(localUp), end);

                // World-space particles fall along a world gravity vector, so
                // "rising" has to be told which way up is on this planet.
                _vaporProcess.Gravity = localUp * 4.0f;
            }
        }

        if (_beamLight != null && _beamLight.IsInsideTree())
        {
            _beamLight.Visible = landed && Stats.LaserLightRange > 0.0f;
            _beamLight.GlobalPosition = end - (direction * 1.2f);
            _beamLight.OmniRange = Stats.LaserLightRange;
            _beamLight.LightEnergy = 5.0f * flicker;
        }
    }

    private MeshInstance3D _beamGlow;
    private MeshInstance3D _beamSpot;
    private OmniLight3D _beamLight;
    private GpuParticles3D _beamVapor;
    private ParticleProcessMaterial _vaporProcess;
    private float _beamClock;

    private static Basis BasisAlongUp(Vector3 up)
    {
        Vector3 side = up.Cross(Vector3.Right);
        if (side.LengthSquared() < 1e-6f)
        {
            side = up.Cross(Vector3.Forward);
        }
        side = side.Normalized();
        return new Basis(side.Cross(up), up, -side);
    }

    /// <summary>
    /// The beam is built here rather than authored, because what it is made of
    /// -- a tapered core, a sheath, a molten spot and a light -- all derive
    /// from stats, and the scene's BeamVisual stays the one node the rest of
    /// this file already moves. Its own box mesh is swapped for cylinders laid
    /// along its Z, so the scale ShowBeam writes still means width, width,
    /// length.
    ///
    /// Tapered hard toward the muzzle -- to a few percent of the end width -- because the
    /// rider sits a metre behind it and looks straight down it: a full-width
    /// tube of additive light from there is a screen of glow, which is what
    /// the flamethrower's first cone was.
    /// </summary>
    private void BuildBeamVisual()
    {
        if (BeamVisual == null)
        {
            return;
        }

        BeamVisual.Mesh = null;
        BeamVisual.MaterialOverride = null;

        MakeBeamLayer("Core", new Color(1.0f, 0.97f, 0.85f, 1.0f), 1.0f, 0.06f);
        _beamGlow = MakeBeamLayer("Glow", new Color(1.0f, 0.45f, 0.12f, 0.3f), Stats.LaserGlowScale, 0.02f);

        // The spot and the light hang off the rig but ignore its transform:
        // BeamVisual is scaled by the beam's length, and anything under it
        // would be too. Added deferred, because this runs inside the rig's own
        // child setup, where add_child is refused -- and a node left out of
        // the tree logs an error on every GlobalPosition write, which at
        // sixty a second through the console was a 130 ms frame.
        _beamSpot = new MeshInstance3D
        {
            Name = "BeamSpot",
            TopLevel = true,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = BeamMaterial(new Color(1.0f, 0.75f, 0.35f, 0.8f)),
        };
        Rig.CallDeferred(Node.MethodName.AddChild, _beamSpot);

        _beamLight = new OmniLight3D
        {
            Name = "BeamLight",
            TopLevel = true,
            Visible = false,
            LightColor = new Color(1.0f, 0.62f, 0.3f),
            ShadowEnabled = false,
            OmniAttenuation = 1.5f,
        };
        Rig.CallDeferred(Node.MethodName.AddChild, _beamLight);

        _beamVapor = ToonFlame.Vapor(Stats.LaserVaporRadius);
        _beamVapor.TopLevel = true;
        _beamVapor.Emitting = false;
        _vaporProcess = (ParticleProcessMaterial)_beamVapor.ProcessMaterial;
        Rig.CallDeferred(Node.MethodName.AddChild, _beamVapor);
    }

    /// <param name="muzzleShare">Width at the muzzle as a share of the width
    /// where it lands. Small: the first cut at a fifth drew the sheath as an
    /// orange wedge over the bottom of the view, reported as "a burden".</param>
    private MeshInstance3D MakeBeamLayer(string name, Color colour, float widthScale, float muzzleShare)
    {
        // CylinderMesh runs along its own Y; a quarter turn about X lays it
        // along BeamVisual's Z with its top on -Z, the end LookAt points at
        // the target, so the wide end is the one that lands. Height 1, so the
        // parent's Z scale is the length.
        MeshInstance3D layer = new()
        {
            Name = name,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh
            {
                TopRadius = 0.5f,
                BottomRadius = 0.5f * muzzleShare,
                Height = 1.0f,
                RadialSegments = 10,
                Rings = 1,
                CapTop = false,
                CapBottom = false,
            },
            MaterialOverride = BeamMaterial(colour),
            Rotation = new Vector3(-Mathf.Pi * 0.5f, 0.0f, 0.0f),
            Scale = new Vector3(widthScale, 1.0f, widthScale),
        };
        BeamVisual.AddChild(layer);
        return layer;
    }

    private static StandardMaterial3D BeamMaterial(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        AlbedoColor = colour,
        DisableReceiveShadows = true,
    };

    /// <summary>
    /// The cannon's one on/off switch, visual and audio together -- the same
    /// arrangement FlameWeapon uses, and for the same reason: everything that
    /// stops the beam already goes through here, so nothing has to remember to
    /// stop the sound separately.
    /// </summary>
    private void SetBeamVisible(bool visible)
    {
        if (BeamVisual != null)
        {
            BeamVisual.Visible = visible;
        }

        if (!visible && _beamSpot != null)
        {
            _beamSpot.Visible = false;
            _beamLight.Visible = false;

            // Stops making vapor; what is already rising finishes rising.
            _beamVapor.Emitting = false;
        }

        if (visible == (_cannonLoop != null))
        {
            return;
        }

        if (visible)
        {
            // Followed on the rig rather than on this node: HoverBikeSystems
            // is a plain Node with no transform of its own, which is exactly
            // what keeps it a bridge rather than part of the vehicle.
            _cannonLoop = AudioDirector.Loop(Sfx.CannonLoop, Rig);
            return;
        }

        _cannonLoop?.Stop();
        _cannonLoop = null;
    }

    private SoundLoop _engineLoop;
    private SoundLoop _cannonLoop;

    /// <summary>
    /// The beam ticks twenty times a second, so this is a steady stream of
    /// instantiate-and-free rather than a one-off. Capped with every other
    /// impact in the game -- see ImpactEffect.MaxLive.
    /// </summary>
    private void SpawnImpact(Vector3 point, Vector3 normal) =>
        ImpactEffect.Spawn(ImpactScene, GetTree().CurrentScene, point, normal);

    // ---------------- Slots ----------------

    /// <summary>Formatted only when it would read differently, so a held
    /// trigger does not allocate a string a frame.</summary>
    private void UpdateWeaponHud()
    {
        if (_hud == null || !IsPiloted)
        {
            return;
        }

        string text = SelectedWeapon == ShipWeapon.Gun
            ? (Stats.MinigunReserve > 0
                ? $"[1] GUN  explosive  {_minigunRemaining}"
                : "[1] GUN  explosive")
            : "[2] LASER";

        if (text != _weaponText)
        {
            _weaponText = text;
            _hud.SetWeapon(text);
        }
    }

    // ---------------- Per-frame ----------------

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        if (_podCooldown > 0.0f)
        {
            _podCooldown -= dt;
        }

        if (_bombCooldown > 0.0f)
        {
            _bombCooldown -= dt;
        }

        bool boosting = false;
        bool laserFiring = false;
        bool gunFiring = false;

        if (IsPiloted)
        {
            boosting = Input.IsActionPressed("sprint");

            // Polled here rather than on the press event: the beam is held,
            // not clicked, and the pod repeats while the button is down. The
            // rifle gets this for free from HandleWeaponInput polling every
            // _Process; the bike has no equivalent loop of its own, so this
            // is it.
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                for (int slot = 0; slot < SlotActions.Length; slot++)
                {
                    if (Input.IsActionJustPressed(SlotActions[slot]))
                    {
                        SelectedWeapon = (ShipWeapon)slot;
                    }
                }

                bool trigger = Input.IsActionPressed("fire");
                laserFiring = trigger && SelectedWeapon == ShipWeapon.Laser;
                gunFiring = trigger && SelectedWeapon == ShipWeapon.Gun;

                // Both actions already exist and neither does anything else
                // while piloted, so the bike needs no new bindings.
                if (Input.IsActionPressed("alt_fire"))
                {
                    FirePod();
                }

                if (Input.IsActionJustPressed("grenade"))
                {
                    ReleaseBomb();
                }
            }
        }

        // Outside the IsPiloted branch on purpose: this is also what puts the
        // beam away when the pilot gets off or the pause menu takes the mouse.
        TickCannon(dt, laserFiring);
        TickMinigun(dt, gunFiring);
        TickShake(dt);

        UpdateFlightHud(boosting);
        UpdateWeaponHud();
    }

    // ---------------- Being hit ----------------

    /// <summary>
    /// Something hit the bike: flak, a spit, a thrown pickle. Nobody has health in
    /// this game's sandbox, so being hit is what it does to the flight -- a
    /// shove along the shot, a shaken camera and a flash -- and that is the
    /// whole cost. It is enough to make hovering in one place under fire hard,
    /// which is what an anti-air tower is for.
    /// </summary>
    public void ApplyHit(Vector3 impulse, float trauma)
    {
        if (Rig == null)
        {
            return;
        }

        Rig.Velocity += impulse;
        _shakeTrauma = Mathf.Min(1.0f, _shakeTrauma + trauma);
        _hud?.FlashHit(trauma);
        AudioDirector.PlayAt(Sfx.ShipImpact, Rig.GlobalPosition);
    }

    /// <summary>
    /// Puts the pod, the bomb bay and the gun back to full. They used to refill
    /// only on boarding, so the one way to rearm was to get off and on again.
    /// </summary>
    public void Rearm()
    {
        _podRemaining = Stats.PodReserve;
        _bombRemaining = Stats.BombReserve;
        _minigunRemaining = Stats.MinigunReserve;
        _minigunQuietTimer = 0.0f;
    }

    /// <summary>Bombs left in the bay. For a test harness's status line.</summary>
    public int BombsRemaining => _bombRemaining;

    private float _shakeTrauma;
    private Transform3D _cameraRest;
    private bool _cameraRestKnown;
    private ulong _shakeRandom = 0x2545F4914F6CDD1DUL;

    /// <summary>
    /// Shakes the seat camera by squared trauma, the CameraRig's own rule, so
    /// one hit settles and a burst builds. Rotation only, about the rest pose:
    /// the camera is never moved off its seat, which is load-bearing (see
    /// CLAUDE.md on the ship camera's placement).
    /// </summary>
    private void TickShake(float delta)
    {
        if (Camera == null)
        {
            return;
        }

        if (!_cameraRestKnown)
        {
            _cameraRest = Camera.Transform;
            _cameraRestKnown = true;
        }

        if (_shakeTrauma <= 0.0f)
        {
            return;
        }

        _shakeTrauma = Mathf.Max(0.0f, _shakeTrauma - (delta * ShakeDecay));
        float amount = _shakeTrauma * _shakeTrauma;

        Basis shake = Basis.FromEuler(new Vector3(
            ShakeRandom() * ShakePitch * amount,
            ShakeRandom() * ShakeYaw * amount,
            ShakeRandom() * ShakeRoll * amount));
        Camera.Transform = new Transform3D(_cameraRest.Basis * shake, _cameraRest.Origin);
    }

    private const float ShakeDecay = 2.2f;
    private const float ShakePitch = 0.05f;
    private const float ShakeYaw = 0.04f;
    private const float ShakeRoll = 0.09f;

    private float ShakeRandom()
    {
        _shakeRandom ^= _shakeRandom << 13;
        _shakeRandom ^= _shakeRandom >> 7;
        _shakeRandom ^= _shakeRandom << 17;
        return ((_shakeRandom >> 40) / (float)(1UL << 23)) - 1.0f;
    }
}
