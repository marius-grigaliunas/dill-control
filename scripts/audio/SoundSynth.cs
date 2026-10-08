using System;
using Godot;

namespace DillControl.Audio;

/// <summary>
/// Turns a <see cref="SynthRecipe"/> into an <see cref="AudioStreamWav"/>.
///
/// PLACEHOLDER, and deliberately a small one. It is one swept oscillator, one
/// noise source, a sweeping one-pole low pass, a one-pole high pass, an
/// optional sub-oscillator body layer, optional impulse crackle, a soft clip
/// and an envelope -- in that order, once per sample. There is no filter
/// resonance, no reverb and no FM, because every one of those is a step toward
/// writing a synthesiser instead of a game, and the thing this replaces is
/// silence.
///
/// Rendering is deterministic: the seed drives its own generator, so a given
/// recipe sounds identical in every session and on every machine.
/// </summary>
public static class SoundSynth
{
    public static AudioStreamWav Render(SynthRecipe recipe)
    {
        if (recipe == null)
        {
            return null;
        }

        int rate = Mathf.Clamp(recipe.MixRate, 8000, 48000);
        float duration = Mathf.Max(0.01f, recipe.Duration);

        // A loop is rendered with the crossfade tail on the end and then folded
        // back over its own head, so the rendered length is what was asked for
        // and the seam is inaudible.
        float crossfade = recipe.Looping
            ? Mathf.Clamp(recipe.LoopCrossfade, 0.005f, duration * 0.5f)
            : 0.0f;

        int frames = Mathf.Max(2, Mathf.RoundToInt(duration * rate));
        int fadeFrames = Mathf.RoundToInt(crossfade * rate);
        int renderFrames = frames + fadeFrames;

        float[] buffer = new float[renderFrames];

        Random rng = new(recipe.Seed);

        float phase = 0.0f;
        float bodyPhase = 0.0f;
        float vibratoPhase = 0.0f;

        // One-pole filter state.
        float lowPassState = 0.0f;
        float highPassState = 0.0f;
        float highPassPrevious = 0.0f;

        // Crackle is a probability per sample rather than a scheduled grid, or
        // every rubble sound would land on a beat.
        float crackleChance = recipe.CrackleRate / rate;
        float crackleDecay = 0.0f;
        float crackleValue = 0.0f;

        for (int i = 0; i < renderFrames; i++)
        {
            float t = (float)i / rate;
            float progress = Mathf.Clamp(t / duration, 0.0f, 1.0f);

            // ---- oscillator ----
            float frequency = ExponentialLerp(recipe.StartFrequency, recipe.EndFrequency, progress);

            if (recipe.VibratoRate > 0.0f && recipe.VibratoDepth > 0.0f)
            {
                vibratoPhase += recipe.VibratoRate / rate;
                frequency *= 1.0f + (Mathf.Sin(vibratoPhase * Mathf.Tau) * recipe.VibratoDepth);
            }

            phase += frequency / rate;
            phase -= Mathf.Floor(phase);

            float tone = Oscillator(recipe.Shape, phase, rng);
            float noise = (float)((rng.NextDouble() * 2.0) - 1.0);
            float sample = Mathf.Lerp(tone, noise, Mathf.Clamp(recipe.NoiseMix, 0.0f, 1.0f));

            // ---- crackle ----
            if (crackleChance > 0.0f)
            {
                if (rng.NextDouble() < crackleChance)
                {
                    crackleValue = (float)((rng.NextDouble() * 2.0) - 1.0);

                    // Each impulse gets its own length, so the texture is not
                    // one repeated tick.
                    float clickSeconds = 0.002f + ((float)rng.NextDouble() * 0.012f);
                    crackleDecay = Mathf.Exp(-1.0f / (rate * clickSeconds));
                }

                sample += crackleValue * recipe.CrackleAmount;
                crackleValue *= crackleDecay;
            }

            // ---- filters ----
            float cutoff = ExponentialLerp(recipe.LowPassStart, recipe.LowPassEnd, progress);
            lowPassState += (sample - lowPassState) * OnePoleCoefficient(cutoff, rate);
            sample = lowPassState;

            if (recipe.HighPass > 0.0f)
            {
                float highPassCoefficient = 1.0f - OnePoleCoefficient(recipe.HighPass, rate);
                highPassState = highPassCoefficient * (highPassState + sample - highPassPrevious);
                highPassPrevious = sample;
                sample = highPassState;
            }

            // ---- body layer ----
            // Added after the filter on purpose: the sub is the part that has
            // to survive, and low-passing a 60 Hz sine with a cutoff that
            // sweeps down to 400 Hz would quietly eat it.
            if (recipe.BodyAmount > 0.0f)
            {
                float bodyFrequency = ExponentialLerp(
                    recipe.BodyStartFrequency, recipe.BodyEndFrequency, progress);

                bodyPhase += bodyFrequency / rate;
                bodyPhase -= Mathf.Floor(bodyPhase);

                float bodyEnvelope = recipe.Sustain
                    ? 1.0f
                    : Mathf.Pow(1.0f - progress, recipe.BodyDecayExponent);

                sample += Mathf.Sin(bodyPhase * Mathf.Tau) * recipe.BodyAmount * bodyEnvelope;
            }

            // ---- drive ----
            if (!Mathf.IsEqualApprox(recipe.Drive, 1.0f))
            {
                sample = SoftClip(sample * recipe.Drive);
            }

            sample *= Envelope(recipe, t, progress);

            buffer[i] = sample;
        }

        if (fadeFrames > 0)
        {
            FoldLoopTail(buffer, frames, fadeFrames);
            Array.Resize(ref buffer, frames);
        }

        Normalise(buffer, recipe.Peak);

        return Encode(buffer, rate, recipe.Looping);
    }

