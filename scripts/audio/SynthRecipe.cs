using Godot;

namespace DillControl.Audio;

/// <summary>Oscillator shape underneath a <see cref="SynthRecipe"/>.</summary>
public enum SynthShape
{
    Noise,
    Sine,
    Saw,
    Square,
    Triangle,
}

/// <summary>
/// A placeholder sound described as numbers instead of as a file.
///
/// PLACEHOLDER. Replaced by real recordings in M7 -- and replacing one is
/// dropping a .wav into the matching <see cref="SoundEvent.Streams"/> slot,
/// which makes the recipe stop being used without anything else changing.
///
/// Why synthesis rather than sixty generated .wav files committed to the repo:
/// a noise burst with an envelope is cheaper to compute than to store, and
/// every number here is a slider instead of a re-export. Recordings arrive
/// through LFS into audio/ and replace these one slot at a time.
///
/// The synth is deliberately small: one swept oscillator, one noise source, a
/// one-pole low pass that can sweep, an optional sub-oscillator "body" layer,
/// optional impulse crackle, soft clipping and an envelope. That is enough to
/// cover a rifle crack, an explosion, a footstep, an engine loop and a UI blip,
/// and anything it cannot cover is a sound that wants a real recording anyway.
/// </summary>
[GlobalClass]
public partial class SynthRecipe : Resource
{
    [ExportGroup("Envelope")]
    [Export(PropertyHint.Range, "0.01,8,0.01,suffix:s")]
    public float Duration { get; set; } = 0.25f;

    /// <summary>Fade in. Keeps a sound from starting on a click.</summary>
    [Export(PropertyHint.Range, "0,0.5,0.001,suffix:s")]
    public float Attack { get; set; } = 0.002f;

    /// <summary>
    /// Decay shape. 1 is linear, higher is snappier -- a rifle crack is around
    /// 4, an explosion tail around 1.5, and a loop uses <see cref="Sustain"/>
    /// instead.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,12,0.1")]
    public float DecayExponent { get; set; } = 3.0f;

    /// <summary>
    /// Hold the envelope flat instead of decaying. What every looping sound
    /// wants, and what nothing else does.
    /// </summary>
    [Export]
    public bool Sustain { get; set; }

    [ExportGroup("Oscillator")]
    [Export]
    public SynthShape Shape { get; set; } = SynthShape.Noise;

    [Export(PropertyHint.Range, "20,12000,1,suffix:Hz")]
    public float StartFrequency { get; set; } = 220.0f;

    /// <summary>Swept to exponentially across the duration, so a pitch drop
    /// sounds like a pitch drop rather than like a linear ramp.</summary>
    [Export(PropertyHint.Range, "20,12000,1,suffix:Hz")]
    public float EndFrequency { get; set; } = 220.0f;

    /// <summary>Blend between the oscillator (0) and white noise (1).</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float NoiseMix { get; set; } = 1.0f;

    [Export(PropertyHint.Range, "0,60,0.5,suffix:Hz")]
    public float VibratoRate { get; set; }

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float VibratoDepth { get; set; }

    [ExportGroup("Filter")]
    [Export(PropertyHint.Range, "60,12000,1,suffix:Hz")]
    public float LowPassStart { get; set; } = 6000.0f;

    [Export(PropertyHint.Range, "60,12000,1,suffix:Hz")]
    public float LowPassEnd { get; set; } = 6000.0f;

    /// <summary>One-pole high pass. Takes the mud out of a small sound.</summary>
    [Export(PropertyHint.Range, "0,2000,1,suffix:Hz")]
    public float HighPass { get; set; }

    [ExportGroup("Body")]
    /// <summary>
    /// A second sine layer an octave or two down, swept on its own. This is the
    /// thump under a gunshot and the sub under an explosion -- the part that
    /// makes a noise burst read as a weapon rather than as static.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float BodyAmount { get; set; }

    [Export(PropertyHint.Range, "20,1200,1,suffix:Hz")]
    public float BodyStartFrequency { get; set; } = 90.0f;

    [Export(PropertyHint.Range, "10,1200,1,suffix:Hz")]
    public float BodyEndFrequency { get; set; } = 40.0f;

    [Export(PropertyHint.Range, "0.2,12,0.1")]
    public float BodyDecayExponent { get; set; } = 2.0f;

    [ExportGroup("Grit")]
    /// <summary>
    /// Impulses per second scattered through the sound. Rubble, fire and a
    /// drill all read as a texture of individual events rather than as a tone.
    /// </summary>
    [Export(PropertyHint.Range, "0,600,1")]
    public float CrackleRate { get; set; }

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float CrackleAmount { get; set; } = 0.5f;

    /// <summary>Soft clip drive. Above 1 it fattens; well above it distorts.</summary>
    [Export(PropertyHint.Range, "0.2,12,0.1")]
    public float Drive { get; set; } = 1.0f;

    [ExportGroup("Output")]
    [Export(PropertyHint.Range, "8000,44100,100,suffix:Hz")]
    public int MixRate { get; set; } = 22050;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float Peak { get; set; } = 0.85f;

    /// <summary>
    /// Renders as a seamless loop. The tail is cross-faded back over the head,
    /// which is what stops a noise-based engine loop clicking once a second.
    /// </summary>
    [Export]
    public bool Looping { get; set; }

    [Export(PropertyHint.Range, "0.01,1,0.01,suffix:s")]
    public float LoopCrossfade { get; set; } = 0.08f;

    /// <summary>Fixed, so a placeholder sounds the same in every session.</summary>
    [Export]
    public int Seed { get; set; } = 1;

    private AudioStreamWav _rendered;

    /// <summary>
    /// The rendered stream, built on first use and kept. Rendering is a few
    /// milliseconds; doing it per shot would not be.
    /// </summary>
    public AudioStreamWav Stream => _rendered ??= SoundSynth.Render(this);

    /// <summary>Drops the cached render, so an edited recipe is heard.</summary>
    public void Invalidate()
    {
        _rendered = null;
    }
}
