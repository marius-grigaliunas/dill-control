using System;
using Godot;
using DillControl.Core;
using DillControl.Horde;
using DillControl.Weapons;

namespace DillControl.Enemies;

/// <summary>
/// Every shot one kind of shooter has in the air, as flat arrays and one
/// multimesh: the spitters' brine, and the compound towers' shot when they
/// come across.
///
/// The <c>Rocket</c> argument, at a smaller size: a shot wants exactly one
/// answer from physics -- what did I hit first -- so it integrates itself
/// instead of being a rigid body. It has a proximity fuse against the one thing
/// it is aimed at, because a bike doing sixty and a shot doing ninety pass each
/// other in a frame and a ray against a capsule would call most of those a
/// miss.
///
/// Collision is tiered so a horde's worth of spitters is affordable. A shot
/// near the target sweeps one World ray a physics frame, so cover close to the
/// player is real cover; a shot further out than <see cref="RayRange"/> only
/// asks the ground, through <see cref="Ground"/>, whether it has gone into it.
/// That is the same trade the horde itself makes: what nobody is close enough
/// to see collide does not need a physics query.
///
/// There is no damage anywhere in here. A hit is reported to whoever owns the
/// target, who decides what it costs.
/// </summary>
public partial class ShotPool : Node3D
{
    private const int FloatsPerInstance = 12;

    /// <summary>What a shot fuses against this frame.</summary>
    public struct ShotTarget
    {
        public bool Valid;
        public Vector3 Position;
    }

    private Vector3[] _position = Array.Empty<Vector3>();
    private Vector3[] _velocity = Array.Empty<Vector3>();
    private float[] _life = Array.Empty<float>();
    private float[] _buffer = Array.Empty<float>();
    private int _count;
    private int _capacity;

    private MultiMeshInstance3D _mesh;
    private Rid _multimeshRid;
    private PackedScene _impactScene;

    public Func<ShotTarget> Target { get; set; }

    /// <summary>A shot reached the target: point, the shove along its path, camera trauma.</summary>
    public Action<Vector3, Vector3, float> Hit { get; set; }

    /// <summary>A shot struck the world or the ground. For sounds and splashes, which the pool does not own.</summary>
    public Action<Vector3> Struck { get; set; }

    /// <summary>
    /// The ground the shots fall toward and land in, for shots past
    /// <see cref="RayRange"/>. Null means every shot rays, whatever its range,
    /// and gravity points down.
    /// </summary>
    public IHordeGround Ground { get; set; }

    public float HitRadius { get; set; } = 1.6f;
    public float Impulse { get; set; } = 7.0f;
    public float Trauma { get; set; } = 0.22f;
    public float Lifetime { get; set; } = 4.0f;

    /// <summary>Gravity along the ground's down, m/s². Zero flies straight.</summary>
    public float Gravity { get; set; }

    /// <summary>Shots this close to the target sweep a physics ray; further ones only test the heightfield.</summary>
    public float RayRange { get; set; } = 40.0f;

    public int ActiveCount => _count;

    /// <summary>Hits delivered this session. For the bench, which needs a number rather than a feeling.</summary>
    public int HitCount { get; private set; }

    /// <summary>Shots fired this session.</summary>
    public int FiredCount { get; private set; }

    /// <summary>
    /// Builds the pool and its mesh. <paramref name="radius"/> is the drawn
    /// size; unshaded and over-bright, so a shot reads against sky and ground
    /// alike at range -- the whole point of a slow shot is that you can see it
    /// coming.
    /// </summary>
    public void Build(int capacity, Color colour, float radius)
    {
        _capacity = Mathf.Max(1, capacity);
        _position = new Vector3[_capacity];
        _velocity = new Vector3[_capacity];
        _life = new float[_capacity];
        _buffer = new float[_capacity * FloatsPerInstance];
        _count = 0;

        _mesh?.QueueFree();
        MultiMesh multimesh = new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new SphereMesh { Radius = radius, Height = radius * 2.0f, RadialSegments = 8, Rings = 4 },
            InstanceCount = _capacity,
            VisibleInstanceCount = 0,
        };
        _mesh = new MultiMeshInstance3D
        {
            Name = "Shots",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = multimesh,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = colour * 2.2f,
            },

