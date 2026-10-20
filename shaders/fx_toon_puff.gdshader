// PLACEHOLDER-adjacent: one cartoon smoke or dust puff -- a ball with a lit
// side and a shadow side, the way a comic draws a cloud -- for the
// explosion's particle emitters, and for the dust of everything that breaks
// (DustField), tinted the colour of what broke.
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
// Multiplied into the particle colour: the dust of a broken thing takes its
// colour. White for the explosions, which colour their puffs by ramp alone.
uniform vec4 tint : source_color = vec4(1.0);
// How far a puff's outline bulges in and out, as a share of its radius: a
// perfect circle reads as a soap bubble once a cloud is made of dozens. 0
// for the explosions' round puffs; the dust turns it up.
uniform float lumpiness : hint_range(0.0, 0.5) = 0.0;
// Bulges round one puff's outline.
uniform float lump_count : hint_range(2.0, 12.0) = 5.0;
// Each puff lighter or darker by up to this share, from its own seed, so a
// cloud is not one flat colour. 0 for the explosions, as before.
uniform float shade_spread : hint_range(0.0, 0.3) = 0.0;

// Which way this puff's bulges sit: from the particle's own index and spin,
// so neighbouring puffs never share an outline.
varying float v_seed;

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
	v_seed = float(INSTANCE_ID) * 2.399 + INSTANCE_CUSTOM.x;
}

// 0..1 round the outline: two sine rings at different counts, so the
// bulges are uneven, like a cumulus edge, not a gear.
float lumps(float angle, float seed) {
	float a = sin(angle * lump_count + seed);
	float b = sin(angle * (lump_count + 3.0) + seed * 1.7);
	return clamp(0.5 + 0.35 * a + 0.15 * b, 0.0, 1.0);
}

void fragment() {
	vec2 p = UV * 2.0 - 1.0;
	float r = length(p);
	float size = clamp(COLOR.a, 0.0, 1.0);
	float angle = atan(p.y, p.x);
	// The bulges only ever take the edge in, so a puff never leaves its quad.
	float edge = size * (1.0 - lumpiness * (1.0 - lumps(angle, v_seed)));
	if (r > edge) {
		discard;
	}

	// Lit where the point is inside a circle shifted toward the light: the
	// classic cartoon ball, a crescent of shadow on the far side -- its edge
	// lumpy too, so the shadow reads as folds rather than a clean crescent.
	vec2 q = p - light_offset * size;
	float fold = 1.0 - lumpiness * 0.8 * (1.0 - lumps(atan(q.y, q.x), v_seed * 1.3 + 2.0));
	bool lit = length(q) < size * 0.82 * fold;
	vec3 c = COLOR.rgb * tint.rgb;
	if (!lit) {
		c *= mix(vec3(1.0), shadow_tint.rgb, shadow_strength) * (1.0 - shadow_strength * 0.45);
	}
	c *= 1.0 + shade_spread * (fract(sin(v_seed * 12.9898) * 43758.5453) * 2.0 - 1.0);
	ALBEDO = c;
}
