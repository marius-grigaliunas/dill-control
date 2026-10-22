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
///   does. No sag of their own.
/// - Each leaf is one thin box on the vine's section body where its stem
///   joins (<see cref="VoxelPart.AddExtraShape"/>): no new bodies, it rides
///   the bend and carries whoever stands on it, and the grapple pulls the
///   vine through it.
/// - A hit inside a leaf takes its health (<see cref="VoxelBody.Intercept"/>);
///   at none it tears off whole, a loose body under the loose-piece cap, and
///   puffs into dust. A leaf whose stalk or arm is cut away falls with it.
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
    private Material _plain;
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
        Array.Fill(_health, Stats.Health);

        Material bent = Flex?.BendMaterial(_plain) ?? _plain;
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
        AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multi,
            MaterialOverride = material,
            VisibilityRangeEnd = Stats.DrawMetres,
            Transform = Model.Transform,
        });
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

    /// <summary>Its stem, from where it joins the vine into the blade's notch.</summary>
    public static Transform3D StemOf(VineLeafSet set, int i, float width)
    {
        float r = set.Size[i] * 0.5f;
        Vector3 basePoint = set.Centre[i] - (set.Midrib[i] * r * 0.7f);
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
        Vector3 basePoint = Set.Centre[i] - (Set.Midrib[i] * Set.Size[i] * 0.35f);
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
        _shape[i] = part.AddExtraShape(part.ToLocal(root), box, local);
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
                Hurt(i, info.Amount, info.Direction);
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
            CollisionLayer = GameLayers.Mask(GameLayers.Prop),
            CollisionMask = GameLayers.Mask(GameLayers.World, GameLayers.Prop),
        };
        body.AddChild(new MeshInstance3D
        {
            Mesh = LeafMesh.Blade(Set.ShapeAt(Set.Shape[i]), Set.Blade, Set.Vein),
            MaterialOverride = _plain,
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

    public void PrintBenchStatus() =>
        GD.Print($"[leaves] {GetParent()?.Name}: {Live} of {Set?.Count ?? 0} on, {_torn} torn, {Loose} loose in the world");
}
