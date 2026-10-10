using System.Text;
using Godot;
using DillControl.Mission;
using DillControl.Planet;

namespace DillControl.Debug;

/// <summary>
/// Checks that generation is deterministic and the cube-sphere's seams are
/// sound, headless, in a few seconds: <c>seedcheck.gd</c> runs it. A contract
/// is a seed (<c>docs/decisions/0018</c>), so a seed that builds two
/// different planets is a broken contract.
/// </summary>
[GlobalClass]
public partial class SeedCheck : Node
{
    /// <summary>Runs every check over <paramref name="seeds"/> seeds and returns a report; the first line says PASS or FAIL.</summary>
    public string Run(int seeds)
    {
        var report = new StringBuilder();
        bool pass = true;

        foreach (int grid in new[] { 32, 64, 127 })
        {
            if (!CubeSphere.SelfTest(grid, out string failure))
            {
                pass = false;
                report.AppendLine($"seams at grid {grid}: {failure}");
            }
        }

        var archetype = GD.Load<PlanetArchetype>("res://resources/planet/planet_archetype_rock.tres");
        if (archetype == null)
        {
            return "FAIL\nno archetype at res://resources/planet/planet_archetype_rock.tres";
        }

        // Coarse heightfields: determinism does not depend on resolution, and
        // a full one is a quarter of a second each.
        ulong previous = 0;
        for (int seed = 1; seed <= seeds; seed++)
        {
            PlanetSpec a = archetype.Roll((ulong)seed);
            PlanetSpec b = archetype.Roll((ulong)seed);
            if (!SameSpec(a, b))
            {
                pass = false;
                report.AppendLine($"seed {seed}: the archetype rolled two different specs");
            }

            ulong first = Heights(a);
            ulong second = Heights(b);
            if (first != second)
            {
                pass = false;
                report.AppendLine($"seed {seed}: two different heightfields ({first:X16} and {second:X16})");
            }
            if (first == previous)
            {
                pass = false;
                report.AppendLine($"seed {seed}: the same heightfield as seed {seed - 1}");
            }
            previous = first;
            report.AppendLine(System.FormattableString.Invariant(
                $"seed {seed}: radius {a.Radius:F0} m, relief {a.Amplitude:F1} m, ridges {a.RidgeShare:F2}, {a.CraterCount} craters, heights {first:X16}"));
        }

        // The compound: the same seed puts it, and every cluster in it, in the
        // same place.
        var kit = GD.Load<CompoundKit>("res://resources/mission/kit_graybox_compound.tres");
        if (kit != null)
        {
            for (int seed = 1; seed <= Mathf.Min(seeds, 4); seed++)
            {
                PlanetSpec spec = archetype.Roll((ulong)seed);
                var heights = new PlanetHeightfield(spec.Radius, spec.Amplitude, 97, spec.DigFloorDepth);
                heights.Generate(spec);
                var first = new System.Collections.Generic.List<CompoundPlacer.ClusterPlacement>();
                var second = new System.Collections.Generic.List<CompoundPlacer.ClusterPlacement>();
                Vector3 a1 = CompoundPlacer.Layout(kit, heights, (ulong)seed, first, out float h1, out int d1);
                Vector3 a2 = CompoundPlacer.Layout(kit, heights, (ulong)seed, second, out float h2, out int d2);
                bool same = a1 == a2 && h1 == h2 && d1 == d2 && first.Count == second.Count;
                for (int i = 0; same && i < first.Count; i++)
                {
                    same = first[i] == second[i];
                }
                if (!same)
                {
                    pass = false;
                    report.AppendLine($"seed {seed}: two different compounds");
                }
                report.AppendLine(System.FormattableString.Invariant(
                    $"seed {seed} compound: {first.Count} clusters, {d1} left out, at ({a1.X:F3}, {a1.Y:F3}, {a1.Z:F3})"));
            }
        }

        return (pass ? "PASS" : "FAIL") + "\n" + report;
    }

    private static ulong Heights(PlanetSpec spec)
    {
        var heights = new PlanetHeightfield(spec.Radius, spec.Amplitude, 97, spec.DigFloorDepth);
        heights.Generate(spec);
        return heights.Fingerprint();
    }

    private static bool SameSpec(PlanetSpec a, PlanetSpec b) =>
        a.Radius == b.Radius && a.Amplitude == b.Amplitude && a.Seed == b.Seed && a.NoiseFrequency == b.NoiseFrequency
        && a.Octaves == b.Octaves && a.Gain == b.Gain && a.RidgeShare == b.RidgeShare && a.CraterCount == b.CraterCount
        && a.SurfaceGravity == b.SurfaceGravity;
}
