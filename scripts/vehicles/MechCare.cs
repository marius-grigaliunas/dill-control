using System.Collections.Generic;
using DillControl.Core;
using DillControl.Mission;
using DillControl.Voxel;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A mech looking after itself (docs/decisions/0046), beside its
/// <see cref="MechCompanion"/>. With voxels gone it asks to be mended, over
/// <see cref="EventBus.MechNeedsRepair"/>, until it is whole; a limb lost
/// whole is ordered new and dropped in a pod, which it walks, limps or crawls
/// to and has fitted at its hinge; badly hurt, it falls back behind the
/// player. Every part fitted goes in the contract's ledger.
/// </summary>
public partial class MechCare : Node
{
    [Export]
    public MechRepairStats Stats { get; set; }

    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public MechCompanion Companion { get; set; }

    /// <summary>The pod a new part comes down in (the company's, <c>prp_drop_pod</c>).</summary>
    [Export]
    public PackedScene PodScene { get; set; }

    /// <summary>What it is doing about its losses, for the asset lab.</summary>
    public string State => _pod == null
        ? $"lost for {_lostFor:0.0} s"
        : $"pod {(_podLanded ? "landed" : "falling")} {(IsInstanceValid(_pod) ? (_pod.GlobalPosition - Walker.GlobalPosition).Slide(Walker.Up).Length() : -1):0.0} m off";

    private float _check;
    private float _lostFor = -1.0f;
    private DropPod _pod;
    private Node3D _podLimb;
    private bool _podLanded;

    public override void _Ready()
    {
        Stats ??= GD.Load<MechRepairStats>("res://resources/vehicles/mech_repair_default.tres");
        if (Walker == null)
        {
            SetPhysicsProcess(false);
        }
    }

    public override void _ExitTree()
    {
        if (_pod != null && IsInstanceValid(_pod))
        {
            _pod.QueueFree();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (Walker.IsDestroyed)
        {
            SetPhysicsProcess(false);
            return;
        }

        if (_pod != null && _podLanded)
        {
            FitWhenThere();
        }

        _check -= dt;
        if (_check > 0.0f)
        {
            return;
        }
        _check = Stats.CheckSeconds;

        List<Node3D> lost = Walker.LostLimbs();
        _lostFor = lost.Count == 0 ? -1.0f : Mathf.Max(0.0f, _lostFor) + Stats.CheckSeconds;
        if (_pod == null && lost.Count > 0 && _lostFor >= Stats.PodDelaySeconds)
        {
            OrderPart(lost[0]);
        }

        if (Walker.MissingVoxels() >= Stats.CallAtMissingVoxels)
        {
            EventBus.EmitMechNeedsRepair(Walker);
        }

        if (Companion != null)
        {
            float core = Walker.Voxels?.PartOf(Walker.Core)?.Health01 ?? 1.0f;
            Companion.FallBackDistance = core < Stats.FallBackBelowCore ? Stats.FallBackDistance : 0.0f;
        }
    }

    /// <summary>A new limb, dropped in a pod a little way off to its side,
    /// clear of the player it stands near.</summary>
    private void OrderPart(Node3D limb)
    {
        if (PodScene?.Instantiate() is not DropPod pod)
        {
            return;
        }
        Vector3 from = Walker.GlobalPosition;
        Vector3 side = Walker.GlobalBasis.X.Slide(Walker.Up).Normalized();
        if (GetViewport().GetCamera3D() is Camera3D camera && (camera.GlobalPosition - from).Dot(side) > 0.0f)
        {
            side = -side;
        }
        // One that cannot get about has it dropped within reach.
        float distance = Walker.CurrentStance == MechWalker.Stance.Sit ? Stats.FitRange * 0.85f : Stats.PodDistance;
        Vector3 landing = from + side * distance;
        _pod = pod;
        _podLimb = limb;
        _podLanded = false;
        Walker.GetParent().AddChild(pod);
        pod.Landed += () => _podLanded = true;
        pod.Drop(new Transform3D(Basis.Identity, landing), null, new ExtractionRules
        {
            DropHeight = Stats.PodDropHeight,
            DropSeconds = Stats.PodDropSeconds,
            LandingRadius = 0.0f,
        });
    }

    /// <summary>Sent to the pod until it is near enough, then the part is fitted.</summary>
    private void FitWhenThere()
    {
        if (!IsInstanceValid(_pod))
        {
            _pod = null;
            return;
        }
        Vector3 at = _pod.GlobalPosition;
        if (Companion != null && !Companion.IsRidden)
        {
            Companion.Errand = at;
        }
        if ((at - Walker.GlobalPosition).Slide(Walker.Up).Length() > Stats.FitRange)
        {
            if (Walker.CurrentStance == MechWalker.Stance.Sit)
            {
                // It lost the last of what it moved with after this one was
                // ordered: another comes down within reach.
                _pod.QueueFree();
                _pod = null;
                _lostFor = Stats.PodDelaySeconds;
                if (Companion != null)
                {
                    Companion.Errand = null;
                }
            }
            return;
        }
        int voxels = Walker.Refit(_podLimb);
        Session.Instance?.Bill($"part: {_podLimb.Name}", Walker.Name, 1, Stats.PartPrice);
        Session.Instance?.Bill("voxels restored", Walker.Name, voxels, Stats.VoxelPrice);
        _pod.QueueFree();
        _pod = null;
        _podLimb = null;
        _lostFor = -1.0f;
        if (Companion != null)
        {
            Companion.Errand = null;
        }
    }
}
