using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Voxel;
using Godot;

namespace DillControl.Vehicles;

/// <summary>
/// A four-legged walker, moved and animated entirely by code. No clips: the
/// body is a CharacterBody3D sliding on a box, and the legs find their own
/// way to keep up with it.
///
/// - Each foot stays planted until the body has moved or turned far enough
///   to leave it behind, then steps to where the body will be a moment later.
///   Because steps follow the body rather than playing a cycle, walking in any
///   direction, turning on the spot and stopping all come out of one rule.
/// - Diagonal pairs step together, and a pair waits for the other to land:
///   a trot.
/// - Each leg is posed by two-bone IK from hip to sole (<see cref="MechLeg"/>).
/// - The body rides a spring: it sinks at speed for reach, dips with each
///   step, leans against acceleration, loads before a jump and squashes on
///   landing.
///
/// What it does is not decided here. Something else sets <see cref="MoveIntent"/>,
/// <see cref="FaceDirection"/>, <see cref="TurretAim"/>, <see cref="Sprint"/>
/// and the jump each frame: <see cref="MechCompanion"/> for the player's ally.
///
/// The model's front is +Z, as every imported model's is.
///
/// A tool script only for the asset lab (<see cref="LabMode"/>): in a scene
/// open in the editor it does nothing at all.
/// </summary>
[Tool]
public partial class MechWalker : CharacterBody3D, IDamageable, IBlastSurface, IHordeTarget
{
	/// <summary>
	/// Walked in the asset lab's view, where there is no physics (docs/decisions/0047):
	/// set before it enters the tree. The ground is the plane under it, nothing
	/// is stamped on, and it walks on a treadmill: the body stays where it is
	/// and planted feet slide back at its speed, as the belt under them does.
	/// The lab steps it with <see cref="LabStep"/>.
	/// </summary>
	public bool LabMode { get; set; }

	/// <summary>How far the treadmill's belt has carried what stands on it,
	/// world space, since it was made.</summary>
	public Vector3 BeltTravel { get; private set; }

	[ExportGroup("Data")]
	[Export]
	public MechTuning Tuning { get; set; }

	[ExportGroup("Scene Wiring")]
	/// <summary>The imported model's root. The body's bob and tilt move it;
	/// the parts below it are posed one by one.</summary>
	[Export]
	public Node3D Model { get; set; }

	[Export]
	public Node3D Turret { get; set; }

	/// <summary>What it is made of: hits break it (docs/decisions/0011). A
	/// <see cref="VoxelBody"/>; held as a plain node because in the editor,
	/// where the walker runs in the asset lab, its script is a placeholder.</summary>
	[Export]
	public Node Voxels { get; set; }

	/// <summary><see cref="Voxels"/> as what it is, or null in the editor.</summary>
	public VoxelBody VoxelParts => Voxels as VoxelBody;

	/// <summary>The part whose loss kills it: worn down below
	/// <see cref="MechTuning.BodyDeathFraction"/>, or emptied.</summary>
	[Export]
	public Node3D Core { get; set; }

	/// <summary>The other part whose loss kills it: emptied or cut free. A
	/// head, a tank's turret; none on the storage bot. Every other part can
	/// go and it fights on (docs/decisions/0046).</summary>
	[Export]
	public Node3D Head { get; set; }

	/// <summary>Front left, front right, back left, back right.</summary>
	[Export]
	public Godot.Collections.Array<Node3D> Thighs { get; set; } = new();

	[Export]
	public Godot.Collections.Array<Node3D> Shins { get; set; } = new();

	[Export]
	public Godot.Collections.Array<Node3D> Feet { get; set; } = new();

	/// <summary>Arms, if it has them: each swings against the leg on the
	/// other side. Right, left, matching the legs' order.</summary>
	[Export]
	public Godot.Collections.Array<Node3D> Arms { get; set; } = new();

	/// <summary>Markers that ride on a moving part (a muzzle on a turret or
	/// an arm, the seat on the turret). The importer puts every marker beside
	/// the parts, so each is moved under the part at the same index of
	/// <see cref="MountedOn"/> here, keeping where it is.</summary>
	[Export]
	public Godot.Collections.Array<Node3D> Mounted { get; set; } = new();

	[Export]
	public Godot.Collections.Array<Node3D> MountedOn { get; set; } = new();

	/// <summary>Where to walk, world space, length up to 1 for full speed.</summary>
	public Vector3 MoveIntent { get; set; }

	/// <summary>Which way the body should turn to face, world space; zero
	/// leaves it where it is.</summary>
	public Vector3 FaceDirection { get; set; }

	/// <summary>Which way the turret should point, world space; zero brings
	/// it back to the front.</summary>
	public Vector3 TurretAim { get; set; }

	/// <summary>Held jump: the jets, once in the air.</summary>
	public bool JumpHeld { get; set; }

	/// <summary>Held sprint: uses <see cref="MechTuning.SprintSpeed"/> when
	/// that is higher than <see cref="MechTuning.MaxSpeed"/>.</summary>
	public bool Sprint { get; set; }

	public bool IsGrounded { get; private set; } = true;

	public float JetFuel01 => Tuning == null || Tuning.JetFuelSeconds <= 0.0f ? 0.0f : _fuel / Tuning.JetFuelSeconds;

	public Vector3 Up { get; private set; } = Vector3.Up;

	/// <summary>Every walker on the player's side. Guns hold fire on targets
	/// near any of them: they break like everything else.</summary>
	public const string AllyGroup = "dill_allies";

	/// <summary>Raised once, as it falls apart.</summary>
	public event Action Destroyed;

	public bool IsDestroyed { get; private set; }

	public bool IsAlive => !IsDestroyed;

	/// <summary>Whether the turret still has anything on it.</summary>
	public bool TurretAlive => Turret != null && (VoxelParts?.PartOf(Turret) is not VoxelPart part || !part.IsEmpty);

	/// <summary>Whether a part (an arm, the turret) still exists.</summary>
	public bool PartAlive(Node3D node) => VoxelParts?.PartOf(node) is not VoxelPart part || !part.IsEmpty;

	/// <summary>Which way the turret points, world space.</summary>
	public Vector3 TurretForward => Turret != null ? Turret.GlobalBasis.Z : GlobalBasis.Z;

	/// <summary>A shove to the body's spring, world space: a gun's recoil
	/// rocks it back and down.</summary>
	public void Kick(Vector3 push)
	{
		Vector3 local = GlobalBasis.Inverse() * push;
		_tiltVelocity += new Vector2(local.Z, local.X) * 0.8f;
		_sinkVelocity -= push.Length() * 0.15f;
	}

	private readonly List<MechLeg> _legs = new();

	/// <summary>What it gets about on, from what is left of it (docs/decisions/0046).</summary>
	public enum Stance
	{
		/// <summary>Every leg: it walks.</summary>
		Walk,
		/// <summary>Some legs lost: one leg at a time, slower, sagging toward what is gone.</summary>
		Limp,
		/// <summary>One leg, an arm planted as a crutch if it has one.</summary>
		Hop,
		/// <summary>No legs: belly down, hauled along on its arms.</summary>
		Crawl,
		/// <summary>Nothing to move with: it turns on the spot and still shoots.</summary>
		Sit,
	}

	public Stance CurrentStance { get; private set; } = Stance.Walk;

	/// <summary>How far the body has come down from its height on its legs, metres.</summary>
	public float BodyDrop => _drop;

