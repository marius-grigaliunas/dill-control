using Godot;
using DillControl.Audio;
using DillControl.Core;

namespace DillControl.Weapons;

/// <summary>
/// Every explosion in the project, as one node.
///
/// PLACEHOLDER geometry -- the art pass replaces the meshes with real
/// particles and a real scorch decal -- but the <i>staging</i> is not
/// placeholder and should
/// survive that replacement, because it is what makes a blast read as an event
/// rather than as a puff.
///
/// Six things happen on six different clocks, which is the whole change from
/// the version that drove a shell, an alpha and a light off one shared curve:
///
/// <list type="number">
/// <item>a hard flash, two or three frames long and several times brighter than
///       the light that follows it, drawn as a comic-book star as well as
///       lit;</item>
/// <item>a fireball, flattened against whatever surface the blast went off
///       against, because a ground burst is a dome and not a ball;</item>
/// <item>a shockwave ring travelling out along that surface past the blast's own
///       radius -- the most readable "this was big" cue available to
///       flat-shaded geometry, and the cheapest;</item>
/// <item>bright fast sparks, thrown up the surface normal rather than
///       spherically;</item>
/// <item>a low dust collar spreading along the ground and a smoke column rising
///       along local up, which are two different motions and used to be one;</item>
/// <item>a scorch mark that outlives all of it, because destruction is
///       remembered as its aftermath.</item>
/// </list>
///
/// Everything is sized from the blast radius rather than authored at a fixed
/// scale. The one thing that has to read from an explosion is how far it
/// reached, and retuning BlastRadius and seeing the same-sized puff would be
/// worse than no effect at all.
/// </summary>
[GlobalClass]
public partial class ExplosionEffect : Node3D
{
    // ---------------- Scene wiring ----------------

    [ExportGroup("Scene Wiring")]

    /// <summary>
    /// Holder whose +Y is the surface normal. Everything that belongs to the
    /// ground -- the fireball's squash, the ring, the sparks, the dust, the
    /// scorch -- hangs off this, so a blast against a wall lays its ring on the
    /// wall with no second code path.
    /// </summary>
    [Export]
    public Node3D SurfaceAlign { get; set; }

    /// <summary>
    /// Holder whose +Y is local up. Only the smoke column uses it, and it
    /// exists because smoke rises away from gravity rather than away from the
    /// wall it was born against -- and because "up" on a planet's night side is
    /// not world up, which the fixed upward gravity on the smoke emitter quietly
    /// got wrong everywhere but one pole.
    /// </summary>
    [Export]
    public Node3D UpAlign { get; set; }

    /// <summary>
    /// The outer fireball: a lumpy cel-shaded ball drawn by
    /// <c>fx_toon_fireball.gdshader</c> -- hot where it faces the camera,
    /// banded out through yellow, orange and red to a smoky rim -- that cools
    /// and then breaks apart. Opaque with a dissolve, so the ink pass outlines
    /// it and a near blast costs one fragment a pixel rather than a stack of
    /// blends.
    /// </summary>
    [Export]
    public MeshInstance3D Shell { get; set; }

    /// <summary>
    /// The hot centre, inside <see cref="Shell"/>, on the same shader run
    /// hotter. Hidden while the shell is whole; it is what shows through the
    /// holes as the shell breaks up, so the fireball burns out from the
    /// outside in. It is also the whole fireball of a <see cref="Minor"/> pop
    /// and of a near blast over budget, both of which drop the shell.
    /// </summary>
    [Export]
    public MeshInstance3D Core { get; set; }

    /// <summary>The shockwave, flattened onto the surface plane.</summary>
    [Export]
    public MeshInstance3D Ring { get; set; }

    /// <summary>
    /// The comic-book star the blast opens with. A quad drawn by
    /// <c>fx_comic_burst.gdshader</c>, which billboards it itself; it hangs off
    /// the root rather than <see cref="SurfaceAlign"/> because it faces the
    /// camera, not the ground.
    /// </summary>
    [Export]
    public MeshInstance3D Burst { get; set; }

    [Export]
    public OmniLight3D Light { get; set; }

    [Export]
    public GpuParticles3D Sparks { get; set; }

    /// <summary>The column that rises. Lives under <see cref="UpAlign"/>.</summary>
    [Export]
    public GpuParticles3D Smoke { get; set; }

    /// <summary>The collar that spreads. Lives under <see cref="SurfaceAlign"/>.</summary>
    [Export]
    public GpuParticles3D Dust { get; set; }

    /// <summary>
    /// The mark left behind. A <see cref="Decal"/> over a procedurally filled
    /// radial gradient rather than an imported texture, on the same argument
    /// the placeholder sounds being a synthesiser makes: a soft dark disc is
    /// cheaper to compute than to store.
    /// </summary>
    [Export]
    public Decal Scorch { get; set; }

    // ---------------- Flash and light ----------------

    [ExportGroup("Flash")]

    /// <summary>
    /// How long the light holds at <see cref="FlashEnergy"/> before it starts to
    /// decay. Two or three frames at 60 Hz, deliberately: the punch of an
    /// explosion is almost entirely in its first two frames, and a light that
    /// merely starts bright and immediately falls has no punch at all.
    /// </summary>
    [Export(PropertyHint.Range, "0,0.4,0.005,suffix:s")]
    public float FlashSeconds { get; set; } = 0.05f;

    /// <summary>Energy during the flash. Several times the afterglow, on purpose.</summary>
    [Export(PropertyHint.Range, "0,120,0.5")]
    public float FlashEnergy { get; set; } = 40.0f;

    /// <summary>Energy the flash drops to, and then decays from.</summary>
    [Export(PropertyHint.Range, "0,40,0.1")]
    public float LightEnergy { get; set; } = 11.0f;

    /// <summary>How long the afterglow takes to reach nothing.</summary>
    [Export(PropertyHint.Range, "0.05,3,0.01,suffix:s")]
    public float LightSeconds { get; set; } = 0.42f;

    /// <summary>Light reach, as a multiple of the blast radius.</summary>
    [Export(PropertyHint.Range, "0.5,4,0.05")]
    public float LightRangeScale { get; set; } = 1.9f;

