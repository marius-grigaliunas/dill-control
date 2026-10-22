using DillControl.Horde;
using DillControl.Weapons;
using Godot;

namespace DillControl.Levels;

/// <summary>
/// Hands the gravity manipulator the horde. The manipulator lives in the
/// player scene, instanced at runtime, and declares what it needs from a crowd
/// as delegates; this node is the one place that knows both sides, so
/// scripts/weapons/ never names scripts/horde/. It finds the gun by its group,
/// once the level says the player exists. The lab's level director did this.
/// </summary>
public partial class ManipulatorBinding : Node
{
    [Export]
    public HordeDirector Horde { get; set; }

    [Export]
    public LevelRoot Level { get; set; }

    public override void _Ready()
    {
        if (Level != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
        }
    }

    // Freed with its horde (a nest lab without one), it must not be called.
    public override void _ExitTree()
    {
        if (Level != null && IsInstanceValid(Level))
        {
            Level.PlayerSpawned -= OnPlayerSpawned;
        }
    }

    private void OnPlayerSpawned(Node3D player) => Bind();

    private void Bind()
    {
        HordeManager horde = Horde?.Manager;
        if (horde == null)
        {
            return;
        }

        foreach (Node node in GetTree().GetNodesInGroup(GravityGun.GroupName))
        {
            if (node is not GravityGun { Manipulator: Manipulator manipulator })
            {
                continue;
            }

            manipulator.ResolveAgent = n => horde.ResolveAgent(n);
            manipulator.AgentMass = handle => horde.AgentMassOf(handle);
            manipulator.MoveAgent = (handle, position) => horde.MoveAgent(handle, position);
            manipulator.ReleaseAgent = (handle, velocity) => horde.ReleaseAgent(handle, velocity);
            manipulator.CollectAgents = (centre, radius, max, into) => horde.CollectAgents(centre, radius, max, into);
            manipulator.AgentPosition = handle => horde.AgentPosition(handle);
            manipulator.DamageAgent = (handle, info) => horde.DamageAgent(handle, info);
        }
    }
}
