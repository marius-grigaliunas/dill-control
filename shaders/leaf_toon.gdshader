// A plant's leaves (LeafMesh, VineLeaves): the toon lighting of voxel_toon,
// colour from the vertices (veins darker) times each leaf's own shade (its
// MultiMesh custom data; a lone leaf has none and keeps its colour), both
// faces drawn and lit from the side seen, so a blade is one sheet of a few
// dozen triangles. Bent as the plant is (flex_bend.gdshaderinc); a torn-off
// leaf keeps flex_row at -1. The ink pass outlines it like everything else.
//
// Shot leaves have holes: leaf_holes holds a row per leaf, a texel per hole
// (its centre across and along the blade, its radius, a seed for its ragged
// edge), and a leaf with any says its row in its custom data's alpha, as
// 2 + row (1 is a shade and no holes). A torn-off leaf names its row in
// leaf_row instead.
shader_type spatial;
render_mode diffuse_toon, specular_toon, world_vertex_coords, cull_disabled;

#include "res://shaders/flex_bend.gdshaderinc"

uniform sampler2D leaf_holes : filter_nearest, repeat_disable;
uniform int leaf_hole_slots = 0;
uniform int leaf_row = -1;

varying flat int hole_row;

void vertex() {
	VERTEX = flex_bend(VERTEX);
	if (INSTANCE_CUSTOM.a > 0.0) {
		COLOR.rgb *= INSTANCE_CUSTOM.rgb;
	}
	hole_row = leaf_row >= 0 ? leaf_row : (INSTANCE_CUSTOM.a > 1.5 ? int(INSTANCE_CUSTOM.a + 0.5) - 2 : -1);
}

void fragment() {
	if (hole_row >= 0) {
		for (int k = 0; k < leaf_hole_slots; k++) {
			vec4 hole = texelFetch(leaf_holes, ivec2(k, hole_row), 0);
			if (hole.z > 0.0) {
				vec2 d = UV - hole.xy;
				float ragged = 1.0 + 0.22 * sin(atan(d.y, d.x) * 5.0 + hole.w * 6.2832);
				if (length(d) < hole.z * ragged) {
					discard;
				}
			}
		}
	}
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	if (!FRONT_FACING) {
		NORMAL = -NORMAL;
	}
}