    /// <summary>
    /// Ceiling on that reach, in metres. A bike bomb's 22 m blast at 1.9 was a
    /// 42 m light, and an omni light that size is a full-screen lighting pass
    /// from anywhere near it -- under a near barrage at 1080p the explosion
    /// lights were worth several milliseconds of GPU on their own. Past twenty
    /// metres the light adds reach nobody reads in the half second it lasts.
    /// </summary>
    [Export(PropertyHint.Range, "4,80,1,suffix:m")]
    public float LightMaxRange { get; set; } = 20.0f;

    // ---------------- Fireball ----------------

    [ExportGroup("Fireball")]

    /// <summary>
    /// The fireball's whole life: expanding, cooling through its colour bands,
    /// then breaking apart. Longer than the old translucent shell's 0.34 s
    /// because the last part of it is the dissolve into smoke, which is what
    /// reads as the fireball burning out rather than being switched off.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,2,0.01,suffix:s")]
    public float FireballSeconds { get; set; } = 0.6f;

    /// <summary>Share of the blast radius the shell reaches at full expansion.</summary>
    [Export(PropertyHint.Range, "0.1,2,0.01")]
    public float ShellRadiusScale { get; set; } = 0.9f;

    /// <summary>
    /// How much the fireball is squashed along the surface normal. A charge
    /// going off on the ground cannot expand into it, so the fire spreads
    /// sideways -- and a perfect sphere is the clearest single tell that an
    /// explosion was drawn rather than simulated. Ignored on an air burst, which
    /// has nothing to be squashed against.
    /// </summary>
    [Export(PropertyHint.Range, "0.2,1,0.01")]
    public float FireballFlatten { get; set; } = 0.62f;

    /// <summary>The core's share of the shell's radius.</summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float CoreRadiusScale { get; set; } = 0.58f;

    /// <summary>
    /// The core's share of the fireball's life. Below 1 so the hot centre goes
    /// out before the shell does, which is what makes the fireball cool rather
    /// than simply shrink. Long enough to still be burning when the opaque
    /// shell starts to break up, so the holes in it show fire rather than sky.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,1,0.01")]
    public float CoreLifeScale { get; set; } = 0.75f;

    // ---------------- Burst ----------------

    [ExportGroup("Burst")]

    /// <summary>How long the comic star lasts. A handful of frames: it is the flash, drawn.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.01,suffix:s")]
    public float BurstSeconds { get; set; } = 0.16f;

    /// <summary>The star's reach, as a share of the blast radius.</summary>
    [Export(PropertyHint.Range, "0.1,2,0.01")]
    public float BurstRadiusScale { get; set; } = 0.75f;

    /// <summary>
    /// Ceiling on the star's size as a share of its distance from the camera.
    /// A bike bomb going off five metres away would otherwise put an opaque
    /// star over the whole screen; this keeps it to a burst you can see the
    /// fight round.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,1,0.01")]
    public float BurstMaxViewShare { get; set; } = 0.4f;

    /// <summary>A <see cref="Minor"/> pop's star, as a share of a full blast's.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MinorBurstScale { get; set; } = 0.55f;

    // ---------------- Shockwave ----------------

    [ExportGroup("Shockwave")]

    [Export(PropertyHint.Range, "0.05,2,0.01,suffix:s")]
    public float RingSeconds { get; set; } = 0.55f;

    /// <summary>
    /// How far past the blast radius the ring travels. Above 1 deliberately:
    /// the pressure wave outruns the damage, and a ring that stops exactly at
    /// the kill radius makes the blast look smaller than it is.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,3,0.01")]
    public float RingRadiusScale { get; set; } = 1.5f;

    /// <summary>How flat the ring lies against the surface.</summary>
    [Export(PropertyHint.Range, "0.02,1,0.01")]
    public float RingFlatten { get; set; } = 0.2f;

    // ---------------- Particles and aftermath ----------------

    [ExportGroup("Aftermath")]

    /// <summary>
    /// How long the emitters keep drawing after the fireball is spent.
    ///
    /// The shell is done in a third of a second and the dust is not. Freeing on
    /// the shell's own clock cut the smoke off mid-puff.
    /// </summary>
    [Export(PropertyHint.Range, "0,8,0.05,suffix:s")]
    public float ParticleLinger { get; set; } = 2.6f;

    /// <summary>
    /// How long the scorch mark lasts. It holds for
    /// <see cref="ScorchHoldFraction"/> of this and fades over the rest.
    ///
    /// This, not the fireball, is what makes a bombed site look bombed. It is
    /// bounded rather than permanent because the mark is owned by this node and
    /// dies with it -- a scorch that outlives its own explosion needs a pool
    /// with a cap, which is a larger change than this one.
    /// </summary>
    [Export(PropertyHint.Range, "0,60,0.5,suffix:s")]
    public float ScorchSeconds { get; set; } = 18.0f;

    /// <summary>Share of <see cref="ScorchSeconds"/> spent at full opacity.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ScorchHoldFraction { get; set; } = 0.6f;

    /// <summary>Scorch radius as a multiple of the blast radius.</summary>
    [Export(PropertyHint.Range, "0.2,2.5,0.01")]
    public float ScorchRadiusScale { get; set; } = 0.85f;

