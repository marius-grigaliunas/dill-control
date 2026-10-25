using DillControl.Core;
using DillControl.Mission;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// The storage bot's other job (docs/decisions/0046): it hears
/// <see cref="EventBus.MechNeedsRepair"/>, takes the nearest mech that asked,
/// goes to it and regrows its lost voxels outward from each hinge, itself
/// included when it is the one hurt. Each voxel goes in the contract's
/// ledger, against the mech it went into. (How it transforms to do it is
/// not designed yet; for now it mends as it is.)
/// </summary>
public partial class MechMender : Node
{
    [Export]
    public MechRepairStats Stats { get; set; }

    /// <summary>The bot itself.</summary>
    [Export]
    public MechWalker Walker { get; set; }

    [Export]
    public MechCompanion Companion { get; set; }

    /// <summary>The mech being mended, or null.</summary>
    public MechWalker Patient { get; private set; }

    private float _bank;

    public override void _Ready()
    {
        Stats ??= GD.Load<MechRepairStats>("res://resources/vehicles/mech_repair_default.tres");
        if (EventBus.Instance != null)
        {
            EventBus.Instance.MechNeedsRepair += OnNeedsRepair;
        }
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
        {
            EventBus.Instance.MechNeedsRepair -= OnNeedsRepair;
        }
    }

    private void OnNeedsRepair(Node3D mech)
    {
        if (Walker == null || Walker.IsDestroyed || mech is not MechWalker asking || asking.IsDestroyed)
        {
            return;
        }
        if (Patient == null || !IsInstanceValid(Patient) || Patient.IsDestroyed
            || Distance(asking) < Distance(Patient) * 0.7f)
        {
            Patient = asking;
        }
    }

    private float Distance(Node3D mech) => (mech.GlobalPosition - Walker.GlobalPosition).Slide(Walker.Up).Length();

    public override void _PhysicsProcess(double delta)
    {
        if (Walker == null || Walker.IsDestroyed)
        {
            return;
        }
        if (Patient == null || !IsInstanceValid(Patient) || Patient.IsDestroyed || Patient.MissingVoxels() == 0)
        {
            Release();
            return;
        }

        if (Patient != Walker)
        {
            // Beside it, on the side it came from.
            Vector3 away = (Walker.GlobalPosition - Patient.GlobalPosition).Slide(Walker.Up);
            Vector3 side = away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Back;
            if (Companion != null && !Companion.IsRidden)
            {
                Companion.Errand = Patient.GlobalPosition + side * Stats.RepairRange * 0.6f;
            }
            if (Distance(Patient) > Stats.RepairRange)
            {
                return;
            }
        }

        _bank += Stats.RegrowPerSecond * (float)delta;
        int voxels = (int)_bank;
        if (voxels <= 0)
        {
            return;
        }
        _bank -= voxels;
        int back = Patient.Regrow(voxels);
        Session.Instance?.Bill("voxels restored", Patient.Name, back, Stats.VoxelPrice);
        if (back == 0)
        {
            Release();
        }
    }

    private void Release()
    {
        if (Patient != null && Companion != null)
        {
            Companion.Errand = null;
        }
        Patient = null;
        _bank = 0.0f;
    }
}
