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
// material whose flex_row names its row of flex_rods: its foot, its up, the
// length of a segment and how far each point of its spine has moved. A vertex
// moves as its height on the spine does. Everything else keeps flex_row at -1
// and is drawn where it was built.
shader_type spatial;
render_mode diffuse_toon, specular_toon, world_vertex_coords;

uniform float emission_scale : hint_range(0.0, 8.0) = 1.0;
uniform sampler2D flex_rods : filter_nearest, repeat_disable;
uniform int flex_row = -1;

void vertex() {
	if (flex_row >= 0) {
		vec4 foot = texelFetch(flex_rods, ivec2(0, flex_row), 0);
		vec4 axis = texelFetch(flex_rods, ivec2(1, flex_row), 0);
		float last = axis.w - 1.0;
		float along = clamp(dot(VERTEX - foot.xyz, axis.xyz) * foot.w, 0.0, last);
		int i = min(int(along), int(last) - 1);
		vec3 a = texelFetch(flex_rods, ivec2(2 + i, flex_row), 0).xyz;
		vec3 b = texelFetch(flex_rods, ivec2(3 + i, flex_row), 0).xyz;
		VERTEX += mix(a, b, along - float(i));
	}
}

void fragment() {
	float glow = UV.x;
	ALBEDO = COLOR.rgb * (1.0 - clamp(glow, 0.0, 1.0));
	EMISSION = COLOR.rgb * glow * emission_scale;
	ROUGHNESS = 1.0;
}
