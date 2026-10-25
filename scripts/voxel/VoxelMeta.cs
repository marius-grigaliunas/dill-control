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

    /// <summary>Chunk graph baked by the importer (docs/decisions/0027).
    /// Absent on a model imported before that: the part runs the cell flood.</summary>
    public const string ChunkVersion = "vox_chunk_version";
    public const string ChunkCells = "vox_chunk_cells";
    public const string ChunkCount = "vox_chunk_count";
    public const string ChunkSolid = "vox_chunk_solid";
    public const string ChunkMin = "vox_chunk_min";
    public const string ChunkMax = "vox_chunk_max";
    public const string ChunkLayers = "vox_chunk_layers";
    public const string ChunkLayerCount = "vox_chunk_layer_count";
    public const string ChunkLayerSumX = "vox_chunk_layer_sum_x";
    public const string ChunkLayerSumY = "vox_chunk_layer_sum_y";
    public const string ChunkEdgeA = "vox_chunk_edge_a";
    public const string ChunkEdgeB = "vox_chunk_edge_b";
    public const string ChunkEdgeFaces = "vox_chunk_edge_faces";
    public const string ChunkBoxOffset = "vox_chunk_box_offset";
    public const string ChunkBoxCentre = "vox_chunk_box_centre";
    public const string ChunkBoxSize = "vox_chunk_box_size";
    public const string ChunkGround = "vox_chunk_ground";

    /// <summary>A mech part's damage map (docs/decisions/0046,
    /// <see cref="VoxDamageMap"/>). Only on models with pivot voxels.</summary>
    public const string PartVersion = "vox_part_version";
    public const string PartParent = "vox_part_parent";
    public const string PartVoxels = "vox_part_voxels";
    public const string PartCentre = "vox_part_centre";
    public const string PartHold = "vox_part_hold";
    public const string PartContactKind = "vox_part_contact_kind";
    public const string PartContactCentre = "vox_part_contact_centre";
    public const string PartContactSize = "vox_part_contact_size";
}
