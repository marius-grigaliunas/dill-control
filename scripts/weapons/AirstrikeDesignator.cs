using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// Points at a patch of ground and calls a salvo down onto it.
///
/// A <see cref="WeaponBase"/> like everything else in the hands, so it holsters,
/// publishes its remaining charges to the HUD ammo readout, and reports camera
/// kick through the same signal. What it fires is not a projectile but an
/// <see cref="Airstrike"/> node parented to the level, which then outlives this
/// weapon being put away.
///
/// The designating ray is allowed to miss. On a 400 m planet only chunks near
/// the player carry a collider, so a ray aimed past about forty metres hits
/// nothing at all -- and the designator's whole point is reaching further than
/// that. When the ray finds nothing it designates a point out at max range
/// instead, and the bombs sort out the real surface on the way down, because
/// each one sweeps for its own impact exactly as a fired rocket does.
/// </summary>
public partial class AirstrikeDesignator : WeaponBase
{
    [ExportGroup("Data")]
    [Export]
    public AirstrikeStats Stats { get; set; }

    /// <summary>
    /// What the company is selling. Left unassigned the designator calls
    /// exactly what it always called -- Stats and its own Ordnance -- so a
    /// level with no ordnance set behaves as it did before there were types.
    /// </summary>
    [Export]
    public OrdnanceSet Ordnance { get; set; }

    [ExportGroup("Effect Scenes")]
    /// <summary>The bomb. The same scene the launcher fires.</summary>
    [Export]
    public PackedScene BombScene { get; set; }

    private int _charges;
    private float _cooldown;
    private RocketStats _ordnance;
    private int _slot;
    private bool _windowOpen = true;

    /// <summary>
    /// Whether calls are rationed and gated at all.
    ///
    /// False -- the sandbox setting -- means charges never deplete and a shut
    /// orbit window never refuses. The window is still tracked and still
    /// displayed, because the ship crossing the sky is worth seeing; it just
    /// stops being a door. Set true to feel the real constraint for a few
    /// minutes, then set it back.
    /// </summary>
    [Export]
    public bool EnforceLimits { get; set; }

    /// <summary>
    /// Group the orbit tracker finds this weapon through.
    ///
    /// Joined rather than wired, because whatever is tracking the ship cannot
    /// hold an [Export] to a weapon inside a scene instanced at runtime -- the
    /// same problem, and the same already-justified answer, as the spike
    /// finding the tuning panel. The state is pushed in on the change, which is
    /// twice an orbit rather than sixty times a second.
    /// </summary>
    public const string OrbitGroupName = "orbit_window";

    /// <summary>
    /// A round was called: which slot, and what it is billed at. A local signal
    /// and not a bus one, because the ledger is a listener this weapon's owner
    /// can name.
    /// </summary>
    [Signal]
    public delegate void OrdnanceCalledEventHandler(int slot, float cost);

    /// <summary>Raised when a call was refused, and why, so the HUD can say so.</summary>
    [Signal]
    public delegate void CallRefusedEventHandler(string reason);

    /// <summary>Raised when the loaded round changes, for the HUD.</summary>
    [Signal]
    public delegate void OrdnanceChangedEventHandler(int slot, string displayName);

    public override bool UsesAmmo => true;

    public override string DisplayName => Stats?.DisplayName ?? "Designator";

    public override void _Ready()
    {
        base._Ready();

        if (Stats == null)
        {
            GD.PushError("AirstrikeDesignator has no AirstrikeStats assigned.");
            IsBroken = true;
            return;
        }

        _charges = Stats.Charges;
        _ordnance = Stats.Ordnance ?? DefaultOrdnance();
        _slot = Ordnance != null ? Ordnance.First() : 0;

        AddToGroup(OrbitGroupName);
    }

    /// <summary>
    /// What a bomb is when the level did not say. Heavier and wider than the
    /// shoulder-fired rocket, and with no direct-hit bonus -- a bomb that
    /// happened to land on a head should not be worth more than one that
    /// cratered next to it, because nobody aimed it that precisely.
    ///
    /// No longer what the shipped designator fires: wpn_strike_designator.tres
    /// carries wpn_strike_bomb.tres now, because these were balance numbers
    /// living in code, untouchable from the tuning panel and unchangeable
    /// without a rebuild. Kept as the fallback, so a level that forgets to
    /// assign ordnance still gets a bomb rather than a null reference.
    /// </summary>
    private static RocketStats DefaultOrdnance() => new()
    {
        DisplayName = "500lb",
        MuzzleSpeed = 34.0f,
        CruiseSpeed = 105.0f,
        Acceleration = 42.0f,
        GravityScale = 1.0f,
        MaxLifetime = 14.0f,
        DirectHitDamage = 0.0f,
        Damage = 420.0f,
        InnerRadius = 6.0f,
        BlastRadius = 19.0f,
        MinMultiplier = 0.35f,
        Impulse = 32.0f,
        ImpulseUpBias = 0.55f,
        SelfImpulseScale = 1.0f,
        DebrisImpulseScale = 4.0f,
    };