    /// <summary>
    /// How long the embers in the scorch's cracks glow. Decal emission is
    /// shaded in the same pass the decal already costs, so this is colour for
    /// nothing.
    /// </summary>
    [Export(PropertyHint.Range, "0,10,0.1,suffix:s")]
    public float EmberSeconds { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,8,0.05")]
    public float EmberEnergy { get; set; } = 4.0f;

    /// <summary>Radius the unscaled shell and ring meshes are authored at, in metres.</summary>
    private const float MeshBaseRadius = 0.5f;

    /// <summary>
    /// How far the surface probe reaches, as a share of the blast radius. Short
    /// on purpose: a warhead going off well above the ground is an air burst and
    /// should get no ring, no dust and no scorch rather than a ring drawn on
    /// ground it never touched.
    /// </summary>
    private const float ProbeReachScale = 0.55f;

    /// <summary>Probe start, above the centre along up, so a blast resting on the ground still finds it.</summary>
    private const float ProbeLift = 0.5f;

    /// <summary>
    /// How far a caller-supplied normal is assumed to have stepped the blast off
    /// its surface. <see cref="Rocket"/> steps back by 0.15 m; matching that puts
    /// the ring and the scorch on the surface rather than floating over it.
    /// </summary>
    private const float SuppliedNormalOffset = 0.15f;

    private Vector3 _centre;
    private Vector3 _up = Vector3.Up;
    private Vector3 _suppliedNormal;
    private float _targetRadius = 1.0f;
    private bool _silent;
    private bool _configured;
    private bool _applied;

    private bool _hasSurface;
    private float _elapsed;
    private float _lifetime;

    /// <summary>A blast that drew nothing still lasts a moment, so its sparks are not cut on the frame they start.</summary>
    private const float MinLifetime = 0.25f;

    private StandardMaterial3D _ringMaterial;
    private float _ringBaseAlpha = 0.9f;
    private float _scorchBaseAlpha = 1.0f;

    // Per-frame bookkeeping kept in C#, so a node that only has a scorch left
    // makes no calls into the engine on most frames.
    private bool _blastDone;
    private float _blastEnds => Mathf.Max(
        Mathf.Max(FireballSeconds, BurstSeconds),
        Mathf.Max(Mathf.Max(RingSeconds, FlashSeconds + LightSeconds), ParticleLinger)) + 0.05f;
    private bool _embersOut;
    private bool _embersChecked;
    private Color _scorchModulate;
    private bool _scorchModulateKnown;
    private bool _scorchGone;

    // ---------------- Budget ----------------

    // Every explosion used to build all six layers whatever else was already on
    // screen. One grenade is fine. An autocannon salvo is sixteen of these in a
    // second and a half inside one 2.5 m circle, and the designator calls the
    // next salvo before the last has landed, so the recordings that found this
    // had two to six blasts a frame: as many omni lights over the same pixels,
    // dozens of alpha-blended emitters, and -- because every node lived the
    // full eighteen seconds of its scorch -- a few hundred scorch decals
    // stacked on the same patch of ground. Draws climbed from 340 to 530 and
    // render GPU from 22 ms to 89 ms while the salvo was running.
    //
    // Each layer that costs per pixel or per light is therefore a budget of
    // live instances, on the ImpactEffect precedent: past about the fourth
    // light in one spot you are looking at the first four. A blast over budget
    // still flashes its fireball if that budget allows and still plays its
    // sound and does its damage -- only the redundant layers are refused.

    /// <summary>
    /// Most omni lights alive at once across every explosion. Two, down from
    /// four: each one is a lighting pass over everything in its reach, and a
    /// salvo's third and fourth flashes are lost in the first two's.
    /// </summary>
    public static int MaxLiveLights { get; set; } = 2;

    /// <summary>
    /// Most explosions whose three particle emitters run at once. Four, down
    /// from six, and fireballs five, down from eight: a cluster salvo lands
    /// around forty blasts a second at its peak, and at 1080p those frames ran
    /// 18 to 23 ms of GPU with the old budgets. Past the first few, another
    /// fireball and another column of smoke land on the same pixels as the
    /// ones already there.
    /// </summary>
    public static int MaxLiveEmitters { get; set; } = 4;

    /// <summary>Most fireballs and rings drawn at once.</summary>
    public static int MaxLiveFireballs { get; set; } = 5;

    /// <summary>
    /// Most scorch decals alive at once. Past it the oldest mark is retired
    /// early, which is the pool-with-a-cap the scorch comment always said a
    /// longer-lived mark would need.
    ///
    /// Twelve, down from 32: a decal is shaded per pixel in the clustered pass
    /// wherever it covers the screen, and a salvo's scorches overlap. Measured
    /// at 1080p under a steady barrage, the cap was worth about 1.1 ms of GPU,
    /// and twelve still leaves a salvo's aftermath visibly burnt.
    /// </summary>
    public static int MaxLiveScorches { get; set; } = 12;

    /// <summary>
    /// Explosions near the camera that may draw at full detail at once.
    ///
    /// Distance changes what an explosion costs more than count does. Measured
    /// at 1080p, blasts the size of a bike bomb landing 5 to 13 m from the
    /// camera took the GPU from 7.7 ms to 20.8 ms, and every layer shared it:
    /// smoke and dust billboards, the fireball shells, the light and the scorch
    /// each cover much of the screen from that close, and their overdraw
    /// stacks. One near blast at full detail is the moment; the second and
    /// third landing on top of it are mostly more of the same pixels. So a near
    /// blast past this budget drops its outer shell and its light and thins its
    /// smoke and dust, and a far one is untouched.
    /// </summary>
    public static int MaxLiveNearFull { get; set; } = 1;

    /// <summary>
    /// A small round's pop rather than an explosion: the hot core, the sparks
    /// and a small scorch, and none of the outer shell, the ring, the smoke,
    /// the dust or the light. Set before the node enters the tree.
    ///
    /// For explosive bullets, which go off a dozen times a second at whatever
    /// range the horde is -- usually point blank. The full effect there is a
    /// screen of alpha-blended shells and smoke for a 2 m blast, measured at
    /// about 3-4 ms of GPU through a burst, and it washed out the crowd being
    /// shot. Minor pops draw from <see cref="MaxLiveMinor"/>, not from the big
    /// blasts' budgets, so a held trigger never takes a grenade's fireball.
    /// </summary>
    public bool Minor { get; set; }

    /// <summary>Most minor pops drawing at once.</summary>
    public static int MaxLiveMinor { get; set; } = 8;

    private static int _liveMinor;
    private bool _ownsMinor;

    /// <summary>"Near" is within this many blast radii of the camera.</summary>
    public static float NearCameraRadii { get; set; } = 2.0f;

    /// <summary>
    /// Smoke and dust kept by a near blast at full detail, and by one over the
    /// near budget. Measured with the view held still, the large billboards
    /// were the biggest single share of a near blast's cost; a second blast's
    /// smoke landing in the first one's cloud reads as the same cloud, so it
    /// gets none, and keeps its sparks and its fireball core.
    /// </summary>
    private const float NearEmitterRatio = 0.5f;
    private const float ReducedEmitterRatio = 0.0f;

    private static int _liveNearFull;
    private bool _ownsNearFull;
    private bool _near;
    private bool _reduced;

    private static int _liveLights;
    private static int _liveEmitters;
    private static int _liveFireballs;
    private static readonly System.Collections.Generic.List<ExplosionEffect> _liveScorches = new();

    private bool _ownsLight;
    private bool _ownsEmitters;
    private bool _ownsFireball;
    private bool _ownsScorch;
    private Vector3 _scorchCentre;
    private float _scorchRadius;

    private float _burstRadius;

    // Instance uniforms on the fireball and burst shaders, so the shared
    // material is never duplicated, which the old per-blast alpha fade had to.
    private static readonly StringName ProgressParam = "progress";
    private static readonly StringName SeedParam = "seed";

    private static ImageTexture _splatAlbedo;
    private static ImageTexture _splatEmission;

    /// <summary>
    /// Everything this effect needs, handed over in one call.
    ///
    /// <b>Call this before adding the node to the tree.</b> <c>AddChild</c> runs
    /// <c>_Ready</c> synchronously, so the previous arrangement -- add, position,
    /// then set the radius and the silence flag -- set both of them after the
    /// node had already sized itself and already played its sound. Every
    /// explosion in the project was therefore built as though its radius were
    /// one metre, and the warm-up shot every level fires on spawn was never
    /// actually silent. Taking the centre here rather than assigning
    /// <c>GlobalPosition</c> afterwards is what lets the surface probe run
    /// somewhere other than the origin.
    /// </summary>
    /// <param name="surfaceNormal">
    /// The normal of whatever the blast struck, when the caller knows it --
    /// <see cref="Rocket"/> sweeps a ray and does. Left at zero, the effect
    /// probes for the ground itself, which is the right answer for a grenade
    /// that has rolled to a stop.
    /// </param>
    public void Configure(
        Vector3 centre,
        float blastRadius,
        Vector3 up,
        Vector3 surfaceNormal = default,
        bool silent = false)
    {
        _centre = centre;
        _targetRadius = Mathf.Max(0.1f, blastRadius);
        _up = up.LengthSquared() > 0.0001f ? up.Normalized() : Vector3.Up;
        _suppliedNormal = surfaceNormal;
        _silent = silent;
        _configured = true;

        // Robust to call order either way: a caller that configures after the
        // node is already in the tree still gets a correctly sized blast.
        if (IsInsideTree())
        {
            Apply();
        }
    }

    public override void _Ready()
    {
        Apply();
    }

    /// <summary>
    /// Builds the whole effect once, from whichever of <see cref="_Ready"/> and
    /// <see cref="Configure"/> happens second.
    /// </summary>
    private void Apply()
    {
        if (_applied)
        {
            return;
        }
        _applied = true;

        if (!_configured)
        {
            // Nothing configured it, so it goes off where it was placed at the
            // radius its meshes are authored at. Keeps an instance dropped into
            // a scene by hand from being invisible.
            _centre = GlobalPosition;
        }

        GlobalPosition = _centre;

        _hasSurface = ResolveSurface(out Vector3 surfacePoint, out Vector3 surfaceNormal);

        Orient(surfacePoint, surfaceNormal);

        if (Minor)
        {
            ApplyMinor(surfacePoint);
            return;
        }

        Camera3D camera = GetViewport()?.GetCamera3D();
        _near = camera != null
                && camera.GlobalPosition.DistanceTo(_centre) < _targetRadius * NearCameraRadii;
        _reduced = _near && _liveNearFull >= MaxLiveNearFull;
        if (_near && !_reduced)
        {
            _ownsNearFull = true;
            _liveNearFull++;
        }

        _ownsFireball = _liveFireballs < MaxLiveFireballs;
        if (_ownsFireball)
        {
            _liveFireballs++;
            SetUpFireball();
            SetUpRing();
            if (_reduced)
            {
                HideMesh(Shell);
                HideMesh(Burst);
            }
            else
            {
                SetUpBurst(1.0f);
            }
        }
        else
        {
            HideMesh(Shell);
            HideMesh(Core);
            HideMesh(Ring);
            HideMesh(Burst);
        }

        _ownsEmitters = _liveEmitters < MaxLiveEmitters;
        if (_ownsEmitters)
        {
            _liveEmitters++;
            SetUpParticles();
        }
        else
        {
            Stop(Sparks);
            Stop(Smoke);
            Stop(Dust);
        }

        _scorchCentre = surfacePoint;
        SetUpScorch();
        ClaimScorch();

        _ownsLight = !_reduced && _liveLights < MaxLiveLights;
        if (_ownsLight)
        {
            _liveLights++;
            SetUpLight();
        }
        else if (Light != null)
        {
            Light.Visible = false;
        }

        // The node lives exactly as long as the longest layer it actually drew.
        // It used to live the full scorch time unconditionally, so an air burst
        // with no mark at all still sat in the tree for eighteen seconds -- and
        // then the particle linger unconditionally, so a blast refused every
        // layer by the budgets still sat there animating nothing for 2.6 s. A
        // cluster salvo kept over a hundred of those alive at once.
        _lifetime = LayerLifetime();
        if (_ownsScorch)
        {
            _lifetime = Mathf.Max(_lifetime, ScorchSeconds);
        }

        PlayBlastSound();
    }

    /// <summary>The <see cref="Minor"/> build: core, sparks and scorch, from their own budget.</summary>
    private void ApplyMinor(Vector3 surfacePoint)
    {
        _ownsMinor = _liveMinor < MaxLiveMinor;
        if (_ownsMinor)
        {
            _liveMinor++;
            SetUpFireball();
            SetUpParticles();
            SetUpBurst(MinorBurstScale);
        }
        else
        {
            HideMesh(Core);
            HideMesh(Burst);
            Stop(Sparks);
        }

        HideMesh(Shell);
        HideMesh(Ring);
        Stop(Smoke);
        Stop(Dust);
        if (Light != null)
        {
            Light.Visible = false;
        }

        _scorchCentre = surfacePoint;
        SetUpScorch();
        ClaimScorch();

        _lifetime = _ownsMinor ? Mathf.Max(MinLifetime, Mathf.Max(FireballSeconds, ParticleLinger)) : MinLifetime;
        if (_ownsScorch)
        {
            _lifetime = Mathf.Max(_lifetime, ScorchSeconds);
        }

        PlayBlastSound();
    }

    /// <summary>
    /// Takes a scorch slot, or declines one. A mark that would land inside a
    /// live mark at least about as big is skipped outright: sixteen autocannon
    /// rounds in a 2.5 m circle drew sixteen decals over the same pixels to
    /// show one burnt patch. Past the cap the oldest mark is retired early.
    /// </summary>
    private void ClaimScorch()
    {
        if (Scorch == null || !Scorch.Visible)
        {
            return;
        }

        _scorchRadius = _targetRadius * ScorchRadiusScale;

        foreach (ExplosionEffect other in _liveScorches)
        {
            if (IsInstanceValid(other)
                && other._scorchRadius >= _scorchRadius * 0.75f
                && other._scorchCentre.DistanceTo(_scorchCentre) < other._scorchRadius * 0.5f)
            {
                Scorch.Visible = false;
                return;
            }
        }

        while (_liveScorches.Count >= MaxLiveScorches && _liveScorches.Count > 0)
        {
            ExplosionEffect oldest = _liveScorches[0];
            _liveScorches.RemoveAt(0);
            if (IsInstanceValid(oldest))
            {
                oldest.RetireScorch();
            }
        }

        _ownsScorch = true;
        _liveScorches.Add(this);
    }

    /// <summary>
    /// Ends this effect's mark early. Everything else on the node is long over
    /// by the time a mark is the oldest of the cap, so it frees on its next tick.
    /// </summary>
    private void RetireScorch()
    {
        _ownsScorch = false;
        if (Scorch != null)
        {
            Scorch.Visible = false;
        }
        _lifetime = Mathf.Min(_lifetime, Mathf.Max(_elapsed, LayerLifetime()));
    }

    /// <summary>How long the layers this blast was actually given take to finish, its scorch aside.</summary>
    private float LayerLifetime()
    {
        float lifetime = MinLifetime;
        if (_ownsLight)
        {
            lifetime = Mathf.Max(lifetime, FlashSeconds + LightSeconds);
        }
        if (_ownsFireball)
        {
            lifetime = Mathf.Max(lifetime, Mathf.Max(FireballSeconds, RingSeconds));
        }
        if (_ownsEmitters)
        {
            lifetime = Mathf.Max(lifetime, ParticleLinger);
        }
        return lifetime;
    }

    private static void HideMesh(MeshInstance3D mesh)
    {
        if (mesh != null)
        {
            mesh.Visible = false;
        }
    }

    private static void Stop(GpuParticles3D emitter)
    {
        if (emitter != null)
        {
            emitter.Emitting = false;
            emitter.Visible = false;
        }
    }

    private void ReleaseNear()
    {
        if (_ownsNearFull)
        {
            _ownsNearFull = false;
            _liveNearFull = Mathf.Max(0, _liveNearFull - 1);
        }
    }

    private void ReleaseLight()
    {
        if (_ownsLight)
        {
            _ownsLight = false;
            _liveLights = Mathf.Max(0, _liveLights - 1);
        }
    }

    private void ReleaseEmitters()
    {
        if (_ownsEmitters)
        {
            _ownsEmitters = false;
            _liveEmitters = Mathf.Max(0, _liveEmitters - 1);
        }
    }

    private void ReleaseFireball()
    {
        if (_ownsFireball)
        {
            _ownsFireball = false;
            _liveFireballs = Mathf.Max(0, _liveFireballs - 1);
        }
    }

    // Everything still held goes back when the node leaves, including on a
    // scene change, where the clamps keep a static counter from going negative
    // and quietly raising a cap for the rest of the session.
    /// <summary>Effect nodes in the tree right now, scorches included. For the perf recorder.</summary>
    public static int LiveCount { get; private set; }

    public override void _EnterTree() => LiveCount++;

    public override void _ExitTree()
    {
        LiveCount = Mathf.Max(0, LiveCount - 1);
        ReleaseNear();
        ReleaseLight();
        ReleaseEmitters();
        ReleaseFireball();
        if (_ownsMinor)
        {
            _ownsMinor = false;
            _liveMinor = Mathf.Max(0, _liveMinor - 1);
        }
        if (_ownsScorch)
        {
            _ownsScorch = false;
            _liveScorches.Remove(this);
        }
    }

    // ---------------- Surface ----------------

    /// <summary>
    /// Where the blast met the world, and which way that surface faces.
    ///
    /// A caller that already knows is believed -- a rocket's flight sweep has
    /// the exact normal and re-querying it could only be a worse copy. Otherwise
    /// one short ray along -up, which is what a grenade lying on the ground
    /// wants and what a shell going off in mid-air correctly fails to find.
    /// </summary>
    private bool ResolveSurface(out Vector3 point, out Vector3 normal)
    {
        if (_suppliedNormal.LengthSquared() > 0.0001f)
        {
            normal = _suppliedNormal.Normalized();
            point = _centre - (normal * SuppliedNormalOffset);
            return true;
        }

        point = _centre;
        normal = _up;

        float reach = Mathf.Max(1.5f, _targetRadius * ProbeReachScale);
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            _centre + (_up * ProbeLift),
            _centre - (_up * reach));

        // World only. A hitbox is not a surface, and neither is the loose rubble
        // the last blast threw around.
        query.CollisionMask = GameLayers.Mask(GameLayers.World);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;

        Godot.Collections.Dictionary result = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (result.Count == 0)
        {
            return false;
        }

        point = result["position"].AsVector3();
        Vector3 hitNormal = result["normal"].AsVector3();
        if (hitNormal.LengthSquared() > 0.0001f)
        {
            normal = hitNormal.Normalized();
        }
        return true;
    }

