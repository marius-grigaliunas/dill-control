using System.Collections.Generic;
using DillControl.Audio;
using DillControl.Core;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// A loose piece of something that broke: a rigid body made of voxels. It is a
/// <see cref="VoxelPart"/> like any other, so it can be shot apart further,
/// and it breaks when it lands hard (<see cref="VoxelDestructionStats.BreakSpeed"/>),
/// crushes what it lands on, and once it has lain still long enough stops
/// being simulated and stays as rubble you can still blow up.
///
/// Made only by <see cref="VoxelDebris"/>, which counts them.
/// </summary>
public partial class VoxelChunk : RigidBody3D, IDamageable, IBlastSurface
{
    private VoxelPart _part;
    private VoxelDestructionStats _stats;
    private VoxelGrid _grid;
    private Color[] _palette;
    private float[] _emission;
    private Material _material;
    private Vector3 _lastVelocity;
    private float _age;
    private float _still;
    private float _crushCooldown;
    private bool _breaking;

    public bool IsSettled { get; private set; }

    public bool IsAlive => _part != null && !_part.IsEmpty;

    private readonly List<VoxelPart> _parts = new();

    public void Setup(VoxelGrid grid, Color[] palette, float[] emission, Material material, VoxelDestructionStats stats)
    {
        _grid = grid;
        _palette = palette;
        _emission = emission;
        _material = material;
        _stats = stats;
    }

    public override void _Ready()
    {
        CollisionLayer = GameLayers.Mask(GameLayers.Prop);
        CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop);
        float volume = _grid.SolidCount * _grid.VoxelSize * _grid.VoxelSize * _grid.VoxelSize;
        Mass = Mathf.Clamp(volume * _stats.Density, 1.0f, 2_000_000.0f);
        CanSleep = true;
        // Heavy: it lands and stays, grinds rather than skates, and a block
        // on its corner falls over instead of spinning on.
        PhysicsMaterialOverride = new PhysicsMaterial { Friction = _stats.Friction, Bounce = 0.0f, Rough = true };
        AngularDampMode = DampMode.Combine;
        AngularDamp = _stats.AngularDamp;

        _part = new VoxelPart
        {
            Name = "Voxels",
            CollisionOwner = this,
            CollisionStep = CollisionStepFor(_grid, _stats.MaxChunkBoxes),
            Velocity = () => LinearVelocity,
        };
        _part.Setup(_grid, VoxelAnchor.Largest, _palette, _emission, _material, _stats);
        _part.Emptied += _ => CallDeferred(Node.MethodName.QueueFree);
        AddChild(_part);
        _parts.Add(_part);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;
        _crushCooldown -= dt;

        Vector3 velocity = LinearVelocity;
        float change = (_lastVelocity - velocity).Length();
        if (_age > 0.15f && !_breaking)
        {
            if (change > _stats.CrushSpeed && _crushCooldown <= 0.0f)
            {
                Crush(change);
            }
            if (change > _stats.BreakSpeed && _grid.SolidCount >= _stats.MinBreakVoxels)
            {
                Break(_lastVelocity);
                return;
            }
        }
        _lastVelocity = velocity;

