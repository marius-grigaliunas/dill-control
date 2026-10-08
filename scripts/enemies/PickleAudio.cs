using Godot;
using DillControl.Audio;

namespace DillControl.Enemies;

/// <summary>
/// What a pickle sounds like: the shared hurt and death sounds, pitched and
/// weighted by the size of the pickle making them.
///
/// Every agent used to play the identical sound, so a cornichon popping and
/// a tank going down were the same noise, and in a crowd of five kinds the
/// ear could not tell what had just died. Size is the cheapest thing that
/// separates them and the most legible: small is high and light, big is low
/// and loud, which is also how the player already reads them on screen.
/// No new sounds and no new bank slots -- the same events, played at a pitch
/// and a level per kind -- so recorded sounds dropped into the bank later
/// inherit the scaling for free.
///
/// Pitch goes as the inverse of height to a power, against a standard
/// pickle; level as a few decibels per doubling. A kind can override either
/// on its <see cref="AgentKindStats"/> when its size says the wrong thing.
/// </summary>
public static class PickleAudio
{
    /// <summary>The height everything is pitched against: a standard pickle.</summary>
    private const float ReferenceHeight = 1.8f;

    /// <summary>How strongly pitch follows size. 1 would be physical; less keeps a tank audible.</summary>
    private const float PitchExponent = 0.75f;

    /// <summary>Decibels per doubling of height.</summary>
    private const float DbPerDoubling = 5.0f;

    public static float PitchFor(AgentKindStats kind)
    {
        if (kind == null)
        {
            return 1.0f;
        }
        if (kind.VoicePitch > 0.0f)
        {
            return kind.VoicePitch;
        }
        float height = Mathf.Max(0.2f, kind.Height);
        return Mathf.Clamp(Mathf.Pow(ReferenceHeight / height, PitchExponent), 0.45f, 2.2f);
    }

    public static float VolumeFor(AgentKindStats kind)
    {
        if (kind == null)
        {
            return 0.0f;
        }
        float height = Mathf.Max(0.2f, kind.Height);
        float db = DbPerDoubling * Mathf.Log(height / ReferenceHeight) / Mathf.Log(2.0f);
        return Mathf.Clamp(db, -8.0f, 8.0f) + kind.VoiceVolumeDb;
    }

    /// <summary>Plays <paramref name="id"/> at <paramref name="position"/> in this kind's voice.</summary>
    public static void Play(Sfx id, Vector3 position, AgentKindStats kind)
    {
        AudioDirector.PlayAt(id, position, PitchFor(kind), VolumeFor(kind));
    }
}