    /// <summary>
    /// Points the two holders. <see cref="SurfaceAlign"/> also moves onto the
    /// surface, so the ring sweeps along the ground rather than through the air
    /// a metre above it.
    /// </summary>
    private void Orient(Vector3 surfacePoint, Vector3 surfaceNormal)
    {
        if (UpAlign != null)
        {
            UpAlign.GlobalBasis = BasisFromUp(_up);
        }

        if (SurfaceAlign == null)
        {
            return;
        }

        SurfaceAlign.GlobalBasis = BasisFromUp(_hasSurface ? surfaceNormal : _up);
        if (_hasSurface)
        {
            SurfaceAlign.GlobalPosition = surfacePoint;
        }
    }

    /// <summary>
    /// An orthonormal basis whose Y is <paramref name="up"/>. The reference axis
    /// is swapped near the poles because every fixed reference is degenerate
    /// somewhere, and on a sphere the blast will eventually go off there.
    /// </summary>
    private static Basis BasisFromUp(Vector3 up)
    {
        Vector3 y = up.Normalized();
        Vector3 reference = Mathf.Abs(y.Y) > 0.99f ? Vector3.Forward : Vector3.Up;

        Vector3 x = reference.Cross(y);
        if (x.LengthSquared() < 1e-6f)
        {
            x = Vector3.Right;
        }
        x = x.Normalized();

        Basis basis = Basis.Identity;
        basis.X = x;
        basis.Y = y;
        basis.Z = x.Cross(y);
        return basis;
    }

