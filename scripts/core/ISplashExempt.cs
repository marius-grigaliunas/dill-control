namespace DillControl.Core;

/// <summary>
/// A damage receiver whose area damage arrives some other way, so
/// <see cref="Blast.ApplyDamage"/> skips it before spending a line-of-sight
/// ray on it.
///
/// The horde's borrowed hit proxies are the case: an agent takes blast damage
/// from <c>EventBus.AreaBlast</c>, which reaches every agent in the radius, and
/// the proxy already drops splash that reaches it through its hitbox. But
/// Blast's own overlap still found every proxy's hitboxes and cast a ray to
/// each before the proxy threw the hit away -- dozens of rays a rocket in a
/// crowd, measured as most of the several milliseconds Blast spent on its own
/// overlap. A marker in core/, implemented in horde/, keeps the dependency
/// pointing the way the rest of this arrangement does: core states the
/// contract and never learns a horde exists.
/// </summary>
public interface ISplashExempt
{
}
