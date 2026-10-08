using Godot;

namespace DillControl.Audio;

/// <summary>
/// Every sound in the game, as one inspectable list.
///
/// THIS IS THE ASSIGN-A-SOUND SURFACE. Open
/// <c>resources/audio/snd_bank_default.tres</c>, find the row you want, expand
/// its <c>Streams</c> array and drop a file in. That is the whole procedure --
/// no rebuild, no code, no scene edit, and the placeholder for that one row
/// stops being used while every other row is untouched.
///
/// Named slots rather than a string-keyed dictionary, for the reason the
/// project already uses fixed slots on <c>HordeComposition</c> and
/// <c>StructureSet</c>: a labelled row is discoverable in the inspector and a
/// misspelled string is silence that nothing reports. The cost is that a new
/// sound is two edits -- an <see cref="Sfx"/> entry and a slot here -- which is
/// the same hand-mirrored pair <c>GameLayers</c> already asks for.
///
/// An entity that wants its own sound rather than the shared one does not need
/// a slot here at all: it exports a <see cref="SoundEvent"/> of its own and
/// passes it to <see cref="AudioDirector"/>, which is the second half of the
/// same story.
///
/// Every slot is initialised from <see cref="SoundLibrary"/>, so an empty
/// .tres means "all placeholders" exactly as the lab settled an empty .tres
/// should.
/// </summary>
[GlobalClass]
public partial class SoundBank : Resource
{
    [ExportGroup("Guns")]
    [Export] public SoundEvent RifleFire { get; set; } = SoundLibrary.RifleFire();
    [Export] public SoundEvent ShotgunFire { get; set; } = SoundLibrary.ShotgunFire();
    [Export] public SoundEvent RailgunFire { get; set; } = SoundLibrary.RailgunFire();
    [Export] public SoundEvent RocketFire { get; set; } = SoundLibrary.RocketFire();
    [Export] public SoundEvent FlameLoop { get; set; } = SoundLibrary.FlameLoop();
    [Export] public SoundEvent CannonLoop { get; set; } = SoundLibrary.CannonLoop();
    [Export] public SoundEvent DryFire { get; set; } = SoundLibrary.DryFire();
    [Export] public SoundEvent ReloadStart { get; set; } = SoundLibrary.ReloadStart();
    [Export] public SoundEvent ReloadFinish { get; set; } = SoundLibrary.ReloadFinish();
    [Export] public SoundEvent WeaponSwitch { get; set; } = SoundLibrary.WeaponSwitch();

    [ExportGroup("Close Quarters")]
    [Export] public SoundEvent MeleeSwingLight { get; set; } = SoundLibrary.MeleeSwingLight();
    [Export] public SoundEvent MeleeSwingHeavy { get; set; } = SoundLibrary.MeleeSwingHeavy();
    [Export] public SoundEvent MeleeImpact { get; set; } = SoundLibrary.MeleeImpact();
    [Export] public SoundEvent GrenadeThrow { get; set; } = SoundLibrary.GrenadeThrow();
    [Export] public SoundEvent GrenadeBounce { get; set; } = SoundLibrary.GrenadeBounce();

    [ExportGroup("Ordnance")]
    [Export] public SoundEvent DesignatorMark { get; set; } = SoundLibrary.DesignatorMark();
    [Export] public SoundEvent StrikeIncoming { get; set; } = SoundLibrary.StrikeIncoming();
    [Export] public SoundEvent Explosion { get; set; } = SoundLibrary.Explosion();
    [Export] public SoundEvent ExplosionDistant { get; set; } = SoundLibrary.ExplosionDistant();

    [ExportGroup("Impacts")]
    [Export] public SoundEvent ImpactDirt { get; set; } = SoundLibrary.ImpactDirt();
    [Export] public SoundEvent ImpactStone { get; set; } = SoundLibrary.ImpactStone();
    [Export] public SoundEvent ImpactMetal { get; set; } = SoundLibrary.ImpactMetal();
    [Export] public SoundEvent ImpactFlesh { get; set; } = SoundLibrary.ImpactFlesh();
    [Export] public SoundEvent ImpactWater { get; set; } = SoundLibrary.ImpactWater();

