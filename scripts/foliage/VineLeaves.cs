using System;
using System.Collections.Generic;
using DillControl.Core;
using DillControl.Voxel;
using Godot;

namespace DillControl.Foliage;

/// <summary>
/// A vine's leaves (docs/handoffs/vine-tool.md, step 4): meshes, not voxels.
///
/// - Drawn as one MultiMesh per leaf shape and one for the stems, in a copy
///   of <c>mat_leaf_toon</c> that bends as the vine's <see cref="PlantFlex"/>
///   does. No sag of their own. They cast shadows as far as the stalk does
///   (<see cref="VoxelBody.WholeShadowsMetres"/>): each MultiMesh is drawn
///   by a near instance that casts and a far one that does not.
/// - Each leaf is one thin box on the vine's section body where its stem
///   joins (<see cref="VoxelPart.AddExtraShape"/>): no new bodies, it rides
///   the bend and carries whoever stands on it, and the grapple pulls the
///   vine through it.
/// - A hit inside a leaf (<see cref="VoxelBody.Intercept"/>) holes it where
///   it lands and takes its health; at none, badly holed, it tears off whole,
///   a loose body under the loose-piece cap, and puffs into dust. A hit where
///   the stem joins shoots it through at once. A leaf whose stalk or arm is
///   cut away falls with it. The holes are a small texture made on the first
///   hit, a row per leaf, read by <c>leaf_toon</c>.
///
/// The leaves are <see cref="Set"/>, in <see cref="Model"/>'s space.
/// </summary>
[GlobalClass]
public partial class VineLeaves : Node3D
{
    public const string MaterialPath = "res://art/materials/mat_leaf_toon.tres";

    [Export] public VineLeafSet Set { get; set; }
    [Export] public VoxelBody Body { get; set; }
    [Export] public PlantFlex Flex { get; set; }
    [Export] public Node3D Model { get; set; }
    [Export] public LeafStats Stats { get; set; }

    /// <summary>Torn leaves in the world now, every vine's: they count
    /// against the loose-piece cap with the voxel pieces.</summary>
    public static int Loose { get; private set; }

    private float[] _health;
    private bool[] _gone;
    private VoxelPart[] _part;
    private VoxelPart.ExtraShape[] _shape;
    private int[] _slot;
    private MultiMesh[] _blades;
    private MultiMesh _stems;
    /// <summary>The far instances, shadowless while the stalk's far copies are.</summary>
    private readonly List<MultiMeshInstance3D> _far = new();
    private Material _plain;
    private ShaderMaterial _material;
    private Image _holes;
    private ImageTexture _holesTexture;
    private int[] _holeCount;
    private bool _ready;
    private int _version = int.MinValue;
    private float _recheck;
    private int _torn;

    private static readonly Dictionary<Vector3I, BoxShape3D> Boxes = new();

    public int Live => Set == null ? 0 : Set.Count - _torn;

    public override void _Ready()
    {
        Stats ??= GD.Load<LeafStats>(LeafStats.DefaultPath);
        _plain = GD.Load<Material>(MaterialPath);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Once the body has its parts and the vine its spine: a frame after
        // the scene is ready.
        if (!_ready)
        {
            _ready = true;
            Build();
            return;
        }
        if (_far.Count > 0 && Body != null && Body.WholeShadowsMetres <= 0.0f)
        {
            // Its sections have taken over, and they cast at any distance.
            foreach (MultiMeshInstance3D far in _far)
            {
                far.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            }
            _far.Clear();
        }
        _recheck -= (float)delta;
        if (_recheck > 0.0f || Set == null || Body == null)
        {
            return;
        }
        _recheck = Stats.RecheckSeconds;
        int version = 0;
        foreach (VoxelPart part in Body.Parts)
        {
            version = unchecked((version * 31) + (part.Grid?.Version ?? 0));
        }
        if (version != _version)
        {
            _version = version;
            Recheck();
        }
    }