    protected override void TickWeapon(float dt)
    {
        if (_cooldown > 0.0f)
        {
            _cooldown = Mathf.Max(0.0f, _cooldown - dt);
        }
    }

    protected override void HandleWeaponInput()
    {
        if (Input.IsActionJustPressed("fire"))
        {
            Call();
        }

        // Right-click cycles the round. It is the only weapon in the loadout
        // with nothing bound to alt_fire -- a designator has no sights to aim
        // down -- so five ordnance types cost no new key at all, which the
        // letter budget demanded.
        if (Input.IsActionJustPressed("alt_fire"))
        {
            CycleOrdnance();
        }
    }

    /// <summary>
    /// Told by the orbit tracker, not asked. Twice an orbit rather than per
    /// frame, which is why this is a push and not a property read.
    /// </summary>
    public void SetOrbitWindow(bool open)
    {
        _windowOpen = open;
    }

    private void CycleOrdnance()
    {
        if (Ordnance == null)
        {
            return;
        }

        int next = Ordnance.Next(_slot);
        if (next == _slot)
        {
            return;
        }

        _slot = next;
        AudioDirector.Play(Sfx.UiMove);
        AddViewModelKick(0.012f, 0.6f);
        EmitSignal(SignalName.OrdnanceChanged, _slot, Loaded?.DisplayName ?? "--");
    }

    /// <summary>
    /// The shortest gap between calls when nothing is being rationed. Long
    /// enough that a held trigger cannot bury the physics step, short enough
    /// that it never reads as waiting.
    /// </summary>
    private const float SandboxCooldown = 0.35f;

    /// <summary>The round currently loaded, or null when no set is assigned.</summary>
    public OrdnanceEntry Loaded => Ordnance?.At(_slot);

    public int OrdnanceSlot => _slot;

    public int RemainingCharges => _charges;

    /// <summary>
    /// Restocks calls, for a supply order that arrived. Not capped at the
    /// stats' own Charges: buying more than you started with is the entire
    /// point of being able to buy any.
    /// </summary>
    public void AddCharges(int charges)
    {
        if (charges <= 0)
        {
            return;
        }

        _charges += charges;
        PublishStatus();
    }

    private void Call()
    {
        if (AimCamera == null || BombScene == null)
        {
            return;
        }

        if (_cooldown > 0.0f)
        {
            return;
        }

        // Both of these are the real game's constraints, and both are off by
        // default here. A toy you can only use for twenty seconds in every
        // hundred and ten is not a toy.
        if (EnforceLimits)
        {
            if (_charges <= 0)
            {
                EmitSignal(SignalName.CallRefused, "NO CHARGES");
                return;
            }

            if (!_windowOpen)
            {
                EmitSignal(SignalName.CallRefused, "NO ORBIT WINDOW");
                return;
            }
        }

        OrdnanceEntry entry = Loaded;
        AirstrikeStats profile = entry?.Profile ?? Stats;
        RocketStats warhead = entry?.Warhead ?? _ordnance;

        if (EnforceLimits)
        {
            _charges--;
        }

        // Still a cooldown, but a technical one rather than a cost: with no
        // gap at all a held trigger queues a salvo per frame and the physics
        // step disappears under bombs. Rate limit, not rationing.
        _cooldown = EnforceLimits ? Stats.Cooldown : SandboxCooldown;

        Airstrike strike = new() { Name = "Airstrike" };

        // Parented to the level, not to this weapon. A holster stops a weapon
        // processing, and a strike that could be cancelled by pressing 1 would
        // not be a commitment.
        GetTree().CurrentScene.AddChild(strike);

        strike.Begin(
            profile,
            warhead,
            BombScene,
            this,
            DesignatedPoint(),
            OwnerUp,
            OwnerGravity);

        // Billed against the payout rather than paid up front, which is what
        // makes every strike spent ore. Local signal: the ledger is a listener
        // this weapon's owner can name.
        EmitSignal(SignalName.OrdnanceCalled, _slot, entry?.Cost ?? 0.0f);

        AudioDirector.Play(Sfx.DesignatorMark);
        AddViewModelKick(0.04f, 2.0f);

        EmitSignal(
            SignalName.Fired,
            new Vector2(Mathf.DegToRad(Stats.KickPitchDegrees), 0.0f),
            Stats.KickRecoverSpeed,
            Stats.KickSnappiness);

        PublishStatus();
    }

