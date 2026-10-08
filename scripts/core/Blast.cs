using System.Collections.Generic;
using Godot;

namespace DillControl.Core;

/// <summary>
/// Reports where on a body an explosion should be measured to.
///
/// A blast measures distance to a point, and for anything the size of a
/// grenade's victims the body origin is close enough. It is not close enough
/// for a building: a wall's origin can be twenty metres from the corner the
/// grenade went off against, so the falloff reports zero and the blast does
/// nothing to a wall it is touching.
///
/// Implementers return the nearest point on their own solid surface. Anything
/// that does not implement this keeps the old behaviour and pays nothing.
/// </summary>
public interface IBlastSurface
{
    /// <summary>Nearest point of this body's solid volume to <paramref name="from"/>.</summary>
    Vector3 NearestBlastPoint(Vector3 from);
}

/// <summary>
/// Area damage, in one place.
///
/// Extracted from Grenade once a second and a third thing needed to explode.
/// The shape of it is the project's existing decision, unchanged: area damage
/// is <b>two queries, not one</b>. A sphere overlap finds who is near the
/// blast, and a separate ray per candidate decides whether the blast could
/// actually reach them. Overlap alone damages through walls, which is what
/// makes area damage feel arbitrary and makes cover worthless.
///
/// It also de-duplicates by <see cref="Hitbox.Receiver"/> and builds a
/// <b>fresh <see cref="DamageInfo"/> per target</b>, because one target carries
/// several hitboxes over one health pool and <see cref="Hitbox.TakeHit"/>
/// mutates what it is handed.
/// </summary>
public static class Blast
{
    /// <summary>
    /// Cap on colliders one blast will consider. Generous enough that nothing
    /// in range is missed in practice, and bounded so a blast in the middle of
    /// a horde cannot allocate without limit.
    /// </summary>
    private const int MaxResults = 96;

    /// <summary>
    /// Ray targets are pulled back by this much so a ray aimed at a surface
    /// does not end inside it and report the surface as its own cover.
    /// </summary>
    private const float LineOfSightMargin = 0.35f;

    /// <summary>Everything one explosion needs to know about itself.</summary>
    public readonly struct Settings
    {
        public Vector3 Centre { get; init; }

        /// <summary>Full damage inside this radius.</summary>
        public float InnerRadius { get; init; }

        /// <summary>Nothing at all beyond this radius.</summary>
        public float Radius { get; init; }

        public float Damage { get; init; }

        /// <summary>Damage share left at the very edge.</summary>
        public float MinMultiplier { get; init; }

        public float Impulse { get; init; }

        /// <summary>
        /// Bends the impulse toward local up, so a blast at your feet lifts you
        /// rather than only sliding you.
        /// </summary>
        public float ImpulseUpBias { get; init; }

        /// <summary>Local up, which on a planet is not world up.</summary>
        public Vector3 Up { get; init; }

        public Node3D Source { get; init; }

        /// <summary>
        /// The collider the blast came out of, when it came out of one. It is
        /// neither damaged by its own blast nor counted as cover against it.
        ///
        /// Default for every explosive that existed before this did: a grenade
        /// or a rocket is not a collider anyone stands behind. It exists for a
        /// source that is itself a large solid -- a body that slams the ground
        /// from inside its own hollow shell would otherwise occlude every
        /// target it had, and carve itself with every attack.
        /// </summary>
        public Rid SourceRid { get; init; }

        /// <summary>Scales the impulse applied to the player specifically.</summary>
        public float SelfImpulseScale { get; init; }

        // What this blast does to the world besides hurting what is in it.
        // All zero by default, which is what every explosive in the project was
        // before ordnance types existed. Blast itself does none of it -- it only
        // carries the numbers into the BlastInfo it already broadcasts, and
        // whoever owns the terrain acts on them. core/ does not get to know
        // that a planet exists.

        /// <summary>Crater radius to dig at the centre. Zero leaves the ground alone.</summary>
        public float CraterRadius { get; init; }

        /// <summary>How deep that crater goes.</summary>
        public float CraterDepth { get; init; }

        /// <summary>
        /// A burn resolved on a tick rather than a detonation: a fire zone
        /// reusing this path for its damage. It digs no crater, throws no
        /// ground and shakes no camera, which every listener that reacts to an
        /// *explosion* checks. See <see cref="BlastInfo.Sustained"/>.
        /// </summary>
        public bool Sustained { get; init; }

        /// <summary>Seconds of burning denial zone left behind.</summary>
        public float FireSeconds { get; init; }

        /// <summary>
        /// Digs no automatic crater. A blast big enough is otherwise given one
        /// sized from its own radius (see <c>SiteOrdnanceEffects</c>); an
        /// explicit <see cref="CraterRadius"/> is still honoured.
        /// </summary>
        public bool NoCrater { get; init; }

        /// <summary>Damage share at a given distance from the centre.</summary>
        public float MultiplierAt(float distance) => FalloffMultiplier(distance, InnerRadius, Radius, MinMultiplier);
    }

