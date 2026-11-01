using Godot;

namespace DillControl.Planet;

/// <summary>
/// The value noise's lattice as a 3D texture, so the menu planet's shaders read
/// one texel blend where they hashed eight corners (<c>planet_backdrop</c>,
/// <c>planet_clouds</c>). Handed to every shader as the global
/// <c>planet_noise</c>; whoever draws with it publishes it.
///
/// Texel k along an edge holds the shaders' hash of lattice cell k, or of k - 128
/// past the middle, so every cell from -64 to 63 is the one the hash gave: noise
/// read inside that window is the hashed noise. Beyond it the texture repeats,
/// still value noise but other values, so a shader only uses it where its
/// coordinates are known to stay inside.
/// </summary>
public static class NoiseLattice
{
    /// <summary>Lattice cells along each edge. The shaders' NOISE_EDGE.</summary>
    public const int Edge = 128;

    private const string Global = "planet_noise";

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
        var slices = new Godot.Collections.Array<Image>();
        byte[] bytes = new byte[Edge * Edge];
        for (int z = 0; z < Edge; z++)
        {
            for (int y = 0; y < Edge; y++)
            {
                for (int x = 0; x < Edge; x++)
                {
                    bytes[(y * Edge) + x] = (byte)Mathf.RoundToInt(Hash(Cell(x), Cell(y), Cell(z)) * 255.0f);
                }
            }
            slices.Add(Image.CreateFromData(Edge, Edge, false, Image.Format.R8, bytes));
        }
        _texture = new ImageTexture3D();
        _texture.Create(Image.Format.R8, Edge, Edge, Edge, false, slices);
        return _texture;
    }

    private static int Cell(int texel) => texel < Edge / 2 ? texel : texel - Edge;

    // The shaders' hash_cell, bit for bit (uint arithmetic wraps the same way).
    private static float Hash(int x, int y, int z)
    {
        uint qx = unchecked((uint)(x + 1024) * 1597334673u);
        uint qy = unchecked((uint)(y + 1024) * 3812015801u);
        uint qz = unchecked((uint)(z + 1024) * 2798796415u);
        uint bits = unchecked((qx ^ qy ^ qz) * 1597334673u);
        return (float)(bits * (1.0 / 4294967295.0));
    }
}
