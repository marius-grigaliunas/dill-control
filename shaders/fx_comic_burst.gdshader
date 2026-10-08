// PLACEHOLDER-adjacent: the comic-book "KA-BOOM" star an explosion opens with.
//
// One quad, a few frames long, drawn as a jagged star with a hot fill and a
// coloured rim that hollows out from the middle as it goes. It is the cheapest
// layer of the explosion -- one quad, opaque with discard, no texture -- and
// the one that does the most to make a blast read as stylised rather than as
// a puff, because it is the shape every comic has already taught the eye.
//
// Opaque so the ink pass outlines it. Billboarded in the vertex stage with
// the node's scale kept, and pulled toward the camera by part of its own size
// so a ground burst seen from eye height is not cut in half by the ground.
shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled, shadows_disabled;

instance uniform float progress = 0.0;
instance uniform float seed = 0.0;

uniform vec4 fill_color : source_color = vec4(1.0, 0.84, 0.16, 1.0);
uniform vec4 rim_color : source_color = vec4(0.96, 0.2, 0.06, 1.0);
uniform vec4 inner_color : source_color = vec4(1.0, 0.98, 0.78, 1.0);
uniform float energy : hint_range(0.5, 4.0) = 1.0;

// Points on the star. Kept a whole number so the spikes meet at the seam.
uniform float spikes : hint_range(5.0, 16.0, 1.0) = 11.0;
// Radius of the valleys between spikes, as a share of the longest spike.
uniform float inner_radius : hint_range(0.1, 0.9) = 0.42;
uniform float rim_width : hint_range(0.0, 0.5) = 0.2;
// Share of the size the quad is pulled toward the camera.
uniform float camera_pull : hint_range(0.0, 1.5) = 0.7;

float hash1(float n) {
	return fract(sin(n * 127.1 + seed * 311.7) * 43758.5453);
}

void vertex() {
	float s = length(MODEL_MATRIX[0].xyz);
	vec4 centre = VIEW_MATRIX * vec4(MODEL_MATRIX[3].xyz, 1.0);

	// Toward the camera, but never through the near plane.
	float depth = -centre.z;
	float pull = clamp(s * camera_pull, 0.0, max(depth - 0.4, 0.0));
	centre.xyz -= normalize(centre.xyz) * pull;

	float a = seed * 6.2831853;
	vec2 corner = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * VERTEX.xy * s;
	POSITION = PROJECTION_MATRIX * vec4(centre.xyz + vec3(corner, 0.0), 1.0);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float angle = atan(p.y, p.x);

	// Each spike gets its own length, chosen per spike so the jitter changes
	// only in the valleys, where every spike is at inner_radius anyway.
	float k = mod(floor(angle * spikes / 6.2831853 + 0.5) + spikes * 4.0, spikes);
	float reach = mix(0.62, 1.0, hash1(k));
	float spike = pow(abs(cos(angle * spikes * 0.5)), 2.5);
	float edge = mix(inner_radius, 1.0, spike * reach);

	if (r > edge) {
		discard;
	}

	// Hollows out from the middle, so the star ends as a ring of spikes.
	float hollow = edge * smoothstep(0.3, 1.0, progress) * 0.95;
	if (r < hollow) {
		discard;
	}

	vec3 c = fill_color.rgb;
	if (r > edge * (1.0 - rim_width) || r < hollow + rim_width * 0.25) {
		c = rim_color.rgb;
	} else if (r < edge * 0.35 && progress < 0.5) {
		c = inner_color.rgb;
	}
	ALBEDO = c * energy;
}
