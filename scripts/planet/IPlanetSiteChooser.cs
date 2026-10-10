namespace DillControl.Planet;

/// <summary>
/// Something that decides where the planet's sites go, once its ground
/// exists and before any of it is levelled or drawn: the compound placer.
/// Called by <see cref="PlanetTerrain.Build"/> between generating the
/// heightfield and levelling the sites' pads.
/// </summary>
public interface IPlanetSiteChooser
{
    /// <summary>The ground exists; move the sites and size their pads.</summary>
    void ChooseSites(PlanetHeightfield heights, ulong seed);

    /// <summary>The sites stand on their levelled ground; fill them.</summary>
    void SitesPlaced();
}
