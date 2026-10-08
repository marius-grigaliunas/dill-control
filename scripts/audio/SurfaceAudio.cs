using Godot;
using DillControl.Core;

namespace DillControl.Audio;

/// <summary>
/// Picks the impact sound for whatever a shot, a swing or a bomb just hit.
///
/// It decides from the collision layer rather than from the type, which is the
/// point: a weapon must not need to name <c>VoxelBuilding</c> to know that a
/// building sounds like stone, and must not depend on the horde or voxel
/// systems at all. Layers are already the project's shared
/// vocabulary for what a thing is, and <c>GameLayers</c> already mirrors them.
///
/// The mapping is deliberately coarse -- flesh, stone, dirt -- because the
/// alternative is a material table, and a material table wants real recordings
/// behind it before it is worth having.
/// </summary>
public static class SurfaceAudio
{
    /// <summary>
    /// The impact sound for a collider. Hitboxes are flesh; anything on Prop is
    /// a built or dynamic object and reads as stone or metal; everything else
    /// is the ground.
    /// </summary>
    public static Sfx ImpactFor(GodotObject collider)
    {
        uint layer = LayerOf(collider);

        if (layer == 0)
        {
            return Sfx.ImpactDirt;
        }

        if ((layer & GameLayers.AnyHitboxMask) != 0 ||
            (layer & GameLayers.Mask(GameLayers.Enemy)) != 0)
        {
            return Sfx.ImpactFlesh;
        }

        // Prop is checked before World because a VoxelBuilding is on both, and
        // a wall should sound like a wall rather than like the hillside it
        // stands on.
        if ((layer & GameLayers.Mask(GameLayers.Prop)) != 0)
        {
            return Sfx.ImpactStone;
        }

        if ((layer & GameLayers.Mask(GameLayers.Water)) != 0)
        {
            return Sfx.ImpactWater;
        }

        return Sfx.ImpactDirt;
    }

    private static uint LayerOf(GodotObject collider)
    {
        return collider switch
        {
            CollisionObject3D body => body.CollisionLayer,
            _ => 0u,
        };
    }
}