	// Whatever it steps with now: its legs left, and arms planted as crutches.
	private readonly List<MechStepper> _steppers = new();
	private readonly List<MechCrutch> _crutches = new();
	private int _groups = 2;

	// How far the body has come down to what holds it, on a spring.
	private float _drop;
	private float _dropVelocity;
	private float _dropTarget;
	private Vector2 _sag;
	private float _bellyHeight;
	private CollisionShape3D _hull;
	private BoxShape3D _hullShape;
	private Vector3 _hullSize;
	private float _hullBottom;

	private readonly List<Transform3D> _armRest = new();
	private readonly List<ArmPose> _arms = new();
	private Transform3D _modelRest;
	private Transform3D _turretRest;
	private float _turretYaw;

	private float _fuel;
	private float _windup = -1.0f;
	private bool _jumpQueued;
	private float _yawRate;
	private Vector3 _lastHorizontal;
	private Vector3 _acceleration;

	// The body's spring: a height offset and a tilt, each with a velocity.
	private float _sink;
	private float _sinkVelocity;
	private Vector2 _tilt;
	private Vector2 _tiltVelocity;

	private float _pivotHeight;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			SetPhysicsProcess(false);
			if (!LabMode)
			{
				return;
			}
		}
		if (Tuning == null || Model == null)
		{
			GD.PushError($"MechWalker '{Name}' needs Tuning and Model.");
			SetPhysicsProcess(false);
			return;
		}

		_modelRest = Model.Transform;
		if (Turret != null)
		{
			_turretRest = Turret.Transform;
		}
		for (int i = 0; i < Mathf.Min(Mounted.Count, MountedOn.Count); i++)
		{
			Mounted[i]?.Reparent(MountedOn[i], true);
		}
		foreach (Node3D arm in Arms)
		{
			_armRest.Add(arm.Transform);
			_arms.Add(new ArmPose { Pointing = RestPointing(arm), Reach = RestReach(arm) });
		}

		int count = Mathf.Min(Thighs.Count, Mathf.Min(Shins.Count, Feet.Count));
		for (int i = 0; i < count; i++)
		{
			// FL and BR step together, FR and BL together.
			// Two legs alternate: a walk.
			int group = i == 0 || i == 3 ? 0 : 1;
			_legs.Add(new MechLeg(Thighs[i], Shins[i], Feet[i], group, _modelRest, Tuning.KneeBias));
		}

		// The body tilts about its hips, not about the feet.
		_pivotHeight = _legs.Count > 0 ? (_modelRest * Thighs[0].Transform.Origin).Y : 0.0f;
		_steppers.AddRange(_legs);

		HookVoxels();
		AddToGroup(AllyGroup);
		AddToGroup(IHordeTarget.GroupName);
		MeasureHull();
		_bellyHeight = BellyHeight();
		_fuel = Tuning.JetFuelSeconds;
		CollisionLayer = GameLayers.Mask(GameLayers.Prop);
		// The player too, so a step that does come too close is blocked
		// rather than walking through them. Stowed on board, they collide
		// with nothing.
		CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Player);
		FloorSnapLength = 0.4f;
		// Never carried by what it stands on: the pieces of its own broken
		// leg spawn under it moving fast, and stepping off one added its
		// speed (200 m/s, in the asset lab). Only the world is a floor that
		// moves it.
		PlatformFloorLayers = GameLayers.Mask(GameLayers.World, GameLayers.Structure);
		PlatformWallLayers = 0;
		PlatformOnLeave = PlatformOnLeaveEnum.DoNothing;

		// Planted where they stand; the first steps correct anything off.
		// The space cannot be queried outside a physics frame.
		Transform3D self = GlobalTransform;
		foreach (MechLeg leg in _legs)
		{
			leg.Planted = self * leg.RestFoot;
		}
	}

	// ---------------- The horde ----------------

	private float _hullRadius = 1.5f;
	private float _hullHeight = 3.0f;

	public bool IsTargetable => !IsDestroyed;

	public Vector3 TargetBase => GlobalPosition;

	public float TargetRadius => _hullRadius;

	public float TargetHeight => _hullHeight;

	/// <summary>What the horde aims at is its hull's box, measured once.</summary>
	private void MeasureHull()
	{
		foreach (Node child in GetChildren())
		{
			if (child is CollisionShape3D { Shape: BoxShape3D box } shape)
			{
				_hullRadius = Mathf.Max(box.Size.X, box.Size.Z) * 0.5f;
				_hullHeight = shape.Position.Y + box.Size.Y * 0.5f;
				// Its own copy: it shrinks as the body comes down, and the
				// scene's box is shared by every mech of the kind.
				_hull = shape;
				_hullShape = (BoxShape3D)box.Duplicate();
				shape.Shape = _hullShape;
				_hullSize = box.Size;
				_hullBottom = shape.Position.Y - box.Size.Y * 0.5f;
				return;
			}
		}
	}

	/// <summary>
	/// A pickle bit, struck or spat at it. The bite goes into the voxels like
	/// any other hit, along the line from the pickle's mouth to the body, so
	/// a crowd round its legs chews the legs.
	/// </summary>
	public void TakeHordeHit(Vector3 from, float damage, Vector3 impulse)
	{
		if (IsDestroyed || VoxelParts == null)
		{
			return;
		}
		Vector3 chest = GlobalPosition + Up * (_hullHeight * 0.5f);
		var info = new DamageInfo
		{
			Amount = damage * Tuning.HordeDamageScale,
			BaseAmount = damage,
			Position = from,
			Direction = (chest - from).Normalized(),
			Distance = 0.0f,
			IsSplash = false,
			// A crowd chewing is a rate, not single hits: banked, and not
			// stopped by armour that stops rifle rounds.
			IsSustained = true,
		};
		VoxelParts.ApplyDamage(info, this);
		Velocity += impulse * Tuning.HordeShove;
	}

	/// <summary>A foot came down: the pickles under it take a stamp, over
	/// the bus, so only the horde hears it.</summary>
	private void Stomp(Vector3 at)
	{
		if (LabMode || Tuning.StompDamage <= 0.0f || Tuning.StompRadius <= 0.0f)
		{
			return;
		}
		EventBus.EmitAreaBlast(new BlastInfo
		{
			Centre = at,
			InnerRadius = Tuning.StompRadius * 0.6f,
			Radius = Tuning.StompRadius,
			Damage = Tuning.StompDamage,
			MinMultiplier = 0.5f,
			Up = Up,
			Source = this,
			SourceRid = GetRid(),
			NoCrater = true,
		});
	}

	// ---------------- Damage ----------------

	private void HookVoxels()
	{
		if (VoxelParts == null)
		{
			return;
		}

		VoxelParts.Velocity = () => Velocity;
		VoxelParts.PartEmptied += OnPartEmptied;
		if (VoxelParts.PartOf(Core) is VoxelPart core)
		{
			core.Carved += (part, _) =>
			{
				if (part.Health01 < Tuning.BodyDeathFraction)
				{
					Destroy();
				}
			};
		}
	}

	public void ApplyDamage(DamageInfo info)
	{
		if (!IsDestroyed)
		{
			VoxelParts?.ApplyDamage(info, this);
		}
	}

	public Vector3 NearestBlastPoint(Vector3 from) => VoxelParts?.NearestBlastPoint(from) ?? GlobalPosition;

	/// <summary>
	/// A part has nothing left, or nothing holds it. The core or the head and
	/// the mech is destroyed; a leg segment takes the rest of its leg with it,
	/// and the mech walks on what it has.
	/// </summary>
	private void OnPartEmptied(Node3D node, VoxelPart part)
	{
		if (IsDestroyed)
		{
			return;
		}
		if (node == Core || (Head != null && node == Head))
		{
			Destroy();
			return;
		}

		foreach (MechLeg leg in _legs)
		{
			if (!leg.Alive || (node != leg.Thigh && node != leg.Shin && node != leg.Foot))
			{
				continue;
			}
			leg.Alive = false;
			leg.Stepping = false;
			foreach (Node3D segment in new[] { leg.Thigh, leg.Shin, leg.Foot })
			{
				VoxelParts.PartOf(segment)?.BreakAway(Vector3.Zero);
			}
		}
		UpdateStance();
	}

	// ---------------- What is left ----------------

	private float StanceSpeed()
	{
		int lost = 0;
		foreach (MechLeg leg in _legs)
		{
			lost += leg.Alive ? 0 : 1;
		}
		return CurrentStance switch
		{
			Stance.Walk => 1.0f,
			Stance.Limp => Mathf.Pow(Tuning.LimpSpeedPerLostLeg, lost),
			Stance.Hop => Tuning.HopSpeedScale,
			Stance.Crawl => Tuning.CrawlSpeedScale,
			_ => 0.0f,
		};
	}

	/// <summary>
	/// Works out what it gets about on from the legs and arms it has left:
	/// which of them step, in what order, how low the body comes down to
	/// them, and which way it sags. Called whenever a part goes.
	/// </summary>
	private void UpdateStance()
	{
		var legs = new List<MechLeg>();
		foreach (MechLeg leg in _legs)
		{
			if (leg.Alive)
			{
				legs.Add(leg);
			}
		}
		var arms = new List<int>();
		for (int i = 0; i < Arms.Count; i++)
		{
			if (PartAlive(Arms[i]))
			{
				arms.Add(i);
			}
		}

		_steppers.Clear();
		_crutches.Clear();
		_sag = Vector2.Zero;
		float sag = Mathf.DegToRad(Tuning.MissingLegSagDegrees);
		float lostSide = 0.0f;
		foreach (MechLeg leg in _legs)
		{
			if (!leg.Alive)
			{
				// A front leg lost tips the nose down (+pitch); a leg on +X
				// lost lowers that side (-roll).
				Vector3 corner = new Vector3(leg.RestFoot.X, 0.0f, leg.RestFoot.Z).Normalized();
				_sag += new Vector2(corner.Z, -corner.X) * sag;
				lostSide += corner.X;
			}
		}

		float belly = Mathf.Max(0.0f, _bellyHeight - Tuning.BellyClearance);
		if (legs.Count == _legs.Count)
		{
			CurrentStance = Stance.Walk;
			for (int i = 0; i < _legs.Count; i++)
			{
				// FL and BR step together, FR and BL; two legs alternate.
				_legs[i].Group = i == 0 || i == 3 ? 0 : 1;
			}
			_steppers.AddRange(_legs);
			_groups = _legs.Count > 1 ? 2 : 1;
			_dropTarget = 0.0f;
		}
		else if (legs.Count >= 2)
		{
			CurrentStance = Stance.Limp;
			for (int i = 0; i < legs.Count; i++)
			{
				legs[i].Group = i;
			}
			_steppers.AddRange(legs);
			_groups = legs.Count;
			_dropTarget = 0.0f;
		}
		else if (legs.Count == 1)
		{
			CurrentStance = Stance.Hop;
			legs[0].Group = 0;
			_steppers.Add(legs[0]);
			_groups = 1;
			_dropTarget = belly * 0.15f;
			// The arm on the side that lost its leg, else any.
			int arm = -1;
			foreach (int i in arms)
			{
				float side = (_modelRest * _armRest[i].Origin).X;
				if (arm < 0 || Mathf.Sign(side) == Mathf.Sign(lostSide))
				{
					arm = i;
				}
			}
			if (arm >= 0)
			{
				AddCrutch(arm, 1, false, belly, out float drop);
				_dropTarget = drop;
				_groups = 2;
			}
		}
		else if (arms.Count > 0)
		{
			CurrentStance = Stance.Crawl;
			_groups = arms.Count > 1 ? 2 : 1;
			_dropTarget = belly;
			for (int k = 0; k < arms.Count; k++)
			{
				AddCrutch(arms[k], k % 2, true, belly, out _);
			}
		}
		else
		{
			CurrentStance = Stance.Sit;
			_groups = 1;
			_dropTarget = belly;
		}

		Transform3D self = GlobalTransform;
		foreach (MechStepper stepper in _steppers)
		{
			stepper.Stepping = false;
			if (stepper is MechCrutch)
			{
				stepper.Planted = self * stepper.RestFoot;
			}
		}
	}

	/// <summary>
	/// An arm as a support: where at rest it plants, and how far the body
	/// must come down for it to reach the ground there. A crutch goes out to
	/// the side of its shoulder; a hauling arm reaches ahead of a body down
	/// on its belly.
	/// </summary>
	private void AddCrutch(int arm, int group, bool haul, float belly, out float drop)
	{
		Vector3 shoulder = _modelRest * _armRest[arm].Origin;
		float length = Mathf.Max(0.5f, _arms[arm].Reach);
		float side = shoulder.X < 0.0f ? -1.0f : 1.0f;
		Vector3 spot;
		if (haul)
		{
			drop = belly;
			float height = Mathf.Max(0.0f, shoulder.Y - drop);
			float ahead = Mathf.Sqrt(Mathf.Max(0.0f, length * length - height * height)) * 0.8f;
			spot = new Vector3(shoulder.X + side * length * Tuning.CrutchSpread * 0.5f, 0.0f, shoulder.Z + ahead);
		}
		else
		{
			spot = new Vector3(shoulder.X + side * length * Tuning.CrutchSpread, 0.0f, shoulder.Z + length * 0.2f);
			float reach = new Vector2(spot.X - shoulder.X, spot.Z - shoulder.Z).Length();
			float height = Mathf.Sqrt(Mathf.Max(0.0f, length * length - reach * reach));
			drop = Mathf.Clamp(shoulder.Y - height, 0.0f, belly);
		}
		var crutch = new MechCrutch(arm, spot, group);
		_crutches.Add(crutch);
		_steppers.Add(crutch);
	}

	/// <summary>How high its core's underside stands over the ground at rest,
	/// from the damage map: what it comes down to with nothing to stand on.</summary>
	private float BellyHeight()
	{
		if (Core != null && VoxDamageMap.TryRead(Core) is DamageBake bake)
		{
			int sole = Array.IndexOf(bake.ContactKinds, ContactKind.Sole);
			if (sole >= 0)
			{
				Vector3 bottom = bake.ContactCentre[sole] - new Vector3(0.0f, bake.ContactSize[sole].Y * 0.5f, 0.0f);
				return (_modelRest * Core.Transform * bottom).Y;
			}
		}
		return _pivotHeight;
	}

	// ---------------- Mending ----------------

	/// <summary>Limbs shot away whole, each by the part a new one is fitted
	/// at: a leg's thigh, an arm.</summary>
	public List<Node3D> LostLimbs()
	{
		var lost = new List<Node3D>();
		foreach (MechLeg leg in _legs)
		{
			if (!leg.Alive)
			{
				lost.Add(leg.Thigh);
			}
		}
		foreach (Node3D arm in Arms)
		{
			if (!PartAlive(arm))
			{
				lost.Add(arm);
			}
		}
		return lost;
	}

	/// <summary>Voxels gone from the parts it still has: what a repair
	/// regrows. Lost limbs are not counted: they are fitted new.</summary>
	public int MissingVoxels()
	{
		int missing = 0;
		foreach (VoxelPart part in VoxelParts?.Parts ?? Array.Empty<VoxelPart>())
		{
			if (!part.IsEmpty)
			{
				missing += part.Missing;
			}
		}
		return missing;
	}

	/// <summary>
	/// Regrows up to <paramref name="voxels"/> of what its parts have lost,
	/// the core first, then the rest, each outward from its hinge. Returns how
	/// many went back.
	/// </summary>
	public int Regrow(int voxels)
	{
		int done = 0;
		if (VoxelParts == null || IsDestroyed)
		{
			return 0;
		}
		var parts = new List<VoxelPart>(VoxelParts.Parts);
		VoxelPart core = VoxelParts.PartOf(Core);
		parts.Sort((a, b) => (a == core ? 0 : 1).CompareTo(b == core ? 0 : 1));
		foreach (VoxelPart part in parts)
		{
			if (done >= voxels)
			{
				break;
			}
			if (!part.IsEmpty)
			{
				done += part.Restore(voxels - done);
			}
		}
		return done;
	}

	/// <summary>
	/// Fits a new limb where one was lost (<see cref="LostLimbs"/>): every
	/// part of it put back whole at its hinge, and it walks on it again.
	/// Returns the voxels it took.
	/// </summary>
	public int Refit(Node3D limb)
	{
		if (VoxelParts == null || IsDestroyed || limb == null)
		{
			return 0;
		}
		int voxels = 0;
		foreach (MechLeg leg in _legs)
		{
			if (leg.Alive || leg.Thigh != limb)
			{
				continue;
			}
			foreach (Node3D segment in new[] { leg.Thigh, leg.Shin, leg.Foot })
			{
				voxels += VoxelParts.Restore(segment, int.MaxValue);
			}
			leg.Alive = true;
			leg.Planted = GlobalTransform * leg.RestFoot;
		}
		if (ArmIndex(limb) >= 0)
		{
			voxels += VoxelParts.Restore(limb, int.MaxValue);
		}
		UpdateStance();
		return voxels;
	}

	/// <summary>For <c>bench.gd</c> and the asset lab: breaks off the next
	/// limb, legs first, then arms. False when none is left.</summary>
	public bool BenchBreakNext()
	{
		foreach (MechLeg leg in _legs)
		{
			if (leg.Alive && VoxelParts?.PartOf(leg.Thigh) is VoxelPart part && !part.IsEmpty)
			{
				part.BreakAway(Vector3.Zero);
				return true;
			}
		}
		foreach (Node3D arm in Arms)
		{
			if (VoxelParts?.PartOf(arm) is VoxelPart part && !part.IsEmpty)
			{
				part.BreakAway(Vector3.Zero);
				return true;
			}
		}
		return false;
	}

	/// <summary>Falls apart: every part left becomes loose chunks, thrown a
	/// little up and out, and the walker is gone.</summary>
	public void Destroy()
	{
		if (IsDestroyed)
		{
			return;
		}
		IsDestroyed = true;
		Destroyed?.Invoke();
		VoxelParts?.BreakAll(Up * 3.0f);
		SetPhysicsProcess(false);
		CollisionLayer = 0;
		CollisionMask = 0;
		GetTree().CreateTimer(0.5).Timeout += QueueFree;
	}

	/// <summary>Jumps from the ground, after the wind-up. Ignored in the air.</summary>
	public void RequestJump()
	{
		_jumpQueued = true;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint())
		{
			return;
		}
		Step((float)delta);
	}

	/// <summary>One frame in the asset lab, <paramref name="dt"/> seconds long.</summary>
	public void LabStep(float dt)
	{
		if (LabMode && Tuning != null && Model != null && dt > 0.0f)
		{
			Step(dt);
		}
	}

	private void Step(float dt)
	{
		Vector3 gravity = LabMode
			? Vector3.Down * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle()
			: GetGravity() * Tuning.GravityScale;
		Up = gravity.LengthSquared() > 1e-6f ? -gravity.Normalized() : Vector3.Up;
		UpDirection = Up;

		bool wasGrounded = IsGrounded;
		float fallSpeed = -Velocity.Dot(Up);

		Move(dt, gravity);
		Turn(dt);

		IsGrounded = LabMode || IsOnFloor();
		if (IsGrounded && !wasGrounded)
		{
			Land(fallSpeed);
		}

		UpdateGait(dt);
		UpdateBody(dt);
		UpdateArms(dt);
		PoseLegs(dt);
		AimTurret(dt);
		ApplyMove(dt);
	}

	// ---------------- Moves ----------------

	/// <summary>A marker on the move playing was passed: 'hit', 'sfx:name', 'shake'.</summary>
	public event Action<string> MoveEvent;

	/// <summary>The move playing, or null.</summary>
	public MechMove PlayingMove { get; private set; }

	/// <summary>How far into the move playing it is, seconds.</summary>
	public float MoveTime => _moveTime;

	/// <summary>The arms the move playing swings, by index into <see cref="Arms"/>.</summary>
	public System.Collections.Generic.IEnumerable<int> MoveArms()
	{
		if (PlayingMove == null)
		{
			yield break;
		}
		for (int i = 0; i < Arms.Count; i++)
		{
			if (Array.IndexOf(_moveParts, Arms[i]) >= 0)
			{
				yield return i;
			}
		}
	}

	private Node3D[] _moveParts = Array.Empty<Node3D>();
	private float _moveTime;
	private float _moveBlend;
	private bool _moveUsesLegs;

	/// <summary>
	/// Plays a move posed in Blender (docs/decisions/0046) over whatever it is
	/// doing: the parts the move claims follow it, eased in and out over
	/// <paramref name="blend"/> seconds; every other part keeps walking. A
	/// claimed part shot away is skipped; a move that has lost most of its
	/// parts is not played. False when it was not.
	/// </summary>
	public bool PlayMove(MechMove move, float blend = 0.1f)
	{
		if (move == null || move.Frames <= 0 || IsDestroyed)
		{
			return false;
		}
		var parts = new Node3D[move.Parts.Length];
		int alive = 0;
		_moveUsesLegs = false;
		for (int i = 0; i < parts.Length; i++)
		{
			parts[i] = Model.GetNodeOrNull<Node3D>(move.Parts[i]);
			if (parts[i] != null && PartAlive(parts[i]))
			{
				alive++;
			}
			foreach (MechLeg leg in _legs)
			{
				_moveUsesLegs |= parts[i] == leg.Thigh || parts[i] == leg.Shin || parts[i] == leg.Foot;
			}
		}
		if (alive * 2 < parts.Length)
		{
			return false;
		}
		PlayingMove = move;
		_moveParts = parts;
		_moveTime = 0.0f;
		_moveBlend = Mathf.Max(blend, 0.0f);
		return true;
	}

	public void StopMove()
	{
		if (PlayingMove == null)
		{
			return;
		}
		PlayingMove = null;
		if (_moveUsesLegs)
		{
			// The feet stand where the move left them.
			foreach (MechLeg leg in _legs)
			{
				if (leg.Alive)
				{
					leg.Planted = Ground(leg.Foot.GlobalPosition);
					leg.Stepping = false;
				}
			}
		}
	}

	private void ApplyMove(float dt)
	{
		if (PlayingMove == null)
		{
			return;
		}
		float before = _moveTime;
		_moveTime += dt;
		for (int e = 0; e < PlayingMove.EventNames.Length && e < PlayingMove.EventTimes.Length; e++)
		{
			float at = PlayingMove.EventTimes[e];
			if (at >= before && at < _moveTime || (before == 0.0f && at <= 0.0f))
			{
				MoveEvent?.Invoke(PlayingMove.EventNames[e]);
			}
		}
		float length = PlayingMove.Length;
		float weight = 1.0f;
		if (_moveBlend > 0.0f)
		{
			weight = Mathf.Clamp(Mathf.Min(_moveTime, length - _moveTime) / _moveBlend, 0.0f, 1.0f);
		}
		for (int i = 0; i < _moveParts.Length; i++)
		{
			Node3D part = _moveParts[i];
			if (part == null || !PartAlive(part))
			{
				continue;
			}
			Transform3D posed = PlayingMove.Sample(i, Mathf.Min(_moveTime, length));
			part.Transform = weight >= 0.999f ? posed : part.Transform.InterpolateWith(posed, weight);
		}
		if (_moveTime >= length)
		{
			StopMove();
		}
	}

	// ---------------- Body motion ----------------

	private void Move(float dt, Vector3 gravity)
	{
		Vector3 velocity = Velocity;
		float vertical = velocity.Dot(Up);
		Vector3 horizontal = velocity - Up * vertical;

		Vector3 intent = MoveIntent.Slide(Up).LimitLength(1.0f);
		float top = Tuning.MaxSpeed;
		if (Sprint && Tuning.SprintSpeed > Tuning.MaxSpeed)
		{
			top = Tuning.SprintSpeed;
		}
		top *= StanceSpeed();
		Vector3 target = intent * top;
		if (_windup >= 0.0f)
		{
			// Planted while it loads.
			target = Vector3.Zero;
		}

		float rate = target.Length() > horizontal.Length() ? Tuning.Acceleration : Tuning.Deceleration;
		if (!IsGrounded)
		{
			rate *= Tuning.AirControl;
		}
		horizontal = horizontal.MoveToward(target, rate * dt);

		if (_jumpQueued && IsGrounded && _windup < 0.0f)
		{
			_windup = Tuning.JumpWindupSeconds;
		}
		_jumpQueued = false;

		if (_windup >= 0.0f)
		{
			_windup -= dt;
			if (_windup < 0.0f)
			{
				vertical = Tuning.JumpSpeed;
				IsGrounded = false;
				foreach (MechStepper leg in _steppers)
				{
					leg.Stepping = false;
				}
			}
		}

		if (!IsOnFloor() || vertical > 0.0f)
		{
			vertical -= gravity.Length() * dt;
			if (JumpHeld && _fuel > 0.0f && !IsOnFloor())
			{
				vertical += gravity.Length() * Tuning.JetThrustOfGravity * dt;
				_fuel -= dt;
			}
		}
		if (IsOnFloor())
		{
			_fuel = Mathf.Min(Tuning.JetFuelSeconds, _fuel + Tuning.JetRefillPerSecond * dt);
		}

		_acceleration = _acceleration.Lerp((horizontal - _lastHorizontal) / Mathf.Max(dt, 1e-4f), 1.0f - Mathf.Exp(-10.0f * dt));
		_lastHorizontal = horizontal;

		if (LabMode)
		{
			// On the treadmill: no jump, and the world under it goes back
			// instead of the body going on.
			Velocity = horizontal;
			Vector3 belt = -horizontal * dt;
			BeltTravel += belt;
			foreach (MechStepper stepper in _steppers)
			{
				stepper.Planted += belt;
				stepper.StepFrom += belt;
			}
			return;
		}

		Velocity = horizontal + Up * vertical;
		MoveAndSlide();
		WadeThroughDebris();
	}

	/// <summary>Loose pieces of voxels do not stop it: one it runs into is
	/// no longer collided with, so a mech wades through rubble and through the
	/// pieces of its own broken legs, which lay round a body come down on its
	/// belly and boxed it in.</summary>
	private void WadeThroughDebris()
	{
		Blockers = "";
		for (int i = 0; i < GetSlideCollisionCount(); i++)
		{
			GodotObject collider = GetSlideCollision(i).GetCollider();
			if (collider is VoxelChunk chunk)
			{
				AddCollisionExceptionWith(chunk);
			}
			Blockers += $"{(collider as Node)?.Name ?? collider?.GetClass()} ";
		}
	}

	/// <summary>What it slid against this tick, for the asset lab.</summary>
	public string Blockers { get; private set; } = "";

	private void Turn(float dt)
	{
		_yawRate = 0.0f;
		Vector3 face = FaceDirection.Slide(Up);
		if (face.LengthSquared() < 1e-4f)
		{
			return;
		}

		Vector3 forward = GlobalBasis.Z.Slide(Up).Normalized();
		float angle = forward.SignedAngleTo(face.Normalized(), Up);
		float step = Mathf.Clamp(angle, -Mathf.DegToRad(Tuning.TurnRateDegrees) * dt, Mathf.DegToRad(Tuning.TurnRateDegrees) * dt);
		GlobalRotate(Up, step);
		_yawRate = step / Mathf.Max(dt, 1e-4f);
	}

	private void Land(float fallSpeed)
	{
		_sinkVelocity -= Mathf.Max(0.0f, fallSpeed) * Tuning.LandingKick;
		Transform3D self = GlobalTransform;
		foreach (MechStepper leg in _steppers)
		{
			leg.Stepping = false;
			leg.Planted = Ground(self * leg.RestFoot);
		}
	}

	// ---------------- Gait ----------------

	private void UpdateGait(float dt)
	{
		Transform3D self = GlobalTransform;
		Vector3 horizontal = Velocity.Slide(Up);

		if (!IsGrounded)
		{
			// Legs hang toward rest in the air, a little extended.
			float follow = 1.0f - Mathf.Exp(-12.0f * dt);
			foreach (MechStepper leg in _steppers)
			{
				if (!leg.Alive)
				{
					continue;
				}
				leg.Planted = leg.Planted.Lerp(self * leg.RestFoot - Up * 0.25f, follow);
			}
			return;
		}

		GaitSpeeds(horizontal.Length(), out float speed01, out float sprint01);
		float stepSeconds = Mathf.Lerp(Tuning.StepSeconds, Tuning.StepSecondsFast, speed01);
		if (sprint01 > 0.0f)
		{
			stepSeconds = Mathf.Lerp(Tuning.StepSecondsFast, Tuning.SprintStepSeconds, sprint01);
		}
		if (_crutches.Count > 0)
		{
			// An arm hauls slower than a leg strides.
			stepSeconds = Mathf.Max(stepSeconds, Tuning.CrutchStepSeconds);
		}
		float lead = Tuning.StepLeadSeconds + stepSeconds * 0.5f;
		bool moving = horizontal.Length() > 0.2f || Mathf.Abs(_yawRate) > 0.3f;
		float threshold = StrideThreshold(speed01, sprint01, moving);
		lead = CapLead(lead, horizontal.Length(), speed01, sprint01);

		// Where each foot would like to land: its rest spot carried forward by
		// the body's motion over the lead time, turn included.
		foreach (MechStepper leg in _steppers)
		{
			if (!leg.Alive || !leg.Stepping)
			{
				continue;
			}

			leg.StepTo = Ground(Predict(self, leg.RestFoot, horizontal, lead - leg.StepT * stepSeconds));
			leg.StepT += dt / stepSeconds;
			if (leg.StepT >= 1.0f)
			{
				leg.Stepping = false;
				leg.Planted = leg.StepTo;
				Stomp(leg.Planted);
			}
		}

		bool anyStepping = false;
		foreach (MechStepper leg in _steppers)
		{
			anyStepping |= leg.Stepping;
		}
		if (anyStepping)
		{
			return;
		}

		// Neither pair in the air: the pair that has fallen furthest behind goes.
		float[] worst = new float[Mathf.Max(1, _groups)];
		foreach (MechStepper leg in _steppers)
		{
			if (!leg.Alive)
			{
				continue;
			}
			float error = (leg.Planted - self * leg.RestFoot).Slide(Up).Length();
			worst[leg.Group] = Mathf.Max(worst[leg.Group], error);
		}
		int group = 0;
		for (int g = 1; g < worst.Length; g++)
		{
			if (worst[g] > worst[group])
			{
				group = g;
			}
		}
		if (worst[group] < threshold)
		{
			return;
		}

		foreach (MechStepper leg in _steppers)
		{
			if (leg.Group != group || !leg.Alive)
			{
				continue;
			}
			leg.Stepping = true;
			leg.StepT = 0.0f;
			leg.StepFrom = leg.Planted;
			leg.StepTo = Ground(Predict(self, leg.RestFoot, horizontal, lead));
		}
	}

	/// <summary>How far a foot may lag before it steps. Creeps and turns use
	/// the short stride; a full run uses the long one; a sprint uses a longer
	/// one still. Mechs that leave the slow and sprint strides at 0 keep one
	/// stride at every speed.</summary>
	private float StrideThreshold(float speed01, float sprint01, bool moving)
	{
		if (!moving)
		{
			return Tuning.IdleTidyThreshold;
		}

		float stride = Tuning.StepThreshold;
		if (Tuning.StepThresholdSlow > 0.0f)
		{
			stride = Mathf.Lerp(Tuning.StepThresholdSlow, Tuning.StepThreshold, speed01);
		}
		if (sprint01 > 0.0f && Tuning.SprintStepThreshold > Tuning.StepThreshold)
		{
			stride = Mathf.Lerp(Tuning.StepThreshold, Tuning.SprintStepThreshold, sprint01);
		}
		return stride;
	}

	/// <summary>Stops a fast body aiming the foot further than the leg can
	/// reach, and keeps a creep or a turn from aiming at the running plant.</summary>
	private float CapLead(float lead, float speed, float speed01, float sprint01)
	{
		if (Tuning.StepReach <= 0.0f)
		{
			return lead;
		}

		float slow = Tuning.StepThresholdSlow > 0.0f ? Tuning.StepThresholdSlow : Tuning.StepReach * 0.35f;
		float reach = Mathf.Lerp(slow, Tuning.StepReach, speed01);
		if (sprint01 > 0.0f && Tuning.SprintStepReach > Tuning.StepReach)
		{
			reach = Mathf.Lerp(Tuning.StepReach, Tuning.SprintStepReach, sprint01);
		}

		// Almost still: predict only the short lead, so a turn plants close.
		if (speed < 0.5f)
		{
			return Mathf.Min(lead, Mathf.Max(Tuning.StepLeadSeconds, 0.08f));
		}
		return Mathf.Min(lead, reach / speed);
	}

	/// <summary>A rest foot spot, moved on by the body's velocity and turn
	/// over <paramref name="seconds"/>.</summary>
	private Vector3 Predict(Transform3D self, Vector3 restFoot, Vector3 horizontal, float seconds)
	{
		seconds = Mathf.Max(seconds, 0.0f);
		Vector3 offset = self.Basis * restFoot;
		offset = offset.Rotated(Up, _yawRate * seconds);
		return self.Origin + offset + horizontal * seconds;
	}

	/// <summary>The ground under a point, or the point on the walker's own
	/// ground plane when there is nothing to hit.</summary>
	private Vector3 Ground(Vector3 point)
	{
		if (LabMode)
		{
			return point - Up * (point - GlobalPosition).Dot(Up);
		}
		PhysicsDirectSpaceState3D space = GetWorld3D().DirectSpaceState;
		var query = PhysicsRayQueryParameters3D.Create(point + Up * 2.5f, point - Up * 3.0f,
			GameLayers.Mask(GameLayers.World, GameLayers.Prop), new Godot.Collections.Array<Rid> { GetRid() });
		Godot.Collections.Dictionary hit = space.IntersectRay(query);
		if (hit.Count > 0)
		{
			return (Vector3)hit["position"];
		}
		return point - Up * (point - GlobalPosition).Dot(Up);
	}

	// ---------------- Body pose ----------------

	private void UpdateBody(float dt)
	{
		Vector3 horizontal = Velocity.Slide(Up);
		GaitSpeeds(horizontal.Length(), out float speed01, out float sprint01);

		float target = -Tuning.WalkCrouch * speed01;
		if (sprint01 > 0.0f)
		{
			target = -Mathf.Lerp(Tuning.WalkCrouch, Tuning.SprintCrouch, sprint01);
		}
		foreach (MechLeg leg in _legs)
		{
			if (leg.Stepping)
			{
				target -= Tuning.StepBob * Mathf.Sin(Mathf.Pi * leg.StepT);
			}
		}
		if (_windup >= 0.0f && Tuning.JumpWindupSeconds > 0.0f)
		{
			target -= Tuning.JumpCrouch * (1.0f - _windup / Tuning.JumpWindupSeconds);
		}

		// Lean back against acceleration and out of a turn, in the body's own
		// frame: x is pitch, y is roll.
		Basis self = GlobalBasis;
		float forwardAccel = _acceleration.Dot(self.Z);
		float sideAccel = _acceleration.Dot(self.X);
		float maxLean = Mathf.DegToRad(Tuning.MaxLeanDegrees);
		var tiltTarget = new Vector2(
			Mathf.Clamp(-forwardAccel * Mathf.DegToRad(Tuning.LeanPerAcceleration), -maxLean, maxLean),
			Mathf.Clamp(sideAccel * Mathf.DegToRad(Tuning.LeanPerAcceleration), -maxLean, maxLean));
		tiltTarget.X += Mathf.DegToRad(Tuning.SprintLeanDegrees) * sprint01;
		tiltTarget += _sag;

		float k = Tuning.BodySpring;
		float c = 2.0f * Tuning.BodyDamping * Mathf.Sqrt(k);
		_sinkVelocity += ((target - _sink) * k - _sinkVelocity * c) * dt;
		_sink += _sinkVelocity * dt;
		_tiltVelocity += ((tiltTarget - _tilt) * k - _tiltVelocity * c) * dt;
		_tilt += _tiltVelocity * dt;
		_dropVelocity += ((_dropTarget - _drop) * k - _dropVelocity * c) * dt;
		_drop += _dropVelocity * dt;
		ShrinkHull();

		var pivot = new Vector3(0.0f, _pivotHeight, 0.0f);
		Basis tilt = new Basis(Vector3.Right, _tilt.X) * new Basis(Vector3.Back, _tilt.Y);
		Transform3D sway = new Transform3D(Basis.Identity, pivot + Vector3.Up * (_sink - _drop))
						   * new Transform3D(tilt, Vector3.Zero)
						   * new Transform3D(Basis.Identity, -pivot);
		Model.Transform = sway * _modelRest;
	}

	/// <summary>The hull comes down with the body, its bottom where it was,
	/// so what is left of the mech is what collides. Changed in steps, not
	/// every frame: Jolt rebuilds a shape that changes.</summary>
	private void ShrinkHull()
	{
		if (_hullShape == null)
		{
			return;
		}
		float height = Mathf.Max(0.8f, _hullSize.Y - Mathf.Max(0.0f, _drop));
		if (Mathf.Abs(height - _hullShape.Size.Y) < 0.05f)
		{
			return;
		}
		_hullShape.Size = new Vector3(_hullSize.X, height, _hullSize.Z);
		_hull.Position = new Vector3(_hull.Position.X, _hullBottom + height * 0.5f, _hull.Position.Z);
		_hullHeight = _hullBottom + height;
	}

	private void PoseLegs(float dt)
	{
		Transform3D model = Model.GlobalTransform;
		Basis level = GlobalBasis * _modelRest.Basis;
		GaitSpeeds(Velocity.Slide(Up).Length(), out _, out float sprint01);
		float stepHeight = Tuning.StepHeight;
		float liftReference = Tuning.StepThreshold;
		float minLift = 0.3f;
		if (Tuning.StepReach > 0.0f)
		{
			liftReference = Tuning.StepThreshold + Tuning.StepReach;
			minLift = 0.22f;
			if (sprint01 > 0.0f && Tuning.SprintStepHeight > stepHeight)
			{
				stepHeight = Mathf.Lerp(Tuning.StepHeight, Tuning.SprintStepHeight, sprint01);
				if (Tuning.SprintStepReach > 0.0f)
				{
					float sprintLag = Tuning.SprintStepThreshold > 0.0f ? Tuning.SprintStepThreshold : Tuning.StepThreshold;
					float sprintReference = sprintLag + Tuning.SprintStepReach;
					liftReference = Mathf.Lerp(liftReference, sprintReference, sprint01);
				}
			}
		}
		foreach (MechLeg leg in _legs)
		{
			if (!leg.Alive)
			{
				continue;
			}
			Vector3 sole = leg.Planted;
			float toe = 0.0f;
			if (leg.Stepping)
			{
				float t = Mathf.Clamp(leg.StepT, 0.0f, 1.0f);
				float eased = t * t * (3.0f - 2.0f * t);
				float arc = Mathf.Sin(Mathf.Pi * t);
				// A short correction lifts a little. A full stride lifts the lot.
				float distance = leg.StepFrom.DistanceTo(leg.StepTo);
				float lift = stepHeight * Mathf.Clamp(distance / Mathf.Max(liftReference, 0.01f), minLift, 1.0f);
				sole = leg.StepFrom.Lerp(leg.StepTo, eased) + Up * (arc * lift);
				toe = arc * 0.35f;
			}
			leg.Pose(model, level, Up, sole, toe);
		}

		PoseArms(dt);
	}

	// ---------------- Arms ----------------

	/// <summary>Raises <paramref name="arm"/> toward a world point and holds
	/// it there. A node that is not one of this walker's arms is ignored, so
	/// a gun on a turret can call it without a branch.</summary>
	public void AimArm(Node3D arm, Vector3 worldTarget)
	{
		int i = ArmIndex(arm);
		if (i < 0)
		{
			return;
		}
		_arms[i].Aim = worldTarget;
		_arms[i].Hold = Tuning.ArmAimHoldSeconds;
	}

	/// <summary>Whether a gun on this node may fire: the arm is raised far
	/// enough, or the node is not an arm (a turret mount is always ready).</summary>
	public bool ArmReady(Node3D node)
	{
		int i = ArmIndex(node);
		return i < 0 || (CrutchOf(i) == null && _arms[i].Raise >= Tuning.ArmReadyFraction);
	}

	/// <summary>This frame's swing override for one arm. <paramref name="pitch"/>
	/// and <paramref name="yaw"/> are radians in the model's space: positive
	/// pitch lifts the fist backward, positive yaw carries it to the model's
	/// left. Wins over aim while <paramref name="weight"/> is above zero.</summary>
	public void StrokeArm(int arm, float pitch, float yaw, float weight)
	{
		if ((uint)arm >= (uint)_arms.Count)
		{
			return;
		}
		_arms[arm].StrokePitch = pitch;
		_arms[arm].StrokeYaw = yaw;
		_arms[arm].StrokeWeight = Mathf.Clamp(weight, 0.0f, 1.0f);
	}

	/// <summary>Where this arm's fist is with the current swing applied, world
	/// space. The shoulder when the arm index is out of range.</summary>
	public Vector3 StrokeTip(int arm)
	{
		if ((uint)arm >= (uint)_armRest.Count || Model == null)
		{
			return GlobalPosition;
		}
		// Where the arm is, however it was posed: a swing, a move, a crutch.
		ArmPose pose = _arms[arm];
		return Arms[arm].GlobalTransform * (pose.Pointing * pose.Reach);
	}

	/// <summary>Whether this arm is mid-slash, so a gun on it should hold.</summary>
	public bool ArmStroking(Node3D arm)
	{
		int i = ArmIndex(arm);
		return i >= 0 && (_arms[i].StrokeWeight > 0.02f || (PlayingMove != null && Array.IndexOf(_moveParts, arm) >= 0));
	}

	/// <summary>The crutch an arm is planted as, or null.</summary>
	private MechCrutch CrutchOf(int arm)
	{
		foreach (MechCrutch crutch in _crutches)
		{
			if (crutch.Arm == arm)
			{
				return crutch;
			}
		}
		return null;
	}

	private int ArmIndex(Node3D node)
	{
		if (node == null)
		{
			return -1;
		}
		for (int i = 0; i < Arms.Count; i++)
		{
			if (Arms[i] == node)
			{
				return i;
			}
		}
		return -1;
	}

	/// <summary>Where this arm points at rest: toward its mounted marker, or
	/// down its own -Y when it has none.</summary>
	private Vector3 RestPointing(Node3D arm)
	{
		int mounted = Mathf.Min(Mounted.Count, MountedOn.Count);
		for (int i = 0; i < mounted; i++)
		{
			if (MountedOn[i] != arm || Mounted[i] == null)
			{
				continue;
			}
			Vector3 local = Mounted[i].Position;
			if (local.LengthSquared() > 1e-6f)
			{
				return local.Normalized();
			}
		}
		return Vector3.Down;
	}

	/// <summary>How far the fist marker sits from the shoulder. The hanging
	/// length of the club, used to place a swing's hits.</summary>
	private float RestReach(Node3D arm)
	{
		int mounted = Mathf.Min(Mounted.Count, MountedOn.Count);
		for (int i = 0; i < mounted; i++)
		{
			if (MountedOn[i] != arm || Mounted[i] == null)
			{
				continue;
			}
			float reach = Mounted[i].Position.Length();
			if (reach > 0.05f)
			{
				return reach;
			}
		}
		return 3.4f;
	}

	private void GaitSpeeds(float speed, out float speed01, out float sprint01)
	{
		float walk = Mathf.Max(Tuning.MaxSpeed, 0.01f);
		speed01 = Mathf.Clamp(speed / walk, 0.0f, 1.0f);
		sprint01 = 0.0f;
		if (Tuning.SprintSpeed > Tuning.MaxSpeed)
		{
			sprint01 = Mathf.Clamp((speed - Tuning.MaxSpeed) / (Tuning.SprintSpeed - Tuning.MaxSpeed), 0.0f, 1.0f);
		}
	}

	private void UpdateArms(float dt)
	{
		float raiseRate = 1.0f / Mathf.Max(Tuning.ArmRaiseSeconds, 1e-3f);
		for (int i = 0; i < _arms.Count; i++)
		{
			ArmPose pose = _arms[i];
			if (pose.Hold > 0.0f)
			{
				pose.Hold -= dt;
				pose.Raise = Mathf.MoveToward(pose.Raise, 1.0f, raiseRate * dt);
			}
			else
			{
				pose.Raise = Mathf.MoveToward(pose.Raise, 0.0f, raiseRate * dt);
			}
		}
	}

	/// <summary>Each arm starts as a gait swing against the opposite leg,
	/// then blends toward its aim, then toward a slash stroke. Stroke wins.</summary>
	private void PoseArms(float dt)
	{
		if (_armRest.Count == 0)
		{
			return;
		}

		Transform3D self = GlobalTransform;
		Vector3 forward = self.Basis.Z.Normalized();
		float max = Mathf.DegToRad(Tuning.MaxArmSwingDegrees);
		float follow = Tuning.ArmSwingFollow > 1e-4f
			? 1.0f - Mathf.Exp(-dt / Tuning.ArmSwingFollow)
			: 1.0f;
		for (int i = 0; i < _armRest.Count; i++)
		{
			Transform3D rest = _armRest[i];
			float angle = 0.0f;
			if (_legs.Count > 0)
			{
				MechLeg opposite = _legs[_legs.Count - 1 - Mathf.Min(i, _legs.Count - 1)];
				float ahead = (opposite.Sole - self * opposite.RestFoot).Dot(forward);
				angle = Mathf.Clamp(-ahead * Mathf.DegToRad(Tuning.ArmSwingDegreesPerMetre), -max, max);
			}
			ArmPose pose = _arms[i];
			pose.Swing = Mathf.Lerp(pose.Swing, angle, follow);
			angle = pose.Swing;
			Transform3D posed = new Transform3D(new Basis(Vector3.Right, angle) * rest.Basis, rest.Origin);

			if (pose.Raise > 1e-4f)
			{
				float t = pose.Raise * pose.Raise * (3.0f - 2.0f * pose.Raise);
				posed = posed.InterpolateWith(AimPose(i, rest), t);
			}
			if (CrutchOf(i) is MechCrutch crutch)
			{
				// Planted, it points at its spot; stepping, at the arc to the next.
				Vector3 sole = crutch.Planted;
				if (crutch.Stepping)
				{
					float t = Mathf.Clamp(crutch.StepT, 0.0f, 1.0f);
					sole = crutch.StepFrom.Lerp(crutch.StepTo, t * t * (3.0f - 2.0f * t)) + Up * (Mathf.Sin(Mathf.Pi * t) * Tuning.StepHeight);
				}
				crutch.Sole = sole;
				Arms[i].Transform = PointPose(i, rest, sole - Model.GlobalTransform * rest.Origin);
				continue;
			}
			if (pose.StrokeWeight > 1e-4f)
			{
				// Pitch then yaw, both in the model's space, so the fist
				// travels the arc instead of twisting toward a look target.
				Basis swing = new Basis(Vector3.Right, pose.StrokePitch) * new Basis(Vector3.Up, pose.StrokeYaw);
				var swung = new Transform3D(swing * rest.Basis, rest.Origin);
				posed = pose.StrokeWeight >= 0.999f ? swung : posed.InterpolateWith(swung, pose.StrokeWeight);
			}
			Arms[i].Transform = posed;
		}
	}

	private Transform3D AimPose(int i, Transform3D rest)
	{
		Vector3 shoulder = Model.GlobalTransform * rest.Origin;
		Vector3 desired = _arms[i].Aim - shoulder;
		if (desired.LengthSquared() < 1e-6f)
		{
			return rest;
		}
		Vector3 local = (Model.GlobalBasis.Inverse() * desired).Normalized();
		float maxYaw = Mathf.DegToRad(Tuning.ArmAimMaxYawDegrees);
		float yaw = Mathf.Clamp(Mathf.Atan2(local.X, local.Z), -maxYaw, maxYaw);
		float horiz = Mathf.Sqrt(local.X * local.X + local.Z * local.Z);
		float pitch = Mathf.Atan2(local.Y, horiz);
		float cp = Mathf.Cos(pitch);
		Vector3 clamped = new Vector3(Mathf.Sin(yaw) * cp, Mathf.Sin(pitch), Mathf.Cos(yaw) * cp);
		return PointPose(i, rest, Model.GlobalBasis * clamped);
	}

	/// <summary>Rest pose rotated so the arm's rest pointing direction now
	/// faces <paramref name="worldDirection"/>.</summary>
	private Transform3D PointPose(int i, Transform3D rest, Vector3 worldDirection)
	{
		if (worldDirection.LengthSquared() < 1e-6f)
		{
			return rest;
		}
		Vector3 from = (rest.Basis * _arms[i].Pointing).Normalized();
		Vector3 to = (Model.GlobalBasis.Inverse() * worldDirection.Normalized()).Normalized();
		if (from.LengthSquared() < 1e-6f || to.LengthSquared() < 1e-6f)
		{
			return rest;
		}
		var turn = new Quaternion(from, to);
		return new Transform3D(new Basis(turn) * rest.Basis, rest.Origin);
	}

	private sealed class ArmPose
	{
		public Vector3 Pointing;
		public Vector3 Aim;
		public float Hold;
		public float Raise;
		public float StrokePitch;
		public float StrokeYaw;
		public float StrokeWeight;
		public float Reach = 3.4f;

		/// <summary>Shoulder angle the gait has eased to, radians. Trails the
		/// foot when <see cref="MechTuning.ArmSwingFollow"/> is set.</summary>
		public float Swing;
	}

	// ---------------- Turret ----------------

	private void AimTurret(float dt)
	{
		if (Turret == null)
		{
			return;
		}

		float target = 0.0f;
		if (TurretAim.LengthSquared() > 1e-4f)
		{
			Vector3 local = Model.GlobalBasis.Inverse() * TurretAim;
			target = Mathf.Atan2(local.X, local.Z);
		}

		float difference = Mathf.Wrap(target - _turretYaw, -Mathf.Pi, Mathf.Pi);
		float step = Mathf.DegToRad(Tuning.TurretTurnRateDegrees) * dt;
		_turretYaw = Mathf.Wrap(_turretYaw + Mathf.Clamp(difference, -step, step), -Mathf.Pi, Mathf.Pi);
		Turret.Transform = new Transform3D(new Basis(Vector3.Up, _turretYaw) * _turretRest.Basis, _turretRest.Origin);
	}

	/// <summary>One line of state, for <c>bench.gd</c>.</summary>
	public string BenchStatus()
	{
		int stepping = 0;
		foreach (MechLeg leg in _legs)
		{
			stepping += leg.Stepping ? 1 : 0;
		}
		Vector3 p = GlobalPosition;
		float health = VoxelParts?.PartOf(Core)?.Health01 ?? 1.0f;
		int legs = 0;
		foreach (MechLeg leg in _legs)
		{
			legs += leg.Alive ? 1 : 0;
		}
		return System.FormattableString.Invariant(
			$"{Name} body={health * 100.0f:F0}% legs={legs}/{_legs.Count} stance={CurrentStance} drop={_drop:F2} destroyed={IsDestroyed} pos=({p.X:F1},{p.Y:F1},{p.Z:F1}) speed={Velocity.Slide(Up).Length():F1} up={Velocity.Dot(Up):F1} grounded={IsGrounded} stepping={stepping} sprint={Sprint} fuel={JetFuel01:F2} turret={Mathf.RadToDeg(_turretYaw):F0}");
	}
}
