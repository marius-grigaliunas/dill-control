using Godot;

namespace DillControl.Levels;

/// <summary>
/// One shape in an arena's ground, placed by hand: a hill, a mesa, a ridge,
/// a basin, or a pad levelled for something to stand on. A child of the
/// <see cref="ArenaTerrain"/>; drag it in the editor and the ground follows
/// (decision 0004: if a designer would want to move it, it is a node).
///
/// Shapes add up in child order, then pads flatten what is under them, so a
/// pad on a hill cuts a shelf into it.
/// </summary>
[Tool]
[GlobalClass]
public partial class TerrainFeature : Marker3D
{
    public enum Shape
    {
        /// <summary>A smooth rise.</summary>
        Hill,
        /// <summary>A flat top with cliffs round it: somewhere the horde cannot climb.</summary>
        Mesa,
        /// <summary>A hill drawn out along the feature's own X, <see cref="Length"/> long.</summary>
        Ridge,
        /// <summary>A smooth hollow.</summary>
        Basin,
        /// <summary>Levels the ground to this node's own height.</summary>
        Pad,
    }

    private Shape _kind = Shape.Hill;
    private float _radius = 60.0f;
    private float _height = 20.0f;
    private float _edge = 30.0f;
    private float _length = 120.0f;
    private float _roughness = 0.25f;

    [Export]
    public Shape Kind
    {
        get => _kind;
        set { _kind = value; Changed(); }
    }

    /// <summary>Metres from the middle (or from the spine, for a ridge) to where the shape ends.</summary>
    [Export(PropertyHint.Range, "2,400,1,suffix:m")]
    public float Radius
    {
        get => _radius;
        set { _radius = value; Changed(); }
    }

    /// <summary>How high a hill, ridge or mesa stands, or how deep a basin goes. Unused by a pad.</summary>
    [Export(PropertyHint.Range, "0,200,0.5,suffix:m")]
    public float Height
    {
        get => _height;
        set { _height = value; Changed(); }
    }

    /// <summary>
    /// A mesa: how wide its cliff is. A pad: how far it blends back into the
    /// ground past its radius. Small is steep.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,200,0.5,suffix:m")]
    public float Edge
    {
        get => _edge;
        set { _edge = value; Changed(); }
    }

    /// <summary>A ridge's length along the feature's own X.</summary>
    [Export(PropertyHint.Range, "0,1000,1,suffix:m")]
    public float Length
    {
        get => _length;
        set { _length = value; Changed(); }
    }

    /// <summary>How far noise pushes the outline in and out, as a share of the radius. Zero is a circle.</summary>
    [Export(PropertyHint.Range, "0,0.6,0.01")]
    public float Roughness
    {
        get => _roughness;
        set { _roughness = value; Changed(); }
    }

    public override void _Ready()
    {
        SetNotifyTransform(true);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationTransformChanged)
        {
            Changed();
        }
    }

    /// <summary>Tells the terrain it stands in to build again. Parent to child only.</summary>
    private void Changed()
    {
        if (IsInsideTree())
        {
            (GetParent() as ArenaTerrain)?.MarkDirty();
        }
    }
}
