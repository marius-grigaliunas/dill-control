using Godot;

namespace DillControl.Weapons;

/// <summary>
/// The gravity manipulator's numbers. They were plain properties on
/// <see cref="Manipulator"/> while it was a key rather than a weapon, which put
/// every one of them out of reach of the tuning panel and behind a rebuild --
/// exactly what the data-not-code rule exists to forbid. A weapon gets a
/// stats resource like every other weapon, and the manipulator is handed these
/// each frame rather than owning them.
/// </summary>
[GlobalClass]
public partial class GravityGunStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Gravity Manipulator";

    [ExportGroup("Grab")]
    [Export(PropertyHint.Range, "2,80,0.5,suffix:m")]
    public float Range { get; set; } = 22.0f;

    /// <summary>How far in front of the camera a held thing floats.</summary>
    [Export(PropertyHint.Range, "1,15,0.1,suffix:m")]
    public float HoldDistance { get; set; } = 4.6f;

    [Export(PropertyHint.Range, "1,40,0.5")]
    public float ServoStrength { get; set; } = 12.0f;

    [Export(PropertyHint.Range, "1,80,0.5,suffix:m/s")]
    public float MaxHoldSpeed { get; set; } = 26.0f;

    /// <summary>
    /// The heaviest thing the beam can lift. It exists so enemy mass means
    /// something: a heavy is a problem precisely because you cannot solve it by
    /// pointing at it.
    /// </summary>
    [Export(PropertyHint.Range, "1,1000,1,suffix:kg")]
    public float MaxGrabMass { get; set; } = 90.0f;

    [ExportGroup("Launch")]
    [Export(PropertyHint.Range, "1,150,0.5,suffix:m/s")]
    public float LaunchSpeed { get; set; } = 42.0f;

    /// <summary>A click that takes hold of nothing is a punt if it is released
    /// inside this window, so one button does both without a mode.</summary>
    [Export(PropertyHint.Range, "0.05,1,0.01,suffix:s")]
    public float PuntTapWindow { get; set; } = 0.22f;

    [ExportGroup("Punt")]
    [Export(PropertyHint.Range, "0.5,10,0.1,suffix:m")]
    public float PuntRadius { get; set; } = 3.6f;

    [Export(PropertyHint.Range, "0.5,15,0.1,suffix:m")]
    public float PuntDistance { get; set; } = 4.2f;

    [Export(PropertyHint.Range, "1,100,0.5")]
    public float PuntImpulse { get; set; } = 26.0f;

    /// <summary>
    /// Damage a thrown or punted thing deals per metre per second of its speed,
    /// at <see cref="ImpactReferenceMass"/>. At the 42 m/s launch that is 84 --
    /// a baseline agent dies, a heavy does not.
    /// </summary>
    [ExportGroup("Thrown Impact")]
    [Export(PropertyHint.Range, "0,20,0.05")]
    public float ImpactDamagePerSpeed { get; set; } = 2.0f;

    /// <summary>Below this a thrown thing stops hurting anything, so a crate
    /// rolling to a stop against an agent is not an attack.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float ImpactMinSpeed { get; set; } = 8.0f;

    /// <summary>
    /// The mass that deals exactly the per-speed damage. Heavier hits harder by
    /// the square root of the ratio, clamped to half and double, so mass
    /// matters without a slab one-shotting everything.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,500,0.5,suffix:kg")]
    public float ImpactReferenceMass { get; set; } = 20.0f;

    /// <summary>How many separate things one throw can hurt.</summary>
    [Export(PropertyHint.Range, "1,16,1")]
    public int ImpactMaxTargets { get; set; } = 4;

    /// <summary>How long after a throw it can still hurt something.</summary>
    [Export(PropertyHint.Range, "0.2,10,0.1,suffix:s")]
    public float ImpactLifetime { get; set; } = 3.0f;

    [ExportGroup("Camera Kick")]
    [Export(PropertyHint.Range, "0,20,0.05,suffix:°")]
    public float KickPitchDegrees { get; set; } = 2.8f;

    // ---------------- Vortex ----------------

    /// <summary>
    /// Holding RMB gathers every agent near a point ahead into a swirling
    /// ball, and letting go flings the lot. One crate or one pickle at a time
    /// was a tool for a game with a dozen enemies; against a crowd the
    /// manipulator needs to move a crowd. The mass gate still applies: a tank
    /// does not get swept up.
    /// </summary>
    [ExportGroup("Vortex")]
    [Export(PropertyHint.Range, "2,40,0.5,suffix:m")]
    public float VortexDistance { get; set; } = 9.0f;

    /// <summary>How far round the vortex's centre agents are pulled in from.</summary>
    [Export(PropertyHint.Range, "1,20,0.5,suffix:m")]
    public float VortexPullRadius { get; set; } = 7.0f;

    /// <summary>Radius of the ball the gathered agents swirl on.</summary>
    [Export(PropertyHint.Range, "0.5,8,0.1,suffix:m")]
    public float VortexBallRadius { get; set; } = 2.4f;

    [Export(PropertyHint.Range, "1,120,1")]
    public int VortexMaxAgents { get; set; } = 40;

    /// <summary>Turns a second the ball spins.</summary>
    [Export(PropertyHint.Range, "0,4,0.05")]
    public float VortexSpin { get; set; } = 1.2f;

    /// <summary>Speed the ball is flung at, along the aim.</summary>
    [Export(PropertyHint.Range, "5,80,1,suffix:m/s")]
    public float VortexLaunchSpeed { get; set; } = 38.0f;

    /// <summary>How far each agent's throw fans away from the aim.</summary>
    [Export(PropertyHint.Range, "0,20,0.5,suffix:m/s")]
    public float VortexScatter { get; set; } = 7.0f;

    [Export(PropertyHint.Range, "0,20,0.05,suffix:°")]
    public float KickYawDegrees { get; set; } = 0.6f;

    [Export(PropertyHint.Range, "0.5,30,0.1")]
    public float KickRecoverSpeed { get; set; } = 5.0f;

    [Export(PropertyHint.Range, "1,60,0.5")]
    public float KickSnappiness { get; set; } = 12.0f;

    [ExportGroup("Viewmodel")]
    [Export(PropertyHint.Range, "0,0.5,0.005,suffix:m")]
    public float ViewModelKickBack { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "0,30,0.1,suffix:°")]
    public float ViewModelKickPitch { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "1,40,0.1")]
    public float ViewModelRecoverSpeed { get; set; } = 6.0f;

    /// <summary>Width of the placeholder tether drawn to whatever is held.</summary>
    [Export(PropertyHint.Range, "0.005,0.5,0.005,suffix:m")]
    public float BeamWidth { get; set; } = 0.05f;
}