    /// <summary>
    /// Where the salvo is going.
    ///
    /// The aim ray first: if it hits something, that is what was meant, and it
    /// works at any range against anything with a collider.
    ///
    /// A miss is the common case and it needs care. On a 400 m planet a ray
    /// cast level from eye height is nearly tangent -- it leaves over the limb
    /// and never comes back -- so simply using the far end of that ray puts the
    /// target tens of metres above the surface and a long way off, and the
    /// bombs released above *that* fly past everything into space. Nothing
    /// visibly happened, which is the worst way for a weapon to fail.
    ///
    /// So a miss walks a bounded distance along the aim flattened into the
    /// local tangent plane, then drops onto the ground with a short ray along
    /// gravity. Both steps use only <see cref="WeaponBase.OwnerUp"/> and
    /// <see cref="WeaponBase.OwnerGravity"/>, which are pushed in each frame --
    /// this class still has no idea it is standing on a sphere.
    ///
    /// The bounded distance matters: terrain colliders are streamed, so the
    /// drop ray only finds ground near the player. Past that the strike lands
    /// where the fallback says rather than where the ground is, which is why
    /// the walk is capped well inside the collider ring.
    /// </summary>
    private Vector3 DesignatedPoint()
    {
        Vector3 origin = AimCamera.GlobalPosition;
        Vector3 direction = -AimCamera.GlobalBasis.Z.Normalized();

        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        uint ground = GameLayers.Mask(GameLayers.World, GameLayers.Prop);

        PhysicsRayQueryParameters3D aim = PhysicsRayQueryParameters3D.Create(
            origin, origin + (direction * Stats.MaxRange));
        aim.CollisionMask = ground;
        aim.CollideWithAreas = false;
        aim.CollideWithBodies = true;

        Godot.Collections.Dictionary hit = space.IntersectRay(aim);
        if (hit.Count > 0)
        {
            return hit["position"].AsVector3();
        }

        Vector3 along = direction.Slide(OwnerUp);
        if (along.LengthSquared() < 1e-6f)
        {
            along = -AimCamera.GlobalBasis.Y.Slide(OwnerUp);
        }

        Vector3 ahead = origin + (along.Normalized() * FallbackDistance);

        // Drop onto whatever is under that, searching a band either side so an
        // uphill target is found as readily as a downhill one.
        Vector3 down = OwnerGravity.LengthSquared() > 0.0001f
            ? OwnerGravity.Normalized()
            : -OwnerUp;

        PhysicsRayQueryParameters3D drop = PhysicsRayQueryParameters3D.Create(
            ahead - (down * FallbackProbe),
            ahead + (down * FallbackProbe));
        drop.CollisionMask = ground;
        drop.CollideWithAreas = false;
        drop.CollideWithBodies = true;

        Godot.Collections.Dictionary landed = space.IntersectRay(drop);
        return landed.Count > 0 ? landed["position"].AsVector3() : ahead;
    }

    /// <summary>
    /// How far ahead a missed aim designates. Inside the streamed collider
    /// ring, so the drop ray below has ground to find.
    /// </summary>
    private const float FallbackDistance = 26.0f;

    /// <summary>How far either side of the fallback point the drop ray looks.</summary>
    private const float FallbackProbe = 30.0f;

    protected override void OnEquipped()
    {
        // Calling in a strike the instant the designator comes up would let a
        // weapon switch double as a trigger pull.
        _cooldown = Mathf.Max(_cooldown, 0.3f);
    }

    public override void PublishStatus()
    {
        // The charges go in the magazine field so the existing HUD readout
        // shows them without learning what an airstrike is.
        // Unlimited reads as a full magazine rather than as a number counting
        // down, so the HUD never suggests there is something to run out of.
        int magazine = Stats?.Charges ?? 0;
        EventBus.EmitAmmoChanged(EnforceLimits ? _charges : magazine, magazine, 0);
    }
}
