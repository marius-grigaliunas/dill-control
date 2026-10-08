using Godot;

namespace DillControl.Audio;

/// <summary>
/// A running looped sound, and the only way to stop one.
///
/// A loop is the one kind of sound the caller has to keep hold of: a
/// flamethrower, an engine, a drill and a fire all start when something begins
/// and stop when it ends, which no fire-and-forget call can express. The handle
/// is deliberately small -- stop it, pitch it, level it -- because anything
/// more would be the caller mixing rather than the director.
///
/// It survives its own player being freed. The player is parented to the thing
/// that owns the sound, so a drill destroyed mid-loop takes its audio with it,
/// and every method here has to keep working afterwards rather than throwing
/// into whatever was ticking it.
/// </summary>
public sealed class SoundLoop
{
    private Node _player;
    private readonly float _baseVolumeDb;
    private readonly float _basePitch;

    internal SoundLoop(Node player, float baseVolumeDb, float basePitch)
    {
        _player = player;
        _baseVolumeDb = baseVolumeDb;
        _basePitch = basePitch;
    }

    public bool IsPlaying => _player switch
    {
        AudioStreamPlayer3D spatial => GodotObject.IsInstanceValid(spatial) && spatial.Playing,
        AudioStreamPlayer flat => GodotObject.IsInstanceValid(flat) && flat.Playing,
        _ => false,
    };

    /// <summary>
    /// Multiplies the event's own tuned pitch rather than replacing it, so a
    /// drill running at its top notch is still recognisably the drill and the
    /// placeholder recipe stays the thing that decides what it sounds like.
    /// </summary>
    public void SetPitch(float multiplier)
    {
        float pitch = Mathf.Max(0.05f, _basePitch * multiplier);

        switch (_player)
        {
            case AudioStreamPlayer3D spatial when GodotObject.IsInstanceValid(spatial):
                spatial.PitchScale = pitch;
                break;
            case AudioStreamPlayer flat when GodotObject.IsInstanceValid(flat):
                flat.PitchScale = pitch;
                break;
        }
    }

    /// <summary>Offsets the event's tuned level, in decibels.</summary>
    public void SetVolumeOffsetDb(float offsetDb)
    {
        float volume = _baseVolumeDb + offsetDb;

        switch (_player)
        {
            case AudioStreamPlayer3D spatial when GodotObject.IsInstanceValid(spatial):
                spatial.VolumeDb = volume;
                break;
            case AudioStreamPlayer flat when GodotObject.IsInstanceValid(flat):
                flat.VolumeDb = volume;
                break;
        }
    }

    /// <summary>
    /// Stops and frees the player. Safe to call twice, and safe to call after
    /// whatever the loop was following has already been freed -- both happen
    /// routinely, because a thing that dies mid-loop is the normal case.
    /// </summary>
    public void Stop()
    {
        switch (_player)
        {
            case AudioStreamPlayer3D spatial when GodotObject.IsInstanceValid(spatial):
                spatial.Stop();
                spatial.QueueFree();
                break;
            case AudioStreamPlayer flat when GodotObject.IsInstanceValid(flat):
                flat.Stop();
                flat.QueueFree();
                break;
        }

        _player = null;
    }
}
