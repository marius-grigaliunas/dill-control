using Godot;

namespace DillControl.Weapons;

/// <summary>
/// How a stroke looks for a target. A fan is a row of rays across an arc,
/// the machete's swing. A ray then a sphere sweep is the Half-Life 2 trace:
/// the sightline first, and a fatter shape only if that line missed.
/// </summary>
public enum MeleeStrokeShape
{
    Fan = 0,
    RaySweep = 1,
}

/// <summary>
/// One melee stroke as four poses and the times between them.
///
/// The poses are in the viewmodel's parent space: +X right, +Y up, -Z
/// forward, Euler degrees in Godot's YXZ order. Positive X pitches the
/// weapon's forward (-Z) up. A weapon reads this and adds its own heft;
/// there is no per-weapon motion in the code. Shared by every melee weapon,
/// the way a mech's slash is one resource (docs/decisions/0010, 0028).
/// </summary>
[GlobalClass]
public partial class MeleeStroke : Resource
{
    [ExportGroup("Poses")]
    /// <summary>Raised, at the top of the windup. Grip position, then Euler degrees.</summary>
    [Export]
    public Vector3 CockedPosition { get; set; } = new(0.06f, 0.06f, -0.16f);

    [Export]
    public Vector3 CockedEulerDegrees { get; set; } = new(78.0f, 10.0f, 16.0f);

    /// <summary>Where a hit ends. Slid so the weapon's Head marker meets the surface.</summary>
    [Export]
    public Vector3 ContactPosition { get; set; } = new(0.04f, -0.36f, -0.40f);

    [Export]
    public Vector3 ContactEulerDegrees { get; set; } = new(-40.0f, 4.0f, 8.0f);

    /// <summary>Where a miss carries through to. No stop, no bounce.</summary>
    [Export]
    public Vector3 FollowPosition { get; set; } = new(0.02f, -0.56f, -0.20f);

    [Export]
    public Vector3 FollowEulerDegrees { get; set; } = new(-76.0f, -4.0f, 12.0f);

    /// <summary>The pose a hit recovers from, after the freeze. The contact slide is applied to it too.</summary>
    [Export]
    public Vector3 RecoverPosition { get; set; } = new(0.10f, -0.16f, -0.30f);

    [Export]
    public Vector3 RecoverEulerDegrees { get; set; } = new(-6.0f, 8.0f, 10.0f);

    [ExportGroup("Timing")]
    [Export(PropertyHint.Range, "0.01,1.2,0.005,suffix:s")]
    public float Raise { get; set; } = 0.18f;

    /// <summary>Held at the top, so the drop reads as a decision.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.005,suffix:s")]
    public float Hold { get; set; } = 0.05f;

    /// <summary>The drop. Short: the weight is the windup and the stop, not a slow swing.</summary>
    [Export(PropertyHint.Range, "0.02,0.8,0.005,suffix:s")]
    public float Strike { get; set; } = 0.09f;

    /// <summary>How long the viewmodel freezes on a hit, before the bounce. Real time.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005,suffix:s")]
    public float ContactHold { get; set; } = 0.055f;

    [Export(PropertyHint.Range, "0.01,1.5,0.005,suffix:s")]
    public float Recover { get; set; } = 0.24f;

    /// <summary>Longer than <see cref="Recover"/>: a whiff costs more than a hit.</summary>
    [Export(PropertyHint.Range, "0.01,1.5,0.005,suffix:s")]
    public float MissRecover { get; set; } = 0.42f;

    /// <summary>Ease-out power on the raise. Above 1 arrives slower than it leaves.</summary>
    [Export(PropertyHint.Range, "1,6,0.1")]
    public float RaiseEase { get; set; } = 2.0f;

    /// <summary>
    /// Ease-in power on the drop. Above 1 accelerates into the hit. Much above
    /// 2 and the last of the drop outruns the head spring, so the head is still
    /// raised when the grip has already stopped.
    /// </summary>
    [Export(PropertyHint.Range, "1,6,0.1")]
    public float StrikeEase { get; set; } = 1.8f;

