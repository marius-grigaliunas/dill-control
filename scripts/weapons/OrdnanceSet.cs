using Godot;

namespace DillControl.Weapons;

/// <summary>
/// What the company is selling today: the five ordnance types the designator
/// cycles between.
///
/// Five fixed <c>[Export]</c> slots rather than an array, on the same terms as
/// the loadout's weapon slots and the horde's six kinds -- the set is decided
/// in the editor, and here it is fixed by the feature lock besides. An empty
/// slot is simply a round the company is out of, and cycling skips it.
///
/// It lives in <c>scripts/weapons/</c> rather than with the rest of the site
/// because it is what a weapon in the player's hands is loaded with, and
/// because the designator would otherwise have to name the site while the site
/// already names the designator.
///
/// The five pull in different directions on purpose. Heavy high explosive
/// craters, which makes it a terrain tool as much as a weapon. Autocannon is
/// cheap and precise and does nothing to the ground. Cluster is wide and
/// shallow, good against a spread horde and useless against a wall. Incendiary
/// denies a lane without killing much. And the cheapest round the company
/// stocks is a coolant dump -- the exact water that created the alligators,
/// sold back as a weapon, which is the whole joke of the game compressed into
/// one line item.
/// </summary>
[GlobalClass]
public partial class OrdnanceSet : Resource
{
    [Export]
    public OrdnanceEntry HighExplosive { get; set; }

    [Export]
    public OrdnanceEntry Autocannon { get; set; }

    [Export]
    public OrdnanceEntry Cluster { get; set; }

    [Export]
    public OrdnanceEntry Incendiary { get; set; }

    [Export]
    public OrdnanceEntry CoolantDump { get; set; }

    public const int SlotCount = 5;

    public OrdnanceEntry At(int index) => index switch
    {
        0 => HighExplosive,
        1 => Autocannon,
        2 => Cluster,
        3 => Incendiary,
        4 => CoolantDump,
        _ => null,
    };

    /// <summary>
    /// The next filled slot after this one, wrapping. Returns the same index
    /// when nothing else is stocked, so cycling an empty set is a no-op rather
    /// than an infinite walk.
    /// </summary>
    public int Next(int index)
    {
        for (int step = 1; step <= SlotCount; step++)
        {
            int candidate = (index + step) % SlotCount;
            if (At(candidate) != null)
            {
                return candidate;
            }
        }

        return index;
    }

    /// <summary>The first filled slot, or zero if the set is empty.</summary>
    public int First()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (At(i) != null)
            {
                return i;
            }
        }

        return 0;
    }
}
