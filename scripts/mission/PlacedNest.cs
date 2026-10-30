using System;
using DillControl.Horde;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// A nest that was placed by hand. The vines are ordinary baked vine scenes
/// standing under <see cref="Vines"/>, moved, swapped or deleted in the editor
/// like any prop; this hands each one's pods (the <c>Pod</c> markers inside its
/// model) to the <see cref="PodField"/> when the level loads, so the colony is
/// there to harm, wake and clear. A generated nest does the same from its plan
/// (<see cref="NestBuilder"/>); this is the handbuilt one.
///
/// The colony has no cucumbers on the ground of its own: they all hang here,
/// and drop into the horde when it wakes. So the horde starts empty.
/// </summary>
[GlobalClass]
public partial class PlacedNest : Node
{
    [ExportGroup("Scene Wiring")]
    /// <summary>Where the vines stand (the level's Foliage).</summary>
    [Export] public Node3D Vines { get; set; }
    [Export] public PodField Pods { get; set; }
    [Export] public HordeDirector Horde { get; set; }

    /// <summary>The prefix of a pod marker's name inside a vine's model.</summary>
    [Export] public string PodPrefix { get; set; } = "Pod";

    /// <summary>False when a <see cref="PlacedNestSite"/> stands the vines on a
    /// planet first and calls <see cref="Register"/> itself.</summary>
    [Export] public bool RegisterOnReady { get; set; } = true;

    public int Registered { get; private set; }

    public override void _Ready()
    {
        if (Vines == null || Pods == null)
        {
            GD.PushError($"PlacedNest '{Name}' needs Vines and Pods; no nest.");
            return;
        }
        if (Horde?.Stats != null)
        {
            // A copy: none of its own, room for the whole nest, no respawns.
            var stats = (HordeStats)Horde.Stats.Duplicate();
            stats.AgentCount = 0;
            stats.AgentCapacity = Math.Max(stats.AgentCapacity, 3000);
            stats.RespawnKilledAgents = false;
            stats.AggroRange = 0.0f;
            Horde.Stats = stats;
        }
        // After every vine has entered the tree and the pod field is ready.
        if (RegisterOnReady)
        {
            CallDeferred(MethodName.Register);
        }
    }

    public void Register()
    {
        var found = new System.Collections.Generic.List<(Node3D Root, Vector3[] Pods)>();
        int total = 0;
        foreach (Node child in Vines.GetChildren())
        {
            if (child is not Node3D root || root.GetNodeOrNull("Voxels") == null)
            {
                continue;
            }
            var pods = new System.Collections.Generic.List<Vector3>();
            Transform3D toRoot = root.GlobalTransform.AffineInverse();
            Collect(root.GetNodeOrNull("Model"), toRoot, pods);
            if (pods.Count > 0)
            {
                found.Add((root, pods.ToArray()));
                total += pods.Count;
            }
        }
        Pods.Reserve(total);
        foreach ((Node3D root, Vector3[] pods) in found)
        {
            Pods.AddVine(root, pods);
        }
        Registered = total;
        GD.Print($"[nest] {found.Count} placed vines, {total} pods");
    }

    private void Collect(Node node, Transform3D toRoot, System.Collections.Generic.List<Vector3> pods)
    {
        if (node == null)
        {
            return;
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is Marker3D marker && marker.Name.ToString().StartsWith(PodPrefix, StringComparison.Ordinal))
            {
                pods.Add((toRoot * marker.GlobalTransform).Origin);
            }
            Collect(child, toRoot, pods);
        }
    }
}
