using System.Collections.Generic;
using Godot;
using DillControl.Core;
using DillControl.Horde;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// Lays a compound out on the contract planet: picks the flattest of a few
/// seeded places for it, levels a pad there, and stands the kit's clusters on
/// the pad where the seed puts them. Sets the horde up from its tier, with
/// its garrison at the compound.
///
/// The seed decides where; the kit and the clusters in it, authored scenes,
/// decide what (<c>docs/decisions/0020</c>, extending 0004). Called by the
/// planet as its site chooser, between generating the ground and levelling
/// it, because the pad has to be in the ground before any of it is drawn.
/// </summary>
[GlobalClass]
public partial class CompoundPlacer : Node, IPlanetSiteChooser
{
    [ExportGroup("Data")]
    [Export]
    public CompoundKit Kit { get; set; }

    /// <summary>How bad the infestation is. Empty leaves the horde as authored.</summary>
    [Export]
    public HordeTier Tier { get; set; }

    [ExportGroup("Scene Wiring")]
    /// <summary>The site the compound stands on: moved, levelled and filled here.</summary>
    [Export]
    public PlanetSite Site { get; set; }

    /// <summary>The horde whose garrison is the compound.</summary>
    [Export]
    public HordeDirector Horde { get; set; }

    /// <summary>Where the compound went, from the planet's centre.</summary>
    public Vector3 Direction { get; private set; } = Vector3.Up;

    /// <summary>Every nest marker in the placed clusters, for the horde and the mission.</summary>
    public List<Marker3D> Nests { get; } = new();

    /// <summary>The arena marker of whichever cluster had one, or null.</summary>
    public Marker3D Arena { get; private set; }

    /// <summary>How many clusters were placed.</summary>
    public int PlacedCount { get; private set; }

    /// <summary>How many the kit asked for that did not fit.</summary>
    public int DroppedCount { get; private set; }

    /// <summary>One cluster's place on the pad: metres on the pad's plane, and a turn about its up.</summary>
    public readonly record struct ClusterPlacement(PackedScene Scene, Vector2 At, float Heading);

    private readonly List<ClusterPlacement> _plan = new();

    // ---------------- Where ----------------