    /// <summary>
    /// The falloff curve itself, shared by <see cref="Settings.MultiplierAt"/>
    /// and <see cref="BlastInfo.MultiplierAt"/> -- the latter is a signal-safe
    /// mirror of a blast's geometry (see <see cref="EventBus.AreaBlast"/>) and
    /// needs the identical curve rather than a second copy that could drift.
    /// </summary>
    public static float FalloffMultiplier(float distance, float innerRadius, float radius, float minMultiplier)
    {
        if (distance <= innerRadius)
        {
            return 1.0f;
        }

        if (distance >= radius || radius <= innerRadius)
        {
            return distance >= radius ? 0.0f : 1.0f;
        }

        float t = (distance - innerRadius) / (radius - innerRadius);
        return Mathf.Lerp(1.0f, minMultiplier, t);
    }

    /// <summary>Damage everything in range that the blast can actually see.</summary>
    /// <returns>How many separate receivers were hurt.</returns>
    private static readonly int SectionEmit = FrameProfiler.Section("blast listeners");
    private static readonly int SectionOverlap = FrameProfiler.Section("blast overlap");

    public static int ApplyDamage(PhysicsDirectSpaceState3D space, in Settings blast)
    {
        // Broadcast once, here, rather than at every call site. Grenade,
        // Rocket and anything that calls ApplyDamage next all reach the horde
        // for free this way, and this is the one bus signal in the project
        // where the sender genuinely cannot name its listener: a weapon must
        // stay ignorant of the horde (docs/decisions/0006), so the dependency
        // can only run the other way -- Blast broadcasts, HordeManager listens.
        using (FrameProfiler.Begin(SectionEmit))
        {
            EventBus.EmitAreaBlast(BlastInfo.From(blast));
        }

        // One entry per receiver, not per hitbox: a target carries several
        // hitboxes over one health pool, and the blast is one event.
        Dictionary<IDamageable, Candidate> best = new();

        List<GodotObject> overlaps;
        using (FrameProfiler.Begin(SectionOverlap))
        {
            overlaps = Overlaps(space, blast, GameLayers.ExplosionMask, true);
        }

        foreach (GodotObject collider in overlaps)
        {
            IDamageable receiver = collider switch
            {
                Hitbox box => box.Receiver,
                IDamageable direct => direct,
                _ => null,
            };

            if (receiver == null || receiver is ISplashExempt || collider is not Node3D node)
            {
                continue;
            }

            if (blast.SourceRid.IsValid && collider is CollisionObject3D own && own.GetRid() == blast.SourceRid)
            {
                continue;
            }

            // A body that can say where its surface is gets asked. This is the
            // difference between a grenade cratering the wall it is lying
            // against and a grenade doing nothing to it.
            Vector3 point = receiver is IBlastSurface surface
                ? surface.NearestBlastPoint(blast.Centre)
                : node.GlobalPosition;

            float distance = blast.Centre.DistanceTo(point);
            float multiplier = blast.MultiplierAt(distance);
            if (multiplier <= 0.0f)
            {
                continue;
            }

            // Exclude the target's own collider from its own cover check. The
            // intent was always there, but it only held for hitboxes, which
            // sit on the Hitbox layer and so were never found by a World ray.
            // A destructible building is on World, so it occluded itself and
            // could never be damaged by a blast at all.
            Rid ignore = node is CollisionObject3D body ? body.GetRid() : default;
            if (!HasLineOfSight(space, blast.Centre, point, ignore, blast.SourceRid))
            {
                continue;
            }

            Candidate candidate = new(collider as Hitbox, point, distance, multiplier);
            if (!best.TryGetValue(receiver, out Candidate existing) || candidate.Score > existing.Score)
            {
                best[receiver] = candidate;
            }
        }

        foreach (KeyValuePair<IDamageable, Candidate> pair in best)
        {
            Apply(pair.Key, pair.Value, blast);
        }

        return best.Count;
    }

