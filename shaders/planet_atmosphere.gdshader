// The air round the menu planet, cel shaded: the glow is three flat bands of
// blue, bright against the surface and fading to nothing in space, not a soft
// gradient. Strongest on the sun's side, a faint ring on the night side.
//
// Drawn on a sphere standing off the planet (PlanetLayer). For each pixel the
// sight line's closest pass to the planet's centre says how much air it
// crosses: just above the limb is the thickest, and the glow thins away
// outward to the layer's edge. Where the line goes into the planet it thins
// inward, so the limb of the planet itself is lit and the middle of its disc is
// clear.
//
// Meant to be seen from outside: a camera inside the sphere sees nothing.

shader_type spatial;
render_mode unshaded, blend_mix, cull_back, depth_draw_never;

instance uniform float planet_radius = 5000.0;
instance uniform float layer_radius = 5400.0;
instance uniform vec3 sun_direction = vec3(0.0, 1.0, 0.0);

uniform vec3 inner_colour : source_color = vec3(0.72, 0.92, 1.0);
uniform vec3 middle_colour : source_color = vec3(0.25, 0.6, 1.0);
uniform vec3 outer_colour : source_color = vec3(0.08, 0.3, 0.9);
uniform float inner_alpha : hint_range(0.0, 1.0) = 0.6;
uniform float middle_alpha : hint_range(0.0, 1.0) = 0.45;
uniform float outer_alpha : hint_range(0.0, 1.0) = 0.3;

// Where the bands break, as a share of the glow at its strongest.
uniform float inner_from : hint_range(0.0, 1.0) = 0.62;
uniform float middle_from : hint_range(0.0, 1.0) = 0.3;
uniform float outer_from : hint_range(0.0, 1.0) = 0.06;

// How quickly the glow thins going out (higher hugs the planet) and how far in
// from the limb the lit rim reaches, as a share of the radius.
uniform float falloff = 2.6;
uniform float rim_depth : hint_range(0.0, 0.5) = 0.015;

// How wide the blend between colours is, as a share of the glow (0 is the old hard bands).
uniform float softness : hint_range(0.0, 0.6) = 0.3;

// Sunrise and sunset colours, how much they take over at the terminator, and how
// wide that band is (in the sun-side dot product; 0.25 is about 15 degrees each way).
uniform vec3 dusk_inner : source_color = vec3(1.0, 0.62, 0.22);
uniform vec3 dusk_outer : source_color = vec3(0.9, 0.22, 0.4);
uniform float dusk_strength : hint_range(0.0, 1.0) = 0.85;
uniform float twilight_width : hint_range(0.05, 1.0) = 0.4;

// What is left on the night side.
uniform float night_glow : hint_range(0.0, 1.0) = 0.12;

varying vec3 world_position;

void vertex() {
	world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	vec3 camera = (INV_VIEW_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz - centre;
	vec3 ray = normalize(world_position - centre - camera);

	// Closest pass of the sight line to the centre.
	float along = max(dot(-camera, ray), 0.0);
	vec3 closest = camera + ray * along;
	float pass_distance = length(closest);

	float glow;
	if (pass_distance >= planet_radius) {
		float height = clamp((pass_distance - planet_radius) / max(layer_radius - planet_radius, 0.001), 0.0, 1.0);
		glow = pow(1.0 - height, falloff);
	} else {
		glow = smoothstep(planet_radius * (1.0 - rim_depth), planet_radius, pass_distance);
	}

	float sun_side = dot(normalize(closest), normalize(sun_direction));
	float day = smoothstep(-0.55, 0.45, sun_side);
	glow *= mix(night_glow, 1.0, day);

	// Blended, not banded: the colour runs outer to middle to inner as the air
	// thickens, and fades out to nothing, so no ring has an edge.
	vec3 colour = mix(outer_colour, middle_colour, smoothstep(outer_from, middle_from + softness, glow));
	colour = mix(colour, inner_colour, smoothstep(middle_from, inner_from + softness, glow));

	// Sunrise and sunset: where the sight line grazes the terminator the air is
	// lit from the side and goes orange at its thick base and rose in the thin
	// outer air. A band round the terminator, widest on the limb.
	float twilight = exp(-pow(sun_side / twilight_width, 2.0));
	vec3 dusk = mix(dusk_outer, dusk_inner, smoothstep(0.05, 0.7, glow));
	colour = mix(colour, dusk, twilight * dusk_strength);

	float alpha = mix(outer_alpha, inner_alpha, smoothstep(outer_from, inner_from, glow));
	alpha *= smoothstep(0.0, outer_from + softness, glow);

	ALBEDO = colour;
	ALPHA = alpha;
}
