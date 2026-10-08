using System;
using System.Collections.Generic;
using Godot;
using DillControl.Audio;

namespace DillControl.Weapons;

/// <summary>
/// The single owner of "what is in my hands right now".
///
/// Fixed slots, no inventory: the slice's arsenal (docs/plan.md) is a rifle,
/// the machete, a launcher and a shotgun, with grenades thrown from a hand of
/// their own. Pickups, ammo types, dropping and a fifth slot are deliberately
/// not built. The slots are fixed [Export]s rather than a list the player can
/// add to: the set of weapons is decided in the editor and never at runtime.
/// An empty slot is skipped, so slot keys number whatever is filled.
///
/// It re-emits the active weapon's <see cref="Fired"/>, which is what lets the
/// player connect camera kick once and never rewire on a switch. It is also the
/// only thing that knows about the switch delay, so a weapon never has to model
/// being half-drawn.
/// </summary>
public partial class WeaponLoadout : Node3D
{
    [ExportGroup("Slots")]
    /// <summary>The rifle: the Brine Ripper.</summary>
    [Export]
    public WeaponBase Primary { get; set; }

    /// <summary>The machete.</summary>
    [Export]
    public WeaponBase Melee { get; set; }

    /// <summary>The rocket launcher: the Gherkin Volley.</summary>
    [Export]
    public WeaponBase Launcher { get; set; }

    /// <summary>The close-range gun: the Dill Hammer shotgun.</summary>
    [Export]
    public WeaponBase Sidearm { get; set; }

    /// <summary>
    /// The hand that throws. Not a slot: it borrows the hands and gives them
    /// back, and the loadout is what mediates that.
    /// </summary>
    [Export]
    public GrenadeThrower Thrower { get; set; }

    [ExportGroup("Switching")]
    /// <summary>
    /// Empty-handed time between putting one weapon away and raising the next.
    /// Not zero: a free switch makes reaching for the melee weapon costless,
    /// which is exactly the decision the melee slot exists to make interesting.
    /// </summary>
    [Export(PropertyHint.Range, "0,1.5,0.01,suffix:s")]
    public float SwitchTime { get; set; } = 0.25f;

    /// <summary>Re-emitted from whichever weapon is active. See class summary.</summary>
    [Signal]
    public delegate void FiredEventHandler(Vector2 recoilRadians, float recoverSpeed, float snappiness);

    /// <summary>Recoil as thrust, re-emitted from the active weapon.</summary>
    [Signal]
    public delegate void ThrustEventHandler(Vector3 impulse);

    [Signal]
    public delegate void ResuppliedEventHandler(float fraction);

    /// <summary>Re-emitted from whichever weapon is active, like Fired.</summary>
    [Signal]
    public delegate void AimStateChangedEventHandler(
        bool aiming, float fovScale, float transitionSpeed);

    [Signal]
    public delegate void ActiveSlotChangedEventHandler(int slot);

    /// <summary>
    /// The crate (and any other resupply) also refills the owner's gas tank.
    /// Assigned by the player; returning true means the crate actually gave
    /// something even if every magazine was already full.
    /// </summary>
    public Func<float, bool> TakeOwnerResupply { get; set; }

    /// <summary>The weapon in hand, or null mid-switch and while suppressed.</summary>
    public WeaponBase Active { get; private set; }

    public int ActiveSlot { get; private set; }

    /// <summary>The filled slots in key order: slot_1 is index 0. For the HUD.</summary>
    public IReadOnlyList<WeaponBase> Slots => _slots;

    /// <summary>True while the hands are empty between two weapons.</summary>
    public bool IsSwitching => _switchRemaining > 0.0f;

    private readonly List<WeaponBase> _slots = new();
    private int _previousSlot;
    private int _pendingSlot = -1;
    private float _switchRemaining;

    /// <summary>
    /// Set while something else owns the hands -- cooking a grenade, or riding
    /// a ship. The weapon is put away and comes back when the suppression lifts,
    /// which is the same path a switch takes.
    ///
    /// Two reasons, kept apart, because either one alone must hold the hands
    /// empty: a grenade's recovery ending while stowed used to lift the stow
    /// too, and the hand weapon fired out of the bike alongside its own gun.
    /// </summary>
    private bool _suppressed;
    private bool _handsBusy;
    private bool _stowed;
    private int _suppressedSlot = -1;