    /// <summary>Shove everything in range that implements <see cref="IBlastReceiver"/>.</summary>
    public static void ApplyImpulses(PhysicsDirectSpaceState3D space, in Settings blast, uint mask)
    {
        foreach (GodotObject collider in Overlaps(space, blast, mask, false))
        {
            if (collider is not IBlastReceiver receiver || collider is not Node3D node)
            {
                continue;
            }

            Vector3 point = node.GlobalPosition;
            float distance = blast.Centre.DistanceTo(point);
            float multiplier = blast.MultiplierAt(distance);

            Rid ignore = node is CollisionObject3D body ? body.GetRid() : default;
            if (multiplier <= 0.0f || !HasLineOfSight(space, blast.Centre, point, ignore, blast.SourceRid))
            {
                continue;
            }

            Vector3 outward = point - blast.Centre;
            outward = outward.LengthSquared() < 0.0001f ? blast.Up : outward.Normalized();
            Vector3 direction = outward.Lerp(blast.Up, blast.ImpulseUpBias).Normalized();

            receiver.ApplyBlastImpulse(
                direction * blast.Impulse * multiplier * blast.SelfImpulseScale);
        }
    }

    /// <summary>
    /// Shove loose rigid bodies. Separate from <see cref="ApplyImpulses"/>
    /// because a RigidBody3D takes a real impulse from the solver rather than
    /// implementing an interface, and because debris wants no line-of-sight
    /// check -- a blast that fails to scatter the rubble in front of it reads
    /// as a dud even when the rule is defensible.
    /// </summary>
    public static void ApplyToRigidBodies(PhysicsDirectSpaceState3D space, in Settings blast, float scale)
    {
        foreach (GodotObject collider in Overlaps(space, blast, GameLayers.Mask(GameLayers.Prop), false))
        {
            if (collider is not RigidBody3D body || body.Freeze)
            {
                continue;
            }

            Vector3 outward = body.GlobalPosition - blast.Centre;
            outward = outward.LengthSquared() < 0.0001f ? blast.Up : outward.Normalized();

            float multiplier = blast.MultiplierAt(blast.Centre.DistanceTo(body.GlobalPosition));
            if (multiplier <= 0.0f)
            {
                continue;
            }

            body.Sleeping = false;
            Vector3 direction = outward.Lerp(blast.Up, blast.ImpulseUpBias * 0.5f).Normalized();
            body.ApplyImpulse(direction * blast.Impulse * multiplier * scale * body.Mass);
        }
    }

    private static void Apply(IDamageable receiver, in Candidate hit, in Settings blast)
    {
        Vector3 direction = hit.Point - blast.Centre;
        direction = direction.LengthSquared() < 0.0001f
            ? blast.Up
            : direction.Normalized();

        // A fresh DamageInfo per target: Hitbox.TakeHit mutates what it is
        // handed, so one shared instance would compound multipliers across
        // everything the blast touched.
        DamageInfo info = new()
        {
            Amount = blast.Damage * hit.Multiplier,
            BaseAmount = blast.Damage,
            Position = hit.Point,
            Normal = -direction,
            Direction = direction,
            Distance = hit.Distance,
            Source = blast.Source,
            IsSplash = true,
        };

        if (hit.Box != null)
        {
            hit.Box.TakeHit(info);
        }
        else
        {
            receiver.ApplyDamage(info);
        }
    }

    private static List<GodotObject> Overlaps(
        PhysicsDirectSpaceState3D space, in Settings blast, uint mask, bool includeAreas)
    {
        PhysicsShapeQueryParameters3D query = new()
        {
            Shape = new SphereShape3D { Radius = blast.Radius },
            Transform = new Transform3D(Basis.Identity, blast.Centre),
            CollisionMask = mask,
            CollideWithAreas = includeAreas,  // Hitboxes are monitorable areas.
            CollideWithBodies = true,
        };

        return DistinctOverlaps(space, query, MaxResults);
    }

    /// <summary>Results asked for per pass of <see cref="DistinctOverlaps"/>.</summary>
    private const int OverlapBatch = 32;

