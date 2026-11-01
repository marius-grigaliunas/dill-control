// The air round the menu planet, over the clouds: only the pixels the clouds
// marked in the stencil buffer (planet_atmosphere draws the rest). The sight
// line ends where it reaches the top of the cloud deck (stop_radius, from
// PlanetLayer.StopAt): the cloud is at most a few hundred metres further, nothing
// to the haze. The light itself is planet_air.gdshaderinc.
//
// Drawn by PlanetLayer as the air layer's overlay, a second pass of the same
// sphere, with the air material's settings copied over every frame.

shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;
stencil_mode read, compare_equal, 1;

#include "res://shaders/planet_air.gdshaderinc"

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

instance uniform float stop_radius = 5450.0;

void fragment() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	vec3 camera = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz - centre;
	vec3 ray = normalize(world_position - centre - camera);

	vec2 air = sphere_crossings(camera, ray, layer_radius);
	vec2 deck = sphere_crossings(camera, ray, stop_radius);
	float t0 = max(air.x, 0.0);
	// From inside the deck the cloud is right there: nothing to haze.
	float t1 = deck.x;
	if (air.y <= 0.0 || t1 <= t0) {
		discard;
	}
	vec3 colour;
	float alpha;
	air_light(camera, ray, t0, t1, colour, alpha);
	ALBEDO = colour;
	ALPHA = alpha;
}
