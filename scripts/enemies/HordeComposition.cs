using Godot;

namespace DillControl.Enemies;

/// <summary>
/// What the horde is made of: one slot per behaviour, each one an
/// <see cref="AgentKindStats"/> carrying its own silhouette, stats, spawn
/// weights and share of the instance budget.
///
/// Five fixed <c>[Export]</c> slots rather than an array, for the reason
/// <c>WeaponLoadout</c> uses fixed slots rather than an inventory: the set is
/// decided in the editor and never added to at runtime. Here it is stronger
/// than that -- the design is locked at five pickles on one silhouette, and a
/// sixth slot would be a change to the lock rather than a change to a table.
///
/// An empty slot is simply a kind that never spawns and costs no buffer, which
/// is what makes "measure the standard alone" a matter of clearing four fields.
/// </summary>
[GlobalClass]
public partial class HordeComposition : Resource
{
    [ExportGroup("Kinds")]
    [Export]
    public AgentKindStats Standard { get; set; }

    [Export]
    public AgentKindStats Cornichon { get; set; }

    [Export]
    public AgentKindStats Thrower { get; set; }

    [Export]
    public AgentKindStats Spitter { get; set; }

    [Export]
    public AgentKindStats Tank { get; set; }

    /// <summary>
    /// Every slot in <see cref="AgentKind"/> order, empty ones included, so a
    /// caller can index by kind without asking which slots were filled.
    /// </summary>
    public AgentKindStats At(int kind) => kind switch
    {
        (int)AgentKind.Standard => Standard,
        (int)AgentKind.Cornichon => Cornichon,
        (int)AgentKind.Thrower => Thrower,
        (int)AgentKind.Spitter => Spitter,
        (int)AgentKind.Tank => Tank,
        _ => null,
    };

    /// <summary>How many slots exist, filled or not.</summary>
    public const int KindCount = 5;

    /// <summary>
    /// Picks a kind for a spawn. <paramref name="roll"/> is a 0..1 sample
    /// supplied by the caller, so this stays a pure function of the table and
    /// the horde keeps one generator.
    /// </summary>
    public int Pick(float roll)
    {
        float total = 0.0f;
        for (int i = 0; i < KindCount; i++)
        {
            total += WeightOf(i);
        }

        if (total <= 0.0f)
        {
            return (int)AgentKind.Standard;
        }

        float target = roll * total;
        float running = 0.0f;

        for (int i = 0; i < KindCount; i++)
        {
            running += WeightOf(i);
            if (target <= running)
            {
                return i;
            }
        }

        return (int)AgentKind.Standard;
    }

    /// <summary>This kind's spawn weight; zero for an empty slot.</summary>
    public float WeightOf(int kind)
    {
        AgentKindStats stats = At(kind);
        return stats != null ? Mathf.Max(0.0f, stats.SpawnWeight) : 0.0f;
    }

    /// <summary>How many instances of this kind the buffers make room for.</summary>
    public int CapacityFor(int kind, int totalCapacity)
    {
        AgentKindStats stats = At(kind);
        float share = stats != null ? Mathf.Clamp(stats.CapacityShare, 0.01f, 1.0f) : 0.02f;
        return Mathf.Max(1, Mathf.CeilToInt(totalCapacity * share));
    }
}
