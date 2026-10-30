using Godot;
using DillControl.Planet;

namespace DillControl.Mission;

/// <summary>
/// A baked, hand-edited nest (docs/decisions/0056) put on a planet. The nest
/// scene stands under this node on flat ground, as it was edited; this node's
/// own place says roughly where on the planet and which way it faces, as a
/// <see cref="PlanetSite"/>'s does. When the planet builds, each vine is moved
/// to the curved ground by its flat distance and bearing from the heart, stood
/// upright with its heading kept, and a small pad levelled under its foot: a
/// vine moved or deleted in the editor stays moved or deleted.
///
/// A vine's bend, bins and leaves read where it stands when it enters the tree,
/// so the nest is held out of the tree until the ground is there, then
/// planted whole. <see cref="Nest"/> hands its pods to the colony after.
/// </summary>
[GlobalClass]
public partial class PlacedNestSite : Node3D, IPlanetSiteChooser
{
    /// <summary>The baked nest scene's root: its children are the vines.</summary>
    [Export] public Node3D Vines { get; set; }
    /// <summary>Told once the vines stand, to take their pods.</summary>
    [Export] public PlacedNest Nest { get; set; }

    [ExportGroup("Pads")]
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")] public float PadRadiusM { get; set; } = 6.0f;
    [Export(PropertyHint.Range, "0,40,0.5,suffix:m")] public float PadBlendM { get; set; } = 8.0f;

    private Node3D[] _vines;
    private Vector3[] _directions;
    private Transform3D[] _flat;
    private Basis _frame;
    private Vector3 _heart;
    private PlanetHeightfield _heights;

    // Out of the tree before its children enter it: this node's own enter
    // is not yet blocked from changing them, and the vines were not ready.
    public override void _EnterTree()
    {
        if (Engine.IsEditorHint() || Vines == null || Vines.GetParent() != this)
        {
            return;
        }
        RemoveChild(Vines);
    }

    public override void _ExitTree()
    {
        // Never planted (the level ended first): not left floating.
        if (Vines != null && IsInstanceValid(Vines) && Vines.GetParent() == null)
        {
            Vines.QueueFree();
        }
    }

    public void ChooseSites(PlanetHeightfield heights, ulong seed)
    {
        _heights = heights;
        if (Vines == null)
        {
            GD.PushError($"PlacedNestSite '{Name}' has no Vines; no nest.");
            return;
        }
        Vector3 position = GlobalPosition;
        _heart = position.LengthSquared() > 1e-6f ? position.Normalized() : Vector3.Up;
        _frame = PlanetHeightfield.UprightBasis(_heart, -GlobalBasis.Z);

        var vines = new System.Collections.Generic.List<Node3D>();
        foreach (Node child in Vines.GetChildren())
        {
            if (child is Node3D vine)
            {
                vines.Add(vine);
            }
        }
        _vines = vines.ToArray();
        _directions = new Vector3[_vines.Length];
        _flat = new Transform3D[_vines.Length];
        for (int i = 0; i < _vines.Length; i++)
        {
            Transform3D flat = Vines.Transform * _vines[i].Transform;
            _flat[i] = flat;
            Vector3 along = new Vector3(flat.Origin.X, 0.0f, flat.Origin.Z);
            _directions[i] = Along(_heart, _frame * along, along.Length() / heights.Radius);
            if (PadRadiusM > 0.0f)
            {
                heights.FlattenPad(_directions[i], PadRadiusM, PadBlendM);
            }
        }
    }

    public void SitesPlaced()
    {
        if (_vines == null)
        {
            return;
        }
        Transform3D parent = GlobalTransform;
        for (int i = 0; i < _vines.Length; i++)
        {
            Vector3 up = _directions[i];
            float arc = _heart.AngleTo(up);
            Vector3 axis = _heart.Cross(up);
            // The flat frame carried along the planet to where the vine stands.
            Basis carried = axis.LengthSquared() > 1e-10f ? new Basis(axis.Normalized(), arc) * _frame : _frame;
            Vector3 at = _heights.SurfacePoint(up) + (up * _flat[i].Origin.Y);
            // Children are placed in the world; the nest node stands at its origin.
            _vines[i].Transform = new Transform3D(carried * _flat[i].Basis, at);
        }
        Vines.Transform = parent.AffineInverse();
        AddChild(Vines);
        Nest?.Register();
    }

    private static Vector3 Along(Vector3 from, Vector3 tangent, float arc)
    {
        Vector3 axis = from.Cross(tangent);
        return axis.LengthSquared() < 1e-8f ? from : from.Rotated(axis.Normalized(), arc).Normalized();
    }
}
