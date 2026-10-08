using Godot;

namespace DillControl.Player;

/// <summary>
/// The player's health: drains, refills, and does not kill yet.
///
/// Empty is "pickled" until the mission has clones (M7): the count goes up,
/// the jar refills from nothing over
/// <see cref="PlayerHealthTuning.PickledRefillSeconds"/> with the player in
/// full control, and nothing can drain it until it is full. Death by cloning
/// replaces this, and the count is what it will read.
///
/// A node of its own rather than fields on <see cref="PlayerController"/>
/// because the controller switches its own processing off while stowed on the
/// bike, and the bike is where most of the lead shot lands. A child keeps
/// ticking; regen that froze for as long as you were flying would be a bug.
/// </summary>
public partial class PlayerHealth : Node
{
    public PlayerHealthTuning Tuning { get; set; }

    public bool Enabled => Tuning?.HealthEnabled ?? false;

    public float Max => Mathf.Max(1.0f, Tuning?.MaxHealth ?? 100.0f);

    public float Current { get; private set; } = float.NaN;

    public float Fraction => Mathf.Clamp(Current / Max, 0.0f, 1.0f);

    /// <summary>How many times health reached zero this session.</summary>
    public int TimesPickled { get; private set; }

    /// <summary>True from the moment health hits zero until it has refilled.</summary>
    public bool IsPickled { get; private set; }

    /// <summary>Real seconds since the last hit that cost something.</summary>
    public float SinceLastHit { get; private set; } = 999.0f;

    /// <summary>Size of the last hit, for the HUD's floating number.</summary>
    public float LastDamage { get; private set; }

    /// <summary>Bumps on every hit, so a reader can tell two equal hits apart.</summary>
    public int HitSerial { get; private set; }

    /// <summary>Total damage taken this session.</summary>
    public float DamageTaken { get; private set; }

    public override void _Ready()
    {
        if (float.IsNaN(Current))
        {
            Current = Max;
        }
    }

    /// <summary>
    /// Takes a hit. Ignored while pickled or disabled. Returns true if this hit
    /// is the one that emptied the jar.
    /// </summary>
    public bool TakeDamage(float amount)
    {
        if (!Enabled || IsPickled || amount <= 0.0f)
        {
            return false;
        }

        float before = Current;
        Current = Mathf.Max(0.0f, Current - amount);
        LastDamage = before - Current;
        DamageTaken += LastDamage;
        SinceLastHit = 0.0f;
        HitSerial++;

        if (Current > 0.0f)
        {
            return false;
        }

        IsPickled = true;
        TimesPickled++;
        return true;
    }

    /// <summary>Topped up to full, as the ammo crate does for gas.</summary>
    public void Refill()
    {
        Current = Max;
        IsPickled = false;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        SinceLastHit += dt;

        if (!Enabled)
        {
            Current = Max;
            IsPickled = false;
            return;
        }

        // A MaxHealth dragged down in the panel takes the pool with it.
        Current = Mathf.Min(Current, Max);

        if (IsPickled)
        {
            Current += Max * dt / Mathf.Max(0.05f, Tuning.PickledRefillSeconds);
            if (Current >= Max)
            {
                Current = Max;
                IsPickled = false;
            }
            return;
        }

        if (SinceLastHit >= Tuning.HealthRegenDelay && Current < Max)
        {
            Current = Mathf.Min(Max, Current + (Tuning.HealthRegenPerSecond * dt));
        }
    }
}