    public void ChooseSites(PlanetHeightfield heights, ulong seed)
    {
        if (Kit == null || Site == null)
        {
            GD.PushError($"CompoundPlacer '{Name}' needs a CompoundKit and a PlanetSite; no compound.");
            return;
        }

        Direction = Layout(Kit, heights, seed, _plan, out float heading, out int dropped);
        DroppedCount = dropped;

        Vector3 east = Direction.Cross(Mathf.Abs(Direction.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 facing = east.Rotated(Direction, heading);
        Site.GlobalTransform = new Transform3D(PlanetHeightfield.UprightBasis(Direction, facing), Direction * heights.Radius);
        Site.PadRadius = Kit.PadRadius;
        Site.PadBlend = Kit.PadBlend;
        Site.KeepColliderRadius = Kit.PadRadius + 60.0f;

        SetUpHorde(heights);
    }

    /// <summary>
    /// Where a kit's compound goes on a planet for a seed, and where each of
    /// its clusters stands on the pad: pure, so a check can run it twice and
    /// compare. Returns the compound's direction from the planet's centre.
    /// </summary>
    public static Vector3 Layout(CompoundKit kit, PlanetHeightfield heights, ulong seed, List<ClusterPlacement> plan,
                                 out float heading, out int dropped)
    {
        SeededRandom random = SeededRandom.Stream(seed, "compound site");
        Vector3 direction = FlattestPlace(kit, heights, random);
        heading = random.Range(0.0f, Mathf.Tau);
        dropped = Plan(kit, SeededRandom.Stream(seed, "compound layout"), plan);
        return direction;
    }

    /// <summary>
    /// The site stands on its pad: the clusters go on it now, inside the
    /// planet's build, so they exist before the first physics frame, when the
    /// horde stamps what stands in its way. (Deferred, they arrived a frame
    /// late and the horde routed through them.)
    /// </summary>
    public void SitesPlaced() => Instance();

    /// <summary>
    /// The candidate with the least relief across where the pad would go:
    /// the centre and two rings of samples, scored by the spread of heights.
    /// Levelling a pad on a slope leaves a cliff at its edge; on near-flat
    /// ground it disappears.
    /// </summary>
    private static Vector3 FlattestPlace(CompoundKit kit, PlanetHeightfield heights, SeededRandom random)
    {
        Vector3 best = Vector3.Up;
        float bestScore = float.MaxValue;
        float reach = (kit.PadRadius + kit.PadBlend) / heights.Radius;

        for (int i = 0; i < Mathf.Max(1, kit.SiteCandidates); i++)
        {
            Vector3 candidate = random.OnSphere();
            Vector3 east = candidate.Cross(Mathf.Abs(candidate.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();

            float low = heights.SampleHeight(candidate);
            float high = low;
            for (int ring = 1; ring <= 2; ring++)
            {
                float arc = reach * ring * 0.5f;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 axis = east.Rotated(candidate, k * Mathf.Tau / 8.0f);
                    float h = heights.SampleHeight(candidate.Rotated(axis, arc));
                    low = Mathf.Min(low, h);
                    high = Mathf.Max(high, h);
                }
            }

            float score = high - low;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private void SetUpHorde(PlanetHeightfield heights)
    {
        if (Horde == null)
        {
            return;
        }

        Horde.GlobalPosition = Direction * (heights.Radius + heights.SampleHeight(Direction) + 2.0f);
        if (Tier == null)
        {
            return;
        }

        // A copy: the stats resource is shared with every level that uses
        // it, and a contract's count is this contract's.
        if (Horde.Stats != null)
        {
            HordeStats stats = (HordeStats)Horde.Stats.Duplicate();
            stats.AgentCount = Tier.AgentCount;
            stats.RespawnKilledAgents = Tier.RespawnKilledAgents;
            stats.AggroRange = Tier.AggroRange;
            stats.LeashRange = Tier.LeashRange;
            stats.RoamRadius = Tier.RoamRadius;
            stats.GarrisonSeedRadius = Mathf.Max(Tier.RoamRadius, 10.0f);
            Horde.Stats = stats;
        }
        if (Tier.Composition != null)
        {
            Horde.Composition = Tier.Composition;
        }
    }

    // ---------------- What ----------------

    /// <summary>
    /// Where each cluster goes on the pad, in metres on its plane. Entries in
    /// the kit's order; each copy tries random spots until its footprint
    /// clears every placed one by the kit's spacing, and is left out if none
    /// does. A dropped cluster is reported, never squeezed in.
    /// </summary>
    private static int Plan(CompoundKit kit, SeededRandom random, List<ClusterPlacement> plan)
    {
        plan.Clear();
        var placed = new List<(Vector2 At, float Radius)>();
        int dropped = 0;

        foreach (CompoundEntry entry in kit.Entries)
        {
            if (entry?.Cluster == null)
            {
                continue;
            }

            float footprint = FootprintOf(entry.Cluster);
            int count = random.RangeInt(entry.MinCount, Mathf.Max(entry.MinCount, entry.MaxCount));
            for (int n = 0; n < count; n++)
            {
                bool fitted = false;
                for (int tries = 0; tries < kit.PlacementTries && !fitted; tries++)
                {
                    float reach = Mathf.Max(0.0f, kit.PadRadius - footprint);
                    float r = reach * Mathf.Sqrt(random.NextFloat());
                    float angle = random.Range(0.0f, Mathf.Tau);
                    Vector2 at = new(r * Mathf.Cos(angle), r * Mathf.Sin(angle));

                    fitted = true;
                    foreach ((Vector2 other, float otherRadius) in placed)
                    {
                        if (at.DistanceTo(other) < footprint + otherRadius + kit.Spacing)
                        {
                            fitted = false;
                            break;
                        }
                    }

                    if (fitted)
                    {
                        placed.Add((at, footprint));
                        plan.Add(new ClusterPlacement(entry.Cluster, at, random.Range(0.0f, Mathf.Tau)));
                    }
                }

                if (!fitted)
                {
                    dropped++;
                }
            }
        }
        return dropped;
    }

    private static readonly Dictionary<PackedScene, float> FootprintCache = new();

    /// <summary>A cluster scene's footprint, read once from an instance of it.</summary>
    private static float FootprintOf(PackedScene scene)
    {
        if (FootprintCache.TryGetValue(scene, out float radius))
        {
            return radius;
        }

        Node probe = scene.Instantiate();
        radius = probe is CompoundCluster cluster ? cluster.FootprintRadius : 20.0f;
        probe.Free();
        FootprintCache[scene] = radius;
        return radius;
    }

    private void Instance()
    {
        if (Kit == null || Site == null)
        {
            return;
        }

        Nests.Clear();
        Arena = null;
        PlacedCount = 0;

        foreach ((PackedScene scene, Vector2 at, float heading) in _plan)
        {
            Node3D node = scene.Instantiate<Node3D>();
            node.Transform = new Transform3D(Basis.FromEuler(new Vector3(0.0f, heading, 0.0f)), new Vector3(at.X, 0.0f, at.Y));
            Site.AddChild(node);
            PlacedCount++;

            if (node is CompoundCluster cluster)
            {
                foreach (Marker3D nest in cluster.Nests)
                {
                    if (nest != null)
                    {
                        Nests.Add(nest);
                    }
                }
                Arena ??= cluster.Arena;
            }
        }

        GD.Print(System.FormattableString.Invariant(
            $"[compound] {Kit.DisplayName}: {PlacedCount} clusters on a {Kit.PadRadius:F0} m pad, {DroppedCount} left out, {Nests.Count} nests{(Arena != null ? ", an arena" : "")}"));
    }
}