    public override void _Ready()
    {
        if (Primary != null)
        {
            _slots.Add(Primary);
        }
        if (Melee != null)
        {
            _slots.Add(Melee);
        }
        if (Launcher != null)
        {
            _slots.Add(Launcher);
        }
        if (Sidearm != null)
        {
            _slots.Add(Sidearm);
        }

        if (_slots.Count == 0)
        {
            GD.PushError("WeaponLoadout has no weapons assigned.");
            SetProcess(false);
            return;
        }

        foreach (WeaponBase weapon in _slots)
        {
            weapon.Fired += OnWeaponFired;
            weapon.Thrust += OnWeaponThrust;
            weapon.AimStateChanged += OnWeaponAimStateChanged;
            weapon.Holster();
        }

        if (Thrower != null)
        {
            Thrower.Fired += OnWeaponFired;
            Thrower.HandsBusy += OnHandsBusy;
        }

        // A group, because this node lives inside a scene instanced at
        // runtime and a rearm pod placed in a level cannot hold a path to it.
        AddToGroup(ResupplyGroupName);

        _previousSlot = _slots.Count > 1 ? 1 : 0;
        EquipImmediate(0);
    }

    public override void _ExitTree()
    {
        foreach (WeaponBase weapon in _slots)
        {
            weapon.Fired -= OnWeaponFired;
            weapon.Thrust -= OnWeaponThrust;
            weapon.AimStateChanged -= OnWeaponAimStateChanged;
        }

        if (Thrower != null)
        {
            Thrower.Fired -= OnWeaponFired;
            Thrower.HandsBusy -= OnHandsBusy;
        }
    }

    private void OnWeaponFired(Vector2 recoilRadians, float recoverSpeed, float snappiness)
    {
        EmitSignal(SignalName.Fired, recoilRadians, recoverSpeed, snappiness);
    }

    private void OnWeaponThrust(Vector3 impulse)
    {
        EmitSignal(SignalName.Thrust, impulse);
    }

    private void OnWeaponAimStateChanged(bool aiming, float fovScale, float transitionSpeed)
    {
        EmitSignal(SignalName.AimStateChanged, aiming, fovScale, transitionSpeed);
    }

    public override void _Process(double delta)
    {
        TickSwitch((float)delta);

        if (Input.MouseMode != Input.MouseModeEnum.Captured || _suppressed)
        {
            return;
        }

        // A table rather than a chain, so a slot's number is simply its
        // position in the list and adding one is adding one line.
        for (int slot = 0; slot < _slots.Count && slot < SlotActions.Length; slot++)
        {
            if (Input.IsActionJustPressed(SlotActions[slot]))
            {
                RequestSlot(slot);
                return;
            }
        }

        // _previousSlot means "the last slot I was on": the toggle is how you
        // carry two without walking the number row.
        if (Input.IsActionJustPressed("weapon_toggle"))
        {
            RequestSlot(_previousSlot);
            return;
        }

        // The wheel walks the slots in order and wraps, wheel up forward.
        // Ignored mid-switch by RequestSlot, so a fast spin lands one slot on
        // rather than skipping weapons the player never saw.
        if (_slots.Count > 1 && Input.IsActionJustPressed("weapon_next"))
        {
            RequestSlot((ActiveSlot + 1) % _slots.Count);
        }
        else if (_slots.Count > 1 && Input.IsActionJustPressed("weapon_prev"))
        {
            RequestSlot((ActiveSlot - 1 + _slots.Count) % _slots.Count);
        }
    }

    /// <summary>Slot keys in slot order. Must exist in the input map -- an
    /// unknown action name makes IsActionJustPressed push an engine error.</summary>
    private static readonly StringName[] SlotActions =
    {
        "slot_1", "slot_2", "slot_3", "slot_4", "slot_5",
    };

    private void TickSwitch(float dt)
    {
        // A cook holds the switch where it is; it resumes when the hands
        // come back.
        if (_switchRemaining <= 0.0f || _suppressed)
        {
            return;
        }

        _switchRemaining -= dt;
        if (_switchRemaining > 0.0f)
        {
            return;
        }

        _switchRemaining = 0.0f;
        if (_pendingSlot >= 0)
        {
            EquipImmediate(_pendingSlot);
            _pendingSlot = -1;
        }
    }

    /// <summary>
    /// Starts a switch to the given slot. Ignored for an out-of-range slot, for
    /// the slot already in hand, and while a switch is already running.
    /// </summary>
    public void RequestSlot(int slot)
    {
        if (slot < 0 || slot >= _slots.Count || slot == ActiveSlot || IsSwitching)
        {
            return;
        }

        _previousSlot = ActiveSlot;
        Active?.Holster();
        Active = null;

        // On the request rather than on the equip: the switch takes SwitchTime
        // and the sound is the confirmation that the request was heard, which
        // is worth nothing a third of a second late.
        AudioDirector.Play(Sfx.WeaponSwitch);

        if (SwitchTime <= 0.0f)
        {
            EquipImmediate(slot);
            return;
        }

        _pendingSlot = slot;
        _switchRemaining = SwitchTime;
    }

