namespace DillControl.Audio;

/// <summary>
/// The placeholder for every sound in the game, as one table.
///
/// PLACEHOLDER, all of it. Each entry is a <see cref="SoundEvent"/> whose
/// <see cref="SoundEvent.Streams"/> is empty, so it plays its
/// <see cref="SynthRecipe"/>. Assigning a real recording to the matching slot
/// on <c>snd_bank_default.tres</c> silences the recipe and keeps every level,
/// distance and retrigger rule already tuned here.
///
/// These are <see cref="SoundBank"/>'s property initialisers, which is what
/// makes an empty snd_bank_default.tres mean "all placeholders" rather than
/// "silence" -- the same convention the lab used for every other .tres in
/// the project.
///
/// The numbers are a mix decision as much as a synthesis one. Two rules run
/// through the whole table and are worth knowing before retuning it:
///
/// UnitSize is the "how far does this carry" dial, not MaxDistance. A rifle
/// carries further than a footstep because its UnitSize is larger, and
/// MaxDistance only says where the voice stops being spawned at all.
///
/// Anything the horde can emit is on a short MaxDistance and a real
/// MinRetriggerSeconds, because there are hundreds of agents and the mix, not
/// the frame budget, is what breaks first.
/// </summary>
public static class SoundLibrary
{
    // ---------------- Guns ----------------

