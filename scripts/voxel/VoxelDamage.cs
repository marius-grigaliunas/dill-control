using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// The one rule for what a hit does to voxels, used by every voxel thing in
/// the game: a building, a mech, a loose chunk. Weapons do not know any of
/// them exist; they hit an <see cref="IDamageable"/>, and this decides where
/// the bite goes and how big it is.
///
/// - A direct hit marches its ray into the pieces and bites where it first
///   meets a voxel, so a round through a gap carves the wall behind.
/// - A blast bites at the point of the body nearest to it (Blast already
///   asked <see cref="IBlastSurface"/>), every piece within reach.
/// - The bite's radius grows with damage, in metres, the same for every
///   model; a melt widens it to the beam's own radius.
/// - Sustained damage (flame, beam) is banked and bites once enough has
///   built up, or a flamethrower would erase what it touched in a second.
/// </summary>
public static class VoxelDamage
{
    private static readonly ConditionalWeakTable<object, Bank> Banks = new();

    private sealed class Bank
    {
        public float Damage;
    }


    public static void Apply(DamageInfo info, IReadOnlyList<VoxelPart> parts, VoxelDestructionStats stats, Node3D target)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionDamage);
        if (info == null || parts.Count == 0)
        {
            return;
        }

        float amount = info.Amount;
        bool melt = info.MeltRadius > 0.0f;
        if (!info.IsSustained && !melt && stats.Armour > 0.0f)
        {
            amount -= stats.Armour;
            if (amount <= 0.0f)
            {
                Report(info, parts, target);
                return;
            }
        }

        float threshold = Mathf.Max(info.IsSustained ? stats.SustainedDamagePerCarve : 0.0f, stats.MinCarveDamage);
        if (threshold > 0.0f && !melt && (info.IsSustained || amount < threshold))
        {
            // Banked: sustained fire, or a hit too small to bite on its own.
            Bank bank = Banks.GetValue(target, _ => new Bank());
            bank.Damage += amount;
            if (bank.Damage < threshold)
            {
                Report(info, parts, target);
                return;
            }
            amount = bank.Damage;
            bank.Damage = 0.0f;
        }

        float radius = stats.CarveVolumePerDamage > 0.0f
            ? Mathf.Min(Mathf.Pow(amount * stats.CarveVolumePerDamage * 3.0f / (4.0f * Mathf.Pi), 1.0f / 3.0f), stats.CarveMax)
            : Mathf.Clamp(stats.CarveBase + amount * stats.CarvePerDamage, 0.0f, stats.CarveMax);
        if (info.MeltRadius > 0.0f)
        {
            radius = Mathf.Max(radius, info.MeltRadius);
        }

        Vector3 point = info.Position;
        Vector3 direction = info.Direction;
        bool direct = !info.IsSplash && direction.LengthSquared() > 1e-6f;
        if (direct)
        {
            // March in from just outside where the shot stopped; the body's
            // collider can be bigger than its voxels (a mech's hull).
            direction = direction.Normalized();
            Vector3 from = point - direction * 0.75f;
            float best = float.MaxValue;
            bool found = false;
            foreach (VoxelPart part in parts)
            {
                if (part.Raycast(from, direction, 6.0f, out Vector3 hit))
                {
                    float d = from.DistanceSquaredTo(hit);
                    if (d < best)
                    {
                        best = d;
                        point = hit;
                        found = true;
                    }
                }
            }
            if (!found)
            {
                Report(info, parts, target);
                return;
            }
            if (info.MeltRadius > 0.0f)
            {
                // A melt bores along the beam rather than pitting the surface.
                point += direction * (radius * 0.5f);
            }
        }

        // Every piece within reach takes the bite: a blast at a hip takes the
        // thigh and the body together.
        foreach (VoxelPart part in parts)
        {
            if (part.IsEmpty || part.NearestPoint(point).DistanceTo(point) > radius + part.Grid.VoxelSize)
            {
                continue;
            }
            part.Carve(point, Mathf.Max(radius, part.Grid.VoxelSize * 0.5f));
        }

        Report(info, parts, target);
    }

    /// <summary>The nearest point of any piece to a world point.</summary>
    public static Vector3 NearestPoint(IReadOnlyList<VoxelPart> parts, Vector3 from, Vector3 fallback)
    {
        float best = float.MaxValue;
        Vector3 nearest = fallback;
        foreach (VoxelPart part in parts)
        {
            if (part.IsEmpty)
            {
                continue;
            }
            Vector3 p = part.NearestPoint(from);
            float d = p.DistanceSquaredTo(from);
            if (d < best)
            {
                best = d;
                nearest = p;
            }
        }
        return nearest;
    }

    /// <summary>Health for the hit marker, and onto the bus, so shooting a
    /// wall reports like shooting a pickle.</summary>
    private static void Report(DamageInfo info, IReadOnlyList<VoxelPart> parts, Node3D target)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(VoxelPart.SectionReport);
        int solid = 0, initial = 0;
        foreach (VoxelPart part in parts)
        {
            solid += part.Grid.SolidCount;
            initial += part.Grid.InitialCount;
        }
        info.HealthFraction = initial > 0 ? (float)solid / initial : 0.0f;
        info.TargetPosition = target.GlobalPosition;
        EventBus.EmitDamageDealt(target, info);
    }
}