    // ---------------- Set-up ----------------

    private void SetUpFireball()
    {
        // One seed for both layers, so the core's lumps sit inside the shell's
        // rather than poking through them.
        float seed = GD.Randf() * 100.0f;

        if (Shell != null)
        {
            Shell.SetInstanceShaderParameter(SeedParam, seed);
            Shell.SetInstanceShaderParameter(ProgressParam, 0.0f);
            Shell.Scale = Vector3.One * 0.001f;
        }

        if (Core != null)
        {
            Core.SetInstanceShaderParameter(SeedParam, seed);
            Core.SetInstanceShaderParameter(ProgressParam, 0.0f);
            Core.Scale = Vector3.One * 0.001f;
        }
    }

    /// <summary>
    /// Sizes the comic star. Capped by its distance from the camera, so a blast
    /// at your feet is a burst you can see round rather than a screen of it.
    /// </summary>
    private void SetUpBurst(float sizeScale)
    {
        if (Burst == null)
        {
            return;
        }

        _burstRadius = _targetRadius * BurstRadiusScale * sizeScale;
        Camera3D camera = GetViewport()?.GetCamera3D();
        if (camera != null)
        {
            _burstRadius = Mathf.Min(
                _burstRadius, camera.GlobalPosition.DistanceTo(_centre) * BurstMaxViewShare);
        }

        Burst.Visible = BurstSeconds > 0.0f && _burstRadius > 0.05f;
        Burst.SetInstanceShaderParameter(SeedParam, GD.Randf());
        Burst.SetInstanceShaderParameter(ProgressParam, 0.0f);
        Burst.Scale = Vector3.One * 0.001f;
    }

