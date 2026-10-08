using System;
using System.Collections.Generic;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// Makes an imported voxel model destructible. Put one in the wrapper scene
/// beside the model; at start it turns every part the importer gave voxels to
/// into a <see cref="VoxelPart"/>, and from then on the model breaks the way
/// every voxel model in the game breaks (docs/decisions/0011).
///
/// - <see cref="Static"/>: a building or a prop. Its parts hang from the
///   ground and get box collision of their own, on World and Prop, that
///   follows every hole; the root needs no collision of its own.
/// - Otherwise: something that moves on a body of its own (a mech on its
///   hull). Parts listed in <see cref="Hinged"/> hang from their hinge, the
///   rest hold together as their biggest piece. Hits reach it through the
///   owner, which forwards <see cref="IDamageable"/> here.
/// </summary>
public partial class VoxelBody : Node
{
    [Export]
    public Node3D Model { get; set; }

    [Export]
    public VoxelDestructionStats Stats { get; set; }

    /// <summary>A building: anchored to the ground, collision of its own.</summary>
    [Export]
    public bool Static { get; set; }

    /// <summary>Parts that hang from their pivot: legs, a turret, arms.</summary>
    [Export]
    public Godot.Collections.Array<Node3D> Hinged { get; set; } = new();

    /// <summary>Where thrown-off pieces get their speed. Defaults to still.</summary>
    public Func<Vector3> Velocity { get; set; } = () => Vector3.Zero;

    /// <summary>Raised when a part has nothing left or nothing holds it.</summary>
    public event Action<Node3D, VoxelPart> PartEmptied;

    public IReadOnlyList<VoxelPart> Parts => _parts;

    private readonly List<VoxelPart> _parts = new();
    private readonly Dictionary<Node3D, VoxelPart> _byNode = new();

    public VoxelPart PartOf(Node3D node) => node != null && _byNode.TryGetValue(node, out VoxelPart part) ? part : null;

    public bool IsAlive
    {
        get
        {
            foreach (VoxelPart part in _parts)
            {
                if (!part.IsEmpty)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public override void _Ready()
    {
        Stats ??= GD.Load<VoxelDestructionStats>(VoxelDestructionStats.DefaultPath);
        if (Model == null || !Model.HasMeta(VoxelMeta.VoxelSize))
        {
            GD.PushWarning($"VoxelBody '{Name}': its model was not imported with voxels; reimport the .vox.");
            return;
        }

        float voxelSize = Model.GetMeta(VoxelMeta.VoxelSize).AsSingle();
        Color[] palette = Model.GetMeta(VoxelMeta.Palette).AsColorArray();
        float[] emission = Model.GetMeta(VoxelMeta.Emission).AsFloat32Array();
        var hinged = new HashSet<Node3D>();
        foreach (Node3D node in Hinged)
        {
            hinged.Add(node);
        }

        foreach (MeshInstance3D host in Hosts(Model))
        {
            var grid = new VoxelGrid(
                host.GetMeta(VoxelMeta.Dims).AsVector3I(),
                (byte[])host.GetMeta(VoxelMeta.Cells).AsByteArray().Clone(),
                host.GetMeta(VoxelMeta.Origin).AsVector3(),
                voxelSize);
            Material material = host.Mesh?.SurfaceGetMaterial(0) ?? host.GetSurfaceOverrideMaterial(0);
            host.Mesh = null;

            VoxelAnchor anchor = Static ? VoxelAnchor.Ground : hinged.Contains(host) ? VoxelAnchor.Hinge : VoxelAnchor.Largest;
            var part = new VoxelPart { Name = "Voxels", Velocity = () => Velocity() };
            part.Setup(grid, anchor, palette, emission, material, Stats);

            if (Static)
            {
                var collider = new VoxelCollider
                {
                    Name = "Collision",
                    Body = this,
                    CollisionLayer = GameLayers.Mask(GameLayers.World, GameLayers.Prop),
                    CollisionMask = 0,
                };
                host.AddChild(collider);
                part.CollisionOwner = collider;
            }

            host.AddChild(part);
            part.Emptied += p => PartEmptied?.Invoke(host, p);
            _parts.Add(part);
            _byNode[host] = part;
        }
    }

    private static IEnumerable<MeshInstance3D> Hosts(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is MeshInstance3D mesh && mesh.HasMeta(VoxelMeta.Cells))
            {
                yield return mesh;
            }
            foreach (MeshInstance3D nested in Hosts(child))
            {
                yield return nested;
            }
        }
    }

    public void ApplyDamage(DamageInfo info, Node3D target) => VoxelDamage.Apply(info, _parts, Stats, target);

    public Vector3 NearestBlastPoint(Vector3 from) =>
        VoxelDamage.NearestPoint(_parts, from, Model?.GlobalPosition ?? Vector3.Zero);

    /// <summary>Everything left falls apart as chunks: the thing is destroyed.</summary>
    public void BreakAll(Vector3 push)
    {
        foreach (VoxelPart part in _parts)
        {
            part.BreakAway(push);
        }
    }
}

/// <summary>
/// The collision a static voxel body's part gets, and what weapons hit: it
/// forwards damage to its <see cref="VoxelBody"/>.
/// </summary>
public partial class VoxelCollider : StaticBody3D, IDamageable, IBlastSurface
{
    public VoxelBody Body { get; set; }

    public bool IsAlive => Body != null && Body.IsAlive;

    public void ApplyDamage(DamageInfo info) => Body?.ApplyDamage(info, this);

    public Vector3 NearestBlastPoint(Vector3 from) => Body?.NearestBlastPoint(from) ?? GlobalPosition;
}
