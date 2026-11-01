// The air round the menu planet, over the ground and space: every pixel the air
// covers that is not a cloud (the clouds mark theirs in the stencil buffer;
// planet_atmosphere_clouds draws those). The sight line ends on the planet's
// sphere, else where it leaves the air. The light itself is planet_air.gdshaderinc.
//
// Drawn on the inside of a sphere (PlanetLayer, AboveSurface is the top of the
// air) with no depth test, after everything opaque: it covers every pixel the
// air does, whether the camera is outside the air or in it.
//
// Cost (the menu's 60 fps on Iris Xe): every pixel the air covers, three to
// eight samples, a few exp() each.

shader_type spatial;
render_mode unshaded, blend_premul_alpha, cull_front, depth_test_disabled, depth_draw_never, shadows_disabled, fog_disabled;
stencil_mode read, compare_not_equal, 1;

#include "res://shaders/planet_air.gdshaderinc"

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	vec3 camera = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz - centre;
	vec3 ray = normalize(world_position - centre - camera);

	vec2 air = sphere_crossings(camera, ray, layer_radius);
	if (air.y <= 0.0) {
		discard;
	}
	float t0 = max(air.x, 0.0);
	float t1 = air.y;
	// The ground: the planet's mean sphere (the relief is a few percent of the
	// air's reach, nothing to the haze).
	vec2 ground = sphere_crossings(camera, ray, planet_radius);
	if (ground.x > 0.0) {
		t1 = min(t1, ground.x);
	}
	if (t1 <= t0) {
		discard;
	}
	vec3 colour;
	float alpha;
	air_light(camera, ray, t0, t1, colour, alpha);
	ALBEDO = colour;
	ALPHA = alpha;
}