    [ExportGroup("Player")]
    [Export] public SoundEvent FootstepDry { get; set; } = SoundLibrary.FootstepDry();
    [Export] public SoundEvent FootstepWet { get; set; } = SoundLibrary.FootstepWet();
    [Export] public SoundEvent FootstepWater { get; set; } = SoundLibrary.FootstepWater();
    [Export] public SoundEvent Jump { get; set; } = SoundLibrary.Jump();
    [Export] public SoundEvent LandSoft { get; set; } = SoundLibrary.LandSoft();
    [Export] public SoundEvent LandHard { get; set; } = SoundLibrary.LandHard();
    [Export] public SoundEvent JetpackLoop { get; set; } = SoundLibrary.JetpackLoop();
    [Export] public SoundEvent ManipulatorGrab { get; set; } = SoundLibrary.ManipulatorGrab();
    [Export] public SoundEvent ManipulatorLaunch { get; set; } = SoundLibrary.ManipulatorLaunch();
    [Export] public SoundEvent GrappleFire { get; set; } = SoundLibrary.GrappleFire();
    [Export] public SoundEvent GrappleAttach { get; set; } = SoundLibrary.GrappleAttach();
    [Export] public SoundEvent GrappleReelLoop { get; set; } = SoundLibrary.GrappleReelLoop();
    [Export] public SoundEvent GrappleRelease { get; set; } = SoundLibrary.GrappleRelease();

    [ExportGroup("Enemies")]
    [Export] public SoundEvent AgentIdle { get; set; } = SoundLibrary.AgentIdle();
    [Export] public SoundEvent AgentHurt { get; set; } = SoundLibrary.AgentHurt();
    [Export] public SoundEvent AgentDie { get; set; } = SoundLibrary.AgentDie();
    [Export] public SoundEvent CornichonDash { get; set; } = SoundLibrary.CornichonDash();
    [Export] public SoundEvent SpitterSpit { get; set; } = SoundLibrary.SpitterSpit();
    [Export] public SoundEvent SpitSplat { get; set; } = SoundLibrary.SpitSplat();
    [Export] public SoundEvent PickleBite { get; set; } = SoundLibrary.PickleBite();
    [Export] public SoundEvent TankStomp { get; set; } = SoundLibrary.TankStomp();
    [Export] public SoundEvent PickleThrow { get; set; } = SoundLibrary.PickleThrow();
    [Export] public SoundEvent NestAmbient { get; set; } = SoundLibrary.NestAmbient();
    [Export] public SoundEvent BossTelegraph { get; set; } = SoundLibrary.BossTelegraph();
    [Export] public SoundEvent BossSlam { get; set; } = SoundLibrary.BossSlam();
    [Export] public SoundEvent BossRollLoop { get; set; } = SoundLibrary.BossRollLoop();
    [Export] public SoundEvent GeyserBubble { get; set; } = SoundLibrary.GeyserBubble();
    [Export] public SoundEvent GeyserErupt { get; set; } = SoundLibrary.GeyserErupt();
    [Export] public SoundEvent WartBurst { get; set; } = SoundLibrary.WartBurst();
    [Export] public SoundEvent BossDeath { get; set; } = SoundLibrary.BossDeath();

    [ExportGroup("Vehicle")]
    [Export] public SoundEvent ShipEngineLoop { get; set; } = SoundLibrary.ShipEngineLoop();
    [Export] public SoundEvent ShipBoost { get; set; } = SoundLibrary.ShipBoost();
    [Export] public SoundEvent ShipBoard { get; set; } = SoundLibrary.ShipBoard();
    [Export] public SoundEvent ShipDisembark { get; set; } = SoundLibrary.ShipDisembark();
    [Export] public SoundEvent ShipImpact { get; set; } = SoundLibrary.ShipImpact();

    [ExportGroup("Site and Compound")]
    [Export] public SoundEvent StructureDestroyed { get; set; } = SoundLibrary.StructureDestroyed();
    [Export] public SoundEvent PodLand { get; set; } = SoundLibrary.PodLand();
    [Export] public SoundEvent PodGrant { get; set; } = SoundLibrary.PodGrant();
    [Export] public SoundEvent CompoundAlarm { get; set; } = SoundLibrary.CompoundAlarm();
    [Export] public SoundEvent TowerFlak { get; set; } = SoundLibrary.TowerFlak();
    [Export] public SoundEvent ArmourDeflect { get; set; } = SoundLibrary.ArmourDeflect();
    [Export] public SoundEvent CoreDeath { get; set; } = SoundLibrary.CoreDeath();

