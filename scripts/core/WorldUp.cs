using Godot;

namespace DillControl.Core;

/// <summary>
/// Which way is up at a point in the world, for code that has no body to ask
/// <c>GetGravity()</c> on: dust, rubble heaps, the ground ray under a falling
/// piece, a blast's up.
///
/// Straight up by default, which is the flats. A planet sets it to "away from
/// the world origin" while it exists (see <c>docs/decisions/0018</c>: the
/// contract planet sits at the origin) and clears it when it leaves. Bodies
/// keep reading <c>GetGravity()</c>; this is for the arithmetic around them.
/// </summary>
public static class WorldUp
{
    private static bool _radial;

    /// <summary>True while a planet centred on the world origin is present.</summary>
    public static bool Radial => _radial;

    /// <summary>Unit up at a world point.</summary>
    public static Vector3 At(Vector3 point)
    {
        if (!_radial)
        {
            return Vector3.Up;
        }
        float length = point.Length();
        return length > 0.001f ? point / length : Vector3.Up;
    }

    /// <summary>Gravity at a world point, at the project's default strength.</summary>
    public static Vector3 GravityAt(Vector3 point) =>
        -At(point) * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    /// <summary>
    /// Standing upright at a point: Y along up there, heading arbitrary but
    /// steady. Identity rotation on the flats, so nothing placed with it
    /// there moves from where it always stood.
    /// </summary>
    public static Transform3D StandingAt(Vector3 point)
    {
        Vector3 up = At(point);
        if (!_radial)
        {
            return new Transform3D(Basis.Identity, point);
        }
        Vector3 side = up.Cross(Mathf.Abs(up.Z) < 0.99f ? Vector3.Back : Vector3.Right).Normalized();
        return new Transform3D(new Basis(side, up, side.Cross(up)), point);
    }

    /// <summary>Called by the planet: up is away from the origin until <see cref="SetFlat"/>.</summary>
    public static void SetRadial() => _radial = true;

    /// <summary>Back to straight up.</summary>
    public static void SetFlat() => _radial = false;
}
