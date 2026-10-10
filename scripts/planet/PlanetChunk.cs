using Godot;

namespace DillControl.Planet;

/// <summary>
/// One square patch of a cube-sphere face: a mesh, and a collider that only
/// exists while the player is near enough to stand on it.
///
/// The chunk owns no height data. It reads the single shared array on
/// <see cref="PlanetTerrain"/>, which is what makes cracks between neighbouring
/// chunks impossible rather than merely unlikely: two adjacent chunks read the
/// same array entry and run the same expression, so the shared vertices come
/// out bit-identical. Per-chunk copies of the edge rows would have to be kept
/// in sync by hand, and would drift the first time a crater landed on a seam.
/// </summary>
public partial class PlanetChunk : StaticBody3D
{
    public int Face { get; private set; }
    public int ChunkU { get; private set; }
    public int ChunkV { get; private set; }

    /// <summary>Outward direction of the chunk centre, for horizon culling.</summary>
    public Vector3 CentreNormal { get; private set; }

    /// <summary>Angular radius of the chunk as seen from the planet centre.</summary>
    public float AngularRadius { get; private set; }

    /// <summary>True when the height data under this chunk has changed.</summary>
    public bool GeometryDirty { get; set; }

    /// <summary>
    /// True while this chunk is inside the streamed collision ring. A C#
    /// field, like <see cref="SetMeshVisible"/>'s guard: streaming asks every
    /// chunk every frame, and reading <c>Disabled</c> back through the engine
    /// a thousand times a frame is time spent asking what we already know.
    /// </summary>
    public bool HasCollision { get; private set; }

    /// <summary>The chunk's centre on the mean sphere, in the planet's space. A field, for the same reason.</summary>
    public Vector3 Centre { get; private set; }

    private MeshInstance3D _meshInstance;
    private CollisionShape3D _collision;
    private ArrayMesh _mesh;
    private PlanetTerrain _terrain;

    public void Initialise(PlanetTerrain terrain, int face, int chunkU, int chunkV, Material material)
    {
        _terrain = terrain;
        Face = face;
        ChunkU = chunkU;
        ChunkV = chunkV;

        Name = $"Chunk_{face}_{chunkU}_{chunkV}";

        // Static level geometry, exactly like the graybox boxes, so hitscan and
        // the player's floor test need no special case for the planet.
        CollisionLayer = DillControl.Core.GameLayers.Mask(DillControl.Core.GameLayers.World);
        CollisionMask = 0;

        int span = terrain.ChunkVertexEdge - 1;
        float half = span / (float)(terrain.FaceVertexEdge - 1);
        float centreA = ((chunkU * span + (span * 0.5f)) / (terrain.FaceVertexEdge - 1) * 2.0f) - 1.0f;
        float centreB = ((chunkV * span + (span * 0.5f)) / (terrain.FaceVertexEdge - 1) * 2.0f) - 1.0f;

        CentreNormal = CubeSphere.Direction(face, centreA, centreB);

        // Worst case angular reach: the corner of the chunk, plus a little for
        // terrain relief lifting a far corner over the horizon.
        Vector3 corner = CubeSphere.Direction(face, centreA - (half * 2.0f), centreB - (half * 2.0f));
        AngularRadius = CentreNormal.AngleTo(corner);

        Centre = CentreNormal * terrain.Radius;
        Position = Centre;

        _mesh = new ArrayMesh();
        _meshInstance = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = _mesh,
            // The planet is lit by one directional light; chunk self-shadowing
            // buys nothing at this scale and costs a second pass per chunk.
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (material != null)
        {
            _meshInstance.MaterialOverride = material;
        }
        AddChild(_meshInstance);

        _collision = new CollisionShape3D { Name = "Collision", Disabled = true };
        AddChild(_collision);

        GeometryDirty = true;
    }

    /// <summary>Rebuilds the visual mesh from the terrain's height array.</summary>
    public void RebuildMesh()
    {
        _terrain.BuildChunkArrays(this, out Vector3[] vertices, out Vector3[] normals, out int[] indices,
                                  out Color[] dug);

        Godot.Collections.Array surface = new();
        surface.Resize((int)Mesh.ArrayType.Max);
        surface[(int)Mesh.ArrayType.Vertex] = vertices;
        surface[(int)Mesh.ArrayType.Normal] = normals;
        surface[(int)Mesh.ArrayType.Index] = indices;

        // Only on a chunk someone has dug into. The shader reads a missing
        // colour array as white, which is what untouched ground is encoded as.
        if (dug != null)
        {
            surface[(int)Mesh.ArrayType.Color] = dug;
        }

        _mesh.ClearSurfaces();
        _mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface, null, _terrain.LodIndices);

        GeometryDirty = false;

        // A collider that is currently resident has to follow the new shape,
        // or the player walks on terrain that is no longer drawn there. Not
        // here, though: the bake is most of a rebuild's cost, so the terrain
        // queues it as a unit of its own and a crater's mesh and its collider
        // land on different frames. See PlanetTerrain.ProcessBuildQueue.
        ColliderStale = HasCollision;
    }

    /// <summary>The mesh moved and the resident collider has not followed yet.</summary>
    public bool ColliderStale { get; set; }

    /// <summary>Builds the trimesh collider from the current mesh surface.</summary>
    public void RebuildCollider()
    {
        _terrain.BuildChunkArrays(this, out Vector3[] vertices, out _, out int[] indices);

        Vector3[] faces = new Vector3[indices.Length];
        for (int i = 0; i < indices.Length; i++)
        {
            faces[i] = vertices[indices[i]];
        }

        ConcavePolygonShape3D shape = new();
        shape.SetFaces(faces);
        _collision.Shape = shape;
        _collision.Disabled = false;
        HasCollision = true;
    }

    /// <summary>Drops the collider, freeing its broadphase entry and its BVH.</summary>
    public void ReleaseCollider()
    {
        _collision.Disabled = true;
        _collision.Shape = null;
        HasCollision = false;
    }

    /// <summary>
    /// The horizon cull calls this for every chunk on the planet every frame --
    /// 864 of them at the shipped radius -- so the guard has to be a C# field
    /// rather than a read back through the engine. Reading MeshInstance3D.Visible
    /// is a marshalled property get, and 864 of those a frame is a tenth of a
    /// millisecond spent asking the renderer what we already told it.
    /// </summary>
    public void SetMeshVisible(bool visible)
    {
        if (_meshVisible == visible)
        {
            return;
        }

        _meshVisible = visible;
        _meshInstance.Visible = visible;
    }

    private bool _meshVisible = true;
}
