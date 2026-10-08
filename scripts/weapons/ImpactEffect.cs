using Godot;

namespace DillControl.Weapons;

/// <summary>
/// Placeholder impact: a small oriented flash and light that fade out and free
/// themselves. PLACEHOLDER -- the art pass replaces it with per-surface
/// decals and sparks.
///
/// It is here because "did that shot land, and where" is the first question a
/// gun has to answer, and a hitscan weapon answers it with nothing at all
/// unless the impact is drawn.
/// </summary>
[GlobalClass]
public partial class ImpactEffect : Node3D
{
    [Export]
    public MeshInstance3D Flash { get; set; }

    [Export]
    public OmniLight3D Light { get; set; }

    [Export(PropertyHint.Range, "0.02,2,0.01,suffix:s")]
    public float Lifetime { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "0,10,0.1")]
    public float LightEnergy { get; set; } = 1.6f;

    /// <summary>
    /// How many of these may exist at once, across every weapon in the game.
    ///
    /// Each one carries an OmniLight3D and a transparent sphere, and the things
    /// that spawn them do not spawn one at a time: the boomstick fires ten
    /// pellets and called this once per pellet, a penetrating round calls it
    /// once per pass, and a machete swing through a pack calls it once per body
    /// in the arc. Ten omni lights and ten alpha-blended spheres inside a
    /// twenty-centimetre cluster is ten times the cost of the picture it draws,
    /// because after about the fourth you are looking at the first four.
    ///
    /// This is the same argument the fire path already makes about sound, one
    /// step further along: HitscanWeapon only announces the impact of the
    /// pellet that draws the tracer, on the grounds that spawning eight voices
    /// to have eight of them refused is cheaper and says the same thing. The
    /// effect never got the same treatment.
    ///
    /// A cap on live effects rather than a limit per shot, because one number
    /// covers every caller including the ones that do not know they are part of
    /// a burst. At the 0.12 s lifetime a 600 rpm carbine holds about 1.2 of
    /// these alive, so a single-projectile weapon is never throttled by it --
    /// only the cases that were genuinely spending ten lights on one wall.
    /// </summary>
    public static int MaxLive { get; set; } = 4;

    /// <summary>Live effects, maintained by the tree callbacks below.</summary>
    public static int LiveCount => _liveCount;

    private static int _liveCount;

    private float _remaining;
    private Vector3 _flashBaseScale = Vector3.One;

    /// <summary>
    /// Instances an impact at a point, oriented along a surface normal, unless
    /// the cap above says the picture already has enough of them. Returns null
    /// when it declined, which every caller is free to ignore.
    ///
    /// Static and here rather than copied into each weapon: there were four
    /// copies of the instance-position-orient dance already, and a rule that
    /// lives in one of them is a rule the other three break.
    /// </summary>
    public static ImpactEffect Spawn(PackedScene scene, Node parent, Vector3 point, Vector3 normal)
    {
        if (scene == null || parent == null || _liveCount >= MaxLive)
        {
            return null;
        }

        // Checked rather than cast: the live count is kept by this class's own
        // tree callbacks, so a scene whose root is something else would spawn
        // effects the cap could never see and would raise it forever.
        Node instance = scene.Instantiate();
        if (instance is not ImpactEffect impact)
        {
            // Freed rather than dropped: a Node is not reference counted, so
            // letting the local go leaves it orphaned for the session.
            instance?.QueueFree();
            GD.PushError("ImpactEffect.Spawn was given a scene whose root is not an ImpactEffect.");
            return null;
        }

        parent.AddChild(impact);
        impact.GlobalPosition = point;

        if (normal != Vector3.Zero)
        {
            // Orient the effect's +Z along the surface normal, guarding against
            // the degenerate case where the normal is parallel to world up.
            Vector3 up = Mathf.Abs(normal.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
            impact.LookAtFromPosition(point, point + normal, up);
        }

        return impact;
    }

    /// <summary>An impact with no surface to align to -- a melee connection.</summary>
    public static ImpactEffect Spawn(PackedScene scene, Node parent, Vector3 point) =>
        Spawn(scene, parent, point, Vector3.Zero);

    public override void _Ready()
    {
        _remaining = Lifetime;
        if (Flash != null)
        {
            _flashBaseScale = Flash.Scale;
        }
    }

    public override void _EnterTree() => _liveCount++;

    // Clamped rather than merely decremented: a scene change frees these
    // alongside everything else, and a static counter that goes negative there
    // would quietly raise the cap for the rest of the session.
    public override void _ExitTree() => _liveCount = Mathf.Max(0, _liveCount - 1);

    public override void _Process(double delta)
    {
        _remaining -= (float)delta;
        if (_remaining <= 0.0f)
        {
            QueueFree();
            return;
        }

        float t = _remaining / Lifetime;
        if (Flash != null)
        {
            // Grows slightly as it fades, so the eye catches it at small sizes.
            Flash.Scale = _flashBaseScale * Mathf.Lerp(1.6f, 0.6f, t);
        }
        if (Light != null)
        {
            Light.LightEnergy = LightEnergy * t;
        }
    }
}