    public static SoundEvent RifleFire() => Event(
        seed: 11, duration: 0.19f, lowPassStart: 9000.0f, lowPassEnd: 900.0f,
        decay: 4.0f, body: 0.55f, bodyStart: 150.0f, bodyEnd: 52.0f, drive: 2.6f,
        unitSize: 20.0f, maxDistance: 300.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent ShotgunFire() => Event(
        seed: 12, duration: 0.36f, lowPassStart: 6500.0f, lowPassEnd: 420.0f,
        decay: 2.6f, body: 0.85f, bodyStart: 115.0f, bodyEnd: 38.0f, drive: 3.2f,
        unitSize: 24.0f, maxDistance: 320.0f, bus: AudioDirector.WeaponBus);

    /// <summary>Tonal rather than a crack -- it is the one gun that is not a
    /// chemical explosion, and it should not sound like one.</summary>
    public static SoundEvent RailgunFire() => Event(
        seed: 13, duration: 0.55f, shape: SynthShape.Saw, noiseMix: 0.22f,
        startFrequency: 2400.0f, endFrequency: 170.0f,
        lowPassStart: 8000.0f, lowPassEnd: 1100.0f, decay: 2.2f,
        body: 0.4f, bodyStart: 180.0f, bodyEnd: 45.0f, drive: 2.0f,
        unitSize: 26.0f, maxDistance: 340.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent RocketFire() => Event(
        seed: 14, duration: 0.62f, lowPassStart: 1100.0f, lowPassEnd: 3600.0f,
        decay: 1.7f, body: 0.6f, bodyStart: 85.0f, bodyEnd: 30.0f, drive: 1.8f,
        unitSize: 22.0f, maxDistance: 300.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent FlameLoop() => Event(
        seed: 15, duration: 1.4f, sustain: true, looping: true, crossfade: 0.16f,
        lowPassStart: 1900.0f, lowPassEnd: 1900.0f, crackleRate: 130.0f, crackleAmount: 0.55f,
        drive: 1.6f, pitchJitter: 0.0f, volumeDb: -3.0f,
        unitSize: 14.0f, maxDistance: 120.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent CannonLoop() => Event(
        seed: 16, duration: 0.8f, sustain: true, looping: true, crossfade: 0.1f,
        shape: SynthShape.Saw, noiseMix: 0.3f, startFrequency: 165.0f, endFrequency: 165.0f,
        vibratoRate: 32.0f, vibratoDepth: 0.05f,
        lowPassStart: 2600.0f, lowPassEnd: 2600.0f, drive: 2.2f, pitchJitter: 0.0f,
        unitSize: 20.0f, maxDistance: 260.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent DryFire() => Event(
        seed: 17, duration: 0.06f, lowPassStart: 7000.0f, lowPassEnd: 2600.0f,
        highPass: 900.0f, decay: 6.0f, volumeDb: -6.0f,
        unitSize: 6.0f, maxDistance: 40.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent ReloadStart() => Event(
        seed: 18, duration: 0.13f, lowPassStart: 4200.0f, lowPassEnd: 1300.0f,
        highPass: 350.0f, decay: 5.0f, volumeDb: -4.0f,
        unitSize: 7.0f, maxDistance: 50.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent ReloadFinish() => Event(
        seed: 19, duration: 0.17f, lowPassStart: 3200.0f, lowPassEnd: 900.0f,
        decay: 4.5f, body: 0.3f, bodyStart: 190.0f, bodyEnd: 95.0f, volumeDb: -3.0f,
        unitSize: 7.0f, maxDistance: 50.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent WeaponSwitch() => Event(
        seed: 20, duration: 0.11f, lowPassStart: 5200.0f, lowPassEnd: 1600.0f,
        highPass: 300.0f, decay: 4.0f, volumeDb: -7.0f,
        unitSize: 6.0f, maxDistance: 40.0f, bus: AudioDirector.WeaponBus);

    // ---------------- Close quarters and thrown ----------------

    public static SoundEvent MeleeSwingLight() => Event(
        seed: 21, duration: 0.22f, lowPassStart: 900.0f, lowPassEnd: 2700.0f,
        highPass: 220.0f, decay: 2.2f, volumeDb: -4.0f,
        unitSize: 8.0f, maxDistance: 50.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent MeleeSwingHeavy() => Event(
        seed: 22, duration: 0.3f, lowPassStart: 650.0f, lowPassEnd: 2100.0f,
        highPass: 160.0f, decay: 1.9f, volumeDb: -2.0f,
        unitSize: 9.0f, maxDistance: 55.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent MeleeImpact() => Event(
        seed: 23, duration: 0.21f, lowPassStart: 2600.0f, lowPassEnd: 320.0f,
        decay: 3.2f, body: 0.7f, bodyStart: 125.0f, bodyEnd: 45.0f, drive: 1.9f,
        unitSize: 12.0f, maxDistance: 80.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent GrenadeThrow() => Event(
        seed: 24, duration: 0.19f, lowPassStart: 2100.0f, lowPassEnd: 850.0f,
        highPass: 200.0f, decay: 2.6f, volumeDb: -6.0f,
        unitSize: 7.0f, maxDistance: 45.0f, bus: AudioDirector.WeaponBus);

    public static SoundEvent GrenadeBounce() => Event(
        seed: 25, duration: 0.09f, lowPassStart: 3600.0f, lowPassEnd: 1200.0f,
        decay: 5.0f, body: 0.3f, bodyStart: 210.0f, bodyEnd: 120.0f,
        volumeDb: -5.0f, pitchJitter: 0.14f, minRetrigger: 0.06f,
        unitSize: 9.0f, maxDistance: 70.0f, bus: AudioDirector.WorldBus);

    // ---------------- Ordnance ----------------

    public static SoundEvent DesignatorMark() => Event(
        seed: 26, duration: 0.15f, shape: SynthShape.Sine, noiseMix: 0.05f,
        startFrequency: 880.0f, endFrequency: 1320.0f, decay: 2.5f, volumeDb: -6.0f,
        pitchJitter: 0.0f, unitSize: 6.0f, maxDistance: 40.0f, bus: AudioDirector.UiBus);

    /// <summary>
    /// The falling whistle. Long and quiet on purpose: it is the one warning
    /// the player gets that a salvo they called is about to land on them.
    /// </summary>
    public static SoundEvent StrikeIncoming() => Event(
        seed: 27, duration: 1.7f, shape: SynthShape.Sine, noiseMix: 0.14f,
        startFrequency: 2300.0f, endFrequency: 400.0f,
        lowPassStart: 6000.0f, lowPassEnd: 2200.0f, decay: 0.8f, attack: 0.25f,
        volumeDb: -4.0f, pitchJitter: 0.03f,
        unitSize: 45.0f, maxDistance: 400.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent Explosion() => Event(
        seed: 28, duration: 1.7f, lowPassStart: 3200.0f, lowPassEnd: 170.0f,
        decay: 1.6f, body: 1.0f, bodyStart: 72.0f, bodyEnd: 24.0f, bodyDecay: 1.2f,
        crackleRate: 45.0f, crackleAmount: 0.3f, drive: 2.3f,
        unitSize: 40.0f, maxDistance: 600.0f, bus: AudioDirector.WorldBus, maxVoices: 12);

    /// <summary>
    /// What an explosion sounds like from further off than its own blast can
    /// reach. Not a quieter copy -- distance eats the top end first, so this is
    /// the same event with the crack taken out and the tail left in.
    /// </summary>
    public static SoundEvent ExplosionDistant() => Event(
        seed: 29, duration: 2.3f, lowPassStart: 700.0f, lowPassEnd: 110.0f,
        decay: 1.3f, attack: 0.05f, body: 0.9f, bodyStart: 58.0f, bodyEnd: 20.0f, bodyDecay: 1.0f,
        volumeDb: -5.0f, unitSize: 120.0f, maxDistance: 1400.0f,
        bus: AudioDirector.WorldBus, maxVoices: 6);

    // ---------------- Impacts ----------------

    public static SoundEvent ImpactDirt() => Event(
        seed: 30, duration: 0.13f, lowPassStart: 1900.0f, lowPassEnd: 520.0f,
        decay: 4.0f, volumeDb: -5.0f, pitchJitter: 0.15f, minRetrigger: 0.03f,
        unitSize: 8.0f, maxDistance: 70.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent ImpactStone() => Event(
        seed: 31, duration: 0.11f, lowPassStart: 6800.0f, lowPassEnd: 1700.0f,
        highPass: 520.0f, decay: 5.0f, volumeDb: -4.0f, pitchJitter: 0.16f, minRetrigger: 0.03f,
        unitSize: 9.0f, maxDistance: 80.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent ImpactMetal() => Event(
        seed: 32, duration: 0.2f, shape: SynthShape.Triangle, noiseMix: 0.45f,
        startFrequency: 1900.0f, endFrequency: 1250.0f,
        lowPassStart: 8000.0f, lowPassEnd: 2600.0f, highPass: 700.0f, decay: 4.5f,
        volumeDb: -4.0f, pitchJitter: 0.18f, minRetrigger: 0.03f,
        unitSize: 10.0f, maxDistance: 90.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent ImpactFlesh() => Event(
        seed: 33, duration: 0.15f, lowPassStart: 1300.0f, lowPassEnd: 300.0f,
        decay: 3.5f, body: 0.5f, bodyStart: 155.0f, bodyEnd: 62.0f,
        volumeDb: -3.0f, pitchJitter: 0.14f, minRetrigger: 0.03f,
        unitSize: 9.0f, maxDistance: 70.0f, bus: AudioDirector.EnemyBus);

    public static SoundEvent ImpactWater() => Event(
        seed: 34, duration: 0.26f, lowPassStart: 4200.0f, lowPassEnd: 900.0f,
        decay: 2.5f, crackleRate: 90.0f, crackleAmount: 0.4f,
        volumeDb: -4.0f, pitchJitter: 0.16f, minRetrigger: 0.04f,
        unitSize: 9.0f, maxDistance: 70.0f, bus: AudioDirector.WorldBus);

    // ---------------- Player ----------------

    public static SoundEvent FootstepDry() => Event(
        seed: 35, duration: 0.1f, lowPassStart: 2300.0f, lowPassEnd: 720.0f,
        decay: 4.0f, volumeDb: -11.0f, pitchJitter: 0.13f,
        unitSize: 4.0f, maxDistance: 30.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent FootstepWet() => Event(
        seed: 36, duration: 0.14f, lowPassStart: 3100.0f, lowPassEnd: 820.0f,
        decay: 3.5f, crackleRate: 70.0f, crackleAmount: 0.35f,
        volumeDb: -9.0f, pitchJitter: 0.13f,
        unitSize: 4.0f, maxDistance: 30.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent FootstepWater() => Event(
        seed: 37, duration: 0.24f, lowPassStart: 5000.0f, lowPassEnd: 1200.0f,
        decay: 2.6f, crackleRate: 150.0f, crackleAmount: 0.45f,
        volumeDb: -7.0f, pitchJitter: 0.13f,
        unitSize: 5.0f, maxDistance: 35.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent Jump() => Event(
        seed: 38, duration: 0.13f, lowPassStart: 1900.0f, lowPassEnd: 800.0f,
        highPass: 180.0f, decay: 3.0f, volumeDb: -10.0f,
        unitSize: 4.0f, maxDistance: 30.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent LandSoft() => Event(
        seed: 39, duration: 0.17f, lowPassStart: 1600.0f, lowPassEnd: 420.0f,
        decay: 3.6f, body: 0.35f, bodyStart: 115.0f, bodyEnd: 52.0f, volumeDb: -8.0f,
        unitSize: 5.0f, maxDistance: 35.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent LandHard() => Event(
        seed: 40, duration: 0.3f, lowPassStart: 2300.0f, lowPassEnd: 260.0f,
        decay: 2.5f, body: 0.85f, bodyStart: 100.0f, bodyEnd: 36.0f, drive: 1.7f,
        volumeDb: -3.0f, unitSize: 8.0f, maxDistance: 60.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent JetpackLoop() => Event(
        seed: 41, duration: 1.0f, sustain: true, looping: true, crossfade: 0.12f,
        lowPassStart: 2500.0f, lowPassEnd: 2500.0f,
        body: 0.3f, bodyStart: 72.0f, bodyEnd: 72.0f, drive: 1.5f,
        volumeDb: -6.0f, pitchJitter: 0.0f,
        unitSize: 8.0f, maxDistance: 80.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent ManipulatorGrab() => Event(
        seed: 42, duration: 0.22f, shape: SynthShape.Sine, noiseMix: 0.3f,
        startFrequency: 300.0f, endFrequency: 940.0f,
        lowPassStart: 4000.0f, lowPassEnd: 2200.0f, decay: 2.5f, volumeDb: -6.0f,
        unitSize: 7.0f, maxDistance: 45.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent ManipulatorLaunch() => Event(
        seed: 43, duration: 0.32f, shape: SynthShape.Sine, noiseMix: 0.35f,
        startFrequency: 960.0f, endFrequency: 190.0f,
        lowPassStart: 4200.0f, lowPassEnd: 1100.0f, decay: 2.2f,
        body: 0.5f, bodyStart: 140.0f, bodyEnd: 50.0f, volumeDb: -3.0f,
        unitSize: 10.0f, maxDistance: 70.0f, bus: AudioDirector.PlayerBus);

    /// <summary>A hiss of line paying out of the launcher.</summary>
    public static SoundEvent GrappleFire() => Event(
        seed: 83, duration: 0.24f, lowPassStart: 5200.0f, lowPassEnd: 1600.0f,
        highPass: 400.0f, decay: 2.4f, body: 0.3f, bodyStart: 210.0f, bodyEnd: 90.0f,
        volumeDb: -5.0f, unitSize: 8.0f, maxDistance: 60.0f, bus: AudioDirector.PlayerBus);

    /// <summary>A hard metallic bite, so the moment the line holds is heard
    /// even when the anchor is eighty metres away.</summary>
    public static SoundEvent GrappleAttach() => Event(
        seed: 84, duration: 0.2f, shape: SynthShape.Square, noiseMix: 0.45f,
        startFrequency: 1400.0f, endFrequency: 620.0f,
        lowPassStart: 6500.0f, lowPassEnd: 1800.0f, decay: 3.4f, drive: 1.6f,
        volumeDb: -4.0f, unitSize: 30.0f, maxDistance: 120.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent GrappleReelLoop() => Event(
        seed: 85, duration: 0.8f, sustain: true, looping: true, crossfade: 0.1f,
        shape: SynthShape.Saw, noiseMix: 0.5f, startFrequency: 95.0f, endFrequency: 95.0f,
        lowPassStart: 1800.0f, lowPassEnd: 1800.0f, vibratoRate: 22.0f, vibratoDepth: 0.08f,
        volumeDb: -10.0f, pitchJitter: 0.0f,
        unitSize: 6.0f, maxDistance: 40.0f, bus: AudioDirector.PlayerBus);

    public static SoundEvent GrappleRelease() => Event(
        seed: 86, duration: 0.14f, shape: SynthShape.Sine, noiseMix: 0.4f,
        startFrequency: 700.0f, endFrequency: 260.0f,
        lowPassStart: 3600.0f, lowPassEnd: 900.0f, decay: 3.0f, volumeDb: -9.0f,
        unitSize: 5.0f, maxDistance: 35.0f, bus: AudioDirector.PlayerBus);

    // ---------------- Enemies ----------------

    public static SoundEvent AgentIdle() => Event(
        seed: 44, duration: 0.75f, shape: SynthShape.Saw, noiseMix: 0.4f,
        startFrequency: 92.0f, endFrequency: 74.0f,
        vibratoRate: 9.0f, vibratoDepth: 0.12f,
        lowPassStart: 900.0f, lowPassEnd: 520.0f, decay: 1.8f, drive: 2.0f,
        volumeDb: -10.0f, pitchJitter: 0.2f, minRetrigger: 0.25f,
        unitSize: 7.0f, maxDistance: 45.0f, bus: AudioDirector.EnemyBus, maxVoices: 5);

    public static SoundEvent AgentHurt() => Event(
        seed: 45, duration: 0.3f, shape: SynthShape.Saw, noiseMix: 0.5f,
        startFrequency: 180.0f, endFrequency: 112.0f,
        lowPassStart: 1700.0f, lowPassEnd: 700.0f, decay: 3.0f, drive: 2.5f,
        volumeDb: -7.0f, pitchJitter: 0.22f, minRetrigger: 0.05f,
        unitSize: 8.0f, maxDistance: 55.0f, bus: AudioDirector.EnemyBus, maxVoices: 6);

    public static SoundEvent AgentDie() => Event(
        seed: 46, duration: 0.8f, shape: SynthShape.Saw, noiseMix: 0.45f,
        startFrequency: 155.0f, endFrequency: 58.0f,
        vibratoRate: 15.0f, vibratoDepth: 0.16f,
        lowPassStart: 1500.0f, lowPassEnd: 360.0f, decay: 1.9f, drive: 2.2f,
        volumeDb: -6.0f, pitchJitter: 0.2f, minRetrigger: 0.06f,
        unitSize: 9.0f, maxDistance: 65.0f, bus: AudioDirector.EnemyBus, maxVoices: 6);

    public static SoundEvent CornichonDash() => Event(
        seed: 47, duration: 0.3f, lowPassStart: 600.0f, lowPassEnd: 2600.0f,
        highPass: 200.0f, decay: 2.2f, volumeDb: -6.0f, pitchJitter: 0.15f, minRetrigger: 0.08f,
        unitSize: 9.0f, maxDistance: 60.0f, bus: AudioDirector.EnemyBus, maxVoices: 5);

    public static SoundEvent SpitterSpit() => Event(
        seed: 48, duration: 0.24f, lowPassStart: 5200.0f, lowPassEnd: 820.0f,
        decay: 2.8f, crackleRate: 120.0f, crackleAmount: 0.4f,
        volumeDb: -7.0f, pitchJitter: 0.18f, minRetrigger: 0.08f,
        unitSize: 8.0f, maxDistance: 55.0f, bus: AudioDirector.EnemyBus, maxVoices: 5);

    /// <summary>Brine landing: a short wet slap, quieter than the spit that threw it.</summary>
    public static SoundEvent SpitSplat() => Event(
        seed: 94, duration: 0.18f, lowPassStart: 3800.0f, lowPassEnd: 600.0f,
        decay: 4.0f, crackleRate: 200.0f, crackleAmount: 0.5f,
        volumeDb: -10.0f, pitchJitter: 0.2f, minRetrigger: 0.06f,
        unitSize: 6.0f, maxDistance: 40.0f, bus: AudioDirector.EnemyBus, maxVoices: 4);

    /// <summary>A pickle reaching you: a crunchy snap, the cue that something is on you.</summary>
    public static SoundEvent PickleBite() => Event(
        seed: 95, duration: 0.14f, noiseMix: 0.85f, lowPassStart: 7000.0f, lowPassEnd: 1400.0f,
        highPass: 300.0f, decay: 5.0f, crackleRate: 260.0f, crackleAmount: 0.6f,
        volumeDb: -5.0f, pitchJitter: 0.2f, minRetrigger: 0.07f,
        unitSize: 6.0f, maxDistance: 35.0f, bus: AudioDirector.EnemyBus, maxVoices: 4);

    public static SoundEvent TankStomp() => Event(
        seed: 49, duration: 0.42f, lowPassStart: 1300.0f, lowPassEnd: 150.0f,
        decay: 2.0f, body: 1.0f, bodyStart: 78.0f, bodyEnd: 28.0f, drive: 1.9f,
        volumeDb: -4.0f, pitchJitter: 0.12f, minRetrigger: 0.12f,
        unitSize: 13.0f, maxDistance: 90.0f, bus: AudioDirector.EnemyBus, maxVoices: 5);

    /// <summary>
    /// A thrower letting go: a grunt and a rising whoosh, so a lob is heard
    /// leaving before it is seen coming down.
    /// </summary>
    public static SoundEvent PickleThrow() => Event(
        seed: 50, duration: 0.45f, shape: SynthShape.Saw, noiseMix: 0.6f,
        startFrequency: 140.0f, endFrequency: 260.0f,
        lowPassStart: 900.0f, lowPassEnd: 3200.0f, highPass: 120.0f, decay: 2.6f, attack: 0.03f, drive: 1.6f,
        volumeDb: -6.0f, pitchJitter: 0.16f, minRetrigger: 0.1f,
        unitSize: 10.0f, maxDistance: 80.0f, bus: AudioDirector.EnemyBus, maxVoices: 4);

    public static SoundEvent NestAmbient() => Event(
        seed: 51, duration: 2.0f, sustain: true, looping: true, crossfade: 0.25f,
        shape: SynthShape.Saw, noiseMix: 0.65f, startFrequency: 62.0f, endFrequency: 62.0f,
        lowPassStart: 750.0f, lowPassEnd: 750.0f, crackleRate: 28.0f, crackleAmount: 0.3f,
        volumeDb: -13.0f, pitchJitter: 0.0f,
        unitSize: 9.0f, maxDistance: 60.0f, bus: AudioDirector.EnemyBus);

    // ---------------- Mother Brine ----------------
    //
    // The largest thing in the game, so the largest UnitSize: she is heard
    // from further away than any gun, because hearing her coming is part of
    // reading her.

    /// <summary>A low groan under every wind-up. One sound for every telegraph,
    /// so "she is about to do something" is learned once.</summary>
    public static SoundEvent BossTelegraph() => Event(
        seed: 87, duration: 0.9f, shape: SynthShape.Saw, noiseMix: 0.35f,
        startFrequency: 62.0f, endFrequency: 44.0f, lowPassStart: 700.0f, lowPassEnd: 380.0f,
        attack: 0.12f, decay: 1.4f, vibratoRate: 5.0f, vibratoDepth: 0.06f, drive: 1.6f,
        volumeDb: -2.0f, unitSize: 60.0f, maxDistance: 400.0f, minRetrigger: 0.3f);

    public static SoundEvent BossSlam() => Event(
        seed: 88, duration: 1.3f, lowPassStart: 2400.0f, lowPassEnd: 160.0f,
        decay: 1.5f, body: 1.0f, bodyStart: 58.0f, bodyEnd: 24.0f, bodyDecay: 1.2f, drive: 2.6f,
        volumeDb: 2.0f, unitSize: 80.0f, maxDistance: 600.0f);

    public static SoundEvent BossRollLoop() => Event(
        seed: 89, duration: 1.2f, sustain: true, looping: true, crossfade: 0.2f,
        lowPassStart: 600.0f, lowPassEnd: 600.0f, crackleRate: 18.0f, crackleAmount: 0.6f,
        body: 0.7f, bodyStart: 40.0f, bodyEnd: 40.0f, drive: 2.0f, pitchJitter: 0.0f,
        volumeDb: -2.0f, unitSize: 60.0f, maxDistance: 400.0f);

    public static SoundEvent GeyserBubble() => Event(
        seed: 90, duration: 1.1f, lowPassStart: 900.0f, lowPassEnd: 1400.0f,
        attack: 0.3f, decay: 0.8f, crackleRate: 40.0f, crackleAmount: 0.8f,
        volumeDb: -6.0f, unitSize: 14.0f, maxDistance: 120.0f, maxVoices: 4);

    public static SoundEvent GeyserErupt() => Event(
        seed: 91, duration: 1.4f, lowPassStart: 5200.0f, lowPassEnd: 700.0f,
        decay: 1.3f, body: 0.5f, bodyStart: 90.0f, bodyEnd: 40.0f, drive: 1.4f,
        volumeDb: -1.0f, unitSize: 30.0f, maxDistance: 260.0f, maxVoices: 4);

    public static SoundEvent WartBurst() => Event(
        seed: 92, duration: 0.6f, lowPassStart: 3000.0f, lowPassEnd: 500.0f,
        decay: 2.2f, body: 0.8f, bodyStart: 120.0f, bodyEnd: 40.0f, crackleRate: 60.0f,
        crackleAmount: 0.5f, drive: 1.8f, volumeDb: 0.0f, unitSize: 40.0f, maxDistance: 300.0f);

    public static SoundEvent BossDeath() => Event(
        seed: 93, duration: 3.2f, shape: SynthShape.Saw, noiseMix: 0.5f,
        startFrequency: 70.0f, endFrequency: 22.0f, lowPassStart: 1200.0f, lowPassEnd: 150.0f,
        attack: 0.05f, decay: 0.9f, body: 0.8f, bodyStart: 50.0f, bodyEnd: 20.0f,
        vibratoRate: 3.0f, vibratoDepth: 0.1f, drive: 2.2f,
        volumeDb: 2.0f, unitSize: 90.0f, maxDistance: 700.0f);

    // ---------------- Vehicle ----------------

    public static SoundEvent ShipEngineLoop() => Event(
        seed: 52, duration: 1.2f, sustain: true, looping: true, crossfade: 0.16f,
        shape: SynthShape.Saw, noiseMix: 0.35f, startFrequency: 72.0f, endFrequency: 72.0f,
        lowPassStart: 1500.0f, lowPassEnd: 1500.0f,
        body: 0.4f, bodyStart: 46.0f, bodyEnd: 46.0f, drive: 1.8f,
        volumeDb: -5.0f, pitchJitter: 0.0f,
        unitSize: 16.0f, maxDistance: 220.0f, bus: AudioDirector.ShipBus);

    public static SoundEvent ShipBoost() => Event(
        seed: 53, duration: 0.5f, lowPassStart: 900.0f, lowPassEnd: 3400.0f,
        decay: 1.8f, body: 0.5f, bodyStart: 95.0f, bodyEnd: 42.0f, drive: 1.7f,
        volumeDb: -4.0f, unitSize: 18.0f, maxDistance: 220.0f, bus: AudioDirector.ShipBus);

    public static SoundEvent ShipBoard() => Event(
        seed: 54, duration: 0.26f, lowPassStart: 3400.0f, lowPassEnd: 900.0f,
        highPass: 250.0f, decay: 4.0f, body: 0.25f, bodyStart: 170.0f, bodyEnd: 80.0f,
        volumeDb: -5.0f, unitSize: 7.0f, maxDistance: 45.0f, bus: AudioDirector.ShipBus);

    public static SoundEvent ShipDisembark() => Event(
        seed: 55, duration: 0.24f, lowPassStart: 2800.0f, lowPassEnd: 750.0f,
        highPass: 220.0f, decay: 4.2f, volumeDb: -6.0f,
        unitSize: 7.0f, maxDistance: 45.0f, bus: AudioDirector.ShipBus);

    public static SoundEvent ShipImpact() => Event(
        seed: 56, duration: 0.36f, lowPassStart: 3600.0f, lowPassEnd: 380.0f,
        decay: 3.0f, body: 0.7f, bodyStart: 130.0f, bodyEnd: 44.0f,
        crackleRate: 70.0f, crackleAmount: 0.35f, drive: 2.0f,
        volumeDb: -3.0f, pitchJitter: 0.12f, minRetrigger: 0.15f,
        unitSize: 16.0f, maxDistance: 160.0f, bus: AudioDirector.ShipBus);

    // ---------------- Site and compound ----------------

    public static SoundEvent StructureDestroyed() => Event(
        seed: 62, duration: 0.95f, lowPassStart: 2400.0f, lowPassEnd: 300.0f,
        decay: 1.8f, body: 0.6f, bodyStart: 84.0f, bodyEnd: 30.0f,
        crackleRate: 190.0f, crackleAmount: 0.5f, drive: 1.8f,
        volumeDb: -2.0f, unitSize: 20.0f, maxDistance: 220.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent PodLand() => Event(
        seed: 64, duration: 0.55f, lowPassStart: 1700.0f, lowPassEnd: 190.0f,
        decay: 2.2f, body: 1.0f, bodyStart: 78.0f, bodyEnd: 27.0f, drive: 1.8f,
        volumeDb: -2.0f, unitSize: 26.0f, maxDistance: 320.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent PodGrant() => Event(
        seed: 65, duration: 0.32f, shape: SynthShape.Sine, noiseMix: 0.06f,
        startFrequency: 920.0f, endFrequency: 1840.0f, decay: 2.4f,
        volumeDb: -5.0f, pitchJitter: 0.0f,
        unitSize: 8.0f, maxDistance: 50.0f, bus: AudioDirector.UiBus);

    /// <summary>
    /// The compound crossing a threshold. The old extraction alarm, slower and
    /// lower: it is a nest waking up, not a klaxon.
    /// </summary>
    public static SoundEvent CompoundAlarm() => Event(
        seed: 67, duration: 1.5f, shape: SynthShape.Square, noiseMix: 0.08f,
        startFrequency: 520.0f, endFrequency: 430.0f,
        vibratoRate: 4.0f, vibratoDepth: 0.09f,
        lowPassStart: 2000.0f, lowPassEnd: 1400.0f, decay: 0.7f, attack: 0.05f,
        volumeDb: -8.0f, pitchJitter: 0.0f,
        unitSize: 40.0f, maxDistance: 400.0f, bus: AudioDirector.UiBus);

    /// <summary>
    /// A spitter tower throwing a ball of lead shot: a wet, heavy thump with a
    /// long reach, because a pilot two hundred metres out needs to hear which
    /// tower is firing.
    /// </summary>
    public static SoundEvent TowerFlak() => Event(
        seed: 60, duration: 0.3f, noiseMix: 0.6f, lowPassStart: 2600.0f, lowPassEnd: 420.0f,
        decay: 3.6f, body: 0.8f, bodyStart: 120.0f, bodyEnd: 46.0f, drive: 2.2f,
        volumeDb: -4.0f, pitchJitter: 0.12f, minRetrigger: 0.05f,
        unitSize: 30.0f, maxDistance: 320.0f, bus: AudioDirector.WorldBus, maxVoices: 6);

    /// <summary>
    /// A round that hit the jar and did not carve it. A bright ricochet, so
    /// "my gun does nothing to this" is something you hear rather than infer.
    /// </summary>
    public static SoundEvent ArmourDeflect() => Event(
        seed: 59, duration: 0.2f, shape: SynthShape.Triangle, noiseMix: 0.35f,
        startFrequency: 2100.0f, endFrequency: 1500.0f,
        lowPassStart: 9000.0f, lowPassEnd: 3200.0f, highPass: 600.0f, decay: 5.0f,
        volumeDb: -6.0f, pitchJitter: 0.2f, minRetrigger: 0.06f,
        unitSize: 12.0f, maxDistance: 140.0f, bus: AudioDirector.WorldBus, maxVoices: 4);

    /// <summary>The hive's core going up: a long, low roll under the explosion itself.</summary>
    public static SoundEvent CoreDeath() => Event(
        seed: 66, duration: 2.8f, shape: SynthShape.Saw, noiseMix: 0.55f,
        startFrequency: 70.0f, endFrequency: 30.0f,
        lowPassStart: 1600.0f, lowPassEnd: 240.0f, decay: 1.1f,
        body: 0.9f, bodyStart: 52.0f, bodyEnd: 24.0f, drive: 1.9f,
        crackleRate: 60.0f, crackleAmount: 0.4f,
        volumeDb: -1.0f, pitchJitter: 0.0f,
        unitSize: 60.0f, maxDistance: 700.0f, bus: AudioDirector.WorldBus);

    // ---------------- World ----------------

    /// <summary>
    /// A round pitting a wall. Heavily retrigger-limited: sustained fire carves
    /// the same wall many times a second and every one of those is a chip.
    /// </summary>
    public static SoundEvent VoxelChip() => Event(
        seed: 71, duration: 0.11f, lowPassStart: 7400.0f, lowPassEnd: 1600.0f,
        highPass: 600.0f, decay: 5.0f, crackleRate: 45.0f, crackleAmount: 0.3f,
        volumeDb: -6.0f, pitchJitter: 0.2f, minRetrigger: 0.05f,
        unitSize: 9.0f, maxDistance: 80.0f, bus: AudioDirector.WorldBus, maxVoices: 6);

    public static SoundEvent VoxelCollapse() => Event(
        seed: 72, duration: 0.85f, lowPassStart: 2500.0f, lowPassEnd: 290.0f,
        decay: 1.8f, body: 0.5f, bodyStart: 82.0f, bodyEnd: 30.0f,
        crackleRate: 230.0f, crackleAmount: 0.55f, drive: 1.7f,
        volumeDb: -3.0f, pitchJitter: 0.1f, minRetrigger: 0.12f,
        unitSize: 18.0f, maxDistance: 200.0f, bus: AudioDirector.WorldBus, maxVoices: 5);

    public static SoundEvent RubbleImpact() => Event(
        seed: 73, duration: 0.17f, lowPassStart: 3400.0f, lowPassEnd: 700.0f,
        decay: 4.0f, crackleRate: 70.0f, crackleAmount: 0.35f,
        volumeDb: -10.0f, pitchJitter: 0.22f, minRetrigger: 0.04f,
        unitSize: 7.0f, maxDistance: 60.0f, bus: AudioDirector.WorldBus, maxVoices: 6);

    public static SoundEvent FireLoop() => Event(
        seed: 74, duration: 1.6f, sustain: true, looping: true, crossfade: 0.2f,
        lowPassStart: 1500.0f, lowPassEnd: 1500.0f,
        crackleRate: 160.0f, crackleAmount: 0.5f, drive: 1.5f,
        volumeDb: -8.0f, pitchJitter: 0.0f,
        unitSize: 11.0f, maxDistance: 120.0f, bus: AudioDirector.WorldBus);

    public static SoundEvent WaterSplash() => Event(
        seed: 75, duration: 0.42f, lowPassStart: 5200.0f, lowPassEnd: 820.0f,
        decay: 2.2f, crackleRate: 170.0f, crackleAmount: 0.45f,
        volumeDb: -5.0f, pitchJitter: 0.14f, minRetrigger: 0.08f,
        unitSize: 10.0f, maxDistance: 80.0f, bus: AudioDirector.WorldBus);

    // ---------------- Interface ----------------

    public static SoundEvent Hitmarker() => Event(
        seed: 77, duration: 0.055f, shape: SynthShape.Sine, noiseMix: 0.22f,
        startFrequency: 1450.0f, endFrequency: 1100.0f, decay: 3.5f,
        volumeDb: -13.0f, pitchJitter: 0.03f, minRetrigger: 0.04f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus, maxVoices: 4);

    public static SoundEvent HitmarkerWeak() => Event(
        seed: 78, duration: 0.075f, shape: SynthShape.Sine, noiseMix: 0.2f,
        startFrequency: 1950.0f, endFrequency: 1520.0f, decay: 3.0f,
        volumeDb: -11.0f, pitchJitter: 0.03f, minRetrigger: 0.04f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus, maxVoices: 4);

    public static SoundEvent UiMove() => Event(
        seed: 79, duration: 0.05f, shape: SynthShape.Sine, noiseMix: 0.04f,
        startFrequency: 660.0f, endFrequency: 660.0f, decay: 3.0f,
        volumeDb: -14.0f, pitchJitter: 0.0f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus);

    public static SoundEvent UiSelect() => Event(
        seed: 80, duration: 0.1f, shape: SynthShape.Sine, noiseMix: 0.04f,
        startFrequency: 880.0f, endFrequency: 1180.0f, decay: 2.6f,
        volumeDb: -12.0f, pitchJitter: 0.0f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus);

    public static SoundEvent PauseOpen() => Event(
        seed: 81, duration: 0.17f, shape: SynthShape.Sine, noiseMix: 0.04f,
        startFrequency: 440.0f, endFrequency: 660.0f, decay: 2.4f,
        volumeDb: -11.0f, pitchJitter: 0.0f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus);

    public static SoundEvent PauseClose() => Event(
        seed: 82, duration: 0.17f, shape: SynthShape.Sine, noiseMix: 0.04f,
        startFrequency: 660.0f, endFrequency: 440.0f, decay: 2.4f,
        volumeDb: -11.0f, pitchJitter: 0.0f,
        unitSize: 1.0f, maxDistance: 1.0f, bus: AudioDirector.UiBus);

    // ---------------- Builder ----------------

    /// <summary>
    /// One long signature so every entry above reads as a row in a table rather
    /// than as eight lines of object initialiser. Every parameter is named at
    /// the call site, which is what keeps that readable.
    /// </summary>
    private static SoundEvent Event(
        int seed,
        float duration,
        SynthShape shape = SynthShape.Noise,
        float noiseMix = 1.0f,
        float startFrequency = 220.0f,
        float endFrequency = 220.0f,
        float lowPassStart = 6000.0f,
        float lowPassEnd = 6000.0f,
        float highPass = 0.0f,
        float decay = 3.0f,
        float attack = 0.002f,
        bool sustain = false,
        float body = 0.0f,
        float bodyStart = 90.0f,
        float bodyEnd = 40.0f,
        float bodyDecay = 2.0f,
        float crackleRate = 0.0f,
        float crackleAmount = 0.5f,
        float vibratoRate = 0.0f,
        float vibratoDepth = 0.0f,
        float drive = 1.0f,
        bool looping = false,
        float crossfade = 0.08f,
        float volumeDb = 0.0f,
        float pitchJitter = 0.06f,
        float unitSize = 10.0f,
        float maxDistance = 140.0f,
        string bus = AudioDirector.WorldBus,
        float minRetrigger = 0.0f,
        int maxVoices = 8)
    {
        return new SoundEvent
        {
            Synth = new SynthRecipe
            {
                Seed = seed,
                Duration = duration,
                Shape = shape,
                NoiseMix = noiseMix,
                StartFrequency = startFrequency,
                EndFrequency = endFrequency,
                LowPassStart = lowPassStart,
                LowPassEnd = lowPassEnd,
                HighPass = highPass,
                DecayExponent = decay,
                Attack = attack,
                Sustain = sustain,
                BodyAmount = body,
                BodyStartFrequency = bodyStart,
                BodyEndFrequency = bodyEnd,
                BodyDecayExponent = bodyDecay,
                CrackleRate = crackleRate,
                CrackleAmount = crackleAmount,
                VibratoRate = vibratoRate,
                VibratoDepth = vibratoDepth,
                Drive = drive,
                Looping = looping,
                LoopCrossfade = crossfade,
            },
            VolumeDb = volumeDb,
            PitchJitter = pitchJitter,
            UnitSize = unitSize,
            MaxDistance = maxDistance,
            Bus = bus,
            MinRetriggerSeconds = minRetrigger,
            MaxVoices = maxVoices,
        };
    }
}