    private void Build()
    {
        if (Set == null || Set.Count == 0 || Body == null || Model == null)
        {
            return;
        }
        int n = Set.Count;
        _health = new float[n];
        _gone = new bool[n];
        _part = new VoxelPart[n];
        _shape = new VoxelPart.ExtraShape[n];
        _slot = new int[n];
        _holeCount = new int[n];
        Array.Fill(_health, Stats.Health);

        // Its own copy, bending as the vine does: its holes are its own too.
        _material = Flex?.BendMaterial(_plain) ?? (ShaderMaterial)_plain.Duplicate();
        Material bent = _material;
        int shapes = Set.ShapeLobes.Length;
        var counts = new int[shapes];
        for (int i = 0; i < n; i++)
        {
            _slot[i] = counts[Set.Shape[i]]++;
        }
        Aabb bounds = Bounds();
        _blades = new MultiMesh[shapes];
        for (int s = 0; s < shapes; s++)
        {
            _blades[s] = Draw($"Blades{s}", LeafMesh.Blade(Set.ShapeAt(s), Set.Blade, Set.Vein), counts[s], bent, bounds);
        }
        _stems = Draw("Stems", LeafMesh.Stem(Set.Stem), n, bent, bounds);
        for (int i = 0; i < n; i++)
        {
            _blades[Set.Shape[i]].SetInstanceTransform(_slot[i], BladeAt(i));
            _blades[Set.Shape[i]].SetInstanceCustomData(_slot[i], TintOf(Set, i));
            _stems.SetInstanceTransform(i, StemAt(i));
            Collide(i);
        }
        foreach (VoxelPart part in Body.Parts)
        {
            part.PlaceExtraShapes();
        }
        Body.Intercept = Take;
    }

