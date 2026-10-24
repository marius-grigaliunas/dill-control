// Shallow water's surface (docs/decisions/0045): a flat cartoon sheet,
// opaque, so the ink pass outlines its shore where it meets the ground and it
// costs no blended overdraw. Toon light like everything else, a few drifting
// ripple bands in a lighter tone, and a hard glint where the sun catches it.
shader_type spatial;
render_mode diffuse_toon, specular_toon;

uniform vec4 colour : source_color = vec4(0.16, 0.36, 0.40, 1.0);
// The ripples' tone, mixed over the colour where a band is.
uniform vec4 ripple_colour : source_color = vec4(0.30, 0.52, 0.55, 1.0);
// Metres between ripple bands, and how far they drift a second.
uniform float ripple_spacing = 6.0;
uniform float ripple_speed = 0.35;
// The share of a band that is drawn: thin lines, not stripes.
uniform float ripple_width : hint_range(0.0, 0.5) = 0.08;
uniform float glint : hint_range(0.0, 1.0) = 0.6;

varying vec3 world;

void vertex() {
	world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Two sets of bands across each other, bent a little so they read as water, not a grid.
float bands(vec2 p, float t) {
	float a = sin(p.x * 0.21 + t * 0.7) * 1.4 + p.y;
	float b = sin(p.y * 0.17 - t * 0.5) * 1.6 + p.x * 0.6 - p.y * 0.4;
	float ra = abs(fract(a / ripple_spacing + t * ripple_speed / ripple_spacing) - 0.5);
	float rb = abs(fract(b / (ripple_spacing * 1.7) - t * ripple_speed / ripple_spacing) - 0.5);
	return step(0.5 - ripple_width, ra) + step(0.5 - ripple_width * 0.6, rb);
}

void fragment() {
	float r = clamp(bands(world.xz, TIME), 0.0, 1.0);
	ALBEDO = mix(colour.rgb, ripple_colour.rgb, r);
	ROUGHNESS = 0.35;
	SPECULAR = glint;
	METALLIC = 0.0;
}
