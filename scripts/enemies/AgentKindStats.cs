using Godot;

namespace DillControl.Enemies;

/// <summary>
/// Everything arguable about one of the five pickles: how it looks, how much it
/// takes to kill, how fast it moves, and the numbers its behaviour needs.
///
/// It is data rather than five subclasses because the five differ only in
/// values and in which event they raise on a channel the horde already has. A
/// behaviour that needs a class of its own has stopped being a variant of one
/// silhouette.
///
/// This lives in <c>scripts/enemies/</c> while the thing that runs it lives in
/// <c>scripts/horde/</c>: the table is what a designer edits, and the
/// flat-array executor is what makes twenty-five thousand of them affordable.
/// </summary>
[GlobalClass]
public partial class AgentKindStats : Resource
{
    [ExportGroup("Identity")]
    [Export]
    public string DisplayName { get; set; } = "Pickle";

    /// <summary>
    /// Which behaviour this configures. The runner switches on it, so two
    /// resources naming the same kind is a tuning choice, not a conflict.
    /// </summary>
    [Export]
    public AgentKind Kind { get; set; } = AgentKind.Standard;

    [ExportGroup("Body")]
    /// <summary>
    /// Silhouette, not decoration. Five grey capsules of identical proportions
    /// would be unreadable at the horizon, and the entire point of five
    /// behaviours is that a player can tell which one is coming.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,3,0.01,suffix:m")]
    public float Radius { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.3,8,0.01,suffix:m")]
    public float Height { get; set; } = 1.8f;

    [Export]
    public Color Colour { get; set; } = new(0.46f, 0.52f, 0.38f);

    /// <summary>
    /// What the manipulator has to be able to lift. The design is explicit that
    /// enemy mass must vary enough that the largest cannot simply be picked up,
    /// or the tool trivialises the horde.
    /// </summary>
    [Export(PropertyHint.Range, "1,600,1,suffix:kg")]
    public float Mass { get; set; } = 70.0f;

    /// <summary>
    /// This kind's voice pitch for hurt and death sounds. Zero derives it from
    /// <see cref="Height"/> (see PickleAudio): small is high, big is low.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.01")]
    public float VoicePitch { get; set; } = 0.0f;

    /// <summary>Added to the level PickleAudio derives from size.</summary>
    [Export(PropertyHint.Range, "-20,20,0.5,suffix:dB")]
    public float VoiceVolumeDb { get; set; } = 0.0f;

    [ExportGroup("Stats")]
    /// <summary>Multiplies the horde's own agent health.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.1")]
    public float HealthMultiplier { get; set; } = 1.0f;

    /// <summary>Multiplies the horde's own agent speed.</summary>
    [Export(PropertyHint.Range, "0.1,4,0.01")]
    public float SpeedMultiplier { get; set; } = 1.0f;

    [ExportGroup("Attack")]
    /// <summary>
    /// How close the player has to be for this kind to bite or strike, in
    /// metres from the agent's middle to the camera. Zero means it never does.
    /// A strike is a shove, a shake, a red flash and a bite out of the
    /// player's health -- a cornichon nips, a tank throws you.
    /// </summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:m")]
    public float StrikeRange { get; set; }

    /// <summary>Seconds between strikes.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05,suffix:s")]
    public float StrikeInterval { get; set; } = 1.0f;

    /// <summary>How hard a strike shoves, in m/s added to the player.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float StrikeImpulse { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float StrikeTrauma { get; set; } = 0.12f;

    /// <summary>Health a strike takes off the player, out of PlayerHealthTuning.MaxHealth.</summary>
    [Export(PropertyHint.Range, "0,100,0.5")]
    public float StrikeDamage { get; set; } = 4.0f;

    [ExportGroup("Dash")]
    /// <summary>
    /// How close the player has to be before this kind dashes at them. Zero
    /// means it never does. A dash is a burst along the ground, not a leap.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float DashRange { get; set; }

    [Export(PropertyHint.Range, "0.1,30,0.05,suffix:s")]
    public float DashInterval { get; set; } = 2.0f;

    /// <summary>Speed added along the heading at the start of a dash.</summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:m/s")]
    public float DashSpeed { get; set; } = 12.0f;

    [ExportGroup("Ranged")]
    /// <summary>
    /// How far away this kind will throw or spit at the player. Zero means it
    /// never does. What it fires is decided by its kind: a thrower lobs a live
    /// cornichon, a spitter spits brine.
    /// </summary>
    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float RangedRange { get; set; }

    /// <summary>Closer than this it does not bother; a strike or nothing.</summary>
    [Export(PropertyHint.Range, "0,50,0.5,suffix:m")]
    public float RangedMinRange { get; set; } = 6.0f;

    [Export(PropertyHint.Range, "0.2,30,0.05,suffix:s")]
    public float RangedInterval { get; set; } = 4.0f;

    /// <summary>Launch speed. A thrower's is a lob's, a spitter's is a shot's.</summary>
    [Export(PropertyHint.Range, "1,150,0.5,suffix:m/s")]
    public float ProjectileSpeed { get; set; } = 30.0f;

    /// <summary>Half-angle of the random scatter, so a crowd's fire lands as a pattern and not a point.</summary>
    [Export(PropertyHint.Range, "0,20,0.1,suffix:deg")]
    public float ProjectileSpreadDegrees { get; set; } = 3.0f;

    /// <summary>How much of the target's motion it leads. One is a perfect lead; zero aims where you are.</summary>
    [Export(PropertyHint.Range, "0,1.5,0.05")]
    public float ProjectileLead { get; set; } = 0.6f;

    /// <summary>How hard a hit shoves, in m/s added to the target.</summary>
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m/s")]
    public float ProjectileImpulse { get; set; } = 4.0f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ProjectileTrauma { get; set; } = 0.15f;

    /// <summary>
    /// Health a hit takes off the player: a spitter's brine, or a thrower's
    /// cornichon landing on them.
    /// </summary>
    [Export(PropertyHint.Range, "0,100,0.5")]
    public float ProjectileDamage { get; set; } = 4.0f;

    [ExportGroup("Mix")]
    /// <summary>Relative likelihood of a spawn being this kind, against the other kinds.</summary>
    [Export(PropertyHint.Range, "0,100,0.5")]
    public float SpawnWeight { get; set; } = 1.0f;

    /// <summary>
    /// The share of the horde's capacity this kind may occupy.
    ///
    /// A memory bill, not a balance dial. Each kind draws from its own instance
    /// buffer -- a multimesh takes one array per RID -- so sizing every kind at
    /// full capacity would be five buffers of mostly zeros. The shares are meant
    /// to sum to somewhat more than one: enough headroom that no kind hits its
    /// cap in normal play, without paying for five full hordes.
    ///
    /// A kind at its cap falls back to the standard rather than failing to
    /// spawn, so the horde is always the size it was asked for.
    /// </summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float CapacityShare { get; set; } = 0.35f;
}
