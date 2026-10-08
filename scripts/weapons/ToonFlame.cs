using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Cartoon fire and vapor as particle emitters: opaque toon puffs, the same
/// shader as the explosion's smoke, coloured white-hot at birth and cooling
/// through yellow, orange and red to a dark smoke as they rise. Opaque with
/// discard, so the ink pass outlines every lick of flame and the fire matches
/// the explosions and the world rather than glowing over them.
///
/// PLACEHOLDER -- real fire particles with a flipbook, month 3. That burning
/// ground is drawn at the size it burns, in the same style as every blast, is
/// the part worth keeping.
///
/// One builder for both the fire zones and the bike laser's impact plume, so
/// the two read as the same fire.
/// </summary>
public static class ToonFlame
{
    private static ShaderMaterial _material;

    /// <summary>
    /// Burning ground: puffs born across a disc of <paramref name="radius"/>
    /// on the emitter's local XZ plane, rising along its local +Y. The caller
    /// turns the node so +Y is the ground's up. Emits continuously; set
    /// <see cref="GpuParticles3D.AmountRatio"/> to let it die down.
    /// </summary>
    public static GpuParticles3D Ground(float radius)
    {
        float area = Mathf.Pi * radius * radius;
        GpuParticles3D emitter = Emitter(
            name: "Flames",
            amount: Mathf.Clamp(Mathf.RoundToInt(area * 0.3f), 16, 96),
            lifetime: 1.1f,
            local: true,
            process: Process(
                ringRadius: radius * 0.85f,
                rise: new Vector2(2.5f, 6.0f),
                spread: 18.0f,
                scale: new Vector2(1.2f, 2.6f),
                buoyancy: 3.0f,
                ramp: FireRamp()));
        return emitter;
    }

    /// <summary>
    /// The plume where an energy beam lands: a tight column of white-hot
    /// vapor boiling up and cooling to smoke, emitted in world space so it is
    /// left behind as the beam sweeps -- the trail is the point.
    /// </summary>
    public static GpuParticles3D Vapor(float radius)
    {
        return Emitter(
            name: "Vapor",
            amount: 64,
            lifetime: 1.3f,
            local: false,
            process: Process(
                ringRadius: radius,
                rise: new Vector2(6.0f, 14.0f),
                spread: 25.0f,
                scale: new Vector2(1.4f, 3.2f),
                buoyancy: 4.0f,
                ramp: VaporRamp()));
    }

    private static GpuParticles3D Emitter(string name, int amount, float lifetime, bool local, ParticleProcessMaterial process)
    {
        return new GpuParticles3D
        {
            Name = name,
            Amount = amount,
            Lifetime = lifetime,
            Emitting = true,
            LocalCoords = local,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = new Vector2(1.0f, 1.0f), Material = Material() },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // A moving emitter's bounds are not worth computing: the fire is
            // small and on screen when it matters.
            VisibilityAabb = new Aabb(new Vector3(-20, -2, -20), new Vector3(40, 30, 40)),
        };
    }

    private static ParticleProcessMaterial Process(
        float ringRadius, Vector2 rise, float spread, Vector2 scale, float buoyancy, Gradient ramp)
    {
        // Swells fast from a lick to a ball, then the colour ramp's alpha --
        // which the puff shader reads as size, not opacity -- shrinks it away.
        Curve grow = new();
        grow.AddPoint(new Vector2(0.0f, 0.3f));
        grow.AddPoint(new Vector2(0.25f, 1.0f));
        grow.AddPoint(new Vector2(1.0f, 0.8f));

        return new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
            EmissionRingAxis = Vector3.Up,
            EmissionRingRadius = Mathf.Max(0.1f, ringRadius),
            EmissionRingInnerRadius = 0.0f,
            EmissionRingHeight = 0.4f,
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = rise.X,
            InitialVelocityMax = rise.Y,
            Gravity = new Vector3(0.0f, buoyancy, 0.0f),
            DampingMin = 2.0f,
            DampingMax = 4.0f,
            ScaleMin = scale.X,
            ScaleMax = scale.Y,
            ScaleCurve = new CurveTexture { Curve = grow },
            ColorRamp = new GradientTexture1D { Gradient = ramp },
            LifetimeRandomness = 0.4f,
        };
    }

    /// <summary>White-hot at the base, through yellow, orange and red, to a
    /// last dark wisp. Alpha is size: full through the hot part, gone by the
    /// end.</summary>
    private static Gradient FireRamp() => new()
    {
        Offsets = new[] { 0.0f, 0.12f, 0.35f, 0.6f, 0.82f, 1.0f },
        Colors = new[]
        {
            new Color(1.0f, 0.97f, 0.8f, 0.55f),
            new Color(1.0f, 0.86f, 0.25f, 0.95f),
            new Color(1.0f, 0.5f, 0.1f, 0.9f),
            new Color(0.85f, 0.18f, 0.08f, 0.7f),
            new Color(0.25f, 0.12f, 0.12f, 0.45f),
            new Color(0.15f, 0.12f, 0.14f, 0.0f),
        },
    };

    /// <summary>Hotter and longer at the top end than the ground fire: the
    /// beam is turning the ground to gas, so it starts white and leaves a
    /// tall column of pale smoke.</summary>
    private static Gradient VaporRamp() => new()
    {
        Offsets = new[] { 0.0f, 0.15f, 0.35f, 0.6f, 1.0f },
        Colors = new[]
        {
            new Color(1.0f, 1.0f, 0.95f, 0.75f),
            new Color(1.0f, 0.9f, 0.45f, 1.0f),
            new Color(1.0f, 0.55f, 0.15f, 0.85f),
            new Color(0.55f, 0.5f, 0.5f, 0.7f),
            new Color(0.35f, 0.33f, 0.36f, 0.0f),
        },
    };

    /// <summary>One material for every flame in the game: the colour comes
    /// from the particles, so nothing about it is per emitter. The shadow
    /// crescent is tinted toward a dark red rather than the smoke's purple,
    /// so a flame's far side reads as cooler fire, not as cloud.</summary>
    private static ShaderMaterial Material()
    {
        if (_material != null)
        {
            return _material;
        }

        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://shaders/fx_toon_puff.gdshader"),
        };
        _material.SetShaderParameter("shadow_strength", 0.45f);
        _material.SetShaderParameter("shadow_tint", new Color(0.95f, 0.35f, 0.25f, 1.0f));
        return _material;
    }
}
