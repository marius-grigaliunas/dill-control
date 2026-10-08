using System.Collections.Generic;
using Godot;
using DillControl.Core;

namespace DillControl.Audio;

/// <summary>
/// The one thing that actually plays a sound.
///
/// Registered as the autoload <c>AudioDirector</c>. The lab's CLAUDE.md records that
/// EventBus was the only autoload, so this is a decision rather than a
/// convenience, and the argument is ownership lifetime: a sound routinely has
/// to outlive the node that asked for it. A rifle fired on the frame it is
/// holstered, an explosion whose effect node frees itself immediately, an
/// agent that dies and is recycled the same frame -- in every one of those the
/// emitter is gone long before the tail is. A pool parented to something that
/// persists is the only arrangement where that does not cut off, and nothing
/// below the scene root persists.
///
/// It is also the only place that knows what an <c>AudioStreamPlayer3D</c> is.
/// Callers name a <see cref="Sfx"/> or hand over a <see cref="SoundEvent"/> and
/// stop there, which is what keeps "assign a sound" from ever meaning "wire up
/// a player node".
///
/// Voices are pooled and capped. Both caps are cost controls with the horde in
/// mind: hundreds of agents can each want a death sound in the same second, and
/// what breaks first is the mix, not the frame.
/// </summary>
public partial class AudioDirector : Node
{
    // -- Bus names. Mirrored into AudioServer at startup; see EnsureBuses. --
    public const string MasterBus = "Master";
    public const string WorldBus = "World";
    public const string WeaponBus = "Weapons";
    public const string EnemyBus = "Enemies";
    public const string PlayerBus = "Player";
    public const string ShipBus = "Ship";
    public const string UiBus = "Ui";

    private const string BankPath = "res://resources/audio/snd_bank_default.tres";

    /// <summary>How many positional one-shots may sound at once, in total.</summary>
    private const int MaxPositionalVoices = 48;

    /// <summary>How many non-positional one-shots may sound at once.</summary>
    private const int MaxFlatVoices = 12;

    public static AudioDirector Instance { get; private set; }

    /// <summary>
    /// Every sound in the game. Loaded from <c>snd_bank_default.tres</c> if it
    /// is there, and constructed from the placeholder library if it is not --
    /// so the project is never silent because a resource went missing.
    /// </summary>
    public SoundBank Bank { get; private set; }

    /// <summary>Master cut, for a mute key or an options screen later.</summary>
    public bool Muted { get; set; }

    private readonly List<AudioStreamPlayer3D> _positionalPool = new();
    private readonly List<AudioStreamPlayer> _flatPool = new();

    /// <summary>Live one-shots, oldest first, so a cap steals the oldest.</summary>
    private readonly List<Voice> _voices = new();

    /// <summary>Last start time per event, for MinRetriggerSeconds.</summary>
    private readonly Dictionary<SoundEvent, ulong> _lastStartMs = new();

    private readonly RandomNumberGenerator _rng = new();

    private sealed class Voice
    {
        public Node Player;
        public SoundEvent Event;
    }