    /// <summary>Bound on passes, so a pathological scene costs a fixed number of queries.</summary>
    private const int MaxOverlapPasses = 8;

    /// <summary>
    /// Every distinct collider a shape query overlaps, up to <paramref name="maxColliders"/>.
    ///
    /// Not <c>IntersectShape</c> on its own, because under Jolt that returns
    /// one result per *triangle* of a <c>ConcavePolygonShape3D</c>. A voxel
    /// building or a scatter boulder touching the query therefore filled every
    /// result slot with copies of itself, and whatever else stood in range --
    /// every agent's hitbox -- was never reported at all. That was the whole
    /// of "the flamethrower does absolutely no damage": measured, a cone full
    /// of agents at six metres returned 24 results and 24 of them were the
    /// same boulder. Each pass excludes what it has already found and asks
    /// again, so one concave body costs one extra query rather than the budget.
    /// </summary>
    public static List<GodotObject> DistinctOverlaps(
        PhysicsDirectSpaceState3D space, PhysicsShapeQueryParameters3D query, int maxColliders)
    {
        List<GodotObject> found = new();
        HashSet<ulong> seen = new();
        Godot.Collections.Array<Rid> exclude = new(query.Exclude);

        for (int pass = 0; pass < MaxOverlapPasses && found.Count < maxColliders; pass++)
        {
            query.Exclude = exclude;
            Godot.Collections.Array<Godot.Collections.Dictionary> results =
                space.IntersectShape(query, OverlapBatch);

            bool added = false;
            foreach (Godot.Collections.Dictionary result in results)
            {
                if (!result.TryGetValue("collider", out Variant collider)
                    || collider.AsGodotObject() is not GodotObject obj
                    || !seen.Add(obj.GetInstanceId()))
                {
                    continue;
                }

                found.Add(obj);
                added = true;
                if (result.TryGetValue("rid", out Variant rid))
                {
                    exclude.Add(rid.AsRid());
                }

                if (found.Count >= maxColliders)
                {
                    break;
                }
            }

            // A short page means the query has nothing left to report.
            if (!added || results.Count < OverlapBatch)
            {
                break;
            }
        }

        return found;
    }

    /// <summary>
    /// True when nothing solid stands between the blast and the target. This is
    /// what makes cover mean something -- without it, a grenade on the far side
    /// of a wall is as good as one at your feet.
    ///
    /// Public so <see cref="DillControl.Horde.HordeManager"/>'s own blast-damage pass
    /// -- which cannot use <see cref="ApplyDamage"/> because most agents have no
    /// collider to be found by its overlap query -- still respects cover rather
    /// than re-deriving this ray by hand.
    /// </summary>
    public static bool HasLineOfSight(
        PhysicsDirectSpaceState3D space, Vector3 centre, Vector3 target, Rid ignore = default, Rid alsoIgnore = default)
    {
        Vector3 toTarget = target - centre;
        float distance = toTarget.Length();
        if (distance <= LineOfSightMargin)
        {
            return true;
        }

        Vector3 direction = toTarget / distance;
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            centre,
            centre + (direction * (distance - LineOfSightMargin)));

        // World only. The target's own collider must not count as its cover,
        // and neither must the grenade or anything else standing in the blast.
        query.CollisionMask = GameLayers.Mask(GameLayers.World);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        if (ignore != default || alsoIgnore != default)
        {
            Godot.Collections.Array<Rid> exclude = new();
            if (ignore != default)
            {
                exclude.Add(ignore);
            }
            if (alsoIgnore != default)
            {
                exclude.Add(alsoIgnore);
            }
            query.Exclude = exclude;
        }

        return space.IntersectRay(query).Count == 0;
    }

    /// <summary>One receiver's best hit, before anything is applied.</summary>
    private readonly struct Candidate(Hitbox box, Vector3 point, float distance, float multiplier)
    {
        public Hitbox Box { get; } = box;
        public Vector3 Point { get; } = point;
        public float Distance { get; } = distance;
        public float Multiplier { get; } = multiplier;

        /// <summary>
        /// Which of two hits on the same receiver wins. The larger multiplier
        /// wins, so a blast that catches both a body and a head resolves as the
        /// head, matching what a direct hit would have done.
        /// </summary>
        public float Score => Multiplier * (Box?.DamageMultiplier ?? 1.0f);
    }
}
