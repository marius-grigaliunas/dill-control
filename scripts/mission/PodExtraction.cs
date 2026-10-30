using Godot;
using DillControl.Core;
using DillControl.Levels;
using DillControl.Planet;
using DillControl.UI;

namespace DillControl.Mission;

/// <summary>
/// The way home (decision 0054): once the colony's contract is cleared, the
/// company sends a landing pod down to a place picked at random on the planet,
/// not the arrival pod's. It lands empty and open; stepping into its hatch
/// boards it, and it climbs away with the player watching the ground fall from
/// its window. Then the contract is complete.
///
/// The site is a roll on the "extraction" stream between
/// <see cref="ExtractionRules.ExtractionMinDistance"/> and
/// <see cref="ExtractionRules.ExtractionMaxDistance"/> from the player, on ground
/// flat enough to stand a pod on, clear of the arrival pod and of what the
/// level lists in <see cref="AvoidRoots"/> (the nest, the hooks). With
/// <see cref="Seed"/> at zero the roll differs every run.
/// </summary>
[GlobalClass]
public partial class PodExtraction : Node
{
    [Export] public ExtractionRules Rules { get; set; }
    [Export] public PackedScene PodScene { get; set; }

    /// <summary>The roll's seed; zero rolls a new site every run.</summary>
    [Export] public long Seed { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export] public LevelRoot Level { get; set; }
    [Export] public ColonyMission Mission { get; set; }
    [Export] public PlanetTerrain Planet { get; set; }
    /// <summary>Where the arrival pod came down: the way home is somewhere else.</summary>
    [Export] public Marker3D LandingSite { get; set; }
    /// <summary>Nodes whose children a pod must not land on: the nest, the hooks.</summary>
    [Export] public Godot.Collections.Array<Node3D> AvoidRoots { get; set; } = new();

    public enum Phase { Waiting, Counting, Inbound, Open, Leaving, Done }

    public Phase Current { get; private set; } = Phase.Waiting;

    /// <summary>Where the pod came or is coming down, once picked.</summary>
    public Vector3 SiteAt { get; private set; }

    /// <summary>A site has been picked.</summary>
    public bool HasSite { get; private set; }

    private ExtractionHud _hud;
    private DropPod _pod;
    private float _timer;
    private float _time;

    public override void _Ready()
    {
        if (Rules == null || PodScene == null || Level == null || Mission == null || Planet == null || LandingSite == null)
        {
            GD.PushError($"PodExtraction '{Name}' needs Rules, PodScene, Level, Mission, Planet and LandingSite; no way home.");
            SetProcess(false);
            return;
        }
        var layer = new CanvasLayer { Name = "ExtractionHud", Layer = 8 };
        _hud = new ExtractionHud { Name = "Hud" };
        _hud.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_hud);
        AddChild(layer);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Current != Phase.Done)
        {
            _time += dt;
        }

        switch (Current)
        {
            case Phase.Waiting:
                if (Mission.Current == ColonyMission.Phase.Cleared)
                {
                    _timer = Rules.ExtractionDelay;
                    SetPhase(Phase.Counting);
                }
                break;
            case Phase.Counting:
                _timer -= dt;
                if (_timer <= 0.0f)
                {
                    Send();
                }
                break;
            case Phase.Done:
                if (Input.IsActionJustPressed("reload"))
                {
                    GetTree().ReloadCurrentScene();
                }
                break;
        }

        bool podValid = _pod != null && IsInstanceValid(_pod);
        _hud.Show(new ExtractionHud.Readout
        {
            Phase = Current,
            Countdown = Mathf.Max(0.0f, _timer),
            PodAt = podValid ? _pod.GlobalPosition + (_pod.GlobalBasis.Y * 4.0f) : null,
            Time = _time,
            Starting = Mission.Starting,
            Alive = Mission.Alive,
            Bonus = Mission.Bonus,
        });
    }

    /// <summary>For the photograph scripts: the contract counts as cleared now.</summary>
    public void BenchClear()
    {
        if (Current == Phase.Waiting)
        {
            _timer = Rules.ExtractionDelay;
            SetPhase(Phase.Counting);
        }
    }

    private void Send()
    {
        Node3D player = Level.Player;
        Vector3 from = player != null && IsInstanceValid(player) ? player.GlobalPosition : LandingSite.GlobalPosition;
        Transform3D site = PickSite(from);
        SiteAt = site.Origin;
        HasSite = true;

        _pod = PodScene.Instantiate<DropPod>();
        Level.AddChild(_pod);
        _pod.Landed += () =>
        {
            _pod.Open();
            SetPhase(Phase.Open);
        };
        _pod.Boarded += rider =>
        {
            SetPhase(Phase.Leaving);
            _pod.LiftOff(rider);
        };
        _pod.Gone += () => SetPhase(Phase.Done);
        _pod.Drop(site, null, Rules);
        SetPhase(Phase.Inbound);
    }

    /// <summary>
    /// A rolled place on the ground at the right distance from
    /// <paramref name="from"/>, flat and clear; the flattest tried if none is both.
    /// </summary>
    private Transform3D PickSite(Vector3 from)
    {
        PlanetHeightfield heights = Planet.Heights;
        ulong seed = Seed != 0 ? (ulong)Seed : Time.GetTicksUsec();
        SeededRandom roll = SeededRandom.Stream(seed, "extraction");
        Vector3 up = from.Normalized();
        Vector3 east = up.Cross(Mathf.Abs(up.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        Vector3 landing = LandingSite.GlobalPosition;
        const float MaxSlope = 0.12f;

        Vector3 bestDirection = up;
        float bestSlope = float.MaxValue;
        bool found = false;
        for (int i = 0; i < 80 && !found; i++)
        {
            Vector3 tangent = east.Rotated(up, roll.Range(0.0f, Mathf.Tau));
            float arc = roll.Range(Rules.ExtractionMinDistance, Mathf.Max(Rules.ExtractionMinDistance, Rules.ExtractionMaxDistance)) / heights.Radius;
            Vector3 direction = ((up * Mathf.Cos(arc)) + (tangent * Mathf.Sin(arc))).Normalized();
            Vector3 point = heights.SurfacePoint(direction);
            if (point.DistanceTo(landing) < Rules.ExtractionClearOfLanding || NearProps(point))
            {
                continue;
            }
            float slope = heights.SlopeAt(direction, 6.0f);
            if (slope < bestSlope)
            {
                bestSlope = slope;
                bestDirection = direction;
            }
            found = slope <= MaxSlope;
        }

        Vector3 ground = heights.SurfacePoint(bestDirection);
        // The hatch faces the player, so they run into it.
        Basis facing = PlanetHeightfield.UprightBasis(bestDirection, from - ground);
        GD.Print(System.FormattableString.Invariant(
            $"[extraction] pod to {up.AngleTo(bestDirection) * heights.Radius:F0} m from the player, slope {Mathf.RadToDeg(bestSlope):F1} deg, seed {seed}"));
        return new Transform3D(facing, ground);
    }

    private bool NearProps(Vector3 point)
    {
        float clear = Rules.ExtractionClearOfProps;
        if (clear <= 0.0f)
        {
            return false;
        }
        foreach (Node3D root in AvoidRoots)
        {
            if (root == null || !IsInstanceValid(root))
            {
                continue;
            }
            foreach (Node child in root.GetChildren())
            {
                if (child is Node3D prop && prop.GlobalPosition.DistanceTo(point) < clear)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private void SetPhase(Phase phase)
    {
        Current = phase;
        GD.Print(System.FormattableString.Invariant($"[extraction] {phase} at {_time:F0} s"));
    }
}