    private void SetUpRing()
    {
        if (Ring == null)
        {
            return;
        }

        // An air burst has no surface for a shockwave to run along, and a ring
        // hanging in the sky is worse than no ring.
        Ring.Visible = _hasSurface;
        if (!_hasSurface)
        {
            return;
        }

        if (Ring.MaterialOverride is StandardMaterial3D shared)
        {
            _ringMaterial = (StandardMaterial3D)shared.Duplicate();
            _ringBaseAlpha = _ringMaterial.AlbedoColor.A;
            Ring.MaterialOverride = _ringMaterial;
        }

        Ring.Scale = Vector3.One * 0.001f;
    }

    /// <summary>
    /// Sizes and starts the three emitters.
    ///
    /// <c>local_coords</c> is set on all of them in the scene, which is what
    /// makes scaling the node scale the particles' velocities with it rather
    /// than only their sprites -- so a rocket's fireball genuinely throws
    /// further than a grenade's instead of throwing the same distance in bigger
    /// billboards.
    /// </summary>
    private void SetUpParticles()
    {
        float scale = ParticleScale();

        // Smoke and dust are the large billboards; near the camera they are
        // most of the overdraw, so a near blast thins them. Sparks are small
        // quads and stay whole.
        float ratio = _reduced ? ReducedEmitterRatio : _near ? NearEmitterRatio : 1.0f;

        if (Sparks != null)
        {
            Sparks.Scale = Vector3.One * scale;
            Sparks.Emitting = true;
        }

        if (Smoke != null)
        {
            Smoke.Scale = Vector3.One * scale;
            Smoke.AmountRatio = ratio;
            Smoke.Emitting = true;
        }

        if (Dust != null)
        {
            // The dust collar is ground contact. There is none in an air burst.
            Dust.Visible = _hasSurface;
            Dust.Scale = Vector3.One * scale;
            Dust.AmountRatio = ratio;
            Dust.Emitting = _hasSurface;
        }
    }

    /// <summary>
    /// Emitter scale from the blast radius, clamped at both ends: a 3 m grenade
    /// still has to throw visible sparks, and a 14 m warhead must not fill the
    /// screen with billboards the size of buildings.
    /// </summary>
    private float ParticleScale() => Mathf.Clamp(_targetRadius / 8.0f, 0.4f, 2.6f);

    private void SetUpScorch()
    {
        if (Scorch == null)
        {
            return;
        }

        Scorch.Visible = _hasSurface && ScorchSeconds > 0.0f;
        if (!Scorch.Visible)
        {
            return;
        }

        EnsureSplatTextures();
        Scorch.TextureAlbedo = _splatAlbedo;
        Scorch.TextureEmission = EmberSeconds > 0.0f ? _splatEmission : null;
        Scorch.EmissionEnergy = EmberEnergy;

        // Any turn about the normal, so a salvo's marks are not one stamp.
        Scorch.Rotation = new Vector3(0.0f, GD.Randf() * Mathf.Tau, 0.0f);

        float width = _targetRadius * ScorchRadiusScale * 2.0f;

        // Deep enough in Y that the projection box still reaches the ground
        // where the terrain rolls away under a wide mark.
        Scorch.Size = new Vector3(width, Mathf.Max(1.5f, _targetRadius * 0.9f), width);

        _scorchBaseAlpha = Scorch.Modulate.A;
    }

