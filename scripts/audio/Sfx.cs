namespace DillControl.Audio;

/// <summary>
/// Every sound the game can make, as an id.
///
/// This is the index into <see cref="SoundBank"/>, and it is a fixed table for
/// the same reason <c>GameLayers</c> is: a compile-time name is what lets a
/// call site read as <c>Sfx.RifleFire</c> instead of as a string that can be
/// misspelled into silence. Adding a sound is one entry here and one slot on
/// the bank -- a deliberate pair, not a convenience.
///
/// Order is load-bearing. <see cref="SoundBank.Resolve"/> builds a flat array
/// in exactly this order and asserts its length, so an entry inserted in the
/// middle without the matching bank slot fails loudly at startup instead of
/// silently shifting every sound after it onto the wrong slot.
/// </summary>
public enum Sfx
{
    None = 0,

    // ---------------- Guns ----------------
    RifleFire,
    ShotgunFire,
    RailgunFire,
    RocketFire,
    FlameLoop,
    CannonLoop,
    DryFire,
    ReloadStart,
    ReloadFinish,
    WeaponSwitch,

    // ---------------- Close quarters and thrown ----------------
    MeleeSwingLight,
    MeleeSwingHeavy,
    MeleeImpact,
    GrenadeThrow,
    GrenadeBounce,

    // ---------------- Ordnance ----------------
    DesignatorMark,
    StrikeIncoming,
    Explosion,
    ExplosionDistant,

    // ---------------- Impacts ----------------
    ImpactDirt,
    ImpactStone,
    ImpactMetal,
    ImpactFlesh,
    ImpactWater,

    // ---------------- Player ----------------
    FootstepDry,
    FootstepWet,
    FootstepWater,
    Jump,
    LandSoft,
    LandHard,
    JetpackLoop,
    ManipulatorGrab,
    ManipulatorLaunch,
    GrappleFire,
    GrappleAttach,
    GrappleReelLoop,
    GrappleRelease,

    // ---------------- Enemies ----------------
    AgentIdle,
    AgentHurt,
    AgentDie,
    CornichonDash,
    SpitterSpit,
    SpitSplat,
    PickleBite,
    TankStomp,
    PickleThrow,
    NestAmbient,
    BossTelegraph,
    BossSlam,
    BossRollLoop,
    GeyserBubble,
    GeyserErupt,
    WartBurst,
    BossDeath,

    // ---------------- Vehicle ----------------
    ShipEngineLoop,
    ShipBoost,
    ShipBoard,
    ShipDisembark,
    ShipImpact,

    // ---------------- Site and compound ----------------
    StructureDestroyed,
    PodLand,
    PodGrant,
    CompoundAlarm,
    TowerFlak,
    ArmourDeflect,
    CoreDeath,

    // ---------------- World ----------------
    VoxelChip,
    VoxelCollapse,
    RubbleImpact,
    FireLoop,
    WaterSplash,

    // ---------------- Interface ----------------
    Hitmarker,
    HitmarkerWeak,
    UiMove,
    UiSelect,
    PauseOpen,
    PauseClose,

    /// <summary>Not a sound. The count, for the bank's own length assertion.</summary>
    Count,
}
