using Godot;
using DillControl.Horde;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// Everything a contract can be made of, in one place: the kinds of planet,
/// the looks, the compound kits and how bad the infestation is. The generator
/// picks one of each by the contract's seed and rolls nothing a designer did
/// not put here (<c>docs/decisions/0018</c>).
///
/// A pick is weighted when a weights array of the same length is given, even
/// otherwise.
/// </summary>
[GlobalClass]
public partial class ContractPools : Resource
{
    [Export]
    public Godot.Collections.Array<PlanetArchetype> Archetypes { get; set; } = new();

    [Export]
    public float[] ArchetypeWeights { get; set; } = System.Array.Empty<float>();

    [Export]
    public Godot.Collections.Array<PlanetBiome> Biomes { get; set; } = new();

    [Export]
    public float[] BiomeWeights { get; set; } = System.Array.Empty<float>();

    [Export]
    public Godot.Collections.Array<CompoundKit> Kits { get; set; } = new();

    [Export]
    public float[] KitWeights { get; set; } = System.Array.Empty<float>();

    [Export]
    public Godot.Collections.Array<HordeTier> Tiers { get; set; } = new();

    [Export]
    public float[] TierWeights { get; set; } = System.Array.Empty<float>();

    /// <summary>The contracts the ship's computer offers at once.</summary>
    [Export(PropertyHint.Range, "1,6,1")]
    public int OffersShown { get; set; } = 3;
}
