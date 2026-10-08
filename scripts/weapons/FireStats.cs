using Godot;

namespace DillControl.Weapons;

/// <summary>
/// How burning ground behaves, whatever lit it: an incendiary strike, the
/// bike's laser held on the ground. Read by <see cref="FireField"/>.
/// </summary>
[GlobalClass]
public partial class FireStats : Resource
{
    [Export(PropertyHint.Range, "0.5,40,0.5,suffix:m")]
    public float Radius { get; set; } = 9.0f;

    [Export(PropertyHint.Range, "0,500,1")]
    public float DamagePerSecond { get; set; } = 26.0f;

    /// <summary>Fires burning at once. A new one past this is not lit: each
    /// is a particle system and a damage query a tick.</summary>
    [Export(PropertyHint.Range, "0,64,1")]
    public int MaxZones { get; set; } = 8;
}
