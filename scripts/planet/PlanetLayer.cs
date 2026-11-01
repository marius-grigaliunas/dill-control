using Godot;

namespace DillControl.Planet;

/// <summary>
/// A sphere standing off a <see cref="PlanetBackdrop"/>'s surface: the cloud
/// deck, the atmosphere. It is a mesh instance whose mesh is always a sphere
/// <see cref="AboveSurface"/> metres over the planet, so changing the planet's
/// radius moves every layer with it. The look is the material's.
///
/// It also tells the material where the sun is (an instance uniform, so one
/// material can serve several layers), and the planet's and its own radius:
/// the atmosphere shader works out how much air a sight line crosses from them.
/// </summary>
[Tool]
[GlobalClass]
public partial class PlanetLayer : MeshInstance3D
{
	/// <summary>The planet this layer wraps. Its spec gives the radius.</summary>
	[Export]
	public PlanetBackdrop Planet { get; set; }

	/// <summary>The light that decides which side is day.</summary>
	[Export]
	public DirectionalLight3D Sun { get; set; }

	/// <summary>How far over the planet's mean radius this layer's sphere stands.</summary>
	[Export(PropertyHint.Range, "0,2000,1,or_greater,suffix:m")]
	public float AboveSurface { get; set; } = 300.0f;

	/// <summary>
	/// For a cloud deck: how far over the surface its underside is, so it fills
	/// the shell between this and <see cref="AboveSurface"/>. Zero for a layer
	/// that has no underside (the atmosphere).
	/// </summary>
	[Export(PropertyHint.Range, "0,2000,1,or_greater,suffix:m")]
	public float BaseAboveSurface { get; set; }

	private float _radius = -1.0f;

	/// <summary>
	/// The sphere is this layer's own business: its radius is the planet's plus
	/// <see cref="AboveSurface"/> and its height is twice that, set every frame.
	/// Hidden in the inspector (and not saved), so there is nothing there to
	/// fight with; change AboveSurface instead.
	/// </summary>
	public override void _ValidateProperty(Godot.Collections.Dictionary property)
	{
		if (property["name"].AsStringName() == MeshInstance3D.PropertyName.Mesh)
		{
			property["usage"] = (int)PropertyUsageFlags.None;
		}
	}

	public override void _Process(double delta)
	{
		float planetRadius = Planet?.Spec?.Radius ?? 0.0f;
		if (planetRadius <= 0.0f)
		{
			return;
		}

		float layerRadius = planetRadius + AboveSurface;
		if (Mesh is not SphereMesh sphere)
		{
			sphere = new SphereMesh { RadialSegments = 192, Rings = 96 };
			Mesh = sphere;
			_radius = -1.0f;
		}
		// Checked against the mesh itself, not the last value written, so a hand
		// edit of the mesh (size, a hemisphere, a coarse count of segments) is put
		// back at once: a six-sided "sphere" draws a few huge triangles, and the
		// layer shows as a stripe with straight edges.
		if (!Mathf.IsEqualApprox(sphere.Radius, layerRadius) || !Mathf.IsEqualApprox(sphere.Height, layerRadius * 2.0f))
		{
			sphere.Radius = layerRadius;
			sphere.Height = layerRadius * 2.0f;
		}
		if (sphere.IsHemisphere)
		{
			sphere.IsHemisphere = false;
		}
		if (sphere.RadialSegments != 192 || sphere.Rings != 96)
		{
			sphere.RadialSegments = 192;
			sphere.Rings = 96;
		}
		_radius = layerRadius;

		SetInstanceShaderParameter("planet_radius", planetRadius);
		SetInstanceShaderParameter("layer_radius", layerRadius);
		if (BaseAboveSurface > 0.0f)
		{
			SetInstanceShaderParameter("base_height", BaseAboveSurface);
		}
		if (Sun != null)
		{
			// A directional light shines along its own -Z: toward the sun is +Z.
			SetInstanceShaderParameter("sun_direction", Sun.GlobalBasis.Z.Normalized());
		}
	}
}
