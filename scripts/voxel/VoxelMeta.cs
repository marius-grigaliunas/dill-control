namespace DillControl.Voxel;

/// <summary>
/// Metadata keys the .vox importer writes and <see cref="VoxelBody"/> reads:
/// a model's voxels travel with its imported scene. On the root: one voxel's
/// size in metres, the palette and the emission per palette index. On each
/// part's mesh node: its cells (MagicaVoxel order and axes, pivot removed),
/// their dimensions, and the voxel-space point that is the node's origin.
/// </summary>
public static class VoxelMeta
{
    public const string VoxelSize = "vox_size";
    public const string Palette = "vox_palette";
    public const string Emission = "vox_emission";
    public const string Cells = "vox_cells";
    public const string Dims = "vox_dims";
    public const string Origin = "vox_origin";
}
