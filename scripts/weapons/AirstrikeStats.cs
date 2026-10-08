using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Everything tunable about a called-in airstrike: how it is designated, how
/// long it takes to arrive, and how the salvo is scattered.
///
/// It does not describe the warhead. That is a whole <see cref="RocketStats"/>
/// on <see cref="Ordnance"/>, because a falling bomb and a fired rocket want
/// exactly the same numbers and the same code -- the only difference is who
/// pointed it and from where. Retuning the launcher and retuning the strike
/// stay separate because they are two different resources, not two code paths.
/// </summary>
[GlobalClass]
public partial class AirstrikeStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Strike Designator";

    [ExportGroup("Uses")]
    /// <summary>
    /// Calls available. Small, because the whole cost model of this weapon is
    /// that you only get a few and you choose where.
    /// </summary>
    [Export(PropertyHint.Range, "1,20,1")]
    public int Charges { get; set; } = 4;

    /// <summary>Dead time after a call before another can be designated.</summary>
    [Export(PropertyHint.Range, "0.5,30,0.1,suffix:s")]
    public float Cooldown { get; set; } = 3.0f;

    [ExportGroup("Designation")]
    /// <summary>
    /// How far the designating ray reaches. Well past the 36 m horizon of a
    /// 400 m planet, so a strike can be put on the far side of a ridge that a
    /// rifle shot could not reach.
    /// </summary>
    [Export(PropertyHint.Range, "10,500,1,suffix:m")]
    public float MaxRange { get; set; } = 220.0f;

    [ExportGroup("Salvo")]
    /// <summary>Seconds between the call and the first bomb landing.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:s")]
    public float LeadTime { get; set; } = 1.6f;

    [Export(PropertyHint.Range, "1,32,1")]
    public int BombCount { get; set; } = 7;

    /// <summary>Seconds between bombs. What turns a salvo into a walked line.</summary>
    [Export(PropertyHint.Range, "0.02,2,0.01,suffix:s")]
    public float BombInterval { get; set; } = 0.22f;

    /// <summary>Radius of the scatter pattern around the designated point.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m")]
    public float SpreadRadius { get; set; } = 11.0f;

    /// <summary>
    /// How high above the target the bombs are released. High enough that they
    /// are seen falling before they land, which is the whole theatre of the
    /// thing, and that they clear anything standing on the surface.
    /// </summary>
    [Export(PropertyHint.Range, "20,600,1,suffix:m")]
    public float DropHeight { get; set; } = 120.0f;

    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,20,0.05,suffix:°")]
    public float KickPitchDegrees { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 8.0f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 16.0f;

    [ExportGroup("Ordnance")]
    /// <summary>
    /// The warhead each bomb carries. Left unassigned, the designator builds a
    /// heavier-than-a-rocket default in code so the weapon still works out of
    /// the box rather than silently calling in nothing.
    /// </summary>
    [Export]
    public RocketStats Ordnance { get; set; }
}
