// PLACEHOLDER-adjacent: one cartoon smoke or dust puff -- a ball with a lit
// side and a shadow side, the way a comic draws a cloud -- for the
// explosion's particle emitters.
//
// Opaque with discard rather than alpha-blended, so the ink pass outlines
// every puff and overlapping puffs read as one lumpy cloud with lines between
// its lobes. That is also cheaper than the soft billboards it replaces, which
// were the largest share of a near blast's cost because blended overdraw is
// paid for every layer.
//
// The particle colour's alpha is the puff's size within its quad rather than
// its opacity: a puff swells with the emitter's scale curve and then shrinks
// away to nothing as its colour ramp's alpha falls, instead of fading.
shader_type spatial;
render_mode unshaded, cull_disabled, fog_disabled, shadows_disabled;

// How dark the shadow side is, as a share of the lit colour.
uniform float shadow_strength : hint_range(0.0, 1.0) = 0.62;
// Shadow tint, multiplied in -- cool, so smoke shadows go purple, not brown.
uniform vec4 shadow_tint : source_color = vec4(0.78, 0.74, 0.95, 1.0);
// Where on the puff the light comes from, in its own quad (screen up is -y).
uniform vec2 light_offset = vec2(-0.22, -0.28);

void vertex() {
	// The particle billboard StandardMaterial3D writes, with scale kept.
	mat4 world = mat4(
		normalize(INV_VIEW_MATRIX[0]),
		normalize(INV_VIEW_MATRIX[1]),
		normalize(INV_VIEW_MATRIX[2]),
		MODEL_MATRIX[3]);
	world = world * mat4(
		vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
		vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0),
		vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
		vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * world;
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float size = clamp(COLOR.a, 0.0, 1.0);
	if (r > size) {
		discard;
	}

	// Lit where the point is inside a circle shifted toward the light: the
	// classic cartoon ball, a crescent of shadow on the far side.
	bool lit = length(p - light_offset * size) < size * 0.82;
	vec3 c = COLOR.rgb;
	if (!lit) {
		c *= mix(vec3(1.0), shadow_tint.rgb, shadow_strength) * (1.0 - shadow_strength * 0.45);
	}
	ALBEDO = c;
}
