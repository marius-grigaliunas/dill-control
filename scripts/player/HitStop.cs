using Godot;
using DillControl.Core;

namespace DillControl.Player;

/// <summary>
/// A beat of frozen time when one hit kills a crowd: the game drops to a
/// crawl for a few hundredths of a second and the camera punches, scaled by
/// how many died. The oldest trick for making an impact feel heavy, and the
/// one that costs nothing to draw.
///
/// Only for a report that covers many deaths at once -- a rocket or a jar
/// into a crowd, which the horde announces as one DamageDealt carrying
/// <see cref="DamageInfo.KillCount"/>. A single kill never freezes anything,
/// and a cooldown keeps sustained fire, which kills constantly, from turning
/// into a stutter. Timed on real time, so the stop ends however slow the
/// game was made to run.
///
/// Script-only node in player.tscn listening on the bus, which is where kill
/// reports already are; it touches <see cref="Engine.TimeScale"/> and the
/// owner's camera rig and nothing else.
/// </summary>
public partial class HitStop : Node
{
    [Export]
    public PlayerController Player { get; set; }

    /// <summary>Kills in one report before time stops at all.</summary>
    [Export(PropertyHint.Range, "1,200,1")]
    public int MinKills { get; set; } = 10;

    /// <summary>Real seconds of stop at the threshold, growing with kills to <see cref="MaxSeconds"/>.</summary>
    [Export(PropertyHint.Range, "0,0.3,0.005,suffix:s")]
    public float BaseSeconds { get; set; } = 0.03f;

    [Export(PropertyHint.Range, "0,0.01,0.0005,suffix:s")]
    public float SecondsPerKill { get; set; } = 0.0015f;

    [Export(PropertyHint.Range, "0,0.3,0.005,suffix:s")]
    public float MaxSeconds { get; set; } = 0.09f;

    /// <summary>Time scale during the stop. Not zero: a frozen frame reads as a hitch.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01")]
    public float StopScale { get; set; } = 0.08f;

    /// <summary>
    /// Shortest gap between two stops. Raised from 0.6 s once the bike's
    /// rounds and rockets started killing ten a hit: at 0.6 s a held trigger
    /// slowed time for up to a sixth of every second, which reads as the game
    /// hitching rather than as a punch. One stop every one and a half seconds
    /// still marks the big blast.
    /// </summary>
    [Export(PropertyHint.Range, "0,3,0.05,suffix:s")]
    public float Cooldown { get; set; } = 1.5f;

    private ulong _readyAtMs;
    private ulong _endAtMs;
    private bool _stopped;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (EventBus.Instance != null)
        {
            EventBus.Instance.DamageDealt += OnDamageDealt;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.DamageDealt -= OnDamageDealt;
        }
        if (_stopped)
        {
            Engine.TimeScale = 1.0;
            _stopped = false;
        }
    }

    private void OnDamageDealt(Node3D target, DamageInfo info)
    {
        if (info == null || info.IsSustained || info.KillCount < MinKills || Time.GetTicksMsec() < _readyAtMs)
        {
            return;
        }

        float seconds = Mathf.Min(MaxSeconds, BaseSeconds + ((info.KillCount - MinKills) * SecondsPerKill));
        ulong now = Time.GetTicksMsec();
        _endAtMs = now + (ulong)(seconds * 1000.0f);
        _readyAtMs = _endAtMs + (ulong)(Cooldown * 1000.0f);
        _stopped = true;
        Engine.TimeScale = StopScale;

        Player?.CameraRig?.AddShake(Mathf.Min(0.6f, 0.15f + (info.KillCount * 0.006f)));
    }

    public override void _Process(double delta)
    {
        // Real milliseconds, not delta: delta is scaled by the very time
        // scale this is holding down.
        if (_stopped && Time.GetTicksMsec() >= _endAtMs)
        {
            Engine.TimeScale = 1.0;
            _stopped = false;
        }
    }
}
