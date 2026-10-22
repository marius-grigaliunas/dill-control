// Every voxel model's material: the toon lighting of mat_palette_toon, with
// colour from the vertices instead of the palette texture.
//
// The .vox importer writes each voxel's palette colour (linear) into COLOR and
// its MagicaVoxel emission strength into UV.x. An emissive voxel is drawn
// unlit in proportion to its strength, so a visor or a neon strip reads as a
// light at night as well as by day, without a light of its own. Its halo is
// the environment's glow, if the level has glow on; see docs/decisions/0009.
//
// A plant bends (docs/decisions/0032): it is drawn with a copy of this
// material whose flex_row names its row of flex_rods (flex_bend.gdshaderinc).
// Everything else keeps flex_row at -1 and is drawn where it was built.
shader_type spatial;
render_mode diffuse_toon, specular_toon, world_vertex_coords;

#include "res://shaders/flex_bend.gdshaderinc"

uniform float emission_scale : hint_range(0.0, 8.0) = 1.0;

void vertex() {
	VERTEX = flex_bend(VERTEX);
}

void fragment() {
	float glow = UV.x;
	ALBEDO = COLOR.rgb * (1.0 - clamp(glow, 0.0, 1.0));
	EMISSION = COLOR.rgb * glow * emission_scale;
	ROUGHNESS = 1.0;
}
