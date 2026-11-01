using DillControl.Core;
using Godot;

namespace DillControl.Planet;

/// <summary>
/// A planet to look at and nothing else: the heightfield of a
/// <see cref="PlanetSpec"/> drawn as the far shell, with no chunks, colliders,
/// gravity or streaming. The main menu's window opens on one.
///
/// A tool node, so the planet is there in the editor to frame, tweak and
/// judge; change the spec (or press Rebuild) and it regrows. Its meshes are
/// children with no owner, so they are never saved into the scene: the scene
/// holds this node and its exports, and the planet is made from them.
///
/// The heightfield is its own, coarse, size (<see cref="HeightEdge"/>): the
/// gameplay spacing of 4.9 m at 5,000 m would be fifteen million heights, and
/// from orbit nobody can tell. It is the same generator, so a spec that grows
/// a planet in a level grows the same planet here.
/// </summary>
[Tool]
[GlobalClass]
public partial class PlanetBackdrop : Node3D
{
	private PlanetSpec _spec;
	private int _heightEdge = 513;
	private int _shellEdge = 257;
	private int _heightMapEdge = 768;
	private float _seaShare = 0.6f;
	private Material _material;
	private PlanetFarShell _shell;
	private bool _queued;
	private int _specSignature;

	/// <summary>The planet's shape. Editing it rebuilds the planet.</summary>
	[Export]
	public PlanetSpec Spec
	{
		get => _spec;
		set
		{
			if (_spec != null)
			{
				_spec.Changed -= Queue;
			}
			_spec = value;
			if (_spec != null)
			{
				_spec.Changed += Queue;
			}
			Queue();
		}
	}

	/// <summary>Vertices along one face edge of the heightfield. 513 on a 5,000 m planet is a height every 15 m.</summary>
	[Export(PropertyHint.Range, "33,1025,16")]
	public int HeightEdge
	{
		get => _heightEdge;
		set
		{
			_heightEdge = value;
			Queue();
		}
	}

	/// <summary>Vertices along one face edge of the drawn shell.</summary>
	[Export(PropertyHint.Range, "17,513,16")]
	public int ShellEdge
	{
		get => _shellEdge;
		set
		{
			_shellEdge = value;
			Queue();
		}
	}

	/// <summary>
	/// Texels along one face edge of the height map the colours are cut from
	/// (a cube, so about 10 m a texel on a 6,000 m planet at 768). The mesh's own
	/// heights are 100 m apart and blended straight between, so cut from them
	/// the coast and the sea's depth bands were straight lines.
	/// </summary>
	[Export(PropertyHint.Range, "64,2048,16")]
	public int HeightMapEdge
	{
		get => _heightMapEdge;
		set
		{
			_heightMapEdge = value;
			Queue();
		}
	}

	/// <summary>
	/// How much of the planet is under the sea, from none to all. The sea level
	/// is found from the planet's own heights, so it stays right when the noise
	/// or the relief changes; it overrides the material's <c>sea_level</c>.
	/// </summary>
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float SeaShare
	{
		get => _seaShare;
		set
		{
			_seaShare = value;
			Queue();
		}
	}

	/// <summary>The planet's look: a <c>planet_backdrop</c> shader material. It is copied, never written to.</summary>
	[Export]
	public Material PlanetMaterial
	{
		get => _material;
		set
		{
			_material = value;
			Queue();
		}
	}

	/// <summary>How fast it turns about its own axis. Zero stands it still. Never turns in the editor.</summary>
	[Export(PropertyHint.Range, "0,10,0.05,suffix:°/s")]
	public float SpinDegreesPerSecond { get; set; } = 0.8f;

	/// <summary>Press to grow the planet again.</summary>
	[Export]
	public bool Rebuild
	{
		get => false;
		set
		{
			if (value)
			{
				Queue();
			}
		}
	}

	public override void _Ready()
	{
		// The shader reads part of its noise from the shared lattice texture.
		NoiseLattice.Publish();
		Queue();
	}

	public override void _Process(double delta)
	{
		if (Engine.IsEditorHint())
		{
			// A C# resource does not announce an edit made in the inspector, so
			// Spec.Changed never fires: watch its values instead.
			if (_spec != null)
			{
				int now = Signature(_spec);
				if (now != _specSignature)
				{
					Queue();
				}
			}
			return;
		}

		if (SpinDegreesPerSecond != 0.0f)
		{
			RotateObjectLocal(Vector3.Up, Mathf.DegToRad(SpinDegreesPerSecond) * (float)delta);
		}
	}

