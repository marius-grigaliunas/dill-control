using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Two rigid forearms and nothing else.
///
/// The shoulders sit on this node, which does not swing, so they stay just
/// off the bottom of the view. The wrists are the weapon's grip markers, which
/// do swing, and each forearm is aimed from its shoulder to its wrist every
/// frame. No skeleton and no clip: the same reason a mech's limbs are plain
/// nodes (docs/decisions/0010). The gloves themselves are children of the grip
/// markers, so they follow the weapon without this node knowing about them.
/// </summary>
public partial class MeleeArms : Node3D
{
    [Export]
    public Node3D ShoulderMain { get; set; }

    [Export]
    public Node3D ShoulderOff { get; set; }

    [Export]
    public Node3D WristMain { get; set; }

    [Export]
    public Node3D WristOff { get; set; }

    [Export]
    public Node3D ForearmMain { get; set; }

    [Export]
    public Node3D ForearmOff { get; set; }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        Place(ForearmMain, ShoulderMain, WristMain);
        Place(ForearmOff, ShoulderOff, WristOff);
    }

    /// <summary>
    /// Points the forearm's -Z from the shoulder at the wrist and stretches it
    /// to fit. The mesh is one metre along -Z, centred so it starts at the
    /// shoulder, and the node's Z scale is the distance.
    /// </summary>
    private void Place(Node3D forearm, Node3D shoulder, Node3D wrist)
    {
        if (forearm == null || shoulder == null || wrist == null)
        {
            return;
        }

        Vector3 from = shoulder.GlobalPosition;
        Vector3 to = wrist.GlobalPosition;
        Vector3 delta = to - from;
        float length = delta.Length();
        if (length < 0.02f)
        {
            return;
        }

        Vector3 direction = delta / length;
        Vector3 up = GlobalBasis.Y;
        if (Mathf.Abs(direction.Dot(up)) > 0.95f)
        {
            up = GlobalBasis.X;
        }

        forearm.GlobalPosition = from;
        forearm.LookAt(to, up);
        forearm.Scale = new Vector3(1.0f, 1.0f, length);
    }
}