    private MultiMesh Draw(string name, Mesh mesh, int count, Material material, Aabb bounds)
    {
        var multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = mesh,
            InstanceCount = count,
            CustomAabb = bounds,
        };
        float shadows = Body.WholeShadowsMetres;
        bool split = shadows > 0.0f && shadows < Stats.DrawMetres;
        AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multi,
            MaterialOverride = material,
            VisibilityRangeEnd = split ? shadows + VoxelBody.HandoverOverlapM : Stats.DrawMetres,
            Transform = Model.Transform,
        });
        if (split)
        {
            var far = new MultiMeshInstance3D
            {
                Name = name + "Far",
                Multimesh = multi,
                MaterialOverride = material,
                VisibilityRangeBegin = shadows,
                VisibilityRangeEnd = Stats.DrawMetres,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Transform = Model.Transform,
            };
            AddChild(far);
            _far.Add(far);
        }
        return multi;
    }

    // Every leaf, with room for the vine to bend.
    private Aabb Bounds()
    {
        var box = new Aabb(Set.Centre[0], Vector3.Zero);
        for (int i = 0; i < Set.Count; i++)
        {
            box = box.Expand(Set.Centre[i]).Expand(Set.Attach[i]);
        }
        return box.Grow(20.0f);
    }

    private Transform3D BladeAt(int i) => BladeOf(Set, i);

    private Transform3D StemAt(int i) => StemOf(Set, i, Stats.StemWidthM);

    /// <summary>Leaf <paramref name="i"/>'s blade in model space: X across, Y its face, Z the midrib.</summary>
    public static Transform3D BladeOf(VineLeafSet set, int i)
    {
        float r = set.Size[i] * 0.5f;
        Vector3 face = set.Face[i], midrib = set.Midrib[i];
        Vector3 side = face.Cross(midrib).Normalized();
        return new Transform3D(new Basis(side * r, face * r, midrib * r), set.Centre[i]);
    }

    /// <summary>Where leaf <paramref name="i"/>'s stem joins its blade, model space: inside its notch.</summary>
    public static Vector3 StemJoinOf(VineLeafSet set, int i) =>
        set.Centre[i] - (set.Midrib[i] * set.Size[i] * 0.5f * LeafMesh.StemJoin(set.ShapeAt(set.Shape[i])));

    /// <summary>Its stem, from where it joins the vine into the blade's notch.</summary>
    public static Transform3D StemOf(VineLeafSet set, int i, float width)
    {
        Vector3 basePoint = StemJoinOf(set, i);
        Vector3 along = basePoint - set.Attach[i];
        if (along.LengthSquared() < 1e-6f)
        {
            along = set.Midrib[i] * 0.1f;
        }
        Vector3 across = along.Cross(Vector3.Up);
        across = (across.LengthSquared() < 1e-6f ? Vector3.Right : across.Normalized()) * width;
        Vector3 over = along.Cross(across).Normalized() * width;
        return new Transform3D(new Basis(across, over, along), set.Attach[i]);
    }

    // Its tint for leaf_toon, which multiplies by INSTANCE_CUSTOM when its alpha is set.
    private static Color TintOf(VineLeafSet set, int i)
    {
        Color tint = i < set.Tint.Length ? set.Tint[i] : Colors.White;
        return new Color(tint.R, tint.G, tint.B, 1.0f);
    }

    /// <summary>The leaves drawn and nothing else, as they stand: the Vines
    /// dock's preview. A node per shape and one for the stems.</summary>
    public static Node3D Drawing(VineLeafSet set, Material material, float stemWidth)
    {
        var node = new Node3D { Name = "Leaves" };
        int shapes = set.ShapeLobes.Length;
        for (int s = 0; s < shapes; s++)
        {
            var mine = new List<int>();
            for (int i = 0; i < set.Count; i++)
            {
                if (set.Shape[i] == s)
                    mine.Add(i);
            }
            var multi = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = LeafMesh.Blade(set.ShapeAt(s), set.Blade, set.Vein),
                InstanceCount = mine.Count,
            };
            for (int k = 0; k < mine.Count; k++)
            {
                multi.SetInstanceTransform(k, BladeOf(set, mine[k]));
                multi.SetInstanceCustomData(k, TintOf(set, mine[k]));
            }
            node.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = material });
        }
        var stems = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = LeafMesh.Stem(set.Stem),
            InstanceCount = set.Count,
        };
        for (int i = 0; i < set.Count; i++)
            stems.SetInstanceTransform(i, StemOf(set, i, stemWidth));
        node.AddChild(new MultiMeshInstance3D { Multimesh = stems, MaterialOverride = material });
        return node;
    }

    // A point just inside the stalk or arm the stem grows from.
    private Vector3 Root(int i)
    {
        Vector3 basePoint = StemJoinOf(Set, i);
        Vector3 out_ = (basePoint - Set.Attach[i]).Normalized();
        float voxel = Body.Parts.Count > 0 && Body.Parts[0].Grid != null ? Body.Parts[0].Grid.VoxelSize : 0.5f;
        return Set.Attach[i] - (out_ * voxel);
    }

    private void Collide(int i)
    {
        Vector3 root = Model.GlobalTransform * Root(i);
        VoxelPart part = PartAt(root);
        if (part == null)
        {
            return;
        }
        float size = Set.Size[i] * 0.85f;
        var key = new Vector3I(Mathf.RoundToInt(size * 100), Mathf.RoundToInt(Stats.ThicknessM * 100), 0);
        if (!Boxes.TryGetValue(key, out BoxShape3D box))
        {
            box = new BoxShape3D { Size = new Vector3(size, Stats.ThicknessM, size) };
            Boxes[key] = box;
        }
        Transform3D blade = BladeAt(i);
        // Its top just over the blade's middle: something to stand on.
        var frame = new Transform3D(blade.Basis.Orthonormalized(),
            blade.Origin - (Set.Face[i] * ((Stats.ThicknessM * 0.5f) - 0.1f)));
        Transform3D local = part.GlobalTransform.AffineInverse() * Model.GlobalTransform * frame;
        _part[i] = part;
        _shape[i] = part.AddExtraShape(part.ToLocal(root), box, local, place: false);
    }

    private VoxelPart PartAt(Vector3 world)
    {
        foreach (VoxelPart part in Body.Parts)
        {
            if (part.Grid == null)
            {
                continue;
            }
            Vector3 v = part.Grid.ToVox(part.ToLocal(world));
            if (v.X >= -1 && v.Y >= -1 && v.Z >= -1 && v.X <= part.Grid.Size.X && v.Y <= part.Grid.Size.Y && v.Z <= part.Grid.Size.Z)
            {
                return part;
            }
        }
        return null;
    }

    // Still joined to something solid where its stem grows?
    private bool Attached(int i)
    {
        VoxelPart part = _part[i];
        if (part == null || !IsInstanceValid(part) || part.Grid == null)
        {
            return false;
        }
        Vector3 v = part.Grid.ToVox(part.ToLocal(Model.GlobalTransform * Root(i)));
        var cell = new Vector3I(Mathf.FloorToInt(v.X), Mathf.FloorToInt(v.Y), Mathf.FloorToInt(v.Z));
        for (int z = -1; z <= 1; z++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    if (part.Grid.At(cell.X + x, cell.Y + y, cell.Z + z) != 0)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void Recheck()
    {
        for (int i = 0; i < Set.Count; i++)
        {
            if (!_gone[i] && !Attached(i))
            {
                Tear(i, Vector3.Zero);
            }
        }
    }

    /// <summary>A hit on the vine: a leaf's if it lands inside one (and then
    /// the voxels take nothing), every leaf near it if it is area damage.</summary>
    private bool Take(DamageInfo info)
    {
        if (_health == null || info == null)
        {
            return false;
        }
        Vector3 built = Flex != null && Flex.Rod != null ? Flex.ToBuilt(info.Position) : info.Position;
        Vector3 at = Model.GlobalTransform.AffineInverse() * built;
        if (info.IsSplash)
        {
            float reach = Stats.SplashRadiusM;
            for (int i = 0; i < Set.Count; i++)
            {
                float d = Set.Centre[i].DistanceTo(at) - (Set.Size[i] * 0.5f);
                if (!_gone[i] && d < reach)
                {
                    Hurt(i, info.Amount * (1.0f - (Mathf.Max(d, 0.0f) / reach)), info.Direction);
                }
            }
            return false;
        }
        for (int i = 0; i < Set.Count; i++)
        {
            if (!_gone[i] && Inside(i, at, 0.6f))
            {
                Shot(i, at, info.Amount, info.Direction);
                return true;
            }
        }
        return false;
    }

    private bool Inside(int i, Vector3 point, float margin)
    {
        Vector3 d = point - Set.Centre[i];
        float r = (Set.Size[i] * 0.5f) + margin;
        Vector3 side = Set.Face[i].Cross(Set.Midrib[i]).Normalized();
        return Mathf.Abs(d.Dot(side)) < r && Mathf.Abs(d.Dot(Set.Midrib[i])) < r
            && Mathf.Abs(d.Dot(Set.Face[i])) < Stats.ThicknessM + margin + (Set.Size[i] * 0.1f);
    }

    // A hit on the blade: through where the stem joins it falls, else it is
    // holed where it was hit.
    private void Shot(int i, Vector3 at, float amount, Vector3 push)
    {
        float r = Set.Size[i] * 0.5f;
        Vector3 stem = StemJoinOf(Set, i);
        if (at.DistanceTo(stem) < r * Stats.StemZoneShare)
        {
            Tear(i, push);
            return;
        }
        Hole(i, at, amount);
        Hurt(i, amount, push);
    }

    // A hole in leaf i at a point in model space, in the blade's own units
    // (BladeOf: X across, Z along, a unit to its half-width).
    private void Hole(int i, Vector3 at, float amount)
    {
        if (_holes == null)
        {
            _holes = Image.CreateEmpty(Stats.HolesPerLeaf, Set.Count, false, Image.Format.Rgbaf);
            _holesTexture = ImageTexture.CreateFromImage(_holes);
            _material.SetShaderParameter("leaf_holes", _holesTexture);
            _material.SetShaderParameter("leaf_hole_slots", Stats.HolesPerLeaf);
        }
        float r = Set.Size[i] * 0.5f;
        Vector3 d = at - Set.Centre[i];
        Vector3 side = Set.Face[i].Cross(Set.Midrib[i]).Normalized();
        float radius = Stats.HoleRadiusM * Mathf.Clamp(Mathf.Sqrt(amount / Stats.HoleReferenceDamage), 0.5f, 3.0f);
        _holes.SetPixel(_holeCount[i] % Stats.HolesPerLeaf, i,
            new Color(d.Dot(side) / r, d.Dot(Set.Midrib[i]) / r, radius / r, (_holeCount[i] * 0.618034f) % 1.0f));
        _holesTexture.Update(_holes);
        if (_holeCount[i]++ == 0)
        {
            Color tint = TintOf(Set, i);
            _blades[Set.Shape[i]].SetInstanceCustomData(_slot[i], new Color(tint.R, tint.G, tint.B, 2.0f + i));
        }
    }

    // A torn leaf keeps its holes: its own copy, unbent, naming its row.
    private Material LooseMaterial(int i)
    {
        if (_holesTexture == null || _holeCount[i] == 0)
        {
            return _plain;
        }
        var own = (ShaderMaterial)_plain.Duplicate();
        own.SetShaderParameter("leaf_holes", _holesTexture);
        own.SetShaderParameter("leaf_hole_slots", Stats.HolesPerLeaf);
        own.SetShaderParameter("leaf_row", i);
        return own;
    }

    private void Hurt(int i, float amount, Vector3 push)
    {
        _health[i] -= amount;
        if (_health[i] <= 0.0f)
        {
            Tear(i, push);
        }
    }

    /// <summary>Leaf <paramref name="i"/> comes off whole: loose under the cap,
    /// dust past it.</summary>
    private void Tear(int i, Vector3 push)
    {
        _gone[i] = true;
        _torn++;
        if (_shape[i] != null && IsInstanceValid(_part[i]))
        {
            _part[i].RemoveExtraShape(_shape[i]);
        }
        _shape[i] = null;
        var hidden = new Transform3D(new Basis(Vector3.Zero, Vector3.Zero, Vector3.Zero), Set.Centre[i]);
        _blades[Set.Shape[i]].SetInstanceTransform(_slot[i], hidden);
        _stems.SetInstanceTransform(i, hidden);

        // Where it was drawn, bent, when it came off.
        Transform3D blade = Model.GlobalTransform * BladeAt(i);
        if (Flex != null && Flex.Rod != null)
        {
            blade.Origin = Flex.ToDrawn(blade.Origin);
        }
        Vector3 up = WorldUp.At(blade.Origin);
        VoxelDebris debris = VoxelDebris.Current;
        bool room = debris == null || debris.ChunkCount + Loose < debris.Stats.MaxChunks;
        if (!room)
        {
            Puff(blade.Origin, up, Vector3.Zero, Set.Size[i]);
            return;
        }
        float r = Set.Size[i] * 0.5f;
        var body = new RigidBody3D
        {
            Name = "LooseLeaf",
            Mass = Stats.MassKg,
            LinearDamp = Stats.LooseDrag,
            AngularDamp = Stats.LooseSpinDrag,
            // Never on anything the player collides with: see GameLayers.Litter.
            CollisionLayer = GameLayers.Mask(GameLayers.Litter),
            CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop, GameLayers.Litter),
        };
        body.AddChild(new MeshInstance3D
        {
            Mesh = LeafMesh.Blade(Set.ShapeAt(Set.Shape[i]), Set.Blade, Set.Vein),
            MaterialOverride = LooseMaterial(i),
            Transform = new Transform3D(Basis.Identity.Scaled(new Vector3(r, r, r)), Vector3.Zero),
        });
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(r * 1.7f, Stats.ThicknessM, r * 1.7f) },
        });
        Node parent = GetTree().CurrentScene ?? GetTree().Root;
        parent.AddChild(body);
        body.GlobalTransform = new Transform3D(blade.Basis.Orthonormalized(), blade.Origin);
        body.LinearVelocity = push.LengthSquared() > 1e-6f ? push.Normalized() * 3.0f : Vector3.Zero;
        Loose++;
        float size = Set.Size[i];
        GetTree().CreateTimer(Stats.LooseSeconds).Timeout += () =>
        {
            Loose--;
            if (IsInstanceValid(body))
            {
                Puff(body.GlobalPosition, WorldUp.At(body.GlobalPosition), body.LinearVelocity, size);
                body.QueueFree();
            }
        };
    }

    private void Puff(Vector3 at, Vector3 up, Vector3 velocity, float size) =>
        VoxelDebris.Current?.Dust(at, up, velocity, Set.Blade, size * Stats.DustScale);

    // ---------------- For bench.gd ----------------

    /// <summary>The live leaf whose blade is nearest <paramref name="height"/>
    /// metres up, or -1.</summary>
    private int LeafNear(string height)
    {
        float metres = float.Parse(height, System.Globalization.CultureInfo.InvariantCulture);
        int best = -1;
        float far = float.MaxValue;
        for (int i = 0; _gone != null && i < Set.Count; i++)
        {
            float d = Mathf.Abs(Set.Centre[i].Y - metres);
            if (!_gone[i] && d < far)
            {
                far = d;
                best = i;
            }
        }
        return best;
    }

    private Vector3 Drawn(Vector3 model)
    {
        Vector3 world = Model.GlobalTransform * model;
        return Flex != null && Flex.Rod != null ? Flex.ToDrawn(world) : world;
    }

    /// <summary>A point over the middle of the leaf nearest that height, to stand on.</summary>
    public Vector3 BenchLeafTop(string height)
    {
        int i = LeafNear(height);
        if (i < 0)
        {
            return GlobalPosition;
        }
        GD.Print(System.FormattableString.Invariant($"[leaves] stand on leaf {i} of {GetParent()?.Name}, {Set.Size[i]:F1} m across, {Set.Centre[i].Y:F1} m up"));
        return Drawn(Set.Centre[i] + (Set.Face[i] * 1.5f));
    }

    /// <summary>The middle of the leaf nearest that height, to aim at.</summary>
    public Vector3 BenchLeafCentre(string height)
    {
        int i = LeafNear(height);
        return i < 0 ? GlobalPosition : Drawn(Set.Centre[i]);
    }

    /// <summary>On the ground 25 m out past the leaf nearest that height, away
    /// from the stalk or arm it grows from: a clear shot up at it.</summary>
    public Vector3 BenchLeafFront(string height)
    {
        int i = LeafNear(height);
        if (i < 0)
        {
            return GlobalPosition;
        }
        Vector3 c = Set.Centre[i];
        Vector3 outward = new Vector3(c.X - Set.Attach[i].X, 0.0f, c.Z - Set.Attach[i].Z);
        if (outward.LengthSquared() < 1e-4f)
        {
            outward = new Vector3(c.X, 0.0f, c.Z);
        }
        Vector3 at = c + (outward.Normalized() * 25.0f);
        return Model.GlobalTransform * new Vector3(at.X, 1.0f, at.Z);
    }

    /// <summary>Six rifle hits across the leaf nearest that height, none
    /// near its stem, through the way a shot holes it. Returns where it is
    /// drawn: its middle, its Y its face.</summary>
    public Transform3D BenchHoleLeaf(string height)
    {
        int i = LeafNear(height);
        if (i < 0)
        {
            return GlobalTransform;
        }
        float r = Set.Size[i] * 0.5f;
        Vector3 side = Set.Face[i].Cross(Set.Midrib[i]).Normalized();
        var random = Core.SeededRandom.Stream((ulong)i, "bench:holes");
        for (int k = 0; k < 6; k++)
        {
            Vector3 at = Set.Centre[i] + (side * r * random.Range(-0.6f, 0.6f)) + (Set.Midrib[i] * r * random.Range(-0.1f, 0.6f));
            Shot(i, at, 32.0f, Vector3.Zero);
        }
        Basis basis = Model.GlobalBasis * BladeOf(Set, i).Basis.Orthonormalized();
        return new Transform3D(basis, Drawn(Set.Centre[i]));
    }

    public void PrintBenchStatus() =>
        GD.Print($"[leaves] {GetParent()?.Name}: {Live} of {Set?.Count ?? 0} on, {_torn} torn, {Loose} loose in the world");
}
