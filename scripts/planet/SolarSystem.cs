using Godot;
using DillControl.Core;
using DillControl.Levels;
using DillControl.Vehicles;

namespace DillControl.Planet;

/// <summary>
/// Everything round the contract planet: the other bodies in its sky, the air
/// thinning to space as you climb, the zero-g beyond the gravity well, and the
/// arrival -- the ship waiting in orbit and the player already on the bike
/// beside it.
///
/// Readies after the planet (it sits under <c>Lighting</c>, after
/// <c>Geometry</c>) and before <c>LevelRoot</c>, so the ship, the bike and the
/// spawn marker are where they belong when the player is made. Rolls from the
/// planet's own seed, on streams of its own.
///
/// The other bodies are scenery: discs in the sky shader, unreachable
/// (<c>docs/decisions/0017</c>: only the contract planet is a place).
/// </summary>
[GlobalClass]
public partial class SolarSystem : Node3D
{
    [ExportGroup("Data")]
    [Export]
    public SystemStats Stats { get; set; }

    [ExportGroup("Scene Wiring")]
    [Export]
    public PlanetTerrain Planet { get; set; }

    [Export]
    public WorldEnvironment Environment { get; set; }

    [Export]
    public DirectionalLight3D Sun { get; set; }

    [Export]
    public LevelRoot Level { get; set; }

    /// <summary>The ship, a set piece that waits in orbit. Optional.</summary>
    [Export]
    public Node3D Ship { get; set; }

    /// <summary>Where on the ship the bike waits. Optional; the ship's own origin otherwise.</summary>
    [Export]
    public Marker3D BikeDock { get; set; }

    /// <summary>The bike's body, moved to the dock.</summary>
    [Export]
    public Node3D Bike { get; set; }

    /// <summary>The bike's systems, which the player is put on.</summary>
    [Export]
    public HoverBikeSystems BikeSystems { get; set; }

    /// <summary>The level's spawn marker, moved to the dock so the player is made beside the bike.</summary>
    [Export]
    public Marker3D PlayerSpawn { get; set; }

    /// <summary>Start riding. Off leaves the player floating at the dock.</summary>
    [Export]
    public bool StartOnBike { get; set; } = true;

    /// <summary>How many other bodies this system has, for the bench.</summary>
    public int BodyCount { get; private set; }

    private ShaderMaterial _sky;
    private float _baseFogDensity = -1.0f;
    private float _baseFogBegin;
    private float _baseFogEnd;
    private float _pushedBlend = -1.0f;
    private float _pushedDip = -1.0f;
    private Vector3 _pushedUp;

    private const float BlendEpsilon = 0.01f;
    private const float DipEpsilon = 0.0025f;

    /// <summary>True once the bodies, the space field and the arrival are set up.</summary>
    public bool IsBuilt { get; private set; }

    public override void _Ready()
    {
        SetProcess(false);
        if (Planet == null || Stats == null)
        {
            GD.PushError($"SolarSystem '{Name}' needs a planet and SystemStats; no system.");
            return;
        }

        // A loading stage builds the planet after every node is ready; a test
        // level builds it in its own ready, before this one.
        if (Planet.IsBuilt)
        {
            Build();
        }
        else
        {
            Planet.Built += Build;
        }
    }

    private void Build()
    {
        Planet.Built -= Build;
        Environment env = Environment?.Environment;
        _sky = env?.Sky?.SkyMaterial as ShaderMaterial;
        if (env != null)
        {
            _baseFogDensity = env.FogDensity;
            _baseFogBegin = env.FogDepthBegin;
            _baseFogEnd = env.FogDepthEnd;
        }

        BuildBodies();
        BuildSpaceField();
        StageArrival();
        IsBuilt = true;
        SetProcess(true);
    }

    // ---------------- The sky's other bodies ----------------

    private void BuildBodies()
    {
        if (_sky == null)
        {
            return;
        }

        SeededRandom random = SeededRandom.Stream(Planet.BuiltSeed, "system bodies");
        int count = Mathf.Clamp(random.RangeInt(Stats.BodiesMin, Stats.BodiesMax), 0, 4);
        Vector3 toSun = Sun != null ? Sun.GlobalBasis.Z.Normalized() : Vector3.Up;
        float clearance = Mathf.Cos(Mathf.DegToRad(Stats.BodySunClearance));

        var directions = new Godot.Collections.Array<Vector4>();
        var colours = new Godot.Collections.Array<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            directions.Add(Vector4.Zero);
            colours.Add(Vector3.Zero);
        }

