using System.Collections.Generic;
using Godot;

namespace DillControl.Voxel;

/// <summary>
/// Dust clouds where a destructible body lost material, in place of the rubble
/// cubes that used to be thrown there.
///
/// The cubes were rigid bodies: they cost a Jolt body each, they piled up into
/// the debris cap, and they read as toy blocks rather than as a building coming
/// apart -- reported as "they don't offer anything". Destruction needs an event
/// the eye can follow, and a billowing cloud in the colour of what broke is
/// that event, for the price of a particle emitter and no physics at all.
///
/// A fixed pool of one-shot emitters, reused oldest-first, so the cost has a
/// ceiling however much is being destroyed. Requests made in one frame are
/// merged by distance before anything is emitted: a collapsing tower reports
/// two dozen lost pieces in one frame, and that should be a few big clouds
/// rather than two dozen small ones drawn over the same pixels.
///
/// PLACEHOLDER look -- untextured radial billboards, as the explosion's own dust
/// is. The pooling and the merging are the part worth keeping.
/// </summary>
public partial class DustField : Node3D
{
    /// <summary>Emitters in the pool: the most clouds alive at once.</summary>
    public int PoolSize { get; set; } = 24;

    /// <summary>Requests closer than this in one frame become one cloud.</summary>
    public float MergeDistance { get; set; } = 4.0f;

    /// <summary>
    /// Most clouds started in one frame. Past it the frame's requests are
    /// merged again at twice the distance, and again, until they fit -- so a
    /// collapsing tower becomes a few big clouds along its whole height rather
    /// than an arbitrary few of two dozen small ones.
    /// </summary>
    public int MaxPerFrame { get; set; } = 6;

    /// <summary>Cloud scale per metre of the cube root of the material lost.</summary>
    public float SizeScale { get; set; } = 1.6f;

    private const float MinScale = 0.6f;

    /// <summary>
    /// The biggest cloud, as a scale on the emitter. A skyscraper coming down
    /// loses a few thousand cubic metres and should raise a cloud the size of a
    /// building; a rifle round should raise a puff.
    /// </summary>
    private const float MaxScale = 8.0f;

    private const int ParticlesPerCloud = 16;

    /// <summary>Lifetime of the smallest cloud. Bigger ones linger longer, as dust does.</summary>
    private const float CloudLifetime = 2.6f;

    /// <summary>Extra seconds of life per unit of scale past one.</summary>
    private const float LifetimePerScale = 0.45f;

    /// <summary>
    /// How much faster a cloud sinks per metre per second its material was
    /// falling. Collapse dust drops with the structure it came off; dust
    /// knocked out of a wall mostly hangs.
    /// </summary>
    private const float SinkPerFallSpeed = 0.9f;

    /// <summary>How far a cloud's colour leans from the broken material toward plain dust.</summary>
    private const float DustBlend = 0.45f;

    private static readonly Color DustColour = new(0.58f, 0.53f, 0.44f);

    private struct Cluster
    {
        public Vector3 Position;
        public Vector3 Up;
        public Color Colour;
        public Vector3 Velocity;
        public float Volume;
    }

    private readonly List<Cluster> _pending = new();
    private readonly List<GpuParticles3D> _pool = new();
    private readonly List<StandardMaterial3D> _materials = new();
    private readonly List<ParticleProcessMaterial> _processes = new();
    private int _next;

    public override void _Ready()
    {
        Texture2D puff = BuildPuffTexture();

        for (int i = 0; i < PoolSize; i++)
        {
            // One material per slot, because the tint is per cloud and a shared
            // material would repaint every live cloud the colour of the newest.
            // And one process material per slot, for the same reason: how fast a
            // cloud sinks depends on what it came from.
            ParticleProcessMaterial process = BuildProcessMaterial();

            StandardMaterial3D material = new()
            {
                AlbedoTexture = puff,
                AlbedoColor = DustColour,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                VertexColorUseAsAlbedo = true,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                BillboardKeepScale = true,
                DisableReceiveShadows = true,
            };

            GpuParticles3D emitter = new()
            {
                Name = $"Dust{i}",
                Amount = ParticlesPerCloud,
                Lifetime = CloudLifetime,
                OneShot = true,
                Explosiveness = 0.8f,
                Emitting = false,
                Visible = false,

                // Simulated in the emitter's own frame, which is turned so its
                // +Y is local up: the dust settles toward this planet's surface
                // rather than toward world -Y, and scaling the node scales the
                // throw along with the sprites.
                LocalCoords = true,
                ProcessMaterial = process,
                DrawPass1 = new QuadMesh { Size = new Vector2(1.8f, 1.8f) },
                MaterialOverride = material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };

            AddChild(emitter);
            _pool.Add(emitter);
            _materials.Add(material);
            _processes.Add(process);
        }
    }

    /// <summary>
    /// Asks for dust where a piece of material was lost this frame. Cheap: it
    /// only joins or opens a pending cluster, and the clouds are started once
    /// per frame from <see cref="_Process"/>.
    /// </summary>
    /// <param name="velocity">What the piece was doing as it left -- a collapse reports its fall.</param>
    /// <param name="size">Edge length of the piece lost, in metres.</param>
    public void Request(Vector3 position, Vector3 up, Vector3 velocity, Color colour, float size)
    {
        Cluster incoming = new()
        {
            Position = position,
            Up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up,
            Velocity = velocity,
            Colour = colour,
            Volume = size * size * size,
        };

        if (!MergeInto(_pending, incoming, MergeDistance))
        {
            _pending.Add(incoming);
        }
    }

    /// <summary>Stops every cloud. Used by the session reset.</summary>
    public void Clear()
    {
        _pending.Clear();
        foreach (GpuParticles3D emitter in _pool)
        {
            emitter.Emitting = false;
            emitter.Visible = false;
        }
    }