    private static float Envelope(SynthRecipe recipe, float t, float progress)
    {
        float attack = recipe.Attack <= 0.0f ? 1.0f : Mathf.Min(1.0f, t / recipe.Attack);

        if (recipe.Sustain)
        {
            return attack;
        }

        return attack * Mathf.Pow(1.0f - progress, recipe.DecayExponent);
    }

    private static float Oscillator(SynthShape shape, float phase, Random rng)
    {
        switch (shape)
        {
            case SynthShape.Sine:
                return Mathf.Sin(phase * Mathf.Tau);
            case SynthShape.Saw:
                return (phase * 2.0f) - 1.0f;
            case SynthShape.Square:
                return phase < 0.5f ? 1.0f : -1.0f;
            case SynthShape.Triangle:
                return phase < 0.5f ? (phase * 4.0f) - 1.0f : 3.0f - (phase * 4.0f);
            default:
                return (float)((rng.NextDouble() * 2.0) - 1.0);
        }
    }

    /// <summary>
    /// Sweeps in pitch space rather than in linear hertz. 4000 Hz down to 200
    /// swept linearly spends most of its time up high and then falls off a
    /// cliff; swept exponentially it reads as one continuous drop.
    /// </summary>
    private static float ExponentialLerp(float from, float to, float t)
    {
        from = Mathf.Max(1.0f, from);
        to = Mathf.Max(1.0f, to);
        return from * Mathf.Pow(to / from, t);
    }

    private static float OnePoleCoefficient(float cutoffHz, int rate)
    {
        float clamped = Mathf.Clamp(cutoffHz, 10.0f, rate * 0.45f);
        return 1.0f - Mathf.Exp(-Mathf.Tau * clamped / rate);
    }

    private static float SoftClip(float x)
    {
        return (float)Math.Tanh(x);
    }

    /// <summary>
    /// Mixes the rendered overrun back over the start, which is what makes a
    /// noise loop seamless. Equal power rather than linear because two
    /// uncorrelated noise signals summed at half gain each are quieter than
    /// either alone, and that dip is audible as a pulse once a second.
    /// </summary>
    private static void FoldLoopTail(float[] buffer, int frames, int fadeFrames)
    {
        for (int i = 0; i < fadeFrames; i++)
        {
            float t = (float)i / fadeFrames;
            float head = Mathf.Sqrt(t);
            float tail = Mathf.Sqrt(1.0f - t);
            buffer[i] = (buffer[i] * head) + (buffer[frames + i] * tail);
        }
    }

    private static void Normalise(float[] buffer, float peak)
    {
        float loudest = 0.0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            loudest = Mathf.Max(loudest, Mathf.Abs(buffer[i]));
        }

        if (loudest < 0.0001f)
        {
            return;
        }

        float gain = Mathf.Clamp(peak, 0.0f, 1.0f) / loudest;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= gain;
        }
    }

    private static AudioStreamWav Encode(float[] buffer, int rate, bool looping)
    {
        byte[] data = new byte[buffer.Length * 2];

        for (int i = 0; i < buffer.Length; i++)
        {
            int scaled = Mathf.RoundToInt(buffer[i] * short.MaxValue);
            short value = (short)Mathf.Clamp(scaled, short.MinValue, short.MaxValue);

            data[i * 2] = (byte)(value & 0xFF);
            data[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return new AudioStreamWav
        {
            Data = data,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = rate,
            Stereo = false,
            LoopMode = looping ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = looping ? buffer.Length : 0,
        };
    }
}
