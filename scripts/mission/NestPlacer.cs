using System;
using DillControl.Core;
using DillControl.Planet;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// Lays a planned nest (<see cref="NestLayout"/>) on the planet, as a site
/// chooser (<see cref="PlanetTerrain.MoreSiteChoosers"/>):
///
/// - its heart at the flattest of a few seeded places on the planet, judged
///   across the nest's own radius;
/// - each vine at its distance and bearing from the heart along the ground,
///   on a small levelled pad, standing on its own up, turned so the way it
///   leans points at the heart, its foot sunk into the pad;
/// - the player's spawn at the mouth of an avenue, facing in.
///
/// The planet is a sphere about the world origin (docs/decisions/0017).
/// </summary>
[GlobalClass]
public partial class NestPlacer : Node, IPlanetSiteChooser
{
    [Export] public NestKit Kit { get; set; }
    /// <summary>The player's spawn: a <see cref="PlanetSite"/> in the planet's sites.</summary>
    [Export] public PlanetSite Spawn { get; set; }

    /// <summary>The plan, from <see cref="NestBuilder"/>: waits for it if it is still growing.</summary>
    public Func<NestLayout> Layout { get; set; }

    public Vector3 Heart { get; private set; } = Vector3.Up;

    /// <summary>Each vine's place, world space, once the sites stand; in
    /// <see cref="NestLayout.Vines"/>' order.</summary>
    public Transform3D[] Places { get; private set; }

    private Vector3[] _directions;
    private NestLayout _layout;
    private PlanetHeightfield _heights;

    public void ChooseSites(PlanetHeightfield heights, ulong seed)
    {
        _heights = heights;
        _layout = Layout?.Invoke();
        if (_layout == null || Kit == null)
        {
            GD.PushError($"NestPlacer '{Name}' has no plan or no kit; no nest.");
            return;
        }

        SeededRandom random = SeededRandom.Stream(seed, "nest:heart");
        float reach = Mathf.Max(_layout.RadiusM + Kit.PadRadiusM, 50.0f);
        float best = float.MaxValue;
        for (int i = 0; i < Math.Max(1, Kit.HeartCandidates); i++)
        {
            Vector3 candidate = random.OnSphere();
            float spread = Spread(heights, candidate, reach);
            if (spread < best)
            {
                best = spread;
                Heart = candidate;
            }
        }

        Vector3 north = Tangent(Heart);
        _directions = new Vector3[_layout.Vines.Count];
        for (int i = 0; i < _layout.Vines.Count; i++)
        {
            NestLayout.Vine vine = _layout.Vines[i];
            _directions[i] = Along(Heart, north.Rotated(Heart, vine.Bearing), vine.RadiusM / heights.Radius);
            heights.FlattenPad(_directions[i], Kit.PadRadiusM, Kit.PadBlendM);
        }

        if (Spawn != null)
        {
            float bearing = _layout.Avenues.Length > 0 ? _layout.Avenues[0] : 0.0f;
            Vector3 at = Along(Heart, north.Rotated(Heart, bearing), (_layout.RadiusM + Kit.SpawnOutsideM) / heights.Radius);
            Spawn.GlobalTransform = new Transform3D(PlanetHeightfield.UprightBasis(at, (Heart - at).Slide(at)), at * heights.Radius);
        }
    }

    public void SitesPlaced()
    {
        if (_layout == null || _directions == null)
        {
            return;
        }
        Places = new Transform3D[_directions.Length];
        for (int i = 0; i < _directions.Length; i++)
        {
            Vector3 up = _directions[i];
            Vector3 toHeart = (Heart - up).Slide(up);
            if (toHeart.LengthSquared() < 1e-8f)
            {
                toHeart = Tangent(up);
            }
            // Its -Z at the heart, then turned about its up so its lean is.
            Basis facing = PlanetHeightfield.UprightBasis(up, toHeart.Normalized());
            Vector3 lean = _layout.Vines[i].Lean;
            float turn = lean.LengthSquared() > 1e-6f ? Mathf.Pi - Mathf.Atan2(lean.X, lean.Z) : 0.0f;
            Places[i] = new Transform3D(facing * new Basis(Vector3.Up, turn),
                _heights.SurfacePoint(up) - (up * Kit.SinkM));
        }
    }

    // A direction on the ground at right angles to up: where bearings count from.
    private static Vector3 Tangent(Vector3 up) =>
        up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();

    // From a direction, an arc of the planet along a tangent.
    private static Vector3 Along(Vector3 from, Vector3 tangent, float arc)
    {
        Vector3 axis = from.Cross(tangent);
        return axis.LengthSquared() < 1e-8f ? from : from.Rotated(axis.Normalized(), arc).Normalized();
    }

    // How uneven the ground is round a direction: high less low, two rings out.
    private static float Spread(PlanetHeightfield heights, Vector3 centre, float reach)
    {
        Vector3 east = Tangent(centre);
        float low = heights.SampleHeight(centre), high = low;
        for (int ring = 1; ring <= 2; ring++)
        {
            for (int k = 0; k < 8; k++)
            {
                float h = heights.SampleHeight(Along(centre, east.Rotated(centre, k * Mathf.Tau / 8.0f), reach * ring * 0.5f / heights.Radius));
                low = Mathf.Min(low, h);
                high = Mathf.Max(high, h);
            }
        }
        return high - low;
    }
}
