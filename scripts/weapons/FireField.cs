using DillControl.Core;
using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Lights the ground wherever a blast says it burns. Every explosive already
/// broadcasts one <see cref="EventBus.AreaBlast"/>, and a warhead that sets
/// fires carries <see cref="BlastInfo.FireSeconds"/>, so fire costs one listener
/// here and no change to anything that fires.
///
/// The fire half of the lab's SiteOrdnanceEffects. Its craters and coolant
/// floods need a terrain that can change shape, and come with the planet (M4).
/// <see cref="Levels.LevelRoot"/> adds one to every level.
/// </summary>
public partial class FireField : Node
{
    public const string DefaultStatsPath = "res://resources/weapons/fire_stats_default.tres";

    [Export]
    public FireStats Stats { get; set; }

    private int _zones;

    public override void _Ready()
    {
        Stats ??= GD.Load<FireStats>(DefaultStatsPath);
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast += OnAreaBlast;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.AreaBlast -= OnAreaBlast;
        }
    }

    private void OnAreaBlast(BlastInfo blast)
    {
        if (blast == null || blast.FireSeconds <= 0.0f || Stats == null || _zones >= Stats.MaxZones)
        {
            return;
        }

        var zone = new FireZone { Name = "FireZone" };
        AddChild(zone);
        Vector3 up = blast.Up.LengthSquared() > 0.0001f ? blast.Up : Vector3.Up;
        zone.Ignite(blast.Centre, up, Stats.Radius, blast.FireSeconds, Stats.DamagePerSecond);
        _zones++;
        zone.TreeExited += () => _zones = Mathf.Max(0, _zones - 1);
    }
}
