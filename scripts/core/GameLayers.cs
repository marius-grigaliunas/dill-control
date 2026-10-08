namespace DillControl.Core;

/// <summary>
/// Physics layer indices, mirrored from the layer names configured in
/// project.godot (<c>layer_names/3d_physics/*</c>). Keep the two in sync by
/// hand -- Godot has no way to generate one from the other.
///
/// Use <see cref="Mask"/> to build query masks so no raw bit literals appear
/// in gameplay code.
/// </summary>
public static class GameLayers
{
    public const int World = 1;
    public const int Player = 2;
    public const int Enemy = 3;
    public const int Hitbox = 4;
    public const int Water = 5;
    public const int Prop = 6;
    public const int Projectile = 7;

    /// <summary>
    /// The horde's borrowed hit proxies. Their own layer so the queries that
    /// want an agent to be hittable -- bullets, the machete, the manipulator,
    /// thrown bodies -- can ask for them, and an explosion's overlap can leave
    /// them out: a blast reaches every agent over AreaBlast, and its overlap
    /// used to page through the nearest ninety-six agents' hitboxes and cast
    /// a ray to each before discarding them, 2-4 ms for one 1.6 m pellet
    /// blast in a crowd.
    /// </summary>
    public const int HordeHitbox = 8;

    /// <summary>Converts 1-based layer indices into a Godot collision mask.</summary>
    public static uint Mask(params int[] layers)
    {
        uint mask = 0u;
        foreach (int layer in layers)
        {
            mask |= 1u << (layer - 1);
        }
        return mask;
    }

    /// <summary>Everything a bullet should be able to stop against or register on.</summary>
    public static uint HitscanMask => Mask(World, Enemy, Prop, Hitbox, HordeHitbox);

    /// <summary>Every kind of hitbox, the horde's included.</summary>
    public static uint AnyHitboxMask => Mask(Hitbox, HordeHitbox);

    /// <summary>
    /// What something thrown or sprayed finds as a target: the explosion set
    /// plus the horde's hitboxes, because a thrown body or a flame cone hits
    /// an agent directly rather than through AreaBlast.
    /// </summary>
    public static uint DirectAreaMask => Mask(Enemy, Prop, Hitbox, HordeHitbox);

    /// <summary>
    /// What an explosion's overlap query looks for. World is deliberately
    /// absent: the sphere query finds victims, and a separate ray per victim
    /// finds the walls between them and the blast.
    /// </summary>
    public static uint ExplosionMask => Mask(Enemy, Prop, Hitbox);

    /// <summary>What a thrown projectile bounces off. Not the player, so a
    /// grenade at your own feet does not become something you trip over.</summary>
    public static uint ProjectileMask => Mask(World, Prop);
}