    private void SetUpLight()
    {
        if (Light == null)
        {
            return;
        }

        Light.OmniRange = Mathf.Min(_targetRadius * LightRangeScale, LightMaxRange);
        Light.LightEnergy = FlashEnergy;
    }

    /// <summary>
    /// Every explosion in the project passes through this node -- the grenade,
    /// the rocket, each airstrike bomb and every ordnance type -- which is why
    /// the blast sound is here and not in five weapons. It is the same argument
    /// that put the effect itself here.
    ///
    /// Two sounds rather than one, because distance does not simply make an
    /// explosion quieter, it takes the crack off it and leaves the roll. The far
    /// one is the only sound that carries past the near one's own range, so a
    /// strike called over the horizon is still heard as a strike.
    /// </summary>
    private void PlayBlastSound()
    {
        if (_silent)
        {
            return;
        }

        AudioDirector.PlayAt(Sfx.Explosion, GlobalPosition);
        AudioDirector.PlayAt(Sfx.ExplosionDistant, GlobalPosition);
    }

    // ---------------- Per frame ----------------

    public override void _Process(double delta)
    {
        _elapsed += (float)delta;
        if (_elapsed >= _lifetime)
        {
            QueueFree();
            return;
        }

        // Once the blast itself is over, all that is left is the scorch, and
        // a node lives on for that for many seconds -- a held minigun or laser
        // keeps a dozen of them alive. Every one of these updates asks the
        // engine whether its layer is still visible, so after the last one
        // has finished they are skipped outright rather than asked again
        // every frame.
        if (!_blastDone)
        {
            UpdateFireball();
            UpdateBurst();
            UpdateRing();
            UpdateLight();
            UpdateParticles();
            _blastDone = _elapsed > _blastEnds;
        }

        UpdateScorch();

        if (_ownsFireball && _elapsed >= Mathf.Max(FireballSeconds, RingSeconds))
        {
            ReleaseFireball();
        }

        // The near slot covers the blast itself -- fireball, light, the thick
        // of the smoke -- not the scorch it leaves, which keeps the node alive
        // for many seconds more and would otherwise mark every later near blast
        // as one too many.
        if (_ownsNearFull && _elapsed >= Mathf.Max(FireballSeconds, FlashSeconds + LightSeconds))
        {
            ReleaseNear();
        }
    }

    private void UpdateFireball()
    {
        UpdateFireballLayer(Shell, FireballSeconds, ShellRadiusScale);

        UpdateFireballLayer(
            Core,
            FireballSeconds * CoreLifeScale,
            ShellRadiusScale * CoreRadiusScale);
    }

    /// <summary>
    /// One shell of the fireball. Both layers move on the same curve and differ
    /// only in how far and how long, which is what keeps the core inside the
    /// shell at every frame rather than only at the start and the end.
    /// </summary>
    private void UpdateFireballLayer(
        MeshInstance3D layer,
        float seconds,
        float radiusScale)
    {
        if (layer == null || !layer.Visible)
        {
            return;
        }

        float t = Progress(_elapsed, seconds);
        if (t >= 1.0f)
        {
            // Done drawing, and the node lives on for the scorch. Hiding costs
            // nothing and a spent shell left at full scale does not.
            layer.Visible = false;
            return;
        }

        // Fast out, slow to a stop: the eye reads the reach from the first few
        // frames, so most of the expansion happens in them. Full size comes a
        // little early, so the ball holds its size while it cools and breaks up.
        float eased = Mathf.Sin(Mathf.Min(t * 1.4f, 1.0f) * Mathf.Pi * 0.5f);
        float radius = _targetRadius * radiusScale * eased;
        float across = radius / MeshBaseRadius;
        float along = _hasSurface ? across * FireballFlatten : across;

        // Local Y of SurfaceAlign is the surface normal, so this squashes the
        // fireball against whatever it went off on.
        layer.Scale = new Vector3(across, along, across);

        // The shader cools it through its bands and dissolves it; the node
        // only says how far through its life it is.
        layer.SetInstanceShaderParameter(ProgressParam, t);
    }

    private void UpdateBurst()
    {
        if (Burst == null || !Burst.Visible)
        {
            return;
        }

        float t = Progress(_elapsed, BurstSeconds);
        if (t >= 1.0f)
        {
            Burst.Visible = false;
            return;
        }

        // Out hard and over: most of its size by the second frame, then it
        // hollows out in the shader.
        float eased = 1.0f - Mathf.Pow(1.0f - t, 4.0f);
        Burst.Scale = Vector3.One * Mathf.Max(_burstRadius * (0.45f + (0.55f * eased)), 0.001f);
        Burst.SetInstanceShaderParameter(ProgressParam, t);
    }

    private void UpdateRing()
    {
        if (Ring == null || !Ring.Visible)
        {
            return;
        }

        float t = Progress(_elapsed, RingSeconds);
        if (t >= 1.0f)
        {
            Ring.Visible = false;
            return;
        }

        // Cubic ease out: the wave leaves hard and decelerates, which is the
        // motion that reads as pressure rather than as a growing circle.
        float eased = 1.0f - Mathf.Pow(1.0f - t, 3.0f);
        float radius = _targetRadius * RingRadiusScale * Mathf.Max(eased, 0.02f);
        float across = radius / MeshBaseRadius;

        Ring.Scale = new Vector3(across, across * RingFlatten, across);

        if (_ringMaterial != null)
        {
            Color albedo = _ringMaterial.AlbedoColor;

            // Fades faster than it grows, so the ring is brightest while it is
            // still small enough to read as a single edge.
            albedo.A = _ringBaseAlpha * Mathf.Pow(1.0f - t, 1.6f);
            _ringMaterial.AlbedoColor = albedo;
        }
    }

