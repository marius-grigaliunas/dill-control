using Godot;

namespace DillControl.Foliage;

/// <summary>
/// The wind every flexing plant leans in. Gusts roll across the field
/// downwind at the wind's own speed, so neighbours sway one after the other
/// rather than all at once. No random numbers: a sum of slow waves, the same
/// on every run.
/// </summary>
[GlobalClass]
public partial class WindStats : Resource
{
    public const string DefaultPath = "res://resources/foliage/wind_default.tres";

    /// <summary>Where it blows toward, in degrees round up from +X toward +Z.</summary>
    [Export(PropertyHint.Range, "0,360,1,suffix:deg")]
    public float DirectionDegrees { get; set; } = 30.0f;

    [Export(PropertyHint.Range, "0,40,0.1,suffix:m/s")]
    public float Speed { get; set; } = 5.0f;

    /// <summary>How much faster than <see cref="Speed"/> a gust blows, at its peak.</summary>
    [Export(PropertyHint.Range, "0,40,0.1,suffix:m/s")]
    public float GustSpeed { get; set; } = 4.0f;

    /// <summary>Seconds between gusts, roughly.</summary>
    [Export(PropertyHint.Range, "1,60,0.5,suffix:s")]
    public float GustSeconds { get; set; } = 7.0f;

    /// <summary>Share of a gust that blows across the wind, so tips trace loops, not lines.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Swirl { get; set; } = 0.35f;

    public Vector3 Direction
    {
        get
        {
            float radians = Mathf.DegToRad(DirectionDegrees);
            return new Vector3(Mathf.Cos(radians), 0.0f, Mathf.Sin(radians));
        }
    }

    /// <summary>The wind at a place and time, in world space, sideways to <paramref name="up"/>.</summary>
    public Vector3 At(Vector3 point, Vector3 up, double time)
    {
        Vector3 along = Direction.Slide(up);
        along = along.LengthSquared() < 1e-6f ? up.Cross(Vector3.Forward).Normalized() : along.Normalized();
        Vector3 across = up.Cross(along);

        // Downwind places see a gust later.
        double delay = point.Dot(along) / Mathf.Max(1.0f, Speed);
        double t = (time - delay) * Mathf.Tau / Mathf.Max(1.0f, GustSeconds);
        float gust = (float)((Mathf.Sin(t) + (0.6 * Mathf.Sin((1.73 * t) + 1.1)) + (0.35 * Mathf.Sin((2.91 * t) + 2.3))) / 1.95);
        gust = 0.5f * (gust + 1.0f);
        double c = (t * 1.31) + (point.Dot(across) * 0.013);
        float sideways = (float)(Mathf.Sin(c) + (0.5 * Mathf.Sin((2.17 * c) + 0.7))) / 1.5f;
        return (along * (Speed + (GustSpeed * gust))) + (across * (Swirl * GustSpeed * sideways));
    }
}