        for (int i = 0; i < count; i++)
        {
            // A few tries for a place away from the sun and the bodies already
            // placed; past that, wherever the last try fell.
            Vector3 direction = random.OnSphere();
            for (int tries = 0; tries < 12 && (direction.Dot(toSun) > clearance || Crowded(direction, directions, i)); tries++)
            {
                direction = random.OnSphere();
            }

            float size = Mathf.DegToRad(random.Range(Stats.BodySizeMin, Stats.BodySizeMax));
            directions[i] = new Vector4(direction.X, direction.Y, direction.Z, Mathf.Cos(size));

            Color colour = Stats.BodyColours.Length > 0
                ? Stats.BodyColours[random.RangeInt(0, Stats.BodyColours.Length - 1)]
                : Colors.Gray;
            colour = Color.FromHsv(Mathf.PosMod(colour.H + random.Range(-0.04f, 0.04f), 1.0f), colour.S,
                                   Mathf.Clamp(colour.V * random.Range(0.85f, 1.1f), 0.0f, 1.0f));
            colours[i] = new Vector3(colour.R, colour.G, colour.B);
        }

        _sky.SetShaderParameter("body_count", count);
        _sky.SetShaderParameter("body_dir_size", directions);
        _sky.SetShaderParameter("body_color", colours);
        BodyCount = count;
    }

    private static bool Crowded(Vector3 direction, Godot.Collections.Array<Vector4> placed, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Vector4 other = placed[i];
            if (direction.Dot(new Vector3(other.X, other.Y, other.Z)) > 0.9f)
            {
                return true;
            }
        }
        return false;
    }

    // ---------------- Space ----------------

    /// <summary>
    /// Zero-g out past the planet's gravity well. Without it the project's
    /// default gravity, straight down in world space, takes over the moment
    /// the well lets go -- a ship in orbit would fall toward -Y.
    /// </summary>
    private void BuildSpaceField()
    {
        Area3D space = new()
        {
            Name = "SpaceField",
            Priority = 0,
            GravitySpaceOverride = Area3D.SpaceOverride.Replace,
            Gravity = 0.0f,
            GravityDirection = Vector3.Down,
            CollisionLayer = 0,
            CollisionMask = GameLayers.Mask(GameLayers.Player, GameLayers.Enemy, GameLayers.Prop, GameLayers.Projectile),
            Monitorable = false,
        };
        space.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new SphereShape3D { Radius = Planet.Radius * Stats.SpaceFieldRadii },
        });
        AddChild(space);
        space.GlobalPosition = Vector3.Zero;
    }

    // ---------------- The arrival ----------------

    /// <summary>
    /// The ship waits past the gravity well, a little round from over the
    /// compound so the compound is ahead and below. The bike waits at its
    /// dock, nose toward the compound, and the spawn marker is moved beside
    /// it so the player is made there.
    /// </summary>
    private void StageArrival()
    {
        Vector3 compound = Planet.Sites.Count > 0 && Planet.Sites[0] != null ? Planet.Sites[0].Direction : Vector3.Up;
        Vector3 east = compound.Cross(Mathf.Abs(compound.Y) < 0.99f ? Vector3.Up : Vector3.Right).Normalized();
        float swing = SeededRandom.Stream(Planet.BuiltSeed, "ship").Range(0.0f, Mathf.Tau);
        Vector3 axis = east.Rotated(compound, swing);
        Vector3 shipDirection = compound.Rotated(axis, Mathf.DegToRad(Stats.ShipOffsetDegrees)).Normalized();
        Vector3 shipPosition = shipDirection * (Planet.Radius * Stats.ShipOrbitRadii);
        Vector3 compoundGround = Planet.Heights.SurfacePoint(compound);
        Vector3 towardCompound = (compoundGround - shipPosition).Normalized();

        if (Ship != null)
        {
            // Upright to the planet, nose along the way down.
            Ship.GlobalTransform = new Transform3D(PlanetHeightfield.UprightBasis(shipDirection, towardCompound), shipPosition);
        }

        Transform3D dock = BikeDock?.GlobalTransform ?? Ship?.GlobalTransform ?? new Transform3D(Basis.Identity, shipPosition);

        // Nose on the compound itself, not levelled: the bike flies six ways
        // with no pitch limit, and a levelled nose flew a tangent straight
        // out into space. Its top is as near the planet's up as that allows.
        Vector3 top = shipDirection.Slide(towardCompound);
        top = top.LengthSquared() > 1e-6f ? top.Normalized() : east;
        Basis facing = new Basis(top.Cross(-towardCompound), top, -towardCompound).Orthonormalized();
        if (Bike != null)
        {
            Bike.GlobalTransform = new Transform3D(facing, dock.Origin);
        }
        if (PlayerSpawn != null)
        {
            PlayerSpawn.GlobalTransform = new Transform3D(facing, dock.Origin + (shipDirection * 1.5f));
        }

        if (StartOnBike && Level != null && BikeSystems != null)
        {
            Level.PlayerSpawned += OnPlayerSpawned;
        }
    }

    private void OnPlayerSpawned(Node3D player)
    {
        Level.PlayerSpawned -= OnPlayerSpawned;

        // Deferred: the bike hears of the player from the same signal, and
        // boarding before it has would board nobody.
        Callable.From(BikeSystems.BoardNow).CallDeferred();
    }

    // ---------------- The air ----------------

    /// <summary>
    /// How much air is overhead: one on the ground, thinning from
    /// <see cref="SystemStats.AtmosphereFadeStart"/> to nothing at
    /// <see cref="SystemStats.AtmosphereFadeEnd"/>, in radii of altitude.
    /// </summary>
    public float AtmosphereAt(Vector3 point)
    {
        float radius = Planet.Radius;
        float altitude = point.Length() - radius;
        return 1.0f - Mathf.SmoothStep(radius * Stats.AtmosphereFadeStart, radius * Stats.AtmosphereFadeEnd, altitude);
    }

    public override void _Process(double delta)
    {
        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            return;
        }

        Vector3 eye = camera.GlobalPosition;
        float distance = Mathf.Max(eye.Length(), 0.001f);
        Vector3 up = eye / distance;
        float blend = AtmosphereAt(eye);
        float ratio = Planet.Radius / Mathf.Max(distance, Planet.Radius);
        float dip = Mathf.Sqrt(Mathf.Max(0.0f, 1.0f - (ratio * ratio)));

        // Every frame, smooth: globals change a shared buffer and leave the
        // Sky alone. See planet_sky.gdshader.
        RenderingServer.GlobalShaderParameterSet("sky_eye_up", up);
        RenderingServer.GlobalShaderParameterSet("sky_horizon_dip", dip);
        RenderingServer.GlobalShaderParameterSet("sky_atmosphere_blend", blend);

        // Throttled: a material write marks the Sky dirty and rebakes its
        // radiance cubemap, which lights the ground.
        if (_sky != null)
        {
            if (Mathf.Abs(blend - _pushedBlend) >= BlendEpsilon)
            {
                _pushedBlend = blend;
                _sky.SetShaderParameter("atmosphere_blend", blend);
            }
            if (Mathf.Abs(dip - _pushedDip) >= DipEpsilon || up.Dot(_pushedUp) < 0.9999f)
            {
                _pushedDip = dip;
                _pushedUp = up;
                _sky.SetShaderParameter("horizon_dip", dip);
                _sky.SetShaderParameter("up_direction", up);
            }
        }

        // The fog was authored for air at the ground. It thins with the air
        // and recedes with height, so the ground reads from up high and space
        // is clear.
        Environment env = Environment?.Environment;
        if (env != null && _baseFogDensity >= 0.0f)
        {
            float altitude = Mathf.Max(0.0f, distance - Planet.Radius);
            env.FogDensity = _baseFogDensity * blend;
            env.FogDepthBegin = _baseFogBegin + (altitude * Stats.FogRecedePerMetre);
            env.FogDepthEnd = _baseFogEnd + (altitude * Stats.FogRecedePerMetre);
        }
    }

    /// <summary>One line of the system's state, for <c>bench.gd</c>.</summary>
    public string BenchStatus()
    {
        Camera3D camera = GetViewport().GetCamera3D();
        Vector3 eye = camera?.GlobalPosition ?? Vector3.Zero;
        return System.FormattableString.Invariant(
            $"altitude={eye.Length() - Planet.Radius:F0} air={AtmosphereAt(eye):F2} bodies={BodyCount}");
    }
}