            // Shots fly anywhere over the planet, and a multimesh is only
            // ever culled whole.
            CustomAabb = new Aabb(new Vector3(-5000, -5000, -5000), new Vector3(10000, 10000, 10000)),
        };
        AddChild(_mesh);
        _multimeshRid = multimesh.GetRid();

        _impactScene ??= GD.Load<PackedScene>("res://scenes/weapons/fx_impact.tscn");
    }

    /// <summary>Launches one shot. False when the pool is full, which the caller treats as a shot not taken.</summary>
    public bool Fire(Vector3 from, Vector3 velocity)
    {
        if (_count >= _capacity)
        {
            return false;
        }

        _position[_count] = from;
        _velocity[_count] = velocity;
        _life[_count] = Lifetime;
        _count++;
        FiredCount++;
        return true;
    }

    public void Clear()
    {
        _count = 0;
        if (_mesh != null)
        {
            _mesh.Multimesh.VisibleInstanceCount = 0;
        }
    }

    /// <summary>
    /// One query for every near shot, kept rather than made each frame. A
    /// query is a ref-counted engine object, and ones left to the garbage
    /// collector can be finalized after the engine has shut down.
    /// </summary>
    private PhysicsRayQueryParameters3D _query;

    public override void _ExitTree()
    {
        _query?.Dispose();
        _query = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_count == 0)
        {
            return;
        }

        float dt = (float)delta;
        ShotTarget target = Target?.Invoke() ?? default;
        PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
        float rayRangeSquared = RayRange * RayRange;

        int i = 0;
        while (i < _count)
        {
            Vector3 from = _position[i];
            if (Gravity > 0.0f)
            {
                _velocity[i] -= (Ground?.UpAt(from) ?? Vector3.Up) * (Gravity * dt);
            }
            Vector3 to = from + (_velocity[i] * dt);
            _life[i] -= dt;

            bool remove = _life[i] <= 0.0f;

            // The fuse first: a shot passing within reach of the target this
            // frame is a hit whether or not a ray would have touched it.
            if (!remove && target.Valid)
            {
                Vector3 closest = ClosestOnSegment(from, to, target.Position);
                if (closest.DistanceSquaredTo(target.Position) <= HitRadius * HitRadius)
                {
                    HitCount++;
                    Hit?.Invoke(closest, _velocity[i].Normalized() * Impulse, Trauma);
                    ImpactEffect.Spawn(_impactScene, this, closest);
                    remove = true;
                }
            }

            if (!remove)
            {
                bool near = Ground == null || !target.Valid
                            || from.DistanceSquaredTo(target.Position) <= rayRangeSquared;

                if (near)
                {
                    _query ??= new PhysicsRayQueryParameters3D
                    {
                        CollisionMask = GameLayers.Mask(GameLayers.World),
                        CollideWithAreas = false,
                        CollideWithBodies = true,
                    };
                    _query.From = from;
                    _query.To = to;
                    Godot.Collections.Dictionary hit = space.IntersectRay(_query);
                    if (hit.Count > 0)
                    {
                        Vector3 point = (Vector3)hit["position"];
                        ImpactEffect.Spawn(_impactScene, this, point, (Vector3)hit["normal"]);
                        Struck?.Invoke(point);
                        remove = true;
                    }
                }

                // On the planet terrain colliders only exist near the camera,
                // so even a near shot asks the ground itself, or it falls
                // through where nobody happens to be standing.
                if (!remove && Ground != null)
                {
                    Vector3 surface = Ground.GroundBelow(to, out _);
                    if ((to - surface).Dot(Ground.UpAt(surface)) <= 0.0f)
                    {
                        Struck?.Invoke(surface);
                        remove = true;
                    }
                }
            }

            if (remove)
            {
                // Swap-remove: order means nothing here.
                _count--;
                _position[i] = _position[_count];
                _velocity[i] = _velocity[_count];
                _life[i] = _life[_count];
                continue;
            }

            _position[i] = to;
            i++;
        }

        WriteInstances();
    }

    /// <summary>One buffer write for the whole pool, the horde's own argument against a call per instance.</summary>
    private void WriteInstances()
    {
        if (_mesh == null)
        {
            return;
        }

        for (int i = 0; i < _count; i++)
        {
            int o = i * FloatsPerInstance;
            Vector3 p = _position[i];
            _buffer[o + 0] = 1.0f; _buffer[o + 1] = 0.0f; _buffer[o + 2] = 0.0f; _buffer[o + 3] = p.X;
            _buffer[o + 4] = 0.0f; _buffer[o + 5] = 1.0f; _buffer[o + 6] = 0.0f; _buffer[o + 7] = p.Y;
            _buffer[o + 8] = 0.0f; _buffer[o + 9] = 0.0f; _buffer[o + 10] = 1.0f; _buffer[o + 11] = p.Z;
        }

        RenderingServer.MultimeshSetBuffer(_multimeshRid, _buffer);
        _mesh.Multimesh.VisibleInstanceCount = _count;
    }

    private static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 point)
    {
        Vector3 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-8f)
        {
            return a;
        }
        float t = Mathf.Clamp((point - a).Dot(ab) / lengthSquared, 0.0f, 1.0f);
        return a + (ab * t);
    }
}
