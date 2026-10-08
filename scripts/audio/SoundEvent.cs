using Godot;

namespace DillControl.Audio;

/// <summary>
/// One sound, and everything about how it is played.
///
/// This is the single currency of the audio system. Anything that wants a
/// sound -- a weapon, a drill, a nest, a button -- names a
/// <see cref="SoundEvent"/> and hands it to <see cref="AudioDirector"/>. It
/// never names a file, a bus, an <c>AudioStreamPlayer3D</c> or a distance.
///
/// ASSIGNING A REAL SOUND, which is the whole point of the shape:
/// drop one or more files into <see cref="Streams"/>. The moment that array is
/// non-empty, <see cref="Synth"/> stops being used and the real recording
/// plays with the same volume, pitch, distance and retrigger rules that were
/// already tuned against the placeholder. Nothing else changes -- not a call
/// site, not a scene, not a rebuild.
///
/// Several streams in the array is a variation set: one is chosen at random
/// per play and the previous choice is never repeated back to back, because a
/// rifle firing the identical waveform thirty times in three seconds is the
/// single thing that makes placeholder audio read as placeholder.
/// </summary>
[GlobalClass]
public partial class SoundEvent : Resource
{
    [ExportGroup("Source")]
    /// <summary>
    /// Real recordings. Empty means the placeholder <see cref="Synth"/> is
    /// used; non-empty means it is not. This array is the replace-a-placeholder
    /// mechanism and the only one there is.
    /// </summary>
    [Export]
    public AudioStream[] Streams { get; set; } = System.Array.Empty<AudioStream>();

    /// <summary>
    /// The procedural placeholder, used only while <see cref="Streams"/> is
    /// empty. PLACEHOLDER -- see <see cref="SynthRecipe"/> for why the
    /// placeholders are recipes rather than committed .wav files.
    /// </summary>
    [Export]
    public SynthRecipe Synth { get; set; }

    [ExportGroup("Level")]
    [Export(PropertyHint.Range, "-60,12,0.5,suffix:dB")]
    public float VolumeDb { get; set; }

    /// <summary>Random level spread per play, plus and minus.</summary>
    [Export(PropertyHint.Range, "0,12,0.1,suffix:dB")]
    public float VolumeJitterDb { get; set; } = 1.5f;

    [Export(PropertyHint.Range, "0.1,4,0.01")]
    public float PitchScale { get; set; } = 1.0f;

    /// <summary>
    /// Random pitch spread per play, as a fraction. 0.08 is roughly a
    /// semitone and a half either way, which is enough to stop repetition
    /// reading as a loop without making a weapon sound broken.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.5,0.01")]
    public float PitchJitter { get; set; } = 0.06f;

    [ExportGroup("Space")]
    /// <summary>
    /// Metres at which the sound is at its unattenuated level. Bigger means
    /// louder further away -- this is the dial for "how far does this carry",
    /// not <see cref="MaxDistance"/>, which only says where it is cut off.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,120,0.5,suffix:m")]
    public float UnitSize { get; set; } = 10.0f;

    /// <summary>
    /// Past this, the sound is not played at all. It is a cost control as much
    /// as a mix decision: the horde is hundreds of agents, and a death sound
    /// audible across a planet is hundreds of voices.
    /// </summary>
    [Export(PropertyHint.Range, "1,2000,1,suffix:m")]
    public float MaxDistance { get; set; } = 140.0f;

    [ExportGroup("Behaviour")]
    /// <summary>
    /// Which mixer bus this lands on. See <see cref="AudioDirector"/> for the
    /// bus table; an unknown name falls back to Master rather than failing.
    /// </summary>
    [Export]
    public string Bus { get; set; } = AudioDirector.WorldBus;

    /// <summary>
    /// Refuses to start again within this many seconds of its last start,
    /// anywhere.
    ///
    /// This is what stops one shotgun shell spawning nine impact sounds and a
    /// carve spawning one per voxel. Zero means no limit, which is right for
    /// anything a player triggers one at a time.
    ///
    /// Deliberately global to the event rather than measured per place. A
    /// position-aware gate would let nine pellets hitting nine different points
    /// through, and nine pellets hitting nine different points is exactly the
    /// case this exists to collapse into one sound.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.01,suffix:s")]
    public float MinRetriggerSeconds { get; set; }

    /// <summary>
    /// How many copies of this event may sound at once. Past it, the oldest is
    /// stolen rather than the new one dropped -- a fresh sound is nearly always
    /// the more interesting one.
    /// </summary>
    [Export(PropertyHint.Range, "1,32,1")]
    public int MaxVoices { get; set; } = 8;

    private int _lastStreamIndex = -1;

    /// <summary>
    /// The stream to play this time. A real recording if one is assigned,
    /// otherwise the rendered placeholder.
    /// </summary>
    public AudioStream Resolve(RandomNumberGenerator rng)
    {
        if (Streams == null || Streams.Length == 0)
        {
            return Synth?.Stream;
        }

        if (Streams.Length == 1)
        {
            return Streams[0];
        }

        // Never the same one twice running. Re-rolling could loop for a long
        // time on an unlucky seed; stepping past the last choice cannot.
        int index = rng.RandiRange(0, Streams.Length - 2);
        if (index >= _lastStreamIndex)
        {
            index++;
        }

        _lastStreamIndex = index;
        return Streams[index];
    }
}