    private void UpdateLight()
    {
        if (Light == null || !Light.Visible)
        {
            return;
        }

        if (_elapsed < FlashSeconds)
        {
            Light.LightEnergy = FlashEnergy;
            return;
        }

        float t = Progress(_elapsed - FlashSeconds, LightSeconds);
        if (t >= 1.0f)
        {
            Light.Visible = false;
            ReleaseLight();
            return;
        }

        // Quadratic, so the drop off the flash is steep and the last of the glow
        // lingers -- an explosion keeps lighting its own smoke.
        Light.LightEnergy = LightEnergy * (1.0f - t) * (1.0f - t);
    }

    private void UpdateParticles()
    {
        if (_elapsed < ParticleLinger)
        {
            return;
        }

        Hide(Sparks);
        Hide(Smoke);
        Hide(Dust);
        ReleaseEmitters();
    }

    private static void Hide(GpuParticles3D emitter)
    {
        if (emitter != null && emitter.Visible)
        {
            emitter.Visible = false;
        }
    }

    /// <summary>
    /// Holds the mark, then fades it. It is the last thing alive, so this is
    /// also what decides when the node frees itself.
    /// </summary>
    private void UpdateScorch()
    {
        if (_scorchGone || Scorch == null || ScorchSeconds <= 0.0f)
        {
            return;
        }

        if (!Scorch.Visible)
        {
            // Hidden by the scorch budget or never shown: nothing brings it
            // back once the effect is set up, so stop asking. Not in the
            // first moments, in case a late Configure has yet to show it.
            _scorchGone = _elapsed > 0.25f;
            return;
        }

        if (!_embersChecked)
        {
            // A scorch that started without emission has no embers to cool.
            _embersChecked = true;
            _embersOut = EmberSeconds <= 0.0f || Scorch.EmissionEnergy <= 0.0f;
        }

        if (!_embersOut)
        {
            float e = 1.0f - Progress(_elapsed, EmberSeconds);
            Scorch.EmissionEnergy = EmberEnergy * e * Mathf.Sqrt(e);
            _embersOut = e <= 0.0f;
        }

        float hold = ScorchSeconds * ScorchHoldFraction;
        if (_elapsed <= hold)
        {
            return;
        }

        if (!_scorchModulateKnown)
        {
            _scorchModulate = Scorch.Modulate;
            _scorchModulateKnown = true;
        }

        float t = Progress(_elapsed - hold, Mathf.Max(ScorchSeconds - hold, 0.0001f));
        _scorchModulate.A = _scorchBaseAlpha * (1.0f - t);
        Scorch.Modulate = _scorchModulate;
    }

    /// <summary>
    /// The scorch's shape and its embers, built once per session from
    /// arithmetic, on the same no-art-pipeline argument the old gradient disc
    /// was made on. A comic splat -- a dark blot with ragged rays -- where that
    /// was a soft circle, and an emission layer of glowing cracks round the
    /// middle that <see cref="EmberSeconds"/> fades out.
    /// </summary>
    private static void EnsureSplatTextures()
    {
        if (_splatAlbedo != null && _splatEmission != null)
        {
            return;
        }

        const int size = 128;
        Image albedo = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        Image emission = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);

        // Fixed ray lengths, so the splat is the same every session.
        const int rays = 9;
        float[] reach = new float[rays];
        uint state = 0x9E3779B9u;
        for (int i = 0; i < rays; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            reach[i] = 0.55f + (0.45f * ((state >> 8) / 16777216.0f));
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = ((x + 0.5f) / (size * 0.5f)) - 1.0f;
                float py = ((y + 0.5f) / (size * 0.5f)) - 1.0f;
                float r = Mathf.Sqrt((px * px) + (py * py));
                float angle = Mathf.Atan2(py, px);

                // Each ray's own length changes only in the valleys between
                // rays, where every ray is at its base radius anyway.
                int k = ((int)Mathf.Floor((((angle / Mathf.Tau) + 1.0f) * rays) + 0.5f)) % rays;
                // A solid blot with fat lobes, not a starburst of thin rays:
                // thin rays vanish on sand at the grazing angle the ground is
                // usually seen at, which the first version found out.
                float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * rays * 0.5f)), 2.0f);
                float wobble = 0.04f * Mathf.Sin((angle * 13.0f) + 1.7f);
                float edge = Mathf.Min(0.98f, 0.6f + wobble + (0.36f * spike * reach[k]));

                // A crisp edge and two flat tones -- char inside a ring of
                // ash -- so the mark is drawn rather than smudged.
                float a = 1.0f - Mathf.SmoothStep(edge * 0.9f, edge, r);
                float inner = Mathf.Clamp(r / edge, 0.0f, 1.0f);
                float shade = inner < 0.62f ? 0.06f : 0.14f;
                albedo.SetPixel(x, y, new Color(shade, shade * 0.85f, shade * 0.8f, a * 0.88f));

                // Thin cracks that curl a little as they leave the middle,
                // hottest in a band round the heart of the mark.
                float crack = Mathf.Pow(Mathf.Abs(Mathf.Sin((angle * 7.0f) + (r * 9.0f))), 14.0f);
                float band = Mathf.SmoothStep(0.08f, 0.22f, inner) * (1.0f - Mathf.SmoothStep(0.5f, 0.85f, inner));
                float heart = 1.0f - Mathf.SmoothStep(0.0f, 0.16f, inner);
                float glow = Mathf.Clamp((crack * band) + (heart * 0.8f), 0.0f, 1.0f) * a;
                emission.SetPixel(x, y, new Color(glow, 0.38f * glow, 0.06f * glow, 1.0f));
            }
        }

        albedo.GenerateMipmaps();
        emission.GenerateMipmaps();
        _splatAlbedo = ImageTexture.CreateFromImage(albedo);
        _splatEmission = ImageTexture.CreateFromImage(emission);
    }

    private static float Progress(float elapsed, float duration)
        => duration <= 0.0f ? 1.0f : Mathf.Clamp(elapsed / duration, 0.0f, 1.0f);
}
