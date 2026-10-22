// A plant's leaves (LeafMesh, VineLeaves): the toon lighting of voxel_toon,
// colour from the vertices (veins darker) times each leaf's own shade (its
// MultiMesh custom data; a lone leaf has none and keeps its colour), both
// faces drawn and lit from the side seen, so a blade is one sheet of a few
// dozen triangles. Bent as the plant is (flex_bend.gdshaderinc); a torn-off
// leaf keeps flex_row at -1. The ink pass outlines it like everything else.
shader_type spatial;
render_mode diffuse_toon, specular_toon, world_vertex_coords, cull_disabled;

#include "res://shaders/flex_bend.gdshaderinc"

void vertex() {
	VERTEX = flex_bend(VERTEX);
	if (INSTANCE_CUSTOM.a > 0.0) {
		COLOR.rgb *= INSTANCE_CUSTOM.rgb;
	}
}

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
}