        _still = Sleeping || velocity.LengthSquared() < 0.01f ? _still + dt : 0.0f;
        if (!IsSettled && _still > _stats.SettleSeconds)
        {
            // Rubble now: no simulation, still breakable.
            IsSettled = true;
            FreezeMode = FreezeModeEnum.Static;
            Freeze = true;
        }
    }

    /// <summary>
    /// The finest collision that stays within <paramref name="maxBoxes"/>:
    /// exact voxels if it can, so pieces of one fracture start touching rather
    /// than overlapping (coarse boxes overhang and would throw them apart).
    /// </summary>
    private static int CollisionStepFor(VoxelGrid grid, int maxBoxes)
    {
        var boxes = new List<(Vector3, Vector3)>();
        int step = grid.SolidCount > 4000 ? 2 : 1;
        for (; step < 8; step *= 2)
        {
            boxes.Clear();
            grid.Boxes(Vector3I.Zero, grid.Size, step, boxes);
            if (boxes.Count <= maxBoxes)
            {
                break;
            }
        }
        return step;
    }

    /// <summary>Voxels in it: how much it matters, when the cap needs room.</summary>
    public int VoxelCount => _grid?.SolidCount ?? 0;

    /// <summary>Lands hard on something: it hurts what is under it, through
    /// the same blast every explosive uses, so the horde, the player and the
    /// buildings take it with no special case. Its reach is its own size: a
    /// storey-sized block lands on everything under its footprint, and what
    /// it lands on is killed (<see cref="VoxelDestructionStats.CrushDamageMax"/>).
    /// Other loose pieces ignore it (<see cref="ApplyDamage"/>).</summary>
    private void Crush(float speed)
    {
        _crushCooldown = 0.4f;
        float damage = Mathf.Min(Mass * speed * _stats.CrushDamagePerMomentum, _stats.CrushDamageMax);
        if (damage < 1.0f)
        {
            return;
        }
        float across = Mathf.Pow(_grid.SolidCount, 1.0f / 3.0f) * _grid.VoxelSize;
        AudioDirector.PlayAt(across > 3.0f ? Sfx.VoxelCollapse : Sfx.RubbleImpact, GlobalPosition);
        // From its centre of mass to past its farthest corner.
        float reach = (new Vector3(_grid.Size.X, _grid.Size.Y, _grid.Size.Z) * _grid.VoxelSize).Length() * 0.5f + 1.5f;
        var blast = new Blast.Settings
        {
            Centre = GlobalPosition,
            InnerRadius = reach * 0.7f,
            Radius = reach,
            Damage = damage,
            MinMultiplier = 0.5f,
            Up = Vector3.Up,
            Source = this,
            SourceRid = GetRid(),
            NoCrater = true,
        };
        Blast.ApplyDamage(GetWorld3D().DirectSpaceState, blast);
    }

    private void Break(Vector3 velocity)
    {
        _breaking = true;
        var normal = new Vector3((float)GD.RandRange(-1, 1), (float)GD.RandRange(-1, 1), (float)GD.RandRange(-1, 1));
        if (normal.LengthSquared() < 0.01f)
        {
            normal = Vector3.Right;
        }
        // The pieces keep some of the speed it had, spread a little apart: into
        // blocks half its size if it is big enough, else cracked in two.
        _part.Velocity = () => velocity * 0.4f;
        // Crumbling makes up to eight pieces; with no room for them under the
        // cap it would only push out pieces still falling, so it cracks instead.
        bool room = VoxelDebris.Current != null && VoxelDebris.Current.HasRoom(8);
        if (!room || !_part.Crumble(Vector3.Zero))
        {
            _part.Shatter(normal, normal.Normalized() * 1.5f);
        }
    }

    /// <summary>Gone to dust: the debris cap needed the room.</summary>
    public void Dissolve()
    {
        if (_part != null && !_part.IsEmpty && VoxelDebris.Current != null)
        {
            VoxelDebris.Current.Dust(GlobalPosition, Vector3.Up, LinearVelocity,
                VoxelDebris.AverageColour(_grid, _palette), Mathf.Pow(_grid.SolidCount, 1.0f / 3.0f) * _grid.VoxelSize);
        }
        QueueFree();
    }

    public void ApplyDamage(DamageInfo info)
    {
        // Rubble landing on rubble does not grind it to dust: a collapse
        // leaves a pile, not a cloud.
        if (info?.Source is VoxelChunk)
        {
            return;
        }
        if (IsSettled && info.IsSplash)
        {
            // A blast wakes rubble up.
            IsSettled = false;
            Freeze = false;
            _still = 0.0f;
        }
        VoxelDamage.Apply(info, _parts, _stats, this);
    }

    public Vector3 NearestBlastPoint(Vector3 from) => VoxelDamage.NearestPoint(_parts, from, GlobalPosition);
}