    public override void _Process(double delta)
    {
        if (_pending.Count == 0 || _pool.Count == 0)
        {
            return;
        }

        // Over budget: merge again at twice the distance until it fits, rather
        // than dropping clusters. Dropping kept an arbitrary few of a tower's
        // two dozen and left the rest of its height with no dust at all.
        float distance = MergeDistance;
        int budget = Mathf.Max(1, MaxPerFrame);
        while (_pending.Count > budget)
        {
            distance *= 2.0f;
            _scratch.Clear();
            foreach (Cluster cluster in _pending)
            {
                if (!MergeInto(_scratch, cluster, distance))
                {
                    _scratch.Add(cluster);
                }
            }

            _pending.Clear();
            _pending.AddRange(_scratch);
        }

        foreach (Cluster cluster in _pending)
        {
            Emit(cluster);
        }

        _pending.Clear();
    }

    private readonly List<Cluster> _scratch = new();

    /// <summary>
    /// Folds <paramref name="incoming"/> into the first cluster within
    /// <paramref name="distance"/>. Volume-weighted, so one big collapse chunk
    /// outweighs a scatter of small carve chips in where the cloud sits, what
    /// colour it is and how fast it sinks.
    /// </summary>
    private static bool MergeInto(List<Cluster> clusters, in Cluster incoming, float distance)
    {
        float distanceSquared = distance * distance;

        for (int i = 0; i < clusters.Count; i++)
        {
            Cluster cluster = clusters[i];
            if (cluster.Position.DistanceSquaredTo(incoming.Position) > distanceSquared)
            {
                continue;
            }

            float volume = cluster.Volume + incoming.Volume;
            float t = incoming.Volume / Mathf.Max(volume, 0.0001f);
            cluster.Position = cluster.Position.Lerp(incoming.Position, t);
            cluster.Colour = cluster.Colour.Lerp(incoming.Colour, t);
            cluster.Velocity = cluster.Velocity.Lerp(incoming.Velocity, t);
            cluster.Volume = volume;
            clusters[i] = cluster;
            return true;
        }

        return false;
    }

    private void Emit(in Cluster cluster)
    {
        int slot = _next;
        _next = (_next + 1) % _pool.Count;

        GpuParticles3D emitter = _pool[slot];

        float scale = Mathf.Clamp(Mathf.Pow(cluster.Volume, 1.0f / 3.0f) * SizeScale, MinScale, MaxScale);
        emitter.GlobalTransform = new Transform3D(BasisFromUp(cluster.Up).Scaled(Vector3.One * scale), cluster.Position);
        emitter.Lifetime = CloudLifetime + (Mathf.Max(0.0f, scale - 1.0f) * LifetimePerScale);

        // Local coordinates, so gravity here is along the cloud's own up and is
        // scaled with the node: divided back out so a big cloud sinks at the
        // same metres per second as a small one.
        float fall = Mathf.Max(0.0f, -cluster.Velocity.Dot(cluster.Up));
        _processes[slot].Gravity = new Vector3(0.0f, -(BaseSink + (fall * SinkPerFallSpeed)) / scale, 0.0f);

        Color tint = cluster.Colour.Lerp(DustColour, DustBlend);
        tint.A = 1.0f;
        _materials[slot].AlbedoColor = tint;

        emitter.Visible = true;
        emitter.Restart();
    }

    /// <summary>How fast dust settles with nothing pushing it, in m/s².</summary>
    private const float BaseSink = 0.7f;

    private static ParticleProcessMaterial BuildProcessMaterial()
    {
        Gradient fade = new()
        {
            // Opaque almost at once: the structure is gone the frame the cloud
            // starts, and a cloud that fades in shows the gap first.
            Offsets = new[] { 0.0f, 0.06f, 1.0f },
            Colors = new[]
            {
                new Color(1, 1, 1, 0.0f),
                new Color(1, 1, 1, 0.75f),
                new Color(0.85f, 0.85f, 0.85f, 0.0f),
            },
        };

        Curve grow = new();
        grow.AddPoint(new Vector2(0.0f, 0.35f));
        grow.AddPoint(new Vector2(1.0f, 1.0f));

        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.8f,
            Direction = Vector3.Up,
            Spread = 80.0f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 3.2f,

            // Dust settles, unlike the explosion's smoke, which rises.
            Gravity = new Vector3(0.0f, -0.7f, 0.0f),
            DampingMin = 1.0f,
            DampingMax = 2.5f,
            AngleMin = -180.0f,
            AngleMax = 180.0f,
            ScaleMin = 1.2f,
            ScaleMax = 2.4f,
            ScaleCurve = new CurveTexture { Curve = grow },
            ColorRamp = new GradientTexture1D { Gradient = fade },
            LifetimeRandomness = 0.35f,
        };
    }

    /// <summary>
    /// The explosion scene's own soft disc, built in code because this pool is.
    /// An untextured quad is a visible square at close range.
    /// </summary>
    private static Texture2D BuildPuffTexture()
    {
        Gradient gradient = new()
        {
            Offsets = new[] { 0.0f, 0.42f, 1.0f },
            Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.72f), new Color(1, 1, 1, 0) },
        };

        return new GradientTexture2D
        {
            Gradient = gradient,
            Width = 64,
            Height = 64,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
        };
    }

    private static Basis BasisFromUp(Vector3 up)
    {
        Vector3 y = up.Normalized();
        Vector3 reference = Mathf.Abs(y.Y) > 0.99f ? Vector3.Forward : Vector3.Up;
        Vector3 x = reference.Cross(y).Normalized();
        return new Basis(x, y, x.Cross(y));
    }
}