    [Export(PropertyHint.Range, "1,6,0.1")]
    public float RecoverEase { get; set; } = 2.0f;

    [ExportGroup("Hit")]
    /// <summary>
    /// When the trace runs and the hit is applied, 0 on the click and 1 as the
    /// drop arrives. The drop itself is aimed from the start of the strike, so
    /// a late value still meets the surface; only the hurt waits.
    /// </summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ResolveAt { get; set; } = 0.72f;

    [Export]
    public MeleeStrokeShape Shape { get; set; } = MeleeStrokeShape.RaySweep;

    [Export(PropertyHint.Range, "0.4,8,0.05,suffix:m")]
    public float Range { get; set; } = 3.2f;

    /// <summary>Fan only: how wide the rays spread.</summary>
    [Export(PropertyHint.Range, "5,170,1,suffix:°")]
    public float ArcDegrees { get; set; } = 70.0f;

    [Export(PropertyHint.Range, "1,21,1")]
    public int ArcRayCount { get; set; } = 9;

    /// <summary>Ray-sweep only: radius of the sphere used when the sightline misses.</summary>
    [Export(PropertyHint.Range, "0.05,1.5,0.01,suffix:m")]
    public float SweepRadius { get; set; } = 0.32f;

    [ExportGroup("Blast")]
    /// <summary>Shockwave when the stroke meets the world or a body. A hitbox also takes the weapon's own damage.</summary>
    [Export(PropertyHint.Range, "0,8,0.05,suffix:m")]
    public float BlastRadius { get; set; } = 2.6f;

    [Export(PropertyHint.Range, "0,400,1")]
    public float BlastDamage { get; set; } = 46.0f;

    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float BlastImpulse { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float BlastUpBias { get; set; } = 0.6f;

    /// <summary>Share of the blast used as the splash around a direct hitbox hit.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SplashScale { get; set; } = 0.45f;

    /// <summary>Off, a slam digs no crater. An explicit crater would still be the blast's own.</summary>
    [Export]
    public bool NoCrater { get; set; } = true;

    [ExportGroup("Feel")]
    /// <summary>Camera pitch on a hit, degrees downward.</summary>
    [Export(PropertyHint.Range, "0,15,0.1,suffix:°")]
    public float KickPitchDegrees { get; set; } = 3.2f;

    /// <summary>Camera shake on a hit, added to the rig's trauma.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ImpactTrauma { get; set; } = 0.22f;

    /// <summary>Viewmodel kick back from the surface, in metres, before heft.</summary>
    [Export(PropertyHint.Range, "0,0.4,0.005,suffix:m")]
    public float BounceBack { get; set; } = 0.05f;

    /// <summary>Viewmodel pitch of that bounce, degrees, lifting the head.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:°")]
    public float BouncePitch { get; set; } = 7.0f;

    /// <summary>Seconds from the click to the start of the drop, before heft.</summary>
    public float WindupSeconds => Raise + Hold;

    /// <summary>Seconds from the click to the drop arriving, before heft.</summary>
    public float PreContactSeconds => Raise + Hold + Strike;

    /// <summary>Ease-out: fast out of the previous pose, settling into the next.</summary>
    public static float EaseOut(float t, float power)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return 1.0f - Mathf.Pow(1.0f - t, Mathf.Max(1.0f, power));
    }

    /// <summary>Ease-in: the drop accelerates. Not smoothstep, which spends its speed in the middle and arrives gently.</summary>
    public static float EaseIn(float t, float power)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        return Mathf.Pow(t, Mathf.Max(1.0f, power));
    }

    /// <summary>A pose's transform at the viewmodel's scale. Rotation only is scaled, so the positions stay in metres.</summary>
    public static Transform3D Scaled(Vector3 position, Vector3 eulerDegrees, float scale)
    {
        Basis rotation = Basis.FromEuler(new Vector3(
            Mathf.DegToRad(eulerDegrees.X),
            Mathf.DegToRad(eulerDegrees.Y),
            Mathf.DegToRad(eulerDegrees.Z)));
        return new Transform3D(rotation.Scaled(Vector3.One * scale), position);
    }
}