    private void EquipImmediate(int slot)
    {
        ActiveSlot = slot;
        Active = _slots[slot];
        Active.Equip();
        EmitSignal(SignalName.ActiveSlotChanged, slot);
    }

    /// <summary>
    /// Lowers the weapon while something else uses the hands, and raises it
    /// again when released. Used by the grenade thrower during a cook.
    /// </summary>
    /// <summary>
    /// Group name for anything that hands out ammunition. See Resupply below.
    /// </summary>
    public const string ResupplyGroupName = "dill_resupply";

    /// <summary>
    /// Refills every slot and the grenade carry, each as a fraction of its own
    /// full reserve.
    ///
    /// It is the loadout's job rather than each weapon's because a supply drop
    /// resupplies a player, not a gun, and the loadout is the only thing that
    /// knows what is in the hands -- including the holstered ones, which are the
    /// ones most likely to be empty.
    ///
    /// Returns true if any weapon actually took something, which is what lets a
    /// resupply point stay quiet when there is nothing to give.
    /// </summary>
    /// <summary>
    /// Hands every slot the player's earned upgrades. Whoever awards them
    /// reaches the loadout through <see cref="ResupplyGroupName"/>, the same
    /// way a crate does.
    /// </summary>
    public void ApplyBonus(PackageBonus bonus)
    {
        foreach (WeaponBase weapon in _slots)
        {
            weapon.Bonus = bonus ?? PackageBonus.None;
        }
    }

    public bool Resupply(float fraction)
    {
        bool given = false;

        foreach (WeaponBase weapon in _slots)
        {
            given |= weapon.Resupply(fraction);
        }

        if (Thrower != null)
        {
            given |= Thrower.Resupply(fraction);
        }

        given |= TakeOwnerResupply?.Invoke(fraction) == true;

        if (given)
        {
            Active?.PublishStatus();
            EmitSignal(SignalName.Resupplied, fraction);
        }

        return given;
    }

    private void OnHandsBusy(bool busy)
    {
        _handsBusy = busy;
        SuppressActive(_handsBusy || _stowed);
    }

    /// <summary>
    /// The owner is riding something. Empties the hands and stops the thrower,
    /// which is its own node and would otherwise still answer the grenade key --
    /// the bike's bomb bay is on the same key.
    /// </summary>
    public void SetStowed(bool stowed)
    {
        _stowed = stowed;
        Thrower?.SetProcess(!stowed && Thrower.Stats != null);
        SuppressActive(_handsBusy || _stowed);
    }

    private void SuppressActive(bool suppressed)
    {
        if (suppressed == _suppressed)
        {
            return;
        }

        _suppressed = suppressed;

        if (suppressed)
        {
            _suppressedSlot = ActiveSlot;
            Active?.Holster();
            Active = null;
            return;
        }

        if (_suppressedSlot >= 0 && _suppressedSlot < _slots.Count)
        {
            EquipImmediate(_suppressedSlot);
        }
        _suppressedSlot = -1;
    }

    /// <summary>
    /// Pushed down from the player each frame. Every slot gets it, not just the
    /// active one, so a weapon coming out of a holster already knows the stance
    /// it is being raised in.
    /// </summary>
    public void SetOwnerLocomotion(bool moving, bool airborne, bool crouched)
    {
        foreach (WeaponBase weapon in _slots)
        {
            weapon.SetOwnerLocomotion(moving, airborne, crouched);
        }
    }

    /// <summary>
    /// Which way is up where the owner is standing, pushed down each frame.
    /// Only the thrower wants it -- a hitscan shot and a swing are already
    /// camera-relative and do not care which way gravity points.
    /// </summary>
    public void SetOwnerUp(Vector3 up)
    {
        Thrower?.SetOwnerUp(up);
    }

    /// <summary>
    /// The owner's gravity frame, pushed to every slot. The thrower keeps its
    /// own narrower SetOwnerUp because it predates this and only ever wanted
    /// the direction.
    /// </summary>
    public void SetOwnerFrame(Vector3 up, Vector3 gravity)
    {
        foreach (WeaponBase weapon in _slots)
        {
            weapon.SetOwnerFrame(up, gravity);
        }
    }

    /// <summary>The owner's velocity, pushed to every slot on the same terms.</summary>
    public void SetOwnerVelocity(Vector3 velocity)
    {
        foreach (WeaponBase weapon in _slots)
        {
            weapon.SetOwnerVelocity(velocity);
        }
    }
}
