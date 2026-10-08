namespace DillControl.Enemies;

/// <summary>
/// The five pickles the one silhouette family carries.
///
/// One mesh family, five behaviours, is what keeps the art bill survivable:
/// one pickle model with variants, animated in the vertex shader. Until the
/// models exist (M3) they are five capsules of five proportions and colours.
///
/// A byte, and stored as one on the agent, because it rides in a struct that is
/// streamed twice a frame at tens of thousands of instances. Order is
/// load-bearing: it indexes the composition table and the per-kind buffers, so
/// a new behaviour is appended rather than inserted.
/// </summary>
public enum AgentKind : byte
{
    /// <summary>The ordinary pickle. Walks the flow field and bites what it reaches.</summary>
    Standard = 0,

    /// <summary>
    /// Small, quick and fragile, and closes the last stretch in a dash. Also
    /// what a thrower throws, what the hive spews and what Mother Brine sheds.
    /// </summary>
    Cornichon = 1,

    /// <summary>
    /// Lobs live cornichons at you from range. It cannot throw anything its
    /// own size, which is why what comes over the top is always small.
    /// </summary>
    Thrower = 2,

    /// <summary>Spits brine at you from range, leading your movement.</summary>
    Spitter = 3,

    /// <summary>
    /// Big and slow and takes a magazine to put down, and shoves hard when it
    /// reaches you. Heavy enough that the manipulator cannot lift it, which is
    /// what stops that tool trivialising the horde.
    /// </summary>
    Tank = 4,
}