    [ExportGroup("World")]
    [Export] public SoundEvent VoxelChip { get; set; } = SoundLibrary.VoxelChip();
    [Export] public SoundEvent VoxelCollapse { get; set; } = SoundLibrary.VoxelCollapse();
    [Export] public SoundEvent RubbleImpact { get; set; } = SoundLibrary.RubbleImpact();
    [Export] public SoundEvent FireLoop { get; set; } = SoundLibrary.FireLoop();
    [Export] public SoundEvent WaterSplash { get; set; } = SoundLibrary.WaterSplash();

    [ExportGroup("Interface")]
    [Export] public SoundEvent Hitmarker { get; set; } = SoundLibrary.Hitmarker();
    [Export] public SoundEvent HitmarkerWeak { get; set; } = SoundLibrary.HitmarkerWeak();
    [Export] public SoundEvent UiMove { get; set; } = SoundLibrary.UiMove();
    [Export] public SoundEvent UiSelect { get; set; } = SoundLibrary.UiSelect();
    [Export] public SoundEvent PauseOpen { get; set; } = SoundLibrary.PauseOpen();
    [Export] public SoundEvent PauseClose { get; set; } = SoundLibrary.PauseClose();

    private SoundEvent[] _byId;

    /// <summary>
    /// The event for an id, or null if that slot was deliberately cleared.
    ///
    /// The flat array is built once and its length asserted against
    /// <see cref="Sfx.Count"/>, so an id added to the enum without a slot added
    /// here fails at startup with a message instead of silently shifting every
    /// later sound onto the wrong row.
    /// </summary>
    public SoundEvent Resolve(Sfx id)
    {
        _byId ??= BuildTable();

        int index = (int)id;
        if (index <= 0 || index >= _byId.Length)
        {
            return null;
        }

        return _byId[index];
    }

    /// <summary>Drops the cached table after a slot is reassigned at runtime.</summary>
    public void Invalidate()
    {
        _byId = null;
    }

    private SoundEvent[] BuildTable()
    {
        // Must be in Sfx order, index 0 being Sfx.None.
        SoundEvent[] table =
        {
            null,

            RifleFire, ShotgunFire, RailgunFire, RocketFire, FlameLoop, CannonLoop,
            DryFire, ReloadStart, ReloadFinish, WeaponSwitch,

            MeleeSwingLight, MeleeSwingHeavy, MeleeImpact, GrenadeThrow, GrenadeBounce,

            DesignatorMark, StrikeIncoming, Explosion, ExplosionDistant,

            ImpactDirt, ImpactStone, ImpactMetal, ImpactFlesh, ImpactWater,

            FootstepDry, FootstepWet, FootstepWater, Jump, LandSoft, LandHard,
            JetpackLoop, ManipulatorGrab, ManipulatorLaunch,
            GrappleFire, GrappleAttach, GrappleReelLoop, GrappleRelease,

            AgentIdle, AgentHurt, AgentDie, CornichonDash, SpitterSpit, SpitSplat, PickleBite, TankStomp,
            PickleThrow, NestAmbient,
            BossTelegraph, BossSlam, BossRollLoop, GeyserBubble, GeyserErupt, WartBurst, BossDeath,

            ShipEngineLoop, ShipBoost, ShipBoard, ShipDisembark, ShipImpact,

            StructureDestroyed, PodLand, PodGrant,
            CompoundAlarm, TowerFlak, ArmourDeflect, CoreDeath,

            VoxelChip, VoxelCollapse, RubbleImpact, FireLoop, WaterSplash,

            Hitmarker, HitmarkerWeak, UiMove, UiSelect, PauseOpen, PauseClose,
        };

        if (table.Length != (int)Sfx.Count)
        {
            GD.PushError(
                $"SoundBank table has {table.Length} entries but Sfx declares {(int)Sfx.Count}. " +
                "Every Sfx entry needs a slot here, in the same order.");
        }

        return table;
    }
}
