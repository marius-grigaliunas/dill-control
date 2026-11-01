using Godot;

namespace DillControl.Planet;

/// <summary>
/// Round balls packed in a 3D texture, for the menu planet's clouds to heap into
/// (<c>planet_clouds.gdshader</c>'s billows): each cell of a lattice holds one
/// ball at a random place with a random size, and a texel holds how deep it is
/// inside the nearest one, 1 at a ball's heart and 0 outside them all. Added to
/// the cloud's density, the balls bulge its tops and edges into round lobes,
/// cauliflower, and the creases between them are where two balls meet. Value
/// noise did this before and made blobs, not balls.
///
/// Repeats every <see cref="Cells"/> cells each way, so the shaders read it with
/// repeat on. Handed to every shader as the global <c>planet_puffs</c>; whoever
/// draws with it publishes it.
/// </summary>
public static class CloudPuffs
{
    /// <summary>Balls along each edge. The shaders' PUFF_CELLS.</summary>
    public const int Cells = 8;

    /// <summary>Texels along each ball's cell: enough that a ball reads round when blended.</summary>
    private const int TexelsPerCell = 8;

    private const int Edge = Cells * TexelsPerCell;

    /// <summary>The smallest and largest ball, as a share of a cell. Above a half they overlap.</summary>
    private const float SmallestBall = 0.55f;
    private const float LargestBall = 0.95f;

    private const string Global = "planet_puffs";

    private static ImageTexture3D _texture;

    /// <summary>Make the texture if it is not made yet, and hand it to the shaders.</summary>
    public static void Publish()
    {
        RenderingServer.GlobalShaderParameterSet(Global, Texture());
    }

    private static ImageTexture3D Texture()
    {
        if (_texture != null)
        {
            return _texture;
        }

        // Each cell's ball: where it sits in the cell and how big it is.
        var centres = new Vector3[Cells * Cells * Cells];
        var radii = new float[centres.Length];
        for (int z = 0; z < Cells; z++)
        {
            for (int y = 0; y < Cells; y++)
            {
                for (int x = 0; x < Cells; x++)
                {
                    int i = Index(x, y, z);
                    centres[i] = new Vector3(x + Hash(x, y, z, 0), y + Hash(x, y, z, 1), z + Hash(x, y, z, 2));
                    radii[i] = Mathf.Lerp(SmallestBall, LargestBall, Hash(x, y, z, 3));
                }
            }
        }

        var slices = new Godot.Collections.Array<Image>();
        byte[] bytes = new byte[Edge * Edge * sizeof(float)];
        for (int tz = 0; tz < Edge; tz++)
        {
            for (int ty = 0; ty < Edge; ty++)
            {
                for (int tx = 0; tx < Edge; tx++)
                {
                    var at = new Vector3(tx + 0.5f, ty + 0.5f, tz + 0.5f) / TexelsPerCell;
                    int cx = (int)at.X;
                    int cy = (int)at.Y;
                    int cz = (int)at.Z;
                    float inside = 0.0f;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = cx + dx;
                                int ny = cy + dy;
                                int nz = cz + dz;
                                int i = Index(Wrap(nx), Wrap(ny), Wrap(nz));
                                // The neighbour's ball, moved to this side of the wrap.
                                Vector3 centre = centres[i] + new Vector3(nx - Wrap(nx), ny - Wrap(ny), nz - Wrap(nz));
                                float depth = 1.0f - (at.DistanceTo(centre) / radii[i]);
                                inside = Mathf.Max(inside, depth);
                            }
                        }
                    }
                    System.BitConverter.TryWriteBytes(new System.Span<byte>(bytes, ((ty * Edge) + tx) * sizeof(float), sizeof(float)), inside);
                }
            }
            slices.Add(Image.CreateFromData(Edge, Edge, false, Image.Format.Rf, bytes));
        }
        _texture = new ImageTexture3D();
        _texture.Create(Image.Format.Rf, Edge, Edge, Edge, false, slices);
        return _texture;
    }

    private static int Index(int x, int y, int z) => (((z * Cells) + y) * Cells) + x;

    private static int Wrap(int c) => ((c % Cells) + Cells) % Cells;

    private static float Hash(int x, int y, int z, int k)
    {
        uint q = unchecked(((uint)x * 1597334673u) ^ ((uint)y * 3812015801u) ^ ((uint)z * 2798796415u) ^ ((uint)k * 2246822519u));
        q = unchecked((q ^ (q >> 15)) * 2654435761u);
        q ^= q >> 13;
        return (float)(q * (1.0 / 4294967295.0));
    }
}