    public override void _EnterTree()
    {
        Instance = this;

        // Sound keeps running while the tree is paused, or opening the pause
        // menu would cut off whatever was in the air and the menu itself could
        // not click.
        ProcessMode = ProcessModeEnum.Always;

        _rng.Randomize();
        EnsureBuses();

        Bank = ResourceLoader.Exists(BankPath)
            ? ResourceLoader.Load<SoundBank>(BankPath) ?? new SoundBank()
            : new SoundBank();

        // Forces the bank to build and length-check its own table now, rather
        // than on whatever the first sound of the session happens to be.
        Bank?.Resolve(Sfx.None);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ---------------- Playing ----------------

    /// <summary>
    /// Plays a sound in the world.
    ///
    /// <paramref name="over"/> is an entity's own exported
    /// <see cref="SoundEvent"/>. Pass it and the bank slot is ignored; leave it
    /// null and the shared one is used. That is the whole override mechanism,
    /// and it is why an entity that wants a distinctive sound needs no bank
    /// slot of its own.
    /// </summary>
    private static readonly int SectionAudio = FrameProfiler.Section("audio");

    public static void PlayAt(Sfx id, Vector3 position, SoundEvent over = null)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionAudio);
        Instance?.PlayPositional(over ?? Instance.Bank?.Resolve(id), position, 1.0f, 0.0f);
    }

    /// <summary>
    /// As <see cref="PlayAt(Sfx, Vector3, SoundEvent)"/>, in a voice: the
    /// event's pitch multiplied by <paramref name="pitchScale"/> and its level
    /// raised by <paramref name="volumeDb"/>. For one event that many sources
    /// share at different sizes -- see <c>PickleAudio</c>. Retrigger gates and
    /// voice caps are the event's own, unchanged.
    /// </summary>
    public static void PlayAt(Sfx id, Vector3 position, float pitchScale, float volumeDb)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionAudio);
        Instance?.PlayPositional(Instance.Bank?.Resolve(id), position, pitchScale, volumeDb);
    }

    /// <summary>
    /// Plays a sound with no position: interface, and anything happening in the
    /// player's own hands that should not fall off with distance.
    /// </summary>
    public static void Play(Sfx id, SoundEvent over = null)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionAudio);
        Instance?.PlayFlat(over ?? Instance.Bank?.Resolve(id), 1.0f, 0.0f);
    }

    /// <summary>
    /// As <see cref="Play(Sfx, SoundEvent)"/>, in a voice: pitch multiplied
    /// and level raised. For one event several sources share -- each gun's
    /// shot from the same rifle or shotgun slot.
    /// </summary>
    public static void Play(Sfx id, SoundEvent over, float pitchScale, float volumeDb)
    {
        using FrameProfiler.Scope scope = FrameProfiler.Begin(SectionAudio);
        Instance?.PlayFlat(over ?? Instance.Bank?.Resolve(id), pitchScale, volumeDb);
    }

    /// <summary>
    /// Starts a looping sound that follows a node, and hands back the handle
    /// that stops it. The player is parented to <paramref name="follow"/>, so
    /// it tracks for free and cannot outlive what it belongs to.
    /// </summary>
    public static SoundLoop Loop(Sfx id, Node3D follow, SoundEvent over = null)
    {
        if (Instance == null || follow == null)
        {
            return null;
        }

        SoundEvent sound = over ?? Instance.Bank?.Resolve(id);
        AudioStream stream = sound?.Resolve(Instance._rng);
        if (stream == null)
        {
            return null;
        }

        AudioStreamPlayer3D player = new()
        {
            Stream = stream,
            Bus = Instance.ResolveBus(sound.Bus),
            UnitSize = sound.UnitSize,
            MaxDistance = sound.MaxDistance,
            VolumeDb = sound.VolumeDb,
            PitchScale = sound.PitchScale,
            ProcessMode = ProcessModeEnum.Always,
            MaxPolyphony = 1,
        };

        follow.AddChild(player);
        player.Play();

        return new SoundLoop(player, sound.VolumeDb, sound.PitchScale);
    }

    private void PlayPositional(SoundEvent sound, Vector3 position, float pitchScale, float volumeDb)
    {
        if (!CanStart(sound) || IsOutOfEarshot(sound, position))
        {
            return;
        }

        AudioStream stream = sound.Resolve(_rng);
        if (stream == null)
        {
            return;
        }

        AudioStreamPlayer3D player = TakePositional();
        if (player == null)
        {
            return;
        }

        player.Stream = stream;
        player.GlobalPosition = position;
        player.Bus = ResolveBus(sound.Bus);
        player.UnitSize = sound.UnitSize;
        player.MaxDistance = sound.MaxDistance;
        player.VolumeDb = sound.VolumeDb + volumeDb + Jitter(sound.VolumeJitterDb);
        player.PitchScale = Mathf.Max(0.05f, PitchFor(sound) * pitchScale);
        player.Play();

        Register(player, sound);
    }

    private void PlayFlat(SoundEvent sound, float pitchScale, float volumeDb)
    {
        if (!CanStart(sound))
        {
            return;
        }

        AudioStream stream = sound.Resolve(_rng);
        if (stream == null)
        {
            return;
        }

        AudioStreamPlayer player = TakeFlat();
        if (player == null)
        {
            return;
        }

        player.Stream = stream;
        player.Bus = ResolveBus(sound.Bus);
        player.VolumeDb = sound.VolumeDb + volumeDb + Jitter(sound.VolumeJitterDb);
        player.PitchScale = Mathf.Max(0.05f, PitchFor(sound) * pitchScale);
        player.Play();

        Register(player, sound);
    }

    // ---------------- Gating ----------------

    private bool CanStart(SoundEvent sound)
    {
        if (sound == null || Muted)
        {
            return false;
        }

        if (sound.MinRetriggerSeconds <= 0.0f)
        {
            return true;
        }

        ulong now = Time.GetTicksMsec();
        ulong gap = (ulong)Mathf.RoundToInt(sound.MinRetriggerSeconds * 1000.0f);

        if (_lastStartMs.TryGetValue(sound, out ulong last) && now - last < gap)
        {
            return false;
        }

        _lastStartMs[sound] = now;
        return true;
    }

    /// <summary>
    /// Refuses a sound the listener could not hear anyway.
    ///
    /// Measured against the active camera rather than the player node, for the
    /// reason the lab's CLAUDE.md records for terrain streaming: a boarded player
    /// is hidden where they got on, so keying this to the player would cut off
    /// every sound around the bike the moment it flew anywhere.
    /// </summary>
    private bool IsOutOfEarshot(SoundEvent sound, Vector3 position)
    {
        Camera3D camera = GetViewport()?.GetCamera3D();
        if (camera == null)
        {
            return false;
        }

        float range = sound.MaxDistance;
        return camera.GlobalPosition.DistanceSquaredTo(position) > range * range;
    }

    private float PitchFor(SoundEvent sound)
    {
        float jitter = 1.0f + Jitter(sound.PitchJitter);
        return Mathf.Max(0.05f, sound.PitchScale * jitter);
    }

    private float Jitter(float amount)
    {
        return amount <= 0.0f ? 0.0f : _rng.RandfRange(-amount, amount);
    }

    // ---------------- Pooling ----------------

    private void Register(Node player, SoundEvent sound)
    {
        _voices.Add(new Voice { Player = player, Event = sound });
        EnforceVoiceCap(sound);
    }

    /// <summary>
    /// Steals the oldest copy of an event past its own cap, rather than
    /// dropping the new one. A fresh sound is nearly always the more
    /// interesting one -- the newest gunshot is the one you just fired.
    /// </summary>
    private void EnforceVoiceCap(SoundEvent sound)
    {
        int cap = Mathf.Max(1, sound.MaxVoices);

        int live = 0;
        for (int i = 0; i < _voices.Count; i++)
        {
            if (_voices[i].Event == sound)
            {
                live++;
            }
        }

        for (int i = 0; i < _voices.Count && live > cap; i++)
        {
            if (_voices[i].Event != sound)
            {
                continue;
            }

            Stop(_voices[i].Player);
            _voices.RemoveAt(i);
            i--;
            live--;
        }
    }

    private static void Stop(Node player)
    {
        switch (player)
        {
            case AudioStreamPlayer3D spatial:
                spatial.Stop();
                break;
            case AudioStreamPlayer flat:
                flat.Stop();
                break;
        }
    }

    /// <summary>
    /// How many placeholder recipes are rendered per frame at startup.
    ///
    /// The recipes are rendered on first use otherwise, and a few milliseconds
    /// of synthesis landing on the frame a player first pulls a trigger is a
    /// hitch in exactly the worst place. This is the same warm-up argument
    /// the lab's CLAUDE.md records for the harmless rocket every level fires on spawn,
    /// spread over frames rather than paid in one.
    /// </summary>
    private const int PrewarmPerFrame = 2;

    private int _prewarmCursor = 1;

    public override void _Process(double delta)
    {
        Prewarm();

        // Voices are reaped here rather than on the Finished signal. A pooled
        // player is reused, so a signal connected per play has to be
        // disconnected per play too, and getting that wrong leaks a handler per
        // gunshot. Scanning a list capped at sixty entries once a frame is
        // cheaper than the bookkeeping it replaces.
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            if (!IsPlaying(_voices[i].Player))
            {
                _voices.RemoveAt(i);
            }
        }
    }

    private void Prewarm()
    {
        if (_prewarmCursor >= (int)Sfx.Count)
        {
            return;
        }

        int end = Mathf.Min(_prewarmCursor + PrewarmPerFrame, (int)Sfx.Count);

        for (; _prewarmCursor < end; _prewarmCursor++)
        {
            // Touching Stream is what renders it. A slot holding a real
            // recording has nothing to render and costs nothing here.
            SoundEvent sound = Bank?.Resolve((Sfx)_prewarmCursor);
            if (sound != null && (sound.Streams == null || sound.Streams.Length == 0))
            {
                _ = sound.Synth?.Stream;
            }
        }
    }

    private static bool IsPlaying(Node player)
    {
        return player switch
        {
            AudioStreamPlayer3D spatial => spatial.Playing,
            AudioStreamPlayer flat => flat.Playing,
            _ => false,
        };
    }

    private AudioStreamPlayer3D TakePositional()
    {
        for (int i = 0; i < _positionalPool.Count; i++)
        {
            if (!_positionalPool[i].Playing)
            {
                return _positionalPool[i];
            }
        }

        if (_positionalPool.Count >= MaxPositionalVoices)
        {
            return null;
        }

        AudioStreamPlayer3D player = new()
        {
            Name = $"Voice3D{_positionalPool.Count}",
            ProcessMode = ProcessModeEnum.Always,

            // One stream per player. Polyphony here would let a single pooled
            // player carry several sounds, which the cap accounting above
            // cannot see.
            MaxPolyphony = 1,
        };

        AddChild(player);
        _positionalPool.Add(player);
        return player;
    }

    private AudioStreamPlayer TakeFlat()
    {
        for (int i = 0; i < _flatPool.Count; i++)
        {
            if (!_flatPool[i].Playing)
            {
                return _flatPool[i];
            }
        }

        if (_flatPool.Count >= MaxFlatVoices)
        {
            return null;
        }

        AudioStreamPlayer player = new()
        {
            Name = $"Voice{_flatPool.Count}",
            ProcessMode = ProcessModeEnum.Always,
            MaxPolyphony = 1,
        };

        AddChild(player);
        _flatPool.Add(player);
        return player;
    }

    // ---------------- Buses ----------------

    /// <summary>
    /// Creates the mixer buses in code rather than shipping a bus layout
    /// resource, for the same reason the gravity fields are built in code: the
    /// table below is the source of truth and a second copy in a binary the
    /// editor manages could only ever disagree with it.
    /// </summary>
    private void EnsureBuses()
    {
        AddBus(WorldBus, 0.0f);
        AddBus(WeaponBus, -1.0f);
        AddBus(EnemyBus, -2.0f);
        AddBus(PlayerBus, -1.0f);
        AddBus(ShipBus, -1.0f);
        AddBus(UiBus, 0.0f);
    }

    /// <summary>
    /// The whole game's mix sits a third quieter than the bus table above
    /// says: linear 0.67, about -3.5 dB, on every bus. It is a trim on the
    /// game's own buses rather than on Master because Master is the player's
    /// volume slider, and a trim there would be overwritten by the saved
    /// setting.
    /// </summary>
    private static readonly float MixTrimDb = Mathf.LinearToDb(0.67f);

    private static void AddBus(string name, float volumeDb)
    {
        if (AudioServer.GetBusIndex(name) >= 0)
        {
            return;
        }

        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, name);
        AudioServer.SetBusSend(index, MasterBus);
        AudioServer.SetBusVolumeDb(index, volumeDb + MixTrimDb);
    }

    /// <summary>
    /// An unknown bus falls back to World rather than going silent, and not to
    /// Master, which would skip the mix trim.
    /// </summary>
    private string ResolveBus(string bus)
    {
        if (string.IsNullOrEmpty(bus) || AudioServer.GetBusIndex(bus) < 0)
        {
            return WorldBus;
        }

        return bus;
    }
}
