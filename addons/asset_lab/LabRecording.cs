#if TOOLS
using System.Collections.Generic;
using DillControl.Vehicles;
using Godot;

namespace DillControl.Editor;

/// <summary>
/// One cycle of a walker's gait, recorded on the treadmill so the asset lab's
/// timeline can play, scrub, ghost and trace it as it does a move. The walk
/// itself is planned live, step by step, and cannot be sought; a recording
/// of it can. A cycle runs from a footfall to the same foot's next one, with
/// every other stepper down once between, so it loops; with what is left of
/// the walker (a limp, a hop, a crawl), whatever that is.
/// </summary>
public sealed class LabRecording
{
    public const float Fps = 60.0f;

    /// <summary>Its model and everything under it, posed each frame.</summary>
    public Node3D[] Nodes { get; private set; }

    /// <summary>Each frame, each node's own transform.</summary>
    public List<Transform3D[]> Frames { get; } = new();

    /// <summary>How far the belt had carried the floor, each frame.</summary>
    public List<Vector3> Belt { get; } = new();

    /// <summary>Its footfalls: the foot (or crutch arm) and when.</summary>
    public List<(Node3D Node, float Time)> Steps { get; } = new();

    public float Length => Frames.Count > 1 ? (Frames.Count - 1) / Fps : 0.0f;

    /// <summary>The longest it warms up and records, seconds: a walker that
    /// never steps comes back with a short still clip.</summary>
    private const float Warmup = 2.5f;
    private const float Longest = 6.0f;

    /// <summary>
    /// Walks <paramref name="walker"/> at <paramref name="steer"/> (x right,
    /// y forward, length the share of top speed) until its gait has settled,
    /// then records one cycle. It goes on from where the cycle ended.
    /// </summary>
    public static LabRecording Record(MechWalker walker, Vector2 steer, bool sprint)
    {
        var recording = new LabRecording();
        var nodes = new List<Node3D> { walker.Model };
        foreach (Node node in walker.Model.FindChildren("*", nameof(Node3D), true, false))
        {
            nodes.Add((Node3D)node);
        }
        recording.Nodes = nodes.ToArray();

        Basis basis = walker.GlobalBasis;
        walker.MoveIntent = basis * new Vector3(-steer.X, 0.0f, steer.Y);
        walker.FaceDirection = Vector3.Zero;
        walker.Sprint = sprint;
        walker.LabSeekMove(null, 0.0f, 0.0f);

        var landed = new List<Node3D>();
        void OnLanded(Node3D node) => landed.Add(node);
        walker.StepLanded += OnLanded;
        try
        {
            const float dt = 1.0f / Fps;
            for (float t = 0.0f; t < Warmup; t += dt)
            {
                walker.LabStep(dt);
            }
            // Start on a footfall, so the clip starts and ends on one.
            landed.Clear();
            for (float t = 0.0f; t < Longest && landed.Count == 0; t += dt)
            {
                walker.LabStep(dt);
            }
            Node3D first = landed.Count > 0 ? landed[0] : null;
            foreach (Node3D node in landed)
            {
                recording.Steps.Add((node, 0.0f));
            }
            recording.Snap(walker);
            var since = new HashSet<Node3D>();
            for (int frame = 1; first != null && frame < Longest * Fps; frame++)
            {
                landed.Clear();
                walker.LabStep(dt);
                recording.Snap(walker);
                foreach (Node3D node in landed)
                {
                    recording.Steps.Add((node, frame / Fps));
                }
                if (landed.Contains(first) && since.Count > 0)
                {
                    break;
                }
                since.UnionWith(landed);
            }
            if (first == null)
            {
                // Standing: a second of it is the cycle.
                while (recording.Frames.Count < Fps + 1)
                {
                    walker.LabStep(dt);
                    recording.Snap(walker);
                }
            }
        }
        finally
        {
            walker.StepLanded -= OnLanded;
        }
        return recording;
    }

    private void Snap(MechWalker walker)
    {
        var pose = new Transform3D[Nodes.Length];
        for (int i = 0; i < Nodes.Length; i++)
        {
            pose[i] = Nodes[i].Transform;
        }
        Frames.Add(pose);
        Belt.Add(walker.BeltTravel);
    }

    /// <summary>Poses every node as it was <paramref name="time"/> seconds
    /// into the cycle, between frames.</summary>
    public void Apply(float time)
    {
        if (Frames.Count == 0)
        {
            return;
        }
        float f = Mathf.Clamp(time * Fps, 0.0f, Frames.Count - 1);
        int a = (int)f;
        int b = Mathf.Min(a + 1, Frames.Count - 1);
        float w = f - a;
        for (int i = 0; i < Nodes.Length; i++)
        {
            if (IsInstanceValid(Nodes[i]))
            {
                Nodes[i].Transform = w < 1e-4f ? Frames[a][i] : Frames[a][i].InterpolateWith(Frames[b][i], w);
            }
        }
    }

    /// <summary>The belt's travel at <paramref name="time"/>, for the floor.</summary>
    public Vector3 BeltAt(float time)
    {
        if (Belt.Count == 0)
        {
            return Vector3.Zero;
        }
        float f = Mathf.Clamp(time * Fps, 0.0f, Belt.Count - 1);
        int a = (int)f;
        return Belt[a].Lerp(Belt[Mathf.Min(a + 1, Belt.Count - 1)], f - a);
    }

    private static bool IsInstanceValid(GodotObject node) => GodotObject.IsInstanceValid(node);
}
#endif
