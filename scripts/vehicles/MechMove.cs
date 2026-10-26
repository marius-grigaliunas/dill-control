using System;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A move posed in Blender and baked by the Dill Mech add-on
/// (tools/blender/, docs/decisions/0046): per frame, each part it claims, as
/// its transform in the model's space. The walker plays it over its own
/// motion (<see cref="MechWalker.PlayMove"/>); the parts it does not claim
/// keep walking. Written by the add-on: edit the action and export again.
/// </summary>
[GlobalClass, Tool]
public partial class MechMove : Resource
{
    /// <summary>The mech it was posed on (its .vox's name).</summary>
    [Export]
    public string Mech { get; set; } = "";

    [Export(PropertyHint.Range, "1,240,1")]
    public float Fps { get; set; } = 30.0f;

    [Export]
    public int Frames { get; set; }

    /// <summary>The parts it moves, by node name.</summary>
    [Export]
    public string[] Parts { get; set; } = Array.Empty<string>();

    /// <summary>Frame by frame, part by part: each claimed part's position.</summary>
    [Export]
    public Vector3[] Positions { get; set; } = Array.Empty<Vector3>();

    /// <summary>The same, rotations as quaternions, x y z w.</summary>
    [Export]
    public float[] Rotations { get; set; } = Array.Empty<float>();

    /// <summary>Its timeline markers: 'hit', 'sfx:name', 'shake'.</summary>
    [Export]
    public string[] EventNames { get; set; } = Array.Empty<string>();

    [Export]
    public float[] EventTimes { get; set; } = Array.Empty<float>();

    /// <summary>Seconds it eases in and out over, set in the asset lab;
    /// below 0 the one who plays it says (a combo's chain blend).</summary>
    [Export(PropertyHint.Range, "-1,1,0.01,suffix:s")]
    public float Blend { get; set; } = -1.0f;

    /// <summary>When the asset lab last saved its timing (speed, blend,
    /// markers). The Blender add-on reads the timing back into the action
    /// and keeps this, and will not export over a stamp it has not read.</summary>
    [Export]
    public string TimingStamp { get; set; } = "";

    public float Length => Frames > 1 ? (Frames - 1) / Fps : 0.0f;

    /// <summary>When its event of that name comes, or -1.</summary>
    public float EventTime(string name)
    {
        int i = Array.IndexOf(EventNames, name);
        return i >= 0 && i < EventTimes.Length ? EventTimes[i] : -1.0f;
    }

    /// <summary>Claimed part <paramref name="part"/> at <paramref name="time"/>
    /// seconds, between the frames either side.</summary>
    public Transform3D Sample(int part, float time)
    {
        int parts = Parts.Length;
        if (Frames <= 0 || parts == 0)
        {
            return Transform3D.Identity;
        }
        float f = Mathf.Clamp(time * Fps, 0.0f, Frames - 1);
        int a = (int)f;
        int b = Math.Min(a + 1, Frames - 1);
        float t = f - a;
        return new Transform3D(new Basis(Rotation(a, part).Slerp(Rotation(b, part), t)),
            Positions[a * parts + part].Lerp(Positions[b * parts + part], t));
    }

    private Quaternion Rotation(int frame, int part)
    {
        int i = (frame * Parts.Length + part) * 4;
        return new Quaternion(Rotations[i], Rotations[i + 1], Rotations[i + 2], Rotations[i + 3]).Normalized();
    }
}
