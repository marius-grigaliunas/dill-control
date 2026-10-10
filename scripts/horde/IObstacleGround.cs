using System;
using Godot;

namespace DillControl.Horde;

/// <summary>
/// A ground whose stamped obstacles can be asked again in one place, when
/// something standing there has changed: a building shot through or brought
/// down (<see cref="Core.EventBus.ObstaclesChanged"/>).
/// </summary>
public interface IObstacleGround
{
    /// <summary>
    /// Asks physics again, for every cell whose centre is within
    /// <paramref name="radius"/> of <paramref name="centre"/>, whether
    /// something that <paramref name="blocks"/> stands in it: newly found
    /// ones are blocked, and cells an obstacle blocked that now stand empty
    /// get their own weight back. What the ground itself blocks (a slope too
    /// steep) stays blocked.
    /// </summary>
    /// <returns>How many cells changed.</returns>
    int RestampObstacles(PhysicsDirectSpaceState3D space, FlowField flow, uint mask, Vector3 centre, float radius,
                         Func<GodotObject, bool> blocks);
}

/// <summary>The one physics question both grounds ask of a cell.</summary>
internal static class ObstacleQuery
{
    /// <summary>
    /// Whether anything that <paramref name="blocks"/> overlaps the query's
    /// box. Up to eight overlaps are looked at, so terrain, loose pieces and
    /// rubble in the same cell do not hide a wall behind them.
    /// </summary>
    public static bool Blocked(PhysicsDirectSpaceState3D space, PhysicsShapeQueryParameters3D query,
                               Func<GodotObject, bool> blocks)
    {
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, 8))
        {
            if (blocks(hit["collider"].AsGodotObject()))
            {
                return true;
            }
        }
        return false;
    }

    public static PhysicsShapeQueryParameters3D Box(float cellSize, uint mask, out BoxShape3D box)
    {
        box = new BoxShape3D { Size = new Vector3(cellSize * 0.9f, 1.6f, cellSize * 0.9f) };
        return new PhysicsShapeQueryParameters3D
        {
            Shape = box,
            CollisionMask = mask,
            CollideWithAreas = false,
            CollideWithBodies = true,
        };
    }
}
