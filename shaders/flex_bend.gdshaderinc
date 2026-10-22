// How a plant bends (docs/decisions/0032): a material whose flex_row names its
// row of flex_rods (its foot, its up, the length of a segment and how far each
// point of its spine has moved) moves a vertex as its height on the spine
// does. flex_row -1 is drawn where it was built. FlexField writes the rows.
// Needs world_vertex_coords.
uniform sampler2D flex_rods : filter_nearest, repeat_disable;
uniform int flex_row = -1;

vec3 flex_bend(vec3 world) {
	if (flex_row < 0) {
		return world;
	}
	vec4 foot = texelFetch(flex_rods, ivec2(0, flex_row), 0);
	vec4 axis = texelFetch(flex_rods, ivec2(1, flex_row), 0);
	float last = axis.w - 1.0;
	float along = clamp(dot(world - foot.xyz, axis.xyz) * foot.w, 0.0, last);
	int i = min(int(along), int(last) - 1);
	vec3 a = texelFetch(flex_rods, ivec2(2 + i, flex_row), 0).xyz;
	vec3 b = texelFetch(flex_rods, ivec2(3 + i, flex_row), 0).xyz;
	return world + mix(a, b, along - float(i));
}