	private static int Signature(PlanetSpec spec)
	{
		System.HashCode hash = new();
		hash.Add(spec.Radius);
		hash.Add(spec.Amplitude);
		hash.Add(spec.Seed);
		hash.Add(spec.NoiseFrequency);
		hash.Add(spec.Octaves);
		hash.Add(spec.Lacunarity);
		hash.Add(spec.Gain);
		hash.Add(spec.RidgeShare);
		hash.Add(spec.CraterCount);
		hash.Add(spec.CraterRadiusMin);
		hash.Add(spec.CraterRadiusMax);
		hash.Add(spec.CraterDepthRatio);
		return hash.ToHashCode();
	}

	private void Queue()
	{
		if (_queued || !IsInsideTree())
		{
			return;
		}
		_queued = true;
		Callable.From(Grow).CallDeferred();
	}

	/// <summary>The height, in metres above the mean radius, that this share of the ground lies below.</summary>
	private static float SeaLevel(PlanetHeightfield heights, float share)
	{
		int total = 6 * heights.FaceVertexEdge * heights.FaceVertexEdge;
		int step = Mathf.Max(1, total / 100000);
		float[] sample = new float[(total + step - 1) / step];
		for (int i = 0; i < sample.Length; i++)
		{
			sample[i] = heights.HeightAt(i * step);
		}
		System.Array.Sort(sample);
		return sample[Mathf.Clamp(Mathf.RoundToInt(share * (sample.Length - 1)), 0, sample.Length - 1)];
	}

	/// <summary>
	/// The heights as a cube map, in terrain amplitudes, sampled from the
	/// heightfield at each texel's direction (the heightfield's own cube is warped,
	/// so its grid is not a cube map's). Faces in Godot's order, +X -X +Y -Y +Z -Z,
	/// each laid out as the cube map lookup reads it.
	/// </summary>
	private static Cubemap HeightMap(PlanetHeightfield heights, int edge)
	{
		float scale = 1.0f / Mathf.Max(heights.Amplitude, 0.001f);
		// The faces fill on worker threads; the images are made here, on the main one.
		var bytesOf = new byte[6][];
		Jobs.For(6, face =>
		{
			byte[] bytes = new byte[edge * edge * 4];
			for (int j = 0; j < edge; j++)
			{
				float b = (((j + 0.5f) / edge) * 2.0f) - 1.0f;
				for (int i = 0; i < edge; i++)
				{
					float a = (((i + 0.5f) / edge) * 2.0f) - 1.0f;
					Vector3 direction = face switch
					{
						0 => new Vector3(1.0f, -b, -a),
						1 => new Vector3(-1.0f, -b, a),
						2 => new Vector3(a, 1.0f, b),
						3 => new Vector3(a, -1.0f, -b),
						4 => new Vector3(a, -b, 1.0f),
						_ => new Vector3(-a, -b, -1.0f),
					};
					float height = heights.SampleHeight(direction.Normalized()) * scale;
					System.BitConverter.TryWriteBytes(new System.Span<byte>(bytes, ((j * edge) + i) * 4, 4), height);
				}
			}
			bytesOf[face] = bytes;
		});
		var faces = new Godot.Collections.Array<Image>();
		for (int face = 0; face < 6; face++)
		{
			faces.Add(Image.CreateFromData(edge, edge, false, Image.Format.Rf, bytesOf[face]));
		}
		var map = new Cubemap();
		map.CreateFromImages(faces);
		return map;
	}

	private void Grow()
	{
		_queued = false;
		if (_spec == null || !IsInsideTree())
		{
			return;
		}

		_specSignature = Signature(_spec);
		long started = System.Diagnostics.Stopwatch.GetTimestamp();
		PlanetHeightfield heights = new(_spec.Radius, _spec.Amplitude, _heightEdge, _spec.DigFloorDepth);
		heights.Generate(_spec);

		Material look = _material?.Duplicate() as Material;
		if (look is ShaderMaterial shader)
		{
			shader.SetShaderParameter("planet_radius", _spec.Radius);
			shader.SetShaderParameter("terrain_amplitude", Mathf.Max(_spec.Amplitude, 0.001f));
			shader.SetShaderParameter("sea_level", SeaLevel(heights, _seaShare) / Mathf.Max(_spec.Amplitude, 0.001f));
			shader.SetShaderParameter("height_map", HeightMap(heights, _heightMapEdge));
		}

		_shell?.QueueFree();
		_shell = new PlanetFarShell { Name = "Shell" };
		AddChild(_shell);
		_shell.Build(heights, _shellEdge, 0.0f, look, underChunks: false);

		double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
		GD.Print(System.FormattableString.Invariant(
			$"[planet backdrop] radius {_spec.Radius:F0} m, relief {_spec.Amplitude:F0} m, heights {_heightEdge}² x6, shell {_shellEdge}² x6, {milliseconds:F0} ms"));
	}
}
