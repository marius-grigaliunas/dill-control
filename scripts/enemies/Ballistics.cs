using Godot;

namespace DillControl.Enemies;

/// <summary>
/// Aim solutions for things that shoot or throw at a moving target: the
/// compound's towers, the spitter's brine and the thrower's cornichons.
///
/// Pure functions of their arguments, with randomness supplied by the caller,
/// so every shooter keeps its own generator and nothing here owns state. The
/// lob is solved in the launch point's local tangent frame, which on a
/// 1200 m planet over a 70 m throw is out by centimetres -- far less than the
/// scatter added on purpose.
/// </summary>
public static class Ballistics
{
    /// <summary>
    /// Where to aim a straight shot to meet a target moving at constant
    /// velocity. Falls back to aiming straight at it when no intercept exists
    /// -- a target outrunning the shot.
    /// </summary>
    public static Vector3 Intercept(Vector3 from, Vector3 target, Vector3 velocity, float speed)
    {
        Vector3 offset = target - from;
        float a = velocity.LengthSquared() - (speed * speed);
        float b = 2.0f * offset.Dot(velocity);
        float c = offset.LengthSquared();

        float t;
        if (Mathf.Abs(a) < 1e-4f)
        {
            t = b < 0.0f ? -c / b : -1.0f;
        }
        else
        {
            float discriminant = (b * b) - (4.0f * a * c);
            if (discriminant < 0.0f)
            {
                return target;
            }

            float root = Mathf.Sqrt(discriminant);
            float t1 = (-b - root) / (2.0f * a);
            float t2 = (-b + root) / (2.0f * a);
            t = Mathf.Min(t1, t2) > 0.0f ? Mathf.Min(t1, t2) : Mathf.Max(t1, t2);
        }

        return t > 0.0f ? target + (velocity * t) : target;
    }

    /// <summary>
    /// Launch velocity for a throw at a fixed speed under gravity along
    /// <paramref name="up"/>, landing on <paramref name="target"/>. The high
    /// arc when <paramref name="high"/> is set: slower to arrive, and visible
    /// the whole way down, which is what makes something thrown at you
    /// dodgeable. A target out of reach at that speed gets the 45 degree throw
    /// that goes furthest, and falls short.
    /// </summary>
    /// <param name="flightTime">Seconds until it lands, for leading a moving target.</param>
    public static Vector3 Lob(Vector3 from, Vector3 target, Vector3 up, float speed, float gravity, bool high,
                              out float flightTime)
    {
        Vector3 offset = target - from;
        float rise = offset.Dot(up);
        Vector3 flat = offset - (up * rise);
        float range = flat.Length();
        Vector3 along = range > 1e-3f ? flat / range : up.Cross(Vector3.Right).Normalized();

        if (gravity <= 1e-3f || range < 1e-3f)
        {
            flightTime = offset.Length() / Mathf.Max(speed, 0.01f);
            return offset.Normalized() * speed;
        }

        float v2 = speed * speed;
        float discriminant = (v2 * v2) - (gravity * ((gravity * range * range) + (2.0f * rise * v2)));

        float angle;
        if (discriminant < 0.0f)
        {
            angle = Mathf.Pi * 0.25f;
        }
        else
        {
            float root = Mathf.Sqrt(discriminant);
            angle = Mathf.Atan((v2 + (high ? root : -root)) / (gravity * range));
        }

        float horizontal = speed * Mathf.Cos(angle);
        flightTime = range / Mathf.Max(horizontal, 0.01f);
        return (along * horizontal) + (up * (speed * Mathf.Sin(angle)));
    }

    /// <summary>
    /// Tilts a direction off its line by up to <paramref name="halfAngle"/>
    /// radians, about a random axis round it. <paramref name="u1"/> and
    /// <paramref name="u2"/> are 0..1 samples from the caller's generator.
    /// </summary>
    public static Vector3 Scatter(Vector3 direction, float halfAngle, float u1, float u2)
    {
        if (halfAngle <= 0.0f)
        {
            return direction;
        }

        Vector3 side = direction.Cross(Mathf.Abs(direction.Y) < 0.95f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 axis = side.Rotated(direction, u1 * Mathf.Tau).Normalized();
        float spread = Mathf.Sqrt(u2) * halfAngle;
        return direction.Rotated(axis, spread).Normalized();
    }
}
