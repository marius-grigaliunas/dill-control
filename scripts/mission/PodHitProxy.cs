using DillControl.Core;
using Godot;

namespace DillControl.Mission;

/// <summary>
/// A collider standing in for whichever hanging pickle is nearest the line
/// the player aims along: the horde's <c>HordeHitProxy</c>, for pods. A nest
/// hangs 25,000 pickles with no bodies, and a hitscan ray needs something to
/// meet; a small pool of these is handed each tick to the full pods nearest
/// the aim (<see cref="PodField"/>), and every weapon reaches them through
/// its <see cref="Hitbox"/> with no weapon code changed.
///
/// One capsule, the whole pickle: it hangs by its head, so no weak point.
/// Splash is the bus's (<see cref="EventBus.AreaBlast"/>), which reaches every
/// pickle in a blast and not only the few wearing a proxy.
/// </summary>
public partial class PodHitProxy : Node3D, IDamageable, ISplashExempt
{
    public PodField Field { get; set; }

    /// <summary>The pod this proxy stands for, or -1.</summary>
    public int Pod { get; private set; } = -1;

    public bool IsAlive => Pod >= 0 && Field != null && Field.StateOf(Pod) == PodState.Full;

    private Hitbox _hitbox;
    private Rid _rid;
    private Transform3D _park;
    private static int _parkedSlots;

    // Parked far under the world, one spot apiece (as the horde's are).
    private const float ParkDepth = -5000.0f;

    public void Build(float radius, float height)
    {
        _hitbox = new Hitbox { Name = "Hitbox", Layer = GameLayers.HordeHitbox };
        _hitbox.AddChild(new CollisionShape3D
        {
            Name = "Collision",
            Shape = new CapsuleShape3D { Radius = radius, Height = height },
        });
        AddChild(_hitbox);
        _rid = _hitbox.GetRid();
        _park = new Transform3D(Basis.Identity, new Vector3((_parkedSlots++ % 4096) * 6.0f, ParkDepth, 6.0f));
        Release();
    }

    /// <summary>Stands it on pod <paramref name="pod"/>'s pickle, where it hangs now.</summary>
    public void Assign(int pod, Transform3D at)
    {
        Pod = pod;
        // Through the server only: a query sees it, and no notification goes
        // down the node's children every tick.
        PhysicsServer3D.AreaSetTransform(_rid, at);
    }

    public void Release()
    {
        Pod = -1;
        if (_hitbox != null)
        {
            PhysicsServer3D.AreaSetTransform(_rid, _park);
        }
    }

    public void ApplyDamage(DamageInfo info)
    {
        if (info == null || info.IsSplash || Pod < 0)
        {
            return;
        }
        Field?.HitPickle(Pod, info);
    }
}
